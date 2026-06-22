using System.Text;
using System.Text.Json;
using DumpToTxt.Core;

namespace DumpToTxt.Tests;

/// <summary>P3 ignore/include engine: gitignore + .dumptotxtignore + globs + precedence, size caps,
/// binary detection, and the full-structure directory tree. End-to-end through DumpEngine where
/// possible (the realistic path), plus IgnoreMatcher unit tests for the pattern translator.</summary>
public class P3IgnoreEngineTests
{
    private static readonly UTF8Encoding NoBom = new(false);

    // ---------- helpers ----------

    private static string NewTree()
    {
        string root = Path.Combine(Path.GetTempPath(), "dtt-p3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void W(string root, string rel, string content)
    {
        string p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content, NoBom);
    }

    private static void WBytes(string root, string rel, byte[] bytes)
    {
        string p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, bytes);
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

    private static JsonElement PackedFile(JsonDocument doc, string path) =>
        doc.RootElement.GetProperty("fileList").EnumerateArray()
            .First(e => e.GetProperty("path").GetString()!.Replace('\\', '/') == path);

    private static string Tree(JsonDocument doc) =>
        doc.RootElement.GetProperty("directoryStructure").GetString()!.Replace('\\', '/');

    private static IgnoreMatcher Matcher(List<string>? exclude = null, List<string>? include = null)
    {
        var cfg = DumpConfig.CreateDefault();
        cfg.ExcludeRegex = "";          // isolate glob behavior from the legacy regex layer
        cfg.ExcludeGlobs = exclude ?? new();
        cfg.IncludeGlobs = include ?? new();
        return IgnoreMatcher.FromConfig(cfg);
    }

    // ---------- IgnoreMatcher unit (pattern translator) ----------

    [Fact]
    public void Glob_Basename_MatchesAtAnyDepth()
    {
        var m = Matcher(exclude: new() { "foo.cs" });
        Assert.True(m.IsExcluded("x/a/foo.cs", "a/foo.cs", false));
        Assert.True(m.IsExcluded("x/foo.cs", "foo.cs", false));
        Assert.False(m.IsExcluded("x/bar.cs", "bar.cs", false));
    }

    [Fact]
    public void Glob_LeadingSlash_IsAnchoredToRoot()
    {
        var m = Matcher(exclude: new() { "/dist" });
        Assert.True(m.IsExcluded("dist", "dist", true));
        Assert.False(m.IsExcluded("src/dist", "src/dist", true)); // anchored: not nested
    }

    [Fact]
    public void Glob_TrailingSlash_IsDirectoryOnly()
    {
        var m = Matcher(exclude: new() { "build/" });
        Assert.True(m.IsExcluded("build", "build", isDir: true));
        Assert.False(m.IsExcluded("build", "build", isDir: false)); // a FILE named build is not matched
    }

    [Fact]
    public void Glob_Star_DoesNotCrossSeparators()
    {
        var m = Matcher(exclude: new() { "*.log" });
        Assert.True(m.IsExcluded("a/b/c.log", "a/b/c.log", false));
        Assert.False(m.IsExcluded("a/b/c.txt", "a/b/c.txt", false));
    }

    [Fact]
    public void Glob_DoubleStar_MatchesAcrossDirectories()
    {
        var m = Matcher(exclude: new() { "**/temp" });
        Assert.True(m.IsExcluded("temp", "temp", true));
        Assert.True(m.IsExcluded("a/b/temp", "a/b/temp", true));
    }

    [Fact]
    public void Glob_Negation_ReincludesLastMatchWins()
    {
        var m = Matcher(exclude: new() { "*.log", "!keep.log" });
        Assert.True(m.IsExcluded("a.log", "a.log", false));
        Assert.False(m.IsExcluded("keep.log", "keep.log", false));
    }

    [Fact]
    public void Glob_CommentAndBlankLines_Ignored()
    {
        var m = Matcher(exclude: new() { "# a comment", "", "real.cs" });
        Assert.False(m.IsExcluded("a.cs", "a.cs", false));
        Assert.True(m.IsExcluded("real.cs", "real.cs", false));
    }

    [Fact]
    public void Glob_BracketClass_MatchesCharSet()
    {
        var m = Matcher(exclude: new() { "[Dd]ebug/" });   // standard VisualStudio .gitignore pattern
        Assert.True(m.IsExcluded("Debug", "Debug", isDir: true));
        Assert.True(m.IsExcluded("debug", "debug", isDir: true));
        Assert.False(m.IsExcluded("Xebug", "Xebug", isDir: true));
    }

    [Fact]
    public void Glob_BracketClass_ExtAlternation()
    {
        var m = Matcher(exclude: new() { "*.[oa]" });
        Assert.True(m.IsExcluded("x/f.o", "x/f.o", false));
        Assert.True(m.IsExcluded("g.a", "g.a", false));
        Assert.False(m.IsExcluded("h.c", "h.c", false));
    }

    [Fact]
    public void Glob_BracketClass_Negated()
    {
        var m = Matcher(exclude: new() { "[!a]bc" });
        Assert.True(m.IsExcluded("xbc", "xbc", false));
        Assert.False(m.IsExcluded("abc", "abc", false));
    }

    [Fact]
    public void Glob_NegatedBracketClass_DoesNotMatchSeparator()
    {
        // Regression: a NON-empty negated bracket "[!x]" must not match '/' (gitignore: brackets never match
        // the separator). A plain "[^x]" translation wrongly matched '/' across a directory boundary.
        var m = Matcher(exclude: new() { "a[!x]b" });
        Assert.False(m.IsExcluded("a/b", "a/b", false));   // '/' must NOT satisfy the negated class
        Assert.True(m.IsExcluded("aYb", "aYb", false));    // a real non-'x' char still matches
        Assert.False(m.IsExcluded("axb", "axb", false));   // the excluded member is honored
    }

    [Fact]
    public void Glob_BracketClass_LiteralSlash_DoesNotMatchSeparator()
    {
        // A gitignore bracket expression never matches the '/' path separator, even when '/' is a member.
        var m = Matcher(exclude: new() { "a[/]b" });
        Assert.False(m.IsExcluded("a/b", "a/b", false));   // must NOT match across the separator
        Assert.False(m.IsExcluded("aXb", "aXb", false));   // '/' is the only member -> matches nothing
    }

    [Fact]
    public void Glob_UnterminatedBracket_IsLiteral()
    {
        var m = Matcher(exclude: new() { "a[b" });
        Assert.True(m.IsExcluded("a[b", "a[b", false));
        Assert.False(m.IsExcluded("ab", "ab", false));
    }

    [Fact]
    public void Glob_InvalidBracketRange_IsDropped_NotThrown()
    {
        // "[z-a]" is a reversed range .NET regex rejects — the rule must be dropped, not crash the dump.
        var m = Matcher(exclude: new() { "[z-a]bc", "keep.cs" });
        Assert.False(m.IsExcluded("abc", "abc", false));        // bad rule silently dropped
        Assert.True(m.IsExcluded("keep.cs", "keep.cs", false));  // sibling rules still compile + match
    }

    [Fact]
    public void Include_EmptyMeansAll_NonEmptyIsWhitelist()
    {
        var none = Matcher();
        Assert.False(none.HasIncludeGlobs);
        Assert.True(none.MatchesInclude("anything.cs"));

        var inc = Matcher(include: new() { "src/**" });
        Assert.True(inc.HasIncludeGlobs);
        Assert.True(inc.MatchesInclude("src/a.cs"));
        Assert.False(inc.MatchesInclude("other/a.cs"));
    }

    // ---------- engine integration: ignore sources ----------

    [Fact]
    public void Gitignore_IsHonored()
    {
        string root = NewTree();
        try
        {
            W(root, ".gitignore", "ignored.cs\n");
            W(root, "ignored.cs", "secret");
            W(root, "kept.cs", "ok");
            var packed = PackedPaths(RenderJson(root, DumpConfig.CreateDefault()));
            Assert.Contains("kept.cs", packed);
            Assert.DoesNotContain("ignored.cs", packed);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DumpToTxtIgnore_IsHonored()
    {
        string root = NewTree();
        try
        {
            W(root, ".dumptotxtignore", "*.gen.cs\n");
            W(root, "a.gen.cs", "generated");
            W(root, "b.cs", "ok");
            var packed = PackedPaths(RenderJson(root, DumpConfig.CreateDefault()));
            Assert.Contains("b.cs", packed);
            Assert.DoesNotContain("a.gen.cs", packed);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Gitignore_DirectoryIsPruned_ChildrenAbsentFromTreeAndContent()
    {
        string root = NewTree();
        try
        {
            W(root, ".gitignore", "skip/\n");
            W(root, "skip/deep.cs", "z");
            W(root, "keep.cs", "y");
            var doc = RenderJson(root, DumpConfig.CreateDefault());
            Assert.DoesNotContain("skip/deep.cs", PackedPaths(doc));
            Assert.DoesNotContain("skip", Tree(doc));
            Assert.Contains("keep.cs", PackedPaths(doc));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void LegacyExcludeRegex_StillLayered()
    {
        string root = NewTree();
        try
        {
            W(root, "node_modules/lib.js", "var x=1;"); // default ExcludeRegex excludes node_modules
            W(root, "app.cs", "class A{}");
            var doc = RenderJson(root, DumpConfig.CreateDefault());
            Assert.DoesNotContain("node_modules/lib.js", PackedPaths(doc));
            Assert.DoesNotContain("node_modules", Tree(doc));
            Assert.Contains("app.cs", PackedPaths(doc));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void IncludeGlobs_WhitelistContent_TreeStillFull()
    {
        string root = NewTree();
        try
        {
            W(root, "src/a.cs", "x");
            W(root, "other/b.cs", "y");
            var cfg = DumpConfig.CreateDefault();
            cfg.IncludeGlobs = new() { "src/**" };
            var doc = RenderJson(root, cfg);
            Assert.Contains("src/a.cs", PackedPaths(doc));
            Assert.DoesNotContain("other/b.cs", PackedPaths(doc)); // content whitelisted
            Assert.Contains("other", Tree(doc));                   // but full structure still shows it
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ExcludeGlob_BeatsInclude_IgnoreWins()
    {
        string root = NewTree();
        try
        {
            W(root, "ok.cs", "x");
            W(root, "secret.cs", "y");
            var cfg = DumpConfig.CreateDefault();
            cfg.IncludeGlobs = new() { "**/*.cs" };
            cfg.ExcludeGlobs = new() { "secret.cs" };
            var packed = PackedPaths(RenderJson(root, cfg));
            Assert.Contains("ok.cs", packed);
            Assert.DoesNotContain("secret.cs", packed);
        }
        finally { Directory.Delete(root, true); }
    }

    // ---------- size caps ----------

    [Fact]
    public void PerFileCap_TruncatesContentAndMarks()
    {
        string root = NewTree();
        try
        {
            W(root, "big.cs", new string('a', 100));
            var cfg = DumpConfig.CreateDefault();
            cfg.MaxFileSizeBytes = 10;
            var f = PackedFile(RenderJson(root, cfg), "big.cs");
            Assert.Equal(10, f.GetProperty("content").GetString()!.Length);
            Assert.True(f.GetProperty("truncated").GetBoolean());
            Assert.Equal(100, f.GetProperty("size").GetInt64());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void TotalCap_StopsContentAfterBudget()
    {
        string root = NewTree();
        try
        {
            W(root, "a1.cs", new string('a', 8));
            W(root, "a2.cs", new string('b', 8));
            var cfg = DumpConfig.CreateDefault();
            cfg.MaxTotalSizeBytes = 10; // a1 (8) fits; a2 only has 2 left
            var doc = RenderJson(root, cfg);
            var f1 = PackedFile(doc, "a1.cs");
            var f2 = PackedFile(doc, "a2.cs");
            Assert.False(f1.TryGetProperty("truncated", out _)); // full
            Assert.Equal(8, f1.GetProperty("content").GetString()!.Length);
            Assert.True(f2.GetProperty("truncated").GetBoolean());
            Assert.Equal(2, f2.GetProperty("content").GetString()!.Length);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Utf16BomFile_CapTruncates_DecodesAsText_NotMojibake()
    {
        string root = NewTree();
        try
        {
            // UTF-16 LE + BOM, 40 'a' (~82 bytes). IsBinary passes it (BOM = text); the cap then clips it.
            byte[] bytes = Encoding.Unicode.GetPreamble()
                .Concat(Encoding.Unicode.GetBytes(new string('a', 40))).ToArray();
            WBytes(root, "u16.cs", bytes);
            var cfg = DumpConfig.CreateDefault();
            cfg.MaxFileSizeBytes = 20;   // clip mid-file
            var f = PackedFile(RenderJson(root, cfg), "u16.cs");
            string content = f.GetProperty("content").GetString()!;
            Assert.True(f.GetProperty("truncated").GetBoolean());
            // Must decode as readable 'a's — NOT NUL/replacement garbage from UTF-8-decoding UTF-16 bytes.
            Assert.NotEqual(0, content.Length);
            Assert.All(content, ch => Assert.Equal('a', ch));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void NoCaps_ByDefault_FullContent()
    {
        string root = NewTree();
        try
        {
            W(root, "big.cs", new string('a', 5000));
            var f = PackedFile(RenderJson(root, DumpConfig.CreateDefault()), "big.cs");
            Assert.Equal(5000, f.GetProperty("content").GetString()!.Length);
            Assert.False(f.TryGetProperty("truncated", out _));
        }
        finally { Directory.Delete(root, true); }
    }

    // ---------- binary detection ----------

    [Fact]
    public void BinaryFile_NulSniff_SkippedAndMarked()
    {
        string root = NewTree();
        try
        {
            WBytes(root, "data.cs", new byte[] { (byte)'x', 0, (byte)'y' }); // whitelisted ext, but NUL
            W(root, "text.cs", "hello");
            var doc = RenderJson(root, DumpConfig.CreateDefault());
            var bin = PackedFile(doc, "data.cs");
            Assert.True(bin.GetProperty("binary").GetBoolean());
            Assert.Equal("", bin.GetProperty("content").GetString());
            Assert.Equal("hello", PackedFile(doc, "text.cs").GetProperty("content").GetString());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Utf16BomFile_NotFlaggedBinary()
    {
        string root = NewTree();
        try
        {
            // UTF-16 LE BOM + "hi": has NUL bytes, but the BOM means it is text.
            WBytes(root, "u16.cs", new byte[] { 0xFF, 0xFE, 0x68, 0x00, 0x69, 0x00 });
            var f = PackedFile(RenderJson(root, DumpConfig.CreateDefault()), "u16.cs");
            Assert.False(f.TryGetProperty("binary", out _));
            Assert.Contains("hi", f.GetProperty("content").GetString());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void BinaryDetectionOff_IncludesRawContent()
    {
        string root = NewTree();
        try
        {
            WBytes(root, "data.cs", new byte[] { (byte)'x', 0, (byte)'y' });
            var cfg = DumpConfig.CreateDefault();
            cfg.DetectBinary = false;
            var f = PackedFile(RenderJson(root, cfg), "data.cs");
            Assert.False(f.TryGetProperty("binary", out _));
        }
        finally { Directory.Delete(root, true); }
    }

    // ---------- full-structure tree ----------

    // ---------- Classic style: the ignore engine + binary detection apply here too (intended P3 behavior) ----------

    [Fact]
    public void Classic_HonorsGitignore_DropsFromListingAndContent()
    {
        string root = NewTree();
        try
        {
            W(root, ".gitignore", "secret.cs\nlogs/\n");
            W(root, "secret.cs", "class Secret {}\n");
            W(root, "logs/x.cs", "class LogX {}\n");
            W(root, "keep.cs", "class Keep {}\n");
            var cfg = DumpConfig.CreateDefault();
            cfg.DotFilesAllow = new();          // don't pack .gitignore itself (its text echoes the patterns)
            cfg.Style = OutputStyle.Classic;
            cfg.OutputTarget = OutputTarget.Stdout;
            string text = new DumpEngine().Run(root, cfg).Text;
            Assert.Contains("class Keep", text);
            Assert.DoesNotContain("class Secret", text); // gitignore'd file: content gone
            Assert.DoesNotContain("class LogX", text);   // pruned dir: content gone
            Assert.DoesNotContain("secret.cs", text);    // and absent from the DIRECTORY LIST
            Assert.DoesNotContain("logs", text);         // pruned dir absent from the DIRECTORY LIST
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Classic_BinaryWhitelistedFile_SkippedWithMarker()
    {
        string root = NewTree();
        try
        {
            WBytes(root, "blob.cs", new byte[] { (byte)'x', 0, (byte)'y' }); // .cs but NUL -> binary
            W(root, "ok.cs", "class Ok {}\n");
            var cfg = DumpConfig.CreateDefault();
            cfg.Style = OutputStyle.Classic;
            cfg.OutputTarget = OutputTarget.Stdout;
            string text = new DumpEngine().Run(root, cfg).Text;
            Assert.Contains("[binary file — content skipped]", text);
            Assert.Contains("class Ok", text);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Tree_ShowsEmptyDirsAndNonLegibleFiles_ContentDoesNot()
    {
        string root = NewTree();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "emptydir"));
            W(root, "a.cs", "x");
            W(root, "notes.unknownext", "data"); // survives ignore, but not legible -> listed only
            var doc = RenderJson(root, DumpConfig.CreateDefault());
            string tree = Tree(doc);
            Assert.Contains("emptydir/", tree);
            Assert.Contains("notes.unknownext", tree);
            Assert.Contains("a.cs", tree);
            var packed = PackedPaths(doc);
            Assert.Contains("a.cs", packed);
            Assert.DoesNotContain("notes.unknownext", packed);
        }
        finally { Directory.Delete(root, true); }
    }
}
