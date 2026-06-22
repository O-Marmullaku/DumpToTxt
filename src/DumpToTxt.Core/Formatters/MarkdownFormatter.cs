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
        sb.Append("- **Total size:** ").Append(DirectoryTree.FormatSize(model.TotalSize)).Append("\r\n");
        sb.Append("- **Total tokens:** ").Append(model.TotalTokens.ToString("N0"))
          .Append(" (`").Append(TokenCounter.EncodingName(cfg.TokenEncoding)).Append("`)\r\n");
        if (cfg.MaxTokens > 0)
            sb.Append(model.TotalTokens > cfg.MaxTokens
                ? $"- **Token budget:** ⚠ {model.TotalTokens:N0} / {cfg.MaxTokens:N0} — OVER BUDGET by {(model.TotalTokens - cfg.MaxTokens):N0}\r\n"
                : $"- **Token budget:** {cfg.MaxTokens:N0} — within budget\r\n");
        sb.Append("\r\n");

        string tree = DirectoryTree.RenderStructureOrNote(model.Entries);
        string treeFence = Fence(tree);
        sb.Append("## Directory structure\r\n\r\n");
        sb.Append(treeFence).Append("\r\n").Append(tree).Append("\r\n").Append(treeFence).Append("\r\n\r\n");

        var top = DirectoryTree.TopByTokens(model.Files, 10);
        if (top.Count > 0)
        {
            sb.Append("## Largest files by tokens\r\n\r\n");
            for (int i = 0; i < top.Count; i++)
                sb.Append($"{i + 1}. `{top[i].RelativePath}` — {top[i].TokenCount:N0} tokens\r\n");
            string tokTree = DirectoryTree.RenderTokenTree(model.Files);
            string tokFence = Fence(tokTree);
            sb.Append("\r\n## Token tree\r\n\r\n");
            sb.Append(tokFence).Append("\r\n").Append(tokTree).Append("\r\n").Append(tokFence).Append("\r\n\r\n");
        }

        sb.Append("## Files\r\n");
        foreach (var f in model.Files)
        {
            sb.Append("\r\n### `").Append(f.RelativePath).Append("` — ")
              .Append(f.TokenCount.ToString("N0")).Append(" tokens\r\n\r\n");
            if (f.IsBinary)
            {
                sb.Append("> [binary file — content skipped]\r\n");
                continue;
            }
            string fence = Fence(f.Content);
            sb.Append(fence).Append(DirectoryTree.LanguageFor(f.FullName)).Append("\r\n");
            sb.Append(f.Content);
            if (!f.Content.EndsWith('\n')) sb.Append("\r\n");
            sb.Append(fence).Append("\r\n");
            if (f.IsTruncated)
                sb.Append("\r\n> [truncated: file is ").Append(DirectoryTree.FormatSize(f.Size)).Append("]\r\n");
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
