using DumpToTxt.Core;

namespace DumpToTxt.Tests;

public class DumpEngineTests
{
    [Fact]
    public void SafeName_ReplacesInvalidChars()
    {
        Assert.Equal("a_b", DumpEngine.SafeName("a/b"));
        Assert.Equal("a_b", DumpEngine.SafeName("a:b"));
        Assert.Equal("dump", DumpEngine.SafeName("   "));
        Assert.Equal("ok", DumpEngine.SafeName("ok"));
    }

    [Fact]
    public void DefaultConfig_HasExpectedDefaults()
    {
        var cfg = DumpConfig.CreateDefault();
        Assert.Contains(".cs", cfg.ExtSet);
        Assert.Contains(".md", cfg.ExtSet);
        Assert.Equal(OutputStyle.Classic, cfg.Style);
        Assert.Equal(DumpConfig.DefaultExcludeRegex, cfg.ExcludeRegex);
    }

    [Fact]
    public void Run_Classic_IncludesLegible_ExcludesIgnoredAndBinary()
    {
        string work = Path.Combine(Path.GetTempPath(), "dtt-test-" + Guid.NewGuid().ToString("N"));
        string src = Path.Combine(work, "src");
        string outDir = Path.Combine(work, "_out");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(Path.Combine(src, "node_modules"));

        File.WriteAllText(Path.Combine(src, "app.cs"), "class A {}");
        File.WriteAllText(Path.Combine(src, "data.bin"), "BINARYDATA");
        File.WriteAllText(Path.Combine(src, "node_modules", "lib.js"), "var x = 1;");

        try
        {
            var result = new DumpEngine().Run(src, DumpConfig.CreateDefault(), outDir);

            Assert.True(File.Exists(result.OutputPath));
            string text = File.ReadAllText(result.OutputPath);

            Assert.Contains("===== DIRECTORY LIST (filtered) =====", text);
            Assert.Contains("===== FILE CONTENTS (LEGIBLE ONLY) =====", text);
            Assert.Contains("class A {}", text);       // legible .cs printed
            Assert.DoesNotContain("BINARYDATA", text); // .bin not whitelisted
            Assert.DoesNotContain("var x = 1;", text); // node_modules excluded
            Assert.Equal(1, result.FilesIncluded);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_MissingPath_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), "dtt-nope-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<FileNotFoundException>(() => new DumpEngine().Run(missing, DumpConfig.CreateDefault()));
    }
}
