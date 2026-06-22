using System.Diagnostics;
using DumpToTxt.Core;

namespace DumpToTxt.App;

internal static class Program
{
    /// <summary>
    /// Entry point. With no path argument, opens the settings GUI (legacy behavior).
    /// With a path, dumps that file/folder and opens the result in Notepad.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        string? target = args.Length > 0 ? args[0] : null;
        if (string.IsNullOrWhiteSpace(target))
        {
            Application.Run(new SettingsForm());
            return;
        }

        try
        {
            var cfg = ConfigStore.Load();
            var result = new DumpEngine().Run(target, cfg);

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
}
