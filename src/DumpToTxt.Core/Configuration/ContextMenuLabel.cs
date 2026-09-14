namespace DumpToTxt.Core;

/// <summary>
/// Computes the dynamic right-click label that reflects the user's chosen output, e.g.
/// "Dump into Clipboard" / "Dump into .md" / "Dump into .txt". The app updates the installed
/// context-menu verb's text from this whenever settings are saved, so the menu always shows
/// what a click will actually produce. Pure (no IO) so it is unit-testable.
/// </summary>
public static class ContextMenuLabel
{
    /// <summary>The main "Dump into …" verb label for the given config.</summary>
    public static string Main(DumpConfig cfg) => $"Dump into {Destination(cfg)}";

    /// <summary>The secondary "changed files" verb label (constant; behavior is git-aware at runtime).</summary>
    public const string Changed = "Dump changed files (since last commit)";

    private static string Destination(DumpConfig cfg) => cfg.OutputTarget switch
    {
        OutputTarget.Clipboard => "Clipboard",
        OutputTarget.Stdout => "Console",
        _ => OutputStyleCatalog.Extension(cfg.Style),
    };
}
