using System.Diagnostics;
using System.Text;
using DumpToTxt.Core;

namespace DumpToTxt.Tests;

/// <summary>The Repomix-style and Gitingest-style layouts and presets reproduce those tools' defaults.</summary>
[Collection("Git environment")]
public sealed class ToolLayoutTests : IDisposable
{
    private static readonly UTF8Encoding NoBom = new(false);
    private static readonly string Sep = new('=', 48);
    private readonly string root = Path.Combine(Path.GetTempPath(), "dtt-tools-" + Guid.NewGuid().ToString("N"));

    public ToolLayoutTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
        catch (IOException) { }
    }

    private void Put(string relative, string content)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, NoBom);
    }

    private string Run(DumpConfig cfg, DumpSelectionMode mode = DumpSelectionMode.Thorough)
    {
        cfg.OutputTarget = OutputTarget.Stdout;
        return new DumpEngine().Run(root, cfg, contentSelection: DumpContentSelection.FromMode(mode, cfg)).Text;
    }

    private static DumpConfig Styled(OutputStyle style)
    {
        var cfg = DumpConfig.CreateDefault();
        cfg.Style = style;
        return cfg;
    }

    [Theory]
    [InlineData(OutputStyle.Repomix, OutputFormatKind.Xml, ".xml", "Repomix-style")]
    [InlineData(OutputStyle.Gitingest, OutputFormatKind.Text, ".txt", "Gitingest-style")]
    public void Catalog_OffersTheToolLayouts(OutputStyle style, OutputFormatKind format, string extension, string layout)
    {
        Assert.Equal(format, OutputStyleCatalog.Format(style));
        Assert.Equal(extension, OutputStyleCatalog.Extension(style));
        Assert.Equal(layout, OutputStyleCatalog.LayoutName(style));
        Assert.Contains(style, OutputStyleCatalog.Layouts(format));
    }

    [Fact]
    public void Repomix_MatchesTheDefaultTemplate()
    {
        Put("README.md", "  # Hi\n\n");
        Put(Path.Combine("src", "main.ts"), "x\n");
        File.WriteAllBytes(Path.Combine(root, "a.png"), [0x89, 0x50, 0x4E, 0x47, 0, 0, 1, 2]);

        string expected = """
            This file is a merged representation of the entire codebase, combined into a single document by DumpToTxt.

            <file_summary>
            This section contains a summary of this file.

            <purpose>
            This file contains a packed representation of the entire repository's contents.
            It is designed to be easily consumable by AI systems for analysis, code review,
            or other automated processes.
            </purpose>

            <file_format>
            The content is organized as follows:
            1. This summary section
            2. Repository information
            3. Directory structure
            4. Repository files (if enabled)
            5. Multiple file entries, each consisting of:
              - File path as an attribute
              - Full contents of the file
            </file_format>

            <usage_guidelines>
            - This file should be treated as read-only. Any changes should be made to the
              original repository files, not this packed version.
            - When processing this file, use the file path to distinguish
              between different files in the repository.
            - Be aware that this file may contain sensitive information. Handle it with
              the same level of security as you would the original repository.
            </usage_guidelines>

            <notes>
            - Some files may have been excluded based on .gitignore rules and DumpToTxt's configuration
            - Binary files are not included in this packed representation. Please refer to the Repository Structure section for a complete list of file paths, including binary files
            - Files matching patterns in .gitignore are excluded
            </notes>

            </file_summary>

            <directory_structure>
            src/
              main.ts
            a.png
            README.md
            </directory_structure>

            <files>
            This section contains the contents of the repository's files.

            <file path="src/main.ts">
            x
            </file>

            <file path="README.md">
            # Hi
            </file>

            </files>

            """.Replace("\r\n", "\n");

        Assert.Equal(expected, Run(Styled(OutputStyle.Repomix)));
    }

    [Fact]
    public void Repomix_NotesWhatTheRunActuallyDid()
    {
        Put("a.txt", "a\n");
        var cfg = Styled(OutputStyle.Repomix);
        Presets.Repomix.Apply(cfg);
        cfg.SecretScan = SecretScanMode.Off;
        cfg.IncludeGlobs = ["*.txt"];
        string text = Run(cfg);
        Assert.StartsWith("This file is a merged representation of a subset of the codebase, containing specifically included files, combined into a single document by DumpToTxt.\nThe content has been processed where security check has been disabled.\n", text);
        Assert.Contains("- Only files matching these patterns are included: *.txt\n", text);
        Assert.Contains("- Files matching default ignore patterns are excluded\n", text);
        Assert.Contains("- Security check has been disabled - content may contain sensitive information\n", text);
        Assert.DoesNotContain("Git change count", text);   // not a repository: nothing was sorted
    }

    [Fact]
    public void Repomix_NamesCustomIgnorePatternsLikeRepomix()
    {
        Put("a.txt", "a\n");
        var cfg = Styled(OutputStyle.Repomix);
        cfg.ExcludeGlobs = ["*.log", "tmp/"];
        string text = Run(cfg);
        Assert.StartsWith("This file is a merged representation of a subset of the codebase, containing files not matching ignore patterns, combined into a single document by DumpToTxt.\n", text);
        Assert.Contains("- Files matching these patterns are excluded: *.log, tmp/\n", text);
        Assert.DoesNotContain("default ignore patterns", text);
    }

    [Fact]
    public void Repomix_TrimsAcrossReadBuffers_WithoutDroppingInnerWhitespace()
    {
        string pad = new(' ', 70_000), inner = new('\n', 70_000);
        Put("a.txt", pad + "start" + inner + "end" + pad);
        string text = Run(Styled(OutputStyle.Repomix));
        Assert.Contains("<file path=\"a.txt\">\nstart" + inner + "end\n</file>", text);
    }

    [Fact]
    public void RepomixPreset_ExcludesRootFilesNamedLikeIgnoredFolders_AndAWorktreeGitFile()
    {
        foreach (string name in new[] { "build", "dist", "out", "vendor", "coverage" }) Put(name, "#!/bin/sh\n");
        Put(".git", "gitdir: C:/somewhere/private/.git/worktrees/wt\n");   // a worktree or submodule root
        Put("keep.txt", "k\n");
        var cfg = DumpConfig.CreateDefault();
        Presets.Repomix.Apply(cfg);
        string text = Run(cfg, cfg.LastSelectionMode);
        Assert.Contains("<directory_structure>\nkeep.txt\n</directory_structure>", text);
        Assert.DoesNotContain("gitdir", text);
    }

    [Fact]
    public void Gitingest_MatchesTheDigestLayout()
    {
        Put("README.md", "# Hi\n");
        Put(Path.Combine("src", "main.py"), "print(1)\r\n");
        Put(".hidden", "h");
        Put("b.txt", "");
        Put(Path.Combine(".config", "c.txt"), "c\n");
        File.WriteAllBytes(Path.Combine(root, "a.bin"), [0, 1, 2, 3]);
        string name = Path.GetFileName(root);

        string tree = $"""
            Directory structure:
            └── {name}/
                ├── README.md
                ├── a.bin
                ├── b.txt
                ├── .hidden
                ├── src/
                │   └── main.py
                └── .config/
                    └── c.txt

            """.Replace("\r\n", "\n");
        static string Block(string path, string content) => $"{Sep}\nFILE: {path}\n{Sep}\n{content}\n\n";
        string expected = tree + "\n" + string.Join("\n",
            Block("README.md", "# Hi\n"), Block("a.bin", "[Binary file]"), Block("b.txt", "[Empty file]"),
            Block(".hidden", "h"), Block("src/main.py", "print(1)\r\n"), Block(".config/c.txt", "c\n"));

        Assert.Equal(expected, Run(Styled(OutputStyle.Gitingest)));
    }

    [Fact]
    public void RepomixPreset_SelectsLikeRepomix()
    {
        Put(Path.Combine("node_modules", "x.js"), "x");
        Put(Path.Combine("sub", "node_modules", "y.js"), "y");
        Put(".env", "SECRET_NAME=1");
        Put(Path.Combine("sub", ".env"), "NESTED=1");
        Put("package-lock.json", "{}");
        Put(Path.Combine("sub", "package-lock.json"), "{}");
        Put(Path.Combine("bin", "tool.cs"), "class T {}");
        Put(Path.Combine("dist", "a.js"), "a");
        Put(Path.Combine("sub", "dist", "b.js"), "b");
        Put("LICENSE", "MIT");

        var cfg = DumpConfig.CreateDefault();
        Presets.Repomix.Apply(cfg);
        Assert.Equal(OutputStyle.Repomix, cfg.Style);
        Assert.Equal(DumpSelectionMode.Thorough, cfg.LastSelectionMode);
        string text = Run(cfg, cfg.LastSelectionMode);

        foreach (string kept in new[] { "sub/.env", "bin/tool.cs", "sub/dist/b.js", "LICENSE" })
            Assert.Contains($"<file path=\"{kept}\">", text);
        foreach (string dropped in new[] { "node_modules", "package-lock.json", "\".env\"", "dist/a.js" })
            Assert.DoesNotContain(dropped, text.Split("<files>")[1]);
        Assert.DoesNotContain("node_modules", text);
    }

    [Fact]
    public void GitingestPreset_SelectsLikeGitingest()
    {
        Put(".gitignore", "ignored.txt\n");
        Put("ignored.txt", "no");
        Put("logo.png", "not really a png");
        Put(Path.Combine("sub", "build", "x.txt"), "x");
        Put(Path.Combine("bin", "tool.cs"), "class T {}");
        Put("LICENSE", "MIT");
        Put("keep.txt", "k");

        var cfg = DumpConfig.CreateDefault();
        Presets.Gitingest.Apply(cfg);
        Assert.Equal(OutputStyle.Gitingest, cfg.Style);
        string text = Run(cfg, cfg.LastSelectionMode);

        Assert.Contains("FILE: LICENSE\n", text);
        Assert.Contains("FILE: keep.txt\n", text);
        foreach (string dropped in new[] { ".gitignore", "ignored.txt", "logo.png", "build", "bin" })
            Assert.DoesNotContain(dropped, text);
    }

    [Fact]
    public void Presets_SurviveASettingsRoundTrip()
    {
        foreach (var preset in new[] { Presets.Repomix, Presets.Gitingest })
        {
            var cfg = DumpConfig.CreateDefault();
            preset.Apply(cfg);
            string path = Path.Combine(root, preset.Name + ".json");
            ConfigStore.SaveTo(path, cfg);
            var back = ConfigStore.LoadFrom(path);
            Assert.Equal(cfg.Style, back.Style);
            Assert.Equal(Presets.NoExcludeRegex, back.ExcludeRegex);
            Assert.Equal(cfg.ExcludeGlobs, back.ExcludeGlobs);
            Assert.Equal(DumpSelectionMode.Thorough, back.LastSelectionMode);
        }
    }

    [Fact]
    public void Presets_NeverWeakenSensitiveDataProtection()
    {
        var cfg = DumpConfig.CreateDefault();
        cfg.SecretScan = SecretScanMode.Redact;
        cfg.SensitiveValuePatterns = ["hunter2"];
        Presets.Repomix.Apply(cfg);
        Presets.Gitingest.Apply(cfg);
        Assert.Equal(SecretScanMode.Redact, cfg.SecretScan);
        Assert.Equal(["hunter2"], cfg.SensitiveValuePatterns);

        Put("a.txt", "password is hunter2\n");
        Assert.DoesNotContain("hunter2", Run(cfg));
    }

    [Fact]
    public void Repomix_OrdersByGitChangeCount_InARepositoryRoot()
    {
        Git("init");
        Git("config", "user.name", "Fixture");
        Git("config", "user.email", "fixture@example.invalid");
        Put("a.txt", "a1\n"); Put("b.txt", "b1\n");
        Git("add", "."); Git("commit", "-m", "one");
        Put("a.txt", "a2\n");
        Git("add", "."); Git("commit", "-m", "two");
        Put("c.txt", "c\n");                                  // never committed: count 0

        string text = Run(Styled(OutputStyle.Repomix));
        int c = text.IndexOf("<file path=\"c.txt\">"), b = text.IndexOf("<file path=\"b.txt\">"),
            a = text.IndexOf("<file path=\"a.txt\">");
        Assert.True(c >= 0 && c < b && b < a, text);
        Assert.Contains("- Files are sorted by Git change count (files with more changes are at the bottom)\n", text);
    }

    private void Git(params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var p = Process.Start(start)!;
        p.StandardOutput.ReadToEnd();
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, err);
    }
}
