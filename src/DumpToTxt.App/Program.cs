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

            Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{result.OutputPath}\"",
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "DumpToTxt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
