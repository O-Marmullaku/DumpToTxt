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

/// <summary>The built-in presets surfaced in the GUI and (once wired by the installer) the right-click
/// submenu. These mirror the North Star's named presets.</summary>
public static class Presets
{
    /// <summary>The original tool's experience: Classic layout, strict legacy file selection
    /// (no .gitignore / .dumptotxtignore / binary skipping), default legible types.</summary>
    public static readonly Preset Classic = new()
    {
        Name = "Classic",
        Description = "Original text output without ignore files or binary checks.",
        Apply = c =>
        {
            c.Style = OutputStyle.Classic;
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
        Description = "Web files: .js, .jsx, .ts, .tsx, .vue, .svelte, .css, .scss, .html, .json.",
        Apply = c =>
        {
            c.ExtSet = new() { ".js", ".jsx", ".ts", ".tsx", ".vue", ".svelte", ".css", ".scss", ".html", ".json" };
            c.IncludeGlobs = new();
        },
    };

    /// <summary>Documentation only — restricts legible types to common doc formats.</summary>
    public static readonly Preset DocsOnly = new()
    {
        Name = "Docs only",
        Description = "Document files: .md, .mdx, .txt, .rst, .adoc.",
        Apply = c =>
        {
            c.ExtSet = new() { ".md", ".mdx", ".txt", ".rst", ".adoc" };
            c.IncludeGlobs = new();
        },
    };

    /// <summary>Only files changed since the last git commit. Falls back to a normal dump outside a repo.</summary>
    public static readonly Preset ChangedFiles = new()
    {
        Name = "Changed files",
        Description = "Files changed since the last Git commit. Uses a normal dump outside a Git repository.",
        Apply = c => c.OnlyGitChanged = true,
    };

    public static readonly IReadOnlyList<Preset> All = new[] { Classic, Frontend, DocsOnly, ChangedFiles };

    /// <summary>Looks up a preset by name (case-insensitive), or null if unknown.</summary>
    public static Preset? ByName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}
