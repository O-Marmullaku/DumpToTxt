namespace DumpToTxt.Core;

/// <summary>
/// All settings that drive a dump. The classic-relevant fields
/// (<see cref="ExtSet"/>, <see cref="DotFilesAllow"/>, <see cref="ExcludeRegex"/>)
/// mirror the legacy settings.json shape so existing configs keep working.
/// </summary>
public sealed class DumpConfig
{
    /// <summary>Default exclude regex (matches the legacy PowerShell default).</summary>
    public const string DefaultExcludeRegex = @"\\(\.git|\.vs|node_modules|dist|build|bin|obj)(\\|$)";

    /// <summary>File extensions whose full contents get printed (lowercase, with dot).</summary>
    public List<string> ExtSet { get; set; } = new();

    /// <summary>Dotfiles printed despite having no whitelisted extension (matched by full name).</summary>
    public List<string> DotFilesAllow { get; set; } = new();

    /// <summary>Backslash-oriented path regex; any full path matching it is skipped.</summary>
    public string ExcludeRegex { get; set; } = DefaultExcludeRegex;

    /// <summary>Output format. Defaults to <see cref="OutputStyle.Classic"/>.</summary>
    public OutputStyle Style { get; set; } = OutputStyle.Classic;

    /// <summary>Where the finished dump is delivered. Defaults to <see cref="OutputTarget.File"/> (legacy).</summary>
    public OutputTarget OutputTarget { get; set; } = OutputTarget.File;

    /// <summary>Directory for file output. Null = the Desktop (legacy behavior).</summary>
    public string? OutputDir { get; set; }

    public static DumpConfig CreateDefault() => new()
    {
        ExtSet = new()
        {
            ".html", ".css", ".js", ".ts", ".json", ".md", ".txt",
            ".php", ".py", ".cs", ".xml", ".yml", ".yaml",
        },
        DotFilesAllow = new() { ".gitignore", ".gitattributes", ".editorconfig", ".env", ".env.example" },
        ExcludeRegex = DefaultExcludeRegex,
        Style = OutputStyle.Classic,
        OutputTarget = OutputTarget.File,
        OutputDir = null,
    };

    public DumpConfig Clone() => new()
    {
        ExtSet = new List<string>(ExtSet),
        DotFilesAllow = new List<string>(DotFilesAllow),
        ExcludeRegex = ExcludeRegex,
        Style = Style,
        OutputTarget = OutputTarget,
        OutputDir = OutputDir,
    };
}
