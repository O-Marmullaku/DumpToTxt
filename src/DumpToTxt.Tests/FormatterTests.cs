using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DumpToTxt.Core;

namespace DumpToTxt.Tests;

/// <summary>Each non-Classic formatter must produce valid, sensible output on a sample tree.</summary>
public class FormatterTests
{
    private static readonly UTF8Encoding NoBom = new(false);

    private static string Make(Action<string> build)
    {
        string root = Path.Combine(Path.GetTempPath(), "dtt-fmt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        build(root);
        return root;
    }

    private static string Render(string root, OutputStyle style)
    {
        var cfg = DumpConfig.CreateDefault();
        cfg.Style = style;
        cfg.OutputTarget = OutputTarget.Stdout; // returns Text, writes no file
        return new DumpEngine().Run(root, cfg).Text;
    }

    private static void Sample(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "a.cs"), "class A {}\n", NoBom);
        File.WriteAllText(Path.Combine(root, "sub", "b.py"), "print(1)\n", NoBom);
    }

    [Fact]
    public void Plain_HasHeaderTreeAndFiles()
    {
        string root = Make(Sample);
        try
        {
            string o = Render(root, OutputStyle.Plain);
            Assert.Contains("DumpToTxt — Plain output", o);
            Assert.Contains("----- Directory structure -----", o);
            Assert.Contains("a.cs", o);
            Assert.Contains("================ File: a.cs ================", o);
            Assert.Contains("class A {}", o);
            Assert.Contains("File: sub" + Path.DirectorySeparatorChar + "b.py", o);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Markdown_HasFencedBlocksWithLanguage()
    {
        string root = Make(Sample);
        try
        {
            string o = Render(root, OutputStyle.Markdown);
            Assert.Contains("# DumpToTxt", o);
            Assert.Contains("## Directory structure", o);
            Assert.Contains("### `a.cs`", o);
            Assert.Contains("```csharp", o);
            Assert.Contains("```python", o);
            Assert.Contains("class A {}", o);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Markdown_EscapesContentBackticksWithLongerFence()
    {
        string root = Make(r => File.WriteAllText(Path.Combine(r, "a.md"), "```\ncode\n```\n", NoBom));
        try
        {
            string o = Render(root, OutputStyle.Markdown);
            Assert.Contains("````", o); // 4-backtick fence because content holds a 3-backtick run
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Xml_IsWellFormed_Utf8_AndHasFiles()
    {
        string root = Make(Sample);
        try
        {
            string o = Render(root, OutputStyle.Xml);
            var doc = XDocument.Parse(o); // throws if malformed
            Assert.Equal("dump", doc.Root!.Name.LocalName);
            Assert.Equal("2", doc.Root!.Attribute("files")!.Value);
            Assert.Equal("utf-8", doc.Declaration!.Encoding, ignoreCase: true);
            var files = doc.Root!.Element("files")!.Elements("file").ToList();
            Assert.Equal(2, files.Count);
            Assert.Contains(files, f => f.Attribute("path")!.Value == "a.cs" && f.Value.Contains("class A {}"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Xml_SanitizesInvalidChars_StillParses()
    {
        // 0x01 is illegal in XML 1.0 (but not a NUL, so it is NOT binary-detected); the formatter
        // must strip it so the doc still parses while the file's content is still rendered.
        string content = "x" + (char)1 + "y\n";
        string root = Make(r => File.WriteAllText(Path.Combine(r, "a.cs"), content, NoBom));
        try
        {
            string o = Render(root, OutputStyle.Xml);
            var doc = XDocument.Parse(o);
            var file = doc.Root!.Element("files")!.Element("file")!;
            Assert.False(file.Value.Contains((char)1)); // ordinal: the illegal char was stripped
            Assert.Contains("xy", file.Value);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Json_IsValid_AndHasFileList()
    {
        string root = Make(Sample);
        try
        {
            string o = Render(root, OutputStyle.Json);
            using var doc = JsonDocument.Parse(o); // throws if malformed
            var r = doc.RootElement;
            Assert.Equal(2, r.GetProperty("files").GetInt32());
            var list = r.GetProperty("fileList");
            Assert.Equal(2, list.GetArrayLength());
            var first = list[0];
            Assert.False(string.IsNullOrEmpty(first.GetProperty("path").GetString()));
            Assert.False(string.IsNullOrEmpty(first.GetProperty("content").GetString()));
        }
        finally { Directory.Delete(root, true); }
    }
}
