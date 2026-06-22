using System.Text;
using DumpToTxt.Core;

namespace DumpToTxt.Tests;

/// <summary>
/// Byte-identical regression guard for the Classic style. The expected text is built here as an
/// independent oracle (not by calling the formatter), so a change in ClassicFormatter output is
/// caught. This pins the v2 canonical Classic layout (uniform CRLF; explicitly sorted dir list).
/// </summary>
public class ClassicGoldenTests
{
    private const string Sep = "==============================";

    [Fact]
    public void Classic_Output_IsByteIdentical_ToGolden()
    {
        string work = Path.Combine(Path.GetTempPath(), "dtt-golden-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(work, "proj");
        string outDir = Path.Combine(work, "_out");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(Path.Combine(root, "node_modules"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));

        var noBom = new UTF8Encoding(false);
        void W(string rel, string content) => File.WriteAllText(Path.Combine(root, rel), content, noBom);

        W(".gitignore", "x\n");          // dotfile (allowed)
        W("a.cs", "class A {}");          // legible, NO trailing newline
        W("b.txt", "hello\n");            // legible, trailing newline
        W("app.min.js", "m\n");           // legible but .min. -> listed only
        W("skip.bin", "BIN");             // non-legible -> listed only
        W("sub\\c.cs", "class C {}\n");   // nested legible
        W("node_modules\\lib.js", "y\n"); // excluded entirely

        try
        {
            var result = new DumpEngine().Run(root, DumpConfig.CreateDefault(), outDir);
            string actual = File.ReadAllText(result.OutputPath!); // ReadAllText strips the UTF-8 BOM

            // dir list, sorted by full path (OrdinalIgnoreCase); node_modules + its child excluded
            string[] listed = { ".gitignore", "a.cs", "app.min.js", "b.txt", "skip.bin", "sub", "sub\\c.cs" };

            var sb = new StringBuilder();
            sb.Append("===== DIRECTORY LIST (filtered) =====\r\n");
            sb.Append("ROOT: ").Append(root).Append("\r\n");
            sb.Append("\r\n");
            foreach (var rel in listed) sb.Append(Path.Combine(root, rel)).Append("\r\n");
            sb.Append("\r\n\r\n===== FILE CONTENTS (LEGIBLE ONLY) =====\r\n");
            Block(sb, root, ".gitignore", "x\n");
            Block(sb, root, "a.cs", "class A {}");
            Block(sb, root, "b.txt", "hello\n");
            Block(sb, root, "sub\\c.cs", "class C {}\n");

            Assert.Equal(sb.ToString(), actual);
            Assert.Equal(4, result.FilesIncluded);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* best effort */ }
        }
    }

    private static void Block(StringBuilder sb, string root, string rel, string content)
    {
        sb.Append("\r\n").Append(Sep).Append("\r\n");
        sb.Append(Path.Combine(root, rel)).Append("\r\n");
        sb.Append(Sep).Append("\r\n");
        sb.Append(content);
        if (!content.EndsWith('\n')) sb.Append("\r\n");
    }

    [Fact]
    public void SingleFile_Skipped_EmitsLegacyNote()
    {
        string work = Path.Combine(Path.GetTempPath(), "dtt-skip-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(work, "proj");
        string outDir = Path.Combine(work, "_out");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outDir);
        string bin = Path.Combine(root, "image.bin");
        File.WriteAllText(bin, "NOTLEGIBLE");

        try
        {
            var result = new DumpEngine().Run(bin, DumpConfig.CreateDefault(), outDir);
            string text = File.ReadAllText(result.OutputPath!);
            Assert.Contains("[Skipped: file not considered legible or is excluded]", text);
            Assert.Contains(bin, text);
            Assert.Equal(0, result.FilesIncluded);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* best effort */ }
        }
    }
}
