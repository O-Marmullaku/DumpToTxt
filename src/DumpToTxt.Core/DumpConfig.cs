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

    /// <summary>Show the review workspace before target-path dumps. Shift always forces review.</summary>
    public bool ShowReviewBeforeDump { get; set; } = true;

    /// <summary>The remembered starting selection for the next review or skipped-review run.</summary>
    public DumpSelectionMode LastSelectionMode { get; set; } = DumpSelectionMode.Basic;

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

    // ---- P5: token counting ----

    /// <summary>BPE encoding for token counts. Defaults to <see cref="TokenEncoding.O200kBase"/> (current era).</summary>
    public TokenEncoding TokenEncoding { get; set; } = TokenEncoding.O200kBase;

    /// <summary>Token budget; when &gt; 0 the dump is flagged/marked once the total exceeds it (warn-only,
    /// no content dropped). 0 = no budget.</summary>
    public long MaxTokens { get; set; }

    // ---- P6: secret scan ----

    /// <summary>What the dump does when a secret is detected. Defaults to <see cref="SecretScanMode.Warn"/>
    /// (flag + count, content untouched). Redact/Skip apply to the non-Classic styles only — Classic output
    /// stays byte-identical; findings still surface in the non-Classic formatters + the post-dump notice.</summary>
    public SecretScanMode SecretScan { get; set; } = SecretScanMode.Warn;

    /// <summary>Enable the generic high-entropy-string detector (catches secrets no named rule matches).
    /// Off by default — it is the noisy detector (hashes / base64 assets can trip it).</summary>
    public bool SecretScanEntropy { get; set; }

    /// <summary>Regex patterns that suppress a finding whose detected secret VALUE matches one (for key=value
    /// rules that is the narrowed value span, not the key name or surrounding line). False-positive allowlist;
    /// empty by default.</summary>
    public List<string> SecretAllowlist { get; set; } = new();

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
        ShowReviewBeforeDump = true,
        LastSelectionMode = DumpSelectionMode.Basic,
        RespectGitignore = true,
        UseDumpToTxtIgnore = true,
        IncludeGlobs = new(),
        ExcludeGlobs = new(),
        DetectBinary = true,
        MaxFileSizeBytes = 0,
        MaxTotalSizeBytes = 0,
        TokenEncoding = TokenEncoding.O200kBase,
        MaxTokens = 0,
        SecretScan = SecretScanMode.Warn,
        SecretScanEntropy = false,
        SecretAllowlist = new(),
    };

    public DumpConfig Clone() => new()
    {
        ExtSet = new List<string>(ExtSet),
        DotFilesAllow = new List<string>(DotFilesAllow),
        ExcludeRegex = ExcludeRegex,
        Style = Style,
        OutputTarget = OutputTarget,
        OutputDir = OutputDir,
        ShowReviewBeforeDump = ShowReviewBeforeDump,
        LastSelectionMode = LastSelectionMode,
        RespectGitignore = RespectGitignore,
        UseDumpToTxtIgnore = UseDumpToTxtIgnore,
        IncludeGlobs = new List<string>(IncludeGlobs),
        ExcludeGlobs = new List<string>(ExcludeGlobs),
        DetectBinary = DetectBinary,
        MaxFileSizeBytes = MaxFileSizeBytes,
        MaxTotalSizeBytes = MaxTotalSizeBytes,
        TokenEncoding = TokenEncoding,
        MaxTokens = MaxTokens,
        SecretScan = SecretScan,
        SecretScanEntropy = SecretScanEntropy,
        SecretAllowlist = new List<string>(SecretAllowlist),
        OnlyGitChanged = OnlyGitChanged,
    };

    /// <summary>Copies only choices made in the run workspace, leaving all content/safety settings intact.</summary>
    public void CopyRunPreferencesFrom(DumpConfig source)
    {
        Style = source.Style;
        OutputTarget = source.OutputTarget;
        OutputDir = source.OutputDir;
        ShowReviewBeforeDump = source.ShowReviewBeforeDump;
        LastSelectionMode = source.LastSelectionMode;
    }
}
