using System.Text.RegularExpressions;
using DumpToTxt.Core;

namespace DumpToTxt.Tests;

/// <summary>Round-trip + robustness for ConfigStore, using the pure Parse/Serialize/SaveTo/LoadFrom
/// seams so no real %APPDATA% config is touched.</summary>
public class ConfigStoreTests
{
    [Fact]
    public void RoundTrip_PreservesNewOutputFields()
    {
        var cfg = new DumpConfig
        {
            ExtSet = new() { ".cs", ".md" },
            DotFilesAllow = new() { ".gitignore" },
            ExcludeRegex = DumpConfig.DefaultExcludeRegex,
            Style = OutputStyle.Markdown,
            OutputTarget = OutputTarget.Clipboard,
            OutputDir = @"C:\dumps",
        };

        var back = ConfigStore.Parse(ConfigStore.Serialize(cfg));

        Assert.Equal(OutputStyle.Markdown, back.Style);
        Assert.Equal(OutputTarget.Clipboard, back.OutputTarget);
        Assert.Equal(@"C:\dumps", back.OutputDir);
        Assert.Equal(cfg.ExtSet, back.ExtSet);
        Assert.Equal(cfg.ExcludeRegex, back.ExcludeRegex);
    }

    [Fact]
    public void RoundTrip_PreservesReviewDefaults()
    {
        var cfg = DumpConfig.CreateDefault();
        cfg.Style = OutputStyle.MarkdownAi;
        cfg.ShowReviewBeforeDump = false;
        cfg.LastSelectionMode = DumpSelectionMode.Thorough;

        var back = ConfigStore.Parse(ConfigStore.Serialize(cfg));

        Assert.Equal(OutputStyle.MarkdownAi, back.Style);
        Assert.False(back.ShowReviewBeforeDump);
        Assert.Equal(DumpSelectionMode.Thorough, back.LastSelectionMode);
    }

    [Fact]
    public void Theme_RoundTrips_AndDefaultsToGraphite()
    {
        var blue = DumpConfig.CreateDefault();
        blue.Theme = UiThemeKind.Blue;

        Assert.Equal(UiThemeKind.Blue, ConfigStore.Parse(ConfigStore.Serialize(blue)).Theme);
        Assert.Equal(UiThemeKind.Graphite, ConfigStore.Parse("{}").Theme);
        Assert.Equal(UiThemeKind.Graphite, ConfigStore.Parse("""{ "Theme": "unknown" }""").Theme);
    }

    [Fact]
    public void CompletionSound_RoundTrips_AndDefaultsOn()
    {
        var silent = DumpConfig.CreateDefault();
        silent.PlayCompletionSound = false;

        Assert.False(ConfigStore.Parse(ConfigStore.Serialize(silent)).PlayCompletionSound);
        Assert.True(ConfigStore.Parse("{}").PlayCompletionSound);
    }

    [Fact]
    public void CopyRunPreferencesFrom_ChangesOnlyRunChoices()
    {
        var saved = DumpConfig.CreateDefault();
        saved.ExtSet = new() { ".cs" };
        saved.ExcludeGlobs = new() { "generated/**" };
        var used = DumpConfig.CreateDefault();
        used.ExtSet = new() { ".md" };
        used.ExcludeGlobs = new() { "other/**" };
        used.Style = OutputStyle.Docx;
        used.OutputTarget = OutputTarget.File;
        used.OutputDir = @"C:\exports";
        used.ShowReviewBeforeDump = false;
        used.LastSelectionMode = DumpSelectionMode.None;
        used.Theme = UiThemeKind.Blue;

        saved.CopyRunPreferencesFrom(used);

        Assert.Equal(new[] { ".cs" }, saved.ExtSet);
        Assert.Equal(new[] { "generated/**" }, saved.ExcludeGlobs);
        Assert.Equal(OutputStyle.Docx, saved.Style);
        Assert.Equal(@"C:\exports", saved.OutputDir);
        Assert.False(saved.ShowReviewBeforeDump);
        Assert.Equal(DumpSelectionMode.None, saved.LastSelectionMode);
        Assert.Equal(UiThemeKind.Graphite, saved.Theme);
    }

    [Fact]
    public void InstallerDoubleEscapedExcludeRegex_RoundTrips()
    {
        // Exactly what the Inno installer hand-writes: every backslash doubled for JSON.
        string json = @"{ ""ExtSet"": ["".cs""], ""ExcludeRegex"": ""\\\\(\\.git|\\.vs|node_modules|dist|build|obj)(\\\\|$)"" }";

        var cfg = ConfigStore.Parse(json);

        Assert.Equal(@"\\(\.git|\.vs|node_modules|dist|build|obj)(\\|$)", cfg.ExcludeRegex);
        _ = new Regex(cfg.ExcludeRegex); // compiles
    }

    [Theory]
    [InlineData("999")]   // out-of-range numeric -> undefined -> guarded back to Classic
    [InlineData("bogus")]
    [InlineData("")]
    public void Style_InvalidOrUndefined_FallsBackToClassic(string raw)
    {
        var cfg = ConfigStore.Parse(@"{ ""ExtSet"": ["".cs""], ""Style"": """ + raw + @""" }");
        Assert.Equal(OutputStyle.Classic, cfg.Style);
    }

    [Fact]
    public void Style_ValidName_Parses()
    {
        var cfg = ConfigStore.Parse(@"{ ""ExtSet"": ["".cs""], ""Style"": ""Markdown"" }");
        Assert.Equal(OutputStyle.Markdown, cfg.Style);
    }

    [Fact]
    public void OutputTarget_Garbage_FallsBackToFile()
    {
        var cfg = ConfigStore.Parse(@"{ ""ExtSet"": ["".cs""], ""OutputTarget"": ""nonsense"" }");
        Assert.Equal(OutputTarget.File, cfg.OutputTarget);
    }

    [Fact]
    public void EmptyExtSet_FallsBackToDefaults()
    {
        var cfg = ConfigStore.Parse(@"{ ""ExtSet"": [] }");
        Assert.Contains(".cs", cfg.ExtSet);
        Assert.Equal(DumpConfig.CreateDefault().ExtSet, cfg.ExtSet);
    }

    [Fact]
    public void LoadFrom_FirstExistingPathWins()
    {
        string a = Path.Combine(Path.GetTempPath(), "dtt-cfg-a-" + Guid.NewGuid().ToString("N") + ".json");
        string b = Path.Combine(Path.GetTempPath(), "dtt-cfg-b-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(a, @"{ ""ExtSet"": ["".cs""], ""Style"": ""Markdown"" }");
        File.WriteAllText(b, @"{ ""ExtSet"": ["".cs""], ""Style"": ""Xml"" }");
        try
        {
            Assert.Equal(OutputStyle.Markdown, ConfigStore.LoadFrom(a, b).Style);
            Assert.Equal(OutputStyle.Xml, ConfigStore.LoadFrom("nope.json", b).Style);
        }
        finally { File.Delete(a); File.Delete(b); }
    }

    [Fact]
    public void IgnoreFields_RoundTrip()
    {
        var cfg = new DumpConfig
        {
            ExtSet = new() { ".cs" },
            RespectGitignore = false,
            UseDumpToTxtIgnore = false,
            IncludeGlobs = new() { "src/**", "*.md" },
            ExcludeGlobs = new() { "**/*.gen.cs" },
            DetectBinary = false,
            MaxFileSizeBytes = 1024,
            MaxTotalSizeBytes = 5 * 1024 * 1024,
        };
        var back = ConfigStore.Parse(ConfigStore.Serialize(cfg));
        Assert.False(back.RespectGitignore);
        Assert.False(back.UseDumpToTxtIgnore);
        Assert.Equal(new[] { "src/**", "*.md" }, back.IncludeGlobs);
        Assert.Equal(new[] { "**/*.gen.cs" }, back.ExcludeGlobs);
        Assert.False(back.DetectBinary);
        Assert.Equal(1024, back.MaxFileSizeBytes);
        Assert.Equal(5 * 1024 * 1024, back.MaxTotalSizeBytes);
    }

    [Fact]
    public void OldConfig_MissingIgnoreFields_GetsDefaults()
    {
        // A legacy settings.json with none of the ignore/include fields must load with sane defaults (back-compat).
        var cfg = ConfigStore.Parse(@"{ ""ExtSet"": ["".cs""] }");
        Assert.True(cfg.RespectGitignore);
        Assert.True(cfg.UseDumpToTxtIgnore);
        Assert.True(cfg.DetectBinary);
        Assert.Empty(cfg.IncludeGlobs);
        Assert.Empty(cfg.ExcludeGlobs);
        Assert.Equal(0, cfg.MaxFileSizeBytes);
        Assert.Equal(0, cfg.MaxTotalSizeBytes);
    }

    [Fact]
    public void SaveTo_LoadFrom_RoundTrips()
    {
        string p = Path.Combine(Path.GetTempPath(), "dtt-cfg-" + Guid.NewGuid().ToString("N") + ".json");
        var cfg = new DumpConfig
        {
            ExtSet = new() { ".cs" },
            DotFilesAllow = new() { ".env" },
            ExcludeRegex = DumpConfig.DefaultExcludeRegex,
            Style = OutputStyle.Xml,
            OutputTarget = OutputTarget.Stdout,
            OutputDir = null,
        };
        try
        {
            ConfigStore.SaveTo(p, cfg);
            var back = ConfigStore.LoadFrom(p);
            Assert.Equal(OutputStyle.Xml, back.Style);
            Assert.Equal(OutputTarget.Stdout, back.OutputTarget);
            Assert.Null(back.OutputDir);
        }
        finally { File.Delete(p); }
    }
}
