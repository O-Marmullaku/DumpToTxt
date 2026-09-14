namespace DumpToTxt.Core;

/// <summary>A named, partial config overlay applied on top of the resolved config at dump time
/// (e.g. via the <c>--preset</c> CLI flag or the GUI Presets tab). A preset only sets the few knobs
/// that define it; everything else flows through from the resolved config.</summary>
public sealed class Preset
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    /// <summary>Mutates the resolved config to express this preset. No IO.</summary>
    public required Action<DumpConfig> Apply { get; init; }
}

/// <summary>Built-in partial overlays exposed by settings and the --preset argument.
/// Explorer registration deliberately remains a single action.</summary>
public static class Presets
{
    /// <summary>The original tool's experience: Classic layout, strict legacy file selection
    /// (no .gitignore / .dumptotxtignore / binary skipping), default legible types.</summary>
    public static readonly Preset Classic = new()
    {
        Name = "Classic",
        Description = "Clean text · Classic layout · ignore and binary checks off.",
        Apply = c =>
        {
            c.Style = OutputStyle.Classic;
            c.LastSelectionMode = DumpSelectionMode.Basic;
            c.RespectGitignore = false;
            c.UseDumpToTxtIgnore = false;
            c.DetectBinary = false;
            c.IncludeGlobs = new();
            c.ExtSet = DumpConfig.CreateDefault().ExtSet;
        },
    };

    /// <summary>Web / UI source only — restricts legible types to the front-end stack.</summary>
    public static readonly Preset Frontend = new()
    {
        Name = "Frontend",
        Description = "Web project · HTML, CSS, JavaScript, TypeScript, and related files.",
        Apply = c =>
        {
            c.ExtSet = new() { ".js", ".jsx", ".ts", ".tsx", ".vue", ".svelte", ".css", ".scss", ".html", ".json" };
            c.LastSelectionMode = DumpSelectionMode.Basic;
            c.IncludeGlobs = new();
        },
    };

    /// <summary>Documentation only — restricts legible types to common doc formats.</summary>
    public static readonly Preset DocsOnly = new()
    {
        Name = "Docs only",
        Description = "Documentation · Markdown, text, reStructuredText, and AsciiDoc files.",
        Apply = c =>
        {
            c.ExtSet = new() { ".md", ".mdx", ".txt", ".rst", ".adoc" };
            c.LastSelectionMode = DumpSelectionMode.Basic;
            c.IncludeGlobs = new();
        },
    };

    /// <summary>Only files changed since the last git commit. Falls back to a normal dump outside a repo.</summary>
    public static readonly Preset ChangedFiles = new()
    {
        Name = "Changed files",
        Description = "Changed files only · Uses Git changes when available.",
        Apply = c => c.OnlyGitChanged = true,
    };

    public static readonly IReadOnlyList<Preset> All = new[] { Classic, Frontend, DocsOnly, ChangedFiles };

    /// <summary>Looks up a preset by name (case-insensitive), or null if unknown.</summary>
    public static Preset? ByName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}
