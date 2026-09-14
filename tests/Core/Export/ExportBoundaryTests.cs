using System.Text;
using System.Text.Json;
using DumpToTxt.Core;

namespace DumpToTxt.Tests;

public sealed class ExportBoundaryTests
{
    [Fact]
    public void FileGrowthAfterEnumerationUsesOpenedSizeAndEnforcesTheByteCap()
    {
        using var fixture = new Fixture();
        string input = Path.Combine(fixture.Root, "input.txt");
        File.WriteAllText(input, "a", new UTF8Encoding(false));
        var cfg = fixture.Config(OutputStyle.Json, OutputTarget.Stdout);
        cfg.MaxFileSizeBytes = 32;
        bool grew = false;

        var result = new DumpEngine().Run(fixture.Root, cfg, progress: new InlineProgress(p =>
        {
            if (p.Phase != "Reading and checking" || grew) return;
            Assert.Equal("input.txt", p.Path);
            Assert.Equal(1L, new FileInfo(input).Length);
            File.WriteAllText(input, new string('x', 512), new UTF8Encoding(false));
            grew = true;
        }));

        Assert.True(grew, "The source must grow after enumeration and before the engine opens it.");
        using var json = JsonDocument.Parse(result.Text);
        var file = Assert.Single(json.RootElement.GetProperty("fileList").EnumerateArray());
        Assert.Equal(512L, file.GetProperty("size").GetInt64());
        Assert.True(file.GetProperty("truncated").GetBoolean());
        Assert.Equal(new string('x', 32), file.GetProperty("content").GetString());
    }

    [Fact]
    public void NestedFileSharingTheOutputBasenameIsIncludedWhenOutputIsElsewhere()
    {
        using var fixture = new Fixture();
        string nested = Directory.CreateDirectory(Path.Combine(fixture.Root, "nested")).FullName;
        string rootName = new DirectoryInfo(fixture.Root).Name;
        DateTime now = DateTime.Now;
        var sources = new Dictionary<string, string>();
        for (int offset = -2; offset <= 2; offset++)
        {
            string name = $"{rootName}-dump-{now.AddMinutes(offset):HH-mm}.txt";
            string content = $"preserved nested source marker {offset + 2}\n";
            sources.Add(name, content);
            File.WriteAllText(Path.Combine(nested, name), content, new UTF8Encoding(false));
        }
        var cfg = fixture.Config(OutputStyle.Classic, OutputTarget.File);

        var result = new DumpEngine().Run(fixture.Root, cfg);

        Assert.NotNull(result.OutputPath);
        string outputPath = result.OutputPath!;
        string basename = Path.GetFileName(outputPath);
        Assert.True(sources.ContainsKey(basename), "The output basename must have a matching nested source fixture.");
        Assert.Equal(Path.GetFullPath(fixture.Output), Path.GetDirectoryName(outputPath));
        Assert.Equal(5, result.FilesIncluded);
        string output = File.ReadAllText(outputPath);
        foreach (var (name, content) in sources)
        {
            Assert.Contains(name, output, StringComparison.Ordinal);
            Assert.Contains(content, output, StringComparison.Ordinal);
            Assert.Equal(content, File.ReadAllText(Path.Combine(nested, name)));
        }
        Assert.Single(Directory.GetFiles(fixture.Output));
    }

    [Fact]
    public void SingleFileInvocationInventoriesOnlyTheChosenFile()
    {
        using var fixture = new Fixture();
        string chosen = Path.Combine(fixture.Root, "chosen.txt");
        File.WriteAllText(chosen, "chosen file body", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(fixture.Root, "sibling.txt"), "sibling body", new UTF8Encoding(false));
        string neighbors = Directory.CreateDirectory(Path.Combine(fixture.Root, "neighbors")).FullName;
        File.WriteAllText(Path.Combine(neighbors, "nested.txt"), "nested neighbor body", new UTF8Encoding(false));

        var result = new DumpEngine().Run(chosen, fixture.Config(OutputStyle.Json, OutputTarget.Stdout));

        using var json = JsonDocument.Parse(result.Text);
        var file = Assert.Single(json.RootElement.GetProperty("fileList").EnumerateArray());
        Assert.Equal("chosen.txt", file.GetProperty("path").GetString());
        Assert.Equal("chosen file body", file.GetProperty("content").GetString());
        Assert.Equal(1, result.FilesIncluded);
        string structure = json.RootElement.GetProperty("directoryStructure").GetString()!;
        Assert.Contains("chosen.txt", structure, StringComparison.Ordinal);
        Assert.DoesNotContain("sibling.txt", structure, StringComparison.Ordinal);
        Assert.DoesNotContain("neighbors", structure, StringComparison.Ordinal);
        Assert.DoesNotContain("nested.txt", structure, StringComparison.Ordinal);
    }

    private sealed class InlineProgress(Action<DumpProgress> report) : IProgress<DumpProgress>
    {
        public void Report(DumpProgress value) => report(value);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _work = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "dtt-boundary-audit-" + Guid.NewGuid().ToString("N")));
        public string Root => Path.Combine(_work, "source");
        public string Output => Path.Combine(_work, "output");
        public Fixture() => Directory.CreateDirectory(Root);
        public DumpConfig Config(OutputStyle style, OutputTarget target)
        {
            var cfg = DumpConfig.CreateDefault();
            cfg.Style = style;
            cfg.OutputTarget = target;
            cfg.OutputDir = Output;
            cfg.SecretScan = SecretScanMode.Off;
            return cfg;
        }
        public void Dispose()
        {
            string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            if (!_work.StartsWith(temp, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to delete a fixture outside the temporary directory.");
            Directory.Delete(_work, true);
        }
    }
}
