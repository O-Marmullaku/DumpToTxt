using DumpToTxt.Core;
using Microsoft.Win32;

namespace DumpToTxt.App;

/// <summary>
/// Best-effort label sync for existing per-user Explorer commands. Never creates registration
/// or changes machine-wide keys. The administrator installer supplies its own static label;
/// a settings save must not require elevation or create an additional visible command.
/// </summary>
internal static class ContextMenu
{
    // Existing per-user verb roots (folder, folder-background, files).
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
