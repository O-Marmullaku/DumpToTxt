using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DumpToTxt.Core;

namespace DumpToTxt.Tests;

/// <summary>P4: precedence resolver (defaults → machine → user → per-folder .dumptotxt.json, nearest-wins,
/// field-level merge), built-in presets, the dynamic context-menu label, and the git "changed files" path.</summary>
public class P4ConfigPresetTests
{
    private static readonly UTF8Encoding NoBom = new(false);

    private static string NewTree()
    {
        string root = Path.Combine(Path.GetTempPath(), "dtt-p4-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void W(string root, string rel, string content)
    {
        string p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content, NoBom);
    }

    private static JsonDocument RenderJson(string root, DumpConfig cfg)
    {
        cfg.Style = OutputStyle.Json;
        cfg.OutputTarget = OutputTarget.Stdout;
        return JsonDocument.Parse(new DumpEngine().Run(root, cfg).Text);
    }

    private static List<string> PackedPaths(JsonDocument doc) =>
        doc.RootElement.GetProperty("fileList").EnumerateArray()
            .Select(e => e.GetProperty("path").GetString()!.Replace('\\', '/')).ToList();

    // ---------- precedence resolver ----------

    [Fact]
    public void Resolve_LayersDefaultsMachineUserFolder()
    {
        string root = NewTree();
        try
        {
            string machine = Path.Combine(root, "machine.json");
            string user = Path.Combine(root, "user.json");
            File.WriteAllText(machine, "{ \"DetectBinary\": false }");
            File.WriteAllText(user, "{ \"Style\": \"Json\" }");
            File.WriteAllText(Path.Combine(root, ".dumptotxt.json"), "{ \"MaxTotalSizeBytes\": 500 }");

            var cfg = ConfigStore.Resolve(root, machine, user);
            Assert.False(cfg.DetectBinary);                 // from machine
            Assert.Equal(OutputStyle.Json, cfg.Style);      // from user
            Assert.Equal(500, cfg.MaxTotalSizeBytes);       // from per-folder file
            Assert.True(cfg.RespectGitignore);              // untouched default
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Resolve_PerFolderNearestWins_AndMerges()
    {
        string root = NewTree();
        try
        {
            File.WriteAllText(Path.Combine(root, ".dumptotxt.json"),
                "{ \"Style\": \"Markdown\", \"MaxFileSizeBytes\": 1000 }");
            string sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, ".dumptotxt.json"), "{ \"Style\": \"Xml\" }");

            string none = Path.Combine(root, "none.json");
            var cfg = ConfigStore.Resolve(sub, none, none);
            Assert.Equal(OutputStyle.Xml, cfg.Style);       // nearest (sub) wins
            Assert.Equal(1000, cfg.MaxFileSizeBytes);       // inherited from root (merge, not replace)
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Resolve_MachineAndUserBothPresent_MergesFieldLevel()
    {
        string root = NewTree();
        try
        {
            string machine = Path.Combine(root, "machine.json");
            string user = Path.Combine(root, "user.json");
            File.WriteAllText(machine, "{ \"DetectBinary\": false, \"Style\": \"Markdown\" }");
            File.WriteAllText(user, "{ \"Style\": \"Json\" }");   // user omits DetectBinary
            var cfg = ConfigStore.Resolve(root, machine, user);
            Assert.Equal(OutputStyle.Json, cfg.Style);   // user overrides machine (field-level)
            Assert.False(cfg.DetectBinary);              // machine field inherited where user is silent
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Resolve_NoFolderFile_MatchesLoadFromUser()
    {
        string root = NewTree();
        try
        {
            var user = DumpConfig.CreateDefault();
            user.Style = OutputStyle.Markdown;
            user.IncludeGlobs = new() { "src/**" };
            user.MaxFileSizeBytes = 2048;
            string userPath = Path.Combine(root, "user.json");
            string machinePath = Path.Combine(root, "machine.json");   // absent
            File.WriteAllText(userPath, ConfigStore.Serialize(user));

            var resolved = ConfigStore.Resolve(root, machinePath, userPath);
            var loaded = ConfigStore.LoadFrom(userPath, machinePath);
            AssertConfigEqual(loaded, resolved);
        }
        finally { Directory.Delete(root, true); }
    }

    // ---------- presets ----------

    [Fact]
    public void Preset_Frontend_NarrowsToWebTypes()
    {
        var cfg = DumpConfig.CreateDefault();
        Presets.Frontend.Apply(cfg);
        Assert.Contains(".vue", cfg.ExtSet);
        Assert.Contains(".tsx", cfg.ExtSet);
        Assert.DoesNotContain(".py", cfg.ExtSet);
    }

    [Fact]
    public void Preset_Classic_IsStrictLegacy()
    {
        var cfg = DumpConfig.CreateDefault();
        cfg.RespectGitignore = true;
        cfg.DetectBinary = true;
        Presets.Classic.Apply(cfg);
        Assert.Equal(OutputStyle.Classic, cfg.Style);
        Assert.False(cfg.RespectGitignore);
        Assert.False(cfg.DetectBinary);
    }

    [Fact]
    public void Preset_ChangedFiles_SetsFlag()
    {
        var cfg = DumpConfig.CreateDefault();
        Presets.ChangedFiles.Apply(cfg);
        Assert.True(cfg.OnlyGitChanged);
    }

    [Fact]
    public void Presets_ByName_IsCaseInsensitive()
    {
        Assert.Same(Presets.Frontend, Presets.ByName("frontend"));
        Assert.Same(Presets.ChangedFiles, Presets.ByName("Changed Files"));
        Assert.Null(Presets.ByName("nope"));
    }

    // ---------- dynamic context-menu label ----------

    [Theory]
    [InlineData(OutputTarget.File, OutputStyle.Markdown, "Dump into .md")]
    [InlineData(OutputTarget.File, OutputStyle.Json, "Dump into .json")]
    [InlineData(OutputTarget.File, OutputStyle.Classic, "Dump into .txt")]
    [InlineData(OutputTarget.Clipboard, OutputStyle.Markdown, "Dump into Clipboard")]
    [InlineData(OutputTarget.Stdout, OutputStyle.Classic, "Dump into Console")]
    public void ContextMenuLabel_ReflectsOutput(OutputTarget target, OutputStyle style, string expected)
    {
        var cfg = DumpConfig.CreateDefault();
        cfg.OutputTarget = target;
        cfg.Style = style;
        Assert.Equal(expected, ContextMenuLabel.Main(cfg));
    }

    // ---------- git "changed files" ----------

    [Fact]
    public void ChangedFiles_OnlyDumpsGitChangedFiles()
    {
        string root = NewTree();
        try
        {
            W(root, "committed.cs", "class C {}\n");
            if (!GitInit(root)) return;                        // git unavailable: skip
            GitRun(root, "add committed.cs");
            GitRun(root, "commit -q -m base");

            W(root, "untracked.cs", "class U {}\n");           // new since commit
            W(root, "another.cs", "class A {}\n");             // new since commit

            var cfg = DumpConfig.CreateDefault();
            cfg.OnlyGitChanged = true;
            var packed = PackedPaths(RenderJson(root, cfg));

            Assert.Contains("untracked.cs", packed);
            Assert.Contains("another.cs", packed);
            Assert.DoesNotContain("committed.cs", packed);     // unchanged since last commit
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void ChangedFiles_SingleUnchangedFile_EmitsNoChangesNote()
    {
        string root = NewTree();
        try
        {
            W(root, "committed.cs", "class C {}\n");
            if (!GitInit(root)) return;                        // git unavailable: skip
            GitRun(root, "add committed.cs");
            GitRun(root, "commit -q -m base");                 // file now unchanged since HEAD

            var cfg = DumpConfig.CreateDefault();
            cfg.OnlyGitChanged = true;
            cfg.Style = OutputStyle.Classic;
            cfg.OutputTarget = OutputTarget.Stdout;
            // Target the single UNCHANGED file directly.
            string text = new DumpEngine().Run(Path.Combine(root, "committed.cs"), cfg).Text;

            Assert.Contains("[Skipped: file has no changes since HEAD]", text);
            Assert.DoesNotContain("[Skipped: file not considered legible or is excluded]", text);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void ChangedFiles_NonAsciiFilename_IsIncluded()
    {
        string root = NewTree();
        try
        {
            W(root, "committed.cs", "class C {}\n");
            if (!GitInit(root)) return;                        // git unavailable: skip
            GitRun(root, "add committed.cs");
            GitRun(root, "commit -q -m base");

            W(root, "café.cs", "class Cafe {}\n");             // non-ASCII, changed since commit
            var cfg = DumpConfig.CreateDefault();
            cfg.OnlyGitChanged = true;
            var packed = PackedPaths(RenderJson(root, cfg));

            // git porcelain (core.quotepath=false) + the UTF-8 StandardOutputEncoding pin keep the literal
            // UTF-8 name, so it matches FileInfo.FullName and survives the changed-files filter.
            Assert.Contains("café.cs", packed);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void ChangedFiles_NotAGitRepo_FallsBackToFullDump()
    {
        string root = NewTree();
        try
        {
            W(root, "a.cs", "x");
            W(root, "b.cs", "y");
            var cfg = DumpConfig.CreateDefault();
            cfg.OnlyGitChanged = true;
            var packed = PackedPaths(RenderJson(root, cfg));
            Assert.Contains("a.cs", packed);
            Assert.Contains("b.cs", packed);                   // no git → nothing filtered
        }
        finally { Directory.Delete(root, true); }
    }

    // ---------- helpers ----------

    private static bool GitInit(string dir)
    {
        if (!GitRun(dir, "init -q")) return false;
        GitRun(dir, "config user.email t@example.com");
        GitRun(dir, "config user.name Test");
        GitRun(dir, "config commit.gpgsign false");
        return true;
    }

    private static bool GitRun(string dir, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("git", args)
            {
                WorkingDirectory = dir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            if (p is null) return false;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static void TryDelete(string dir)
    {
        // .git holds read-only packed objects on Windows; clear the bit before deleting.
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
            Directory.Delete(dir, true);
        }
        catch { /* best effort */ }
    }

    private static void AssertConfigEqual(DumpConfig a, DumpConfig b)
    {
        Assert.Equal(a.Style, b.Style);
        Assert.Equal(a.OutputTarget, b.OutputTarget);
        Assert.Equal(a.OutputDir, b.OutputDir);
        Assert.Equal(a.ExcludeRegex, b.ExcludeRegex);
        Assert.Equal(a.RespectGitignore, b.RespectGitignore);
        Assert.Equal(a.UseDumpToTxtIgnore, b.UseDumpToTxtIgnore);
        Assert.Equal(a.DetectBinary, b.DetectBinary);
        Assert.Equal(a.MaxFileSizeBytes, b.MaxFileSizeBytes);
        Assert.Equal(a.MaxTotalSizeBytes, b.MaxTotalSizeBytes);
        Assert.Equal(a.ExtSet, b.ExtSet);
        Assert.Equal(a.DotFilesAllow, b.DotFilesAllow);
        Assert.Equal(a.IncludeGlobs, b.IncludeGlobs);
        Assert.Equal(a.ExcludeGlobs, b.ExcludeGlobs);
    }
}
