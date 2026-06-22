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

    // ---- P3: ignore / include engine ----

    /// <summary>Honor a root <c>.gitignore</c> when gathering (additive with the other layers).</summary>
    public bool RespectGitignore { get; set; } = true;

    /// <summary>Honor a root <c>.dumptotxtignore</c> when gathering (gitignore syntax).</summary>
    public bool UseDumpToTxtIgnore { get; set; } = true;

    /// <summary>Include globs (gitignore syntax). When non-empty, a file is packed only if it matches one.</summary>
    public List<string> IncludeGlobs { get; set; } = new();

    /// <summary>Exclude globs (gitignore syntax); layered on top of the other exclusion sources.</summary>
    public List<string> ExcludeGlobs { get; set; } = new();

    /// <summary>Detect binary files (NUL-byte sniff) and skip their content with a marker.</summary>
    public bool DetectBinary { get; set; } = true;

    /// <summary>Per-file content cap in bytes; content beyond it is truncated with a marker. 0 = unlimited.</summary>
    public long MaxFileSizeBytes { get; set; }

    /// <summary>Total content cap in bytes across the whole dump; 0 = unlimited.</summary>
    public long MaxTotalSizeBytes { get; set; }

    /// <summary>Transient (NOT persisted to settings.json): when true the dump is restricted to files
    /// changed since the last git commit. Set by the "Changed files" preset / <c>--changed</c>; if the
    /// target is not inside a git repo the engine falls back to a normal dump.</summary>
    public bool OnlyGitChanged { get; set; }

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
        RespectGitignore = true,
        UseDumpToTxtIgnore = true,
        IncludeGlobs = new(),
        ExcludeGlobs = new(),
        DetectBinary = true,
        MaxFileSizeBytes = 0,
        MaxTotalSizeBytes = 0,
    };

    public DumpConfig Clone() => new()
    {
        ExtSet = new List<string>(ExtSet),
        DotFilesAllow = new List<string>(DotFilesAllow),
        ExcludeRegex = ExcludeRegex,
        Style = Style,
        OutputTarget = OutputTarget,
        OutputDir = OutputDir,
        RespectGitignore = RespectGitignore,
        UseDumpToTxtIgnore = UseDumpToTxtIgnore,
        IncludeGlobs = new List<string>(IncludeGlobs),
        ExcludeGlobs = new List<string>(ExcludeGlobs),
        DetectBinary = DetectBinary,
        MaxFileSizeBytes = MaxFileSizeBytes,
        MaxTotalSizeBytes = MaxTotalSizeBytes,
        OnlyGitChanged = OnlyGitChanged,
    };
}
