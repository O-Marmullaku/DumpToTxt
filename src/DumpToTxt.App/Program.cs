using System.Diagnostics;
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

        var (target, presetName, changed) = ParseArgs(args);
        if (string.IsNullOrWhiteSpace(target))
        {
            Application.Run(new SettingsForm());
            return;
        }

        try
        {
            // Resolve MERGES defaults → machine → user → per-folder .dumptotxt.json (nearest wins).
            var cfg = ConfigStore.Resolve(target!);
            if (presetName is not null) Presets.ByName(presetName)?.Apply(cfg);
            if (changed) cfg.OnlyGitChanged = true;

            var result = new DumpEngine().Run(target!, cfg);

            switch (cfg.OutputTarget)
            {
                case OutputTarget.Clipboard:
                    Clipboard.SetText(string.IsNullOrEmpty(result.Text) ? " " : result.Text);
                    MessageBox.Show(
                        $"Copied {result.FilesIncluded} file(s) to the clipboard.",
                        "DumpToTxt", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;

                case OutputTarget.Stdout:
                    Console.Out.Write(result.Text);
                    Console.Out.Flush();
                    break;

                case OutputTarget.File:
                default:
                    // Open the written file; surface its path if the viewer can't launch.
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "notepad.exe",
                            Arguments = $"\"{result.OutputPath}\"",
                            UseShellExecute = true,
                        });
                    }
                    catch (Exception viewerEx)
                    {
                        MessageBox.Show(
                            $"Dump written to:\n{result.OutputPath}\n\nCould not open Notepad: {viewerEx.Message}",
                            "DumpToTxt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "DumpToTxt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
                if (i + 1 < args.Length) preset = args[++i];
            }
            else if (a.StartsWith("--preset=", StringComparison.OrdinalIgnoreCase))
                preset = a.Substring("--preset=".Length);
            else if (!a.StartsWith("--", StringComparison.Ordinal) && target is null)
                target = a;
        }
        return (target, preset, changed);
    }
}
