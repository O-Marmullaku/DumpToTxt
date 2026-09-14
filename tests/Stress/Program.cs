using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using DumpToTxt.App;
using DumpToTxt.Core;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var clock = Stopwatch.StartNew();
        var process = Process.GetCurrentProcess();
        long privatePeak = 0, managedPeak = 0; int handlesPeak = 0;
        using var sampling = new System.Threading.Timer(_ => { try { process.Refresh(); privatePeak = Math.Max(privatePeak, process.PrivateMemorySize64); managedPeak = Math.Max(managedPeak, GC.GetTotalMemory(false)); handlesPeak = Math.Max(handlesPeak, process.HandleCount); } catch { } }, null, 0, 20);
        var mode = args[0]; var root = args[1]; var report = args[2];
        var cfg = DumpConfig.CreateDefault(); cfg.ExcludeRegex = ""; cfg.RespectGitignore = false; cfg.UseDumpToTxtIgnore = false; cfg.PlayCompletionSound = false;
        try
        {
            if (mode is "ui" or "ui-cancel" or "ui-deep")
            {
                typeof(DumpSelectionForm).Assembly.GetType("ApplicationConfiguration", true)!
                    .GetMethod("Initialize")!.Invoke(null, null);
            }
            if (mode == "ui-deep")
            {
                File.WriteAllText(report, JsonSerializer.Serialize(DeepNavigationScenario.Run(root, cfg),
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (mode is "ui" or "ui-cancel")
            {
                using var form = new DumpSelectionForm(root, cfg);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var tree = (TreeView)typeof(DumpSelectionForm).GetField("_tree", flags)!.GetValue(form)!;
                var gaps = new List<double>(); var toggles = new List<double>();
                double last = 0, shown = 0, complete = 0, cancelAt = 0, postStart = 0;
                int phase = 0, postPhase = 0;
                using var beat = new System.Windows.Forms.Timer { Interval = 15 };
                form.Shown += (_, _) => { shown = clock.Elapsed.TotalMilliseconds; last = shown; beat.Start(); };
                beat.Tick += (_, _) =>
                {
                    var now = clock.Elapsed.TotalMilliseconds; gaps.Add(now - last); last = now;
                    bool scanDone = (bool)typeof(DumpSelectionForm).GetField("_scanComplete", flags)!.GetValue(form)!;
                    if (scanDone && complete == 0) complete = now;
                    if (mode == "ui-cancel" && now > 350)
                    {
                        cancelAt = now; beat.Stop(); form.Close(); return;
                    }
                    if (tree.Nodes.Count > 0 && now > 1000 + phase * 800 && phase < 6)
                    {
                        var sw = Stopwatch.StartNew();
                        var node = tree.Nodes[0];
                        if (phase == 1) node.Expand();
                        else if (phase == 3) { form.Size = new System.Drawing.Size(1000, 720); tree.SelectedNode = node; node.EnsureVisible(); }
                        else typeof(DumpSelectionForm).GetMethod("ToggleNode", flags)!.Invoke(form, new object[] { node });
                        toggles.Add(sw.Elapsed.TotalMilliseconds); phase++;
                    }
                    if (complete > 0 && phase >= 6 && postStart == 0) postStart = now;
                    if (postStart > 0 && postPhase < 9 && now - postStart > postPhase * 500)
                    {
                        var action = Stopwatch.StartNew();
                        var formType = typeof(DumpSelectionForm);
                        var modes = (ComboBox)formType.GetField("_modeCombo", flags)!.GetValue(form)!;
                        var ignore = (CheckBox)formType.GetField("_respectGitignore", flags)!.GetValue(form)!;
                        switch (postPhase)
                        {
                            case 0: formType.GetMethod("ToggleNode", flags)!.Invoke(form, new object[] { tree.Nodes[0] }); break;
                            case 1: modes.SelectedIndex = 1; break;
                            case 2: case 4: formType.GetMethod("ShowPreview", flags)!.Invoke(form, new object?[] { null, null }); break;
                            case 3: case 6: formType.GetMethod("ShowMap", flags)!.Invoke(form, null); break;
                            case 5: modes.SelectedIndex = 0; break;
                            case 7: ignore.Checked = true; break;
                            case 8: ignore.Checked = false; break;
                        }
                        toggles.Add(action.Elapsed.TotalMilliseconds);
                        postPhase++;
                    }
                    if ((postPhase == 9 && scanDone && now - postStart > 5000) || now > 45000)
                    { cancelAt = now; beat.Stop(); form.Close(); }
                };
                NativeTestWindow.Show(form);
                Application.Run(form);
                gaps.Sort(); toggles.Sort();
                File.WriteAllText(report, JsonSerializer.Serialize(new { mode, root, shownMs = shown, scanCompleteMs = complete, postCompletionActions = postPhase, finalScanComplete = (bool)typeof(DumpSelectionForm).GetField("_scanComplete", flags)!.GetValue(form)!, totalMs = clock.Elapsed.TotalMilliseconds, heartbeatP95Ms = gaps.Count > 0 ? gaps[(int)((gaps.Count - 1) * .95)] : 0, heartbeatMaxMs = gaps.LastOrDefault(), toggleP95Ms = toggles.Count > 0 ? toggles[(int)((toggles.Count - 1) * .95)] : 0, toggleMaxMs = toggles.LastOrDefault(), cancelMs = clock.Elapsed.TotalMilliseconds - cancelAt, samples = gaps.Count, privatePeak, managedPeak, handlesPeak, gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2) }, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                cfg.OutputTarget = OutputTarget.File; cfg.OutputDir = args[3]; cfg.Style = args.Length > 4 ? Enum.Parse<OutputStyle>(args[4]) : OutputStyle.Classic;
                cfg.SecretScan = args.Length > 5 ? Enum.Parse<SecretScanMode>(args[5]) : SecretScanMode.Warn;
                bool planted = Path.GetFileName(root).StartsWith("payload-planted-", StringComparison.Ordinal);
                bool reviewed = false;
                var result = new DumpEngine().Run(root, cfg, contentSelection: DumpContentSelection.FromMode(DumpSelectionMode.Thorough, cfg),
                    reviewSensitiveData: planted && cfg.SecretScan == SecretScanMode.Warn ? review =>
                    {
                        if (review.Items.Count != 1 || Directory.Exists(cfg.OutputDir))
                            throw new InvalidDataException("Planted review must contain one finding before publication.");
                        reviewed = true;
                        return SensitiveDataDecision.Keep;
                    }
                : null);
                var exportMs = clock.Elapsed.TotalMilliseconds;
                if (planted && (result.SecretFindingCount != 1 || cfg.SecretScan == SecretScanMode.Warn && !reviewed))
                    throw new InvalidDataException("The planted key was not reviewed/detected exactly once.");
                string? verifiedSha256 = null;
                if (cfg.Style == OutputStyle.Classic && (result.SecretFindingCount == 0 || planted) && result.OutputPath is not null)
                    verifiedSha256 = ClassicArtifactCheck.Verify(root, result.OutputPath, planted ? cfg.SecretScan : null);
                File.WriteAllText(report, JsonSerializer.Serialize(new { mode, root, totalMs = exportMs, result.FilesIncluded, result.OutputPath, result.SecretFindingCount, result.Cancelled, verifiedSha256, outputBytes = result.OutputPath is null ? 0 : new FileInfo(result.OutputPath).Length, privatePeak, managedPeak, handlesPeak, gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2) }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine(File.ReadAllText(report)); return 0;
        }
        catch (Exception ex) { File.WriteAllText(report, JsonSerializer.Serialize(new { mode, root, error = ex.ToString(), totalMs = clock.Elapsed.TotalMilliseconds, privatePeak, managedPeak, handlesPeak }, new JsonSerializerOptions { WriteIndented = true })); Console.Error.WriteLine(ex); return 1; }
    }
}
