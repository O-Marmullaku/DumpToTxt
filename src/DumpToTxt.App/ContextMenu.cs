using DumpToTxt.Core;
using Microsoft.Win32;

namespace DumpToTxt.App;

/// <summary>
/// Best-effort sync of the per-user (HKCU) right-click verb labels to the current output choice, so the
/// main "Dump into …" entry tracks the chosen style/target. It updates ONLY verbs that already exist
/// (created by the installer or a future per-user registration) — it never creates keys and never touches
/// HKLM, so it is a harmless no-op when the menu isn't installed per-user. The full menu design (both
/// verbs, the --preset/--changed commands) lives in docs/to-do-for-human.md.
/// </summary>
internal static class ContextMenu
{
    // Per-user verb roots the installer is expected to create (folder, folder-background, files).
    private static readonly string[] MainVerbKeys =
    {
        @"Software\Classes\Directory\shell\DumpToTxt",
        @"Software\Classes\Directory\Background\shell\DumpToTxt",
        @"Software\Classes\*\shell\DumpToTxt",
    };

    public static void TrySyncLabels(DumpConfig cfg)
    {
        string label = ContextMenuLabel.Main(cfg);
        foreach (var key in MainVerbKeys)
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(key, writable: true);
                if (k is null) continue;             // verb not installed per-user: skip
                k.SetValue("MUIVerb", label);
                k.SetValue(null, label);             // (Default) too, for shells that read it
            }
            catch { /* best effort — never block a settings save */ }
        }
    }
}
