namespace DumpToTxt.Core;

/// <summary>Accepted review choices for one exact target, stored only in user settings.</summary>
public sealed class DumpReviewPreferences
{
    public OutputStyle Style { get; set; }
    public OutputTarget OutputTarget { get; set; }
    public string? OutputDir { get; set; }
    public bool ShowReviewBeforeDump { get; set; } = true;
    public DumpSelectionMode Mode { get; set; }
    public bool RespectGitignore { get; set; } = true;
    public bool UseDumpToTxtIgnore { get; set; } = true;
    public SecretScanMode SecretScan { get; set; } = SecretScanMode.Warn;
    public Dictionary<string, bool> PathOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static DumpReviewPreferences Capture(DumpConfig config, DumpContentSelection selection) => new()
    {
        Style = config.Style,
        OutputTarget = config.OutputTarget,
        OutputDir = config.OutputDir,
        ShowReviewBeforeDump = config.ShowReviewBeforeDump,
        Mode = config.LastSelectionMode,
        RespectGitignore = config.RespectGitignore,
        UseDumpToTxtIgnore = config.UseDumpToTxtIgnore,
        SecretScan = config.SecretScan,
        PathOverrides = new(selection.PathOverrides, StringComparer.OrdinalIgnoreCase),
    };

    public void Apply(DumpConfig config)
    {
        config.Style = Style;
        config.OutputTarget = OutputTarget;
        config.OutputDir = OutputDir;
        config.ShowReviewBeforeDump = ShowReviewBeforeDump;
        config.LastSelectionMode = Mode;
        config.RespectGitignore = RespectGitignore;
        config.UseDumpToTxtIgnore = UseDumpToTxtIgnore;
        config.SecretScan = SecretScan;
    }
}
