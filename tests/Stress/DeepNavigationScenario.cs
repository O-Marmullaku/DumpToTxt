using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using DumpToTxt.App;
using DumpToTxt.Core;

internal static class NativeTestWindow
{
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    public static void Show(Form form)
    {
        // Consume the hidden helper's startup flag, then reveal only the tested window.
        form.Show();
        ShowWindow(form.Handle, 1);
    }
}

internal static class DeepNavigationScenario
{
    public static object Run(string fixture, DumpConfig config)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var clock = Stopwatch.StartNew();
        var model = new DumpReviewTree(DumpSelectionMode.Thorough);
        const int levels = 40;
        var type = DumpFileTypeKey.FromFileName("file.txt");
        string path = "";
        for (int level = 0; level < levels; level++)
        {
            for (int side = 1; side < 200; side++)
            {
                string sidePath = path + $"side-{side:D3}.txt";
                model.AddText(new(sidePath, sidePath, type, 1, 0, true));
            }
            path += "chain/";
            model.AddPath(new(path, path.TrimEnd('/'), true));
        }
        string leafPath = path + "leaf.txt";
        model.AddText(new(leafPath, leafPath, type, 1000, 0, true));
        int inventory = model.PathCount;
        using var form = new DumpSelectionForm(fixture, config);
        var formType = typeof(DumpSelectionForm);
        var tree = (TreeView)formType.GetField("_tree", flags)!.GetValue(form)!;
        var realized = (Dictionary<string, TreeNode>)formType.GetField("_realized", flags)!.GetValue(form)!;
        using var timer = new System.Windows.Forms.Timer { Interval = 20 };
        bool initialized = false, completed = false;
        int depth = 0, peak = 0;
        int rebases = 0;
        string? pendingRebase = null;
        var expansions = new List<double>();
        var populationTimes = new List<double>();
        Stopwatch? currentExpansion = null;
        double populationMs = 0;
        // This handler runs after the production BeforeExpand handler and separates
        // page preparation from the remainder of the native expansion call.
        tree.BeforeExpand += (_, _) => populationMs = currentExpansion?.Elapsed.TotalMilliseconds ?? 0;
        path = "";
        Exception? failure = null;
        timer.Tick += (_, _) =>
        {
            try
            {
                if (clock.Elapsed.TotalSeconds > 30)
                    throw new TimeoutException($"Deep navigation timed out at level {depth}, initialized={initialized}.");
                if (!initialized)
                {
                    if (!(bool)formType.GetField("_scanComplete", flags)!.GetValue(form)!) return;
                    formType.GetMethod("ReleaseNodes", flags)!.Invoke(form, new object[] { tree.Nodes });
                    tree.Nodes.Clear();
                    formType.GetField("_review", flags)!.SetValue(form, model);
                    formType.GetField("_viewRoot", flags)!.SetValue(form, model.Root);
                    formType.GetMethod("RefreshTree", flags)!.Invoke(form, null);
                    initialized = true;
                }
                if (pendingRebase is not null)
                {
                    var view = (DumpReviewNode)formType.GetField("_viewRoot", flags)!.GetValue(form)!;
                    if (view.RelativePath != pendingRebase) throw new InvalidOperationException("Requested folder view was not reached.");
                    pendingRebase = null;
                }
                if (depth < levels)
                {
                    path = path.Length == 0 ? "chain" : path + "/chain";
                    if (!realized.TryGetValue(path, out var row))
                        throw new InvalidOperationException($"Unreachable folder: {path}");
                    currentExpansion = Stopwatch.StartNew();
                    row.Expand();
                    expansions.Add(currentExpansion.Elapsed.TotalMilliseconds);
                    populationTimes.Add(populationMs);
                    currentExpansion = null;
                    if (!row.IsExpanded) { pendingRebase = path; rebases++; }
                    depth++;
                    peak = Math.Max(peak, realized.Count);
                    if (peak > 1600) throw new InvalidOperationException("Native row bound exceeded.");
                    return;
                }
                if (!realized.TryGetValue(leafPath, out var leaf))
                    throw new InvalidOperationException("Deep leaf is unreachable.");
                formType.GetMethod("ToggleNode", flags)!.Invoke(form, new object[] { leaf });
                if (model.SelectedBytes != levels * 199) throw new InvalidOperationException("Deep exclusion changed unrelated content.");
                while (((DumpReviewNode)formType.GetField("_viewRoot", flags)!.GetValue(form)!).Parent is not null)
                {
                    var back = tree.Nodes.Cast<TreeNode>().Single(row => row.Text.StartsWith("Back to ", StringComparison.Ordinal));
                    formType.GetMethod("PreviewNode", flags)!.Invoke(form, new object[] { back });
                }
                var restore = tree.Nodes.Cast<TreeNode>().FirstOrDefault(row => row.Text.StartsWith("Browse all ", StringComparison.Ordinal));
                if (restore is not null) formType.GetMethod("PreviewNode", flags)!.Invoke(form, new object[] { restore });
                if (!realized.ContainsKey("side-199.txt") || model.PathCount != inventory || model.SelectedBytes != levels * 199 || rebases == 0)
                    throw new InvalidOperationException("Restoring siblings changed inventory or selection.");
                completed = true;
                timer.Stop();
                form.Close();
            }
            catch (Exception error)
            {
                failure = error;
                timer.Stop();
                form.Close();
            }
        };
        form.Shown += (_, _) => timer.Start();
        NativeTestWindow.Show(form);
        Application.Run(form);
        if (failure is not null) throw failure;
        if (!completed) throw new InvalidOperationException("Deep navigation closed before completing assertions.");
        expansions.Sort();
        return new { mode = "ui-deep", levels = depth, logicalPaths = inventory, peakRealizedRows = peak, rebases,
            selectedBytes = model.SelectedBytes, elapsedMs = clock.Elapsed.TotalMilliseconds,
            expansionP95Ms = expansions[(int)((expansions.Count - 1) * .95)], expansionMaxMs = expansions[^1],
            populationMaxMs = populationTimes.Max(),
            latencyTargetPassed = expansions[(int)((expansions.Count - 1) * .95)] <= 100 && expansions[^1] <= 250,
            passed = true };
    }
}
