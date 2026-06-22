using System.Text;

namespace DumpToTxt.Core;

/// <summary>Markdown pack: summary header, fenced directory tree, then per-file fenced code blocks.</summary>
public sealed class MarkdownFormatter : IDumpFormatter
{
    public OutputStyle Style => OutputStyle.Markdown;

    public string Render(DumpModel model, DumpConfig cfg)
    {
        var sb = new StringBuilder();
        sb.Append("# DumpToTxt\r\n\r\n");
        sb.Append("- **Root:** `").Append(model.Root).Append("`\r\n");
        sb.Append("- **Files:** ").Append(model.Files.Count).Append("\r\n");
        sb.Append("- **Total size:** ").Append(DirectoryTree.FormatSize(model.TotalSize)).Append("\r\n\r\n");

        sb.Append("## Directory structure\r\n\r\n");
        sb.Append("```\r\n").Append(DirectoryTree.RenderOrNote(model.Files)).Append("\r\n```\r\n\r\n");

        sb.Append("## Files\r\n");
        foreach (var f in model.Files)
        {
            sb.Append("\r\n### `").Append(f.RelativePath).Append("`\r\n\r\n");
            string fence = Fence(f.Content);
            sb.Append(fence).Append(DirectoryTree.LanguageFor(f.FullName)).Append("\r\n");
            sb.Append(f.Content);
            if (!f.Content.EndsWith('\n')) sb.Append("\r\n");
            sb.Append(fence).Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>Returns a backtick fence at least 3 long, and longer than any run inside the content.</summary>
    private static string Fence(string content)
    {
        int max = 0, run = 0;
        foreach (char c in content)
        {
            if (c == '`') { run++; if (run > max) max = run; }
            else run = 0;
        }
        return new string('`', Math.Max(3, max + 1));
    }
}
