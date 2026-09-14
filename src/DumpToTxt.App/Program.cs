using System.Diagnostics;
using System.IO.Compression;
using DumpToTxt.Core;

namespace DumpToTxt.App;

internal static class Program
{
    /// <summary>
    /// Entry point. With no path argument, opens the settings GUI (legacy behavior). With a path,
    /// dumps that file/folder. Optional flags: <c>--preset &lt;name&gt;</c> overlays a named preset,
    /// <c>--changed</c> restricts to git-changed files. These back the right-click menu verbs.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        string? target, presetName;
        bool changed;
        try { (target, presetName, changed) = ParseArgs(args); }
        catch (ArgumentException argumentError)
        {
            Environment.ExitCode = 2;
            Console.Error.WriteLine(argumentError.Message);
            MessageBox.Show(argumentError.Message, "DumpToTxt", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        try
        {
            var userConfig = ConfigStore.Load();
            UiTheme.Apply(userConfig.Theme);
            if (string.IsNullOrWhiteSpace(target))
            {
                Application.Run(new SettingsForm());
                return;
            }

            // Resolve MERGES defaults → machine → user → per-folder .dumptotxt.json (nearest wins).
            var cfg = ConfigStore.Resolve(target!);
            if (presetName is not null) Presets.ByName(presetName)?.Apply(cfg);
            if (changed) cfg.OnlyGitChanged = true;

            DumpContentSelection selection;
            bool forceReview = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
            if (cfg.ShowReviewBeforeDump || forceReview)
            {
                using var selectionForm = new DumpSelectionForm(target!, cfg);
                if (selectionForm.ShowDialog() != DialogResult.OK
                    || selectionForm.Selection is null
                    || selectionForm.UpdatedConfig is null) return;
                cfg = selectionForm.UpdatedConfig;
                selection = selectionForm.Selection;
                try { ConfigStore.SaveRunPreferences(cfg); }
                catch (Exception preferenceError) when (preferenceError is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    MessageBox.Show($"Your choices could not be remembered. This dump will still use them.\n\n{preferenceError.Message}",
                        "DumpToTxt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                selection = DumpContentSelection.FromMode(cfg.LastSelectionMode, cfg);
            }

            using var exporting = new ExportProgressForm(target!, cfg, selection,
                cfg.OutputTarget == OutputTarget.Stdout ? Console.Out : null);
            exporting.ShowDialog();
            if (exporting.Failure is { } failure) throw failure;
            var result = exporting.Result ?? throw new InvalidOperationException("The dump did not produce a result.");
            if (result.Cancelled) return;

            bool hasSecrets = result.SecretFindingCount > 0;
            bool hasWarning = hasSecrets || result.TokenBudgetExceeded;
            string notice = CompletionNotice(result, cfg);

            switch (cfg.OutputTarget)
            {
                case OutputTarget.Clipboard:
                    Clipboard.SetText(string.IsNullOrEmpty(result.Text) ? " " : result.Text);
                    CompletionSound.Play(cfg.PlayCompletionSound);
                    MessageBox.Show(
                        $"Copied {result.FilesIncluded} file(s) to the clipboard." + notice,
                        "DumpToTxt", MessageBoxButtons.OK,
                        hasWarning ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                    break;

                case OutputTarget.Stdout:
                    Console.Out.Flush();
                    if (hasWarning) Console.Error.WriteLine(notice.Trim());
                    break;

                case OutputTarget.File:
                default:
                    CompletionSound.Play(cfg.PlayCompletionSound);
                    if (hasWarning)
                        MessageBox.Show(
                            $"Dump written to:\n{result.OutputPath}{notice}",
                            "DumpToTxt — export notice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    if (ShouldConfirmOpen(result.OutputPath!)
                        && MessageBox.Show($"Dump saved to:\n{result.OutputPath}\n\nThis is a large result. Open it in the associated app now?",
                            "DumpToTxt", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        break;
                    // Open with the user's associated app (Word for .docx, editor for text formats).
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = result.OutputPath!,
                            UseShellExecute = true,
                        });
                    }
                    catch (Exception viewerEx)
                    {
                        MessageBox.Show(
                            $"Dump written to:\n{result.OutputPath}\n\nCould not open it: {viewerEx.Message}",
                            "DumpToTxt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            MessageBox.Show(ex.Message, "DumpToTxt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static bool ShouldConfirmOpen(string path)
    {
        const long limit = 32L * 1024 * 1024;
        if (new FileInfo(path).Length > limit) return true;
        if (!string.Equals(Path.GetExtension(path), ".docx", StringComparison.OrdinalIgnoreCase)) return false;
        using var package = ZipFile.OpenRead(path);
        // Compression can hide a huge Word workload behind a small on-disk package.
        return (package.GetEntry("word/document.xml")?.Length ?? long.MaxValue) > limit;
    }

    private static string CompletionNotice(DumpResult result, DumpConfig config)
    {
        string notice = result.SecretFindingCount > 0
            ? $"\n\n{result.SecretFindingCount} sensitive value(s) detected in {result.FilesWithSecrets} file(s)"
              + $" (action: {result.EffectiveSecretScan})."
              + (result.FilesContentOmitted > 0
                  ? $"\n{result.FilesContentOmitted} file(s) had their ENTIRE content omitted (Skip)." : "")
            : "";
        if (result.TokenBudgetExceeded)
            notice += $"\n\nToken budget exceeded: {result.TotalTokens:N0} body tokens; budget {config.MaxTokens:N0}. The selected content was preserved.";
        return notice;
    }

    /// <summary>Pulls the first non-flag argument as the target, plus <c>--preset &lt;name&gt;</c>
    /// (or <c>--preset=name</c>) and <c>--changed</c>.</summary>
    private static (string? target, string? preset, bool changed) ParseArgs(string[] args)
    {
        string? target = null, preset = null;
        bool changed = false;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (string.Equals(a, "--changed", StringComparison.OrdinalIgnoreCase))
                changed = true;
            else if (string.Equals(a, "--preset", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("Choose a preset name after --preset.");
                preset = args[++i];
            }
            else if (a.StartsWith("--preset=", StringComparison.OrdinalIgnoreCase))
                preset = a.Substring("--preset=".Length);
            else if (a.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unknown option: {a}");
            else if (target is not null)
                throw new ArgumentException("Choose one file or folder for each dump.");
            else target = a;
        }
        if (preset is not null && Presets.ByName(preset) is null)
            throw new ArgumentException($"Unknown preset: {preset}");
        return (target, preset, changed);
    }
}
