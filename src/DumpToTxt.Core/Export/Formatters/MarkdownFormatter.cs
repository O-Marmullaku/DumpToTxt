using System.Text;

namespace DumpToTxt.Core;

/// <summary>Markdown pack: summary header, fenced directory tree, then per-file fenced code blocks.</summary>
public sealed class MarkdownFormatter : IDumpFormatter
{
    public OutputStyle Style { get; }

    public MarkdownFormatter(OutputStyle style = OutputStyle.Markdown)
    {
        if (style is not (OutputStyle.Markdown or OutputStyle.MarkdownAi or OutputStyle.MarkdownCompact))
            throw new ArgumentOutOfRangeException(nameof(style));
        Style = style;
    }

    public string Render(DumpModel model, DumpConfig cfg)
    {
        using var writer = new BoundedTextWriter(DumpEngine.MaxMaterializedCharacters);
        Write(model, cfg, writer);
        return writer.ToString();
    }

    public void Write(DumpModel model, DumpConfig cfg, TextWriter writer)
    {
        if (Style == OutputStyle.MarkdownAi) { RenderAi(model, cfg, writer); return; }
        if (Style == OutputStyle.MarkdownCompact) { RenderCompact(model, cfg, writer); return; }
        var sb = new TextOutput(writer);
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

        if (cfg.SecretScan != SecretScanMode.Off)
        {
            sb.Append("## Secret scan (").Append(cfg.SecretScan).Append(")\r\n\r\n");
            if (model.SecretFindingCount == 0)
                sb.Append("No secrets detected.\r\n\r\n");
            else
            {
                sb.Append("**").Append(model.SecretFindingCount).Append("** secret(s) in **")
                  .Append(model.FilesWithSecrets).Append("** file(s).\r\n\r\n");
                foreach (var kv in SecretScanner.RuleTally(model.Files))
                    sb.Append("- `").Append(kv.Key).Append("`: ").Append(kv.Value).Append("\r\n");
                sb.Append("\r\n");
                foreach (var f in model.Files)
                {
                    if (f.Secrets.Count == 0) continue;
                    sb.Append("- `").Append(f.RelativePath).Append("`\r\n");
                    foreach (var s in f.Secrets)
                        sb.Append("  - line ").Append(s.Line).Append(" — ").Append(s.RuleName)
                          .Append(" — `").Append(s.Preview).Append("`\r\n");
                }
                sb.Append("\r\n");
            }
        }

        sb.Append("## Files\r\n");
        foreach (var f in model.Files)
        {
            sb.Append("\r\n### `").Append(f.RelativePath).Append("` — ")
              .Append(f.TokenCount.ToString("N0")).Append(" tokens");
            if (f.Secrets.Count > 0) sb.Append(" — ⚠ ").Append(f.Secrets.Count).Append(" secret(s)");
            sb.Append("\r\n\r\n");
            if (f.IsBinary)
            {
                sb.Append("> [binary file — content skipped]\r\n");
                continue;
            }
            var body = OutputBody.For(f, cfg.SecretScan, out bool secretSkipped);
            if (secretSkipped)
            {
                sb.Append("> [content skipped: ").Append(f.Secrets.Count).Append(" secret(s) detected]\r\n");
                continue;
            }
            string fence = body.Fence();
            sb.Append(fence).Append(DirectoryTree.LanguageFor(f.FullName)).Append("\r\n");
            sb.Append(body);
            if (!body.EndsWith('\n')) sb.Append("\r\n");
            sb.Append(fence).Append("\r\n");
            if (f.IsTruncated)
                sb.Append("\r\n> [truncated: file is ").Append(DirectoryTree.FormatSize(f.Size)).Append("]\r\n");
        }
    }

    private static void RenderAi(DumpModel model, DumpConfig cfg, TextWriter writer)
    {
        var sb = new TextOutput(writer);
        sb.Append("# DumpToTxt — AI-friendly Markdown\r\n\r\n");
        sb.Append("Root: `").Append(model.Root).Append("`  \r\n");
        sb.Append("Files: ").Append(model.Files.Count).Append("  \r\n");
        sb.Append("Estimated tokens: ").Append(model.TotalTokens.ToString("N0")).Append("\r\n\r\n");
        AppendBudget(sb, model, cfg);
        sb.Append("## Directory structure\r\n\r\n```text\r\n")
            .Append(DirectoryTree.RenderStructureOrNote(model.Entries)).Append("\r\n```\r\n");
        foreach (var f in model.Files)
        {
            string path = f.RelativePath.Replace("--", "—", StringComparison.Ordinal);
            sb.Append("\r\n<!-- file:start path=\"").Append(path).Append("\" -->\r\n")
              .Append("## File: `").Append(f.RelativePath).Append("`\r\n\r\n");
            if (f.IsBinary)
            {
                sb.Append("[binary file — content skipped]\r\n");
            }
            else
            {
                var body = OutputBody.For(f, cfg.SecretScan, out bool skipped);
                if (skipped) sb.Append("[content skipped: sensitive information detected]\r\n");
                else
                {
                    string fence = body.Fence();
                    sb.Append(fence).Append(DirectoryTree.LanguageFor(f.FullName)).Append("\r\n")
                      .Append(body);
                    if (!body.EndsWith('\n')) sb.Append("\r\n");
                    sb.Append(fence).Append("\r\n");
                }
            }
            if (f.IsTruncated) sb.Append("[truncated: file is ").Append(DirectoryTree.FormatSize(f.Size)).Append("]\r\n");
            sb.Append("<!-- file:end path=\"").Append(path).Append("\" -->\r\n");
        }
    }

    private static void RenderCompact(DumpModel model, DumpConfig cfg, TextWriter writer)
    {
        var sb = new TextOutput(writer).Append("# Files\r\n");
        AppendBudget(sb, model, cfg);
        foreach (var f in model.Files)
        {
            sb.Append("\r\n## `").Append(f.RelativePath).Append("`\r\n");
            if (f.IsBinary) { sb.Append("[binary file — content skipped]\r\n"); continue; }
            var body = OutputBody.For(f, cfg.SecretScan, out bool skipped);
            if (skipped) { sb.Append("[content skipped: sensitive information detected]\r\n"); continue; }
            string fence = body.Fence();
            sb.Append(fence).Append(DirectoryTree.LanguageFor(f.FullName)).Append("\r\n").Append(body);
            if (!body.EndsWith('\n')) sb.Append("\r\n");
            sb.Append(fence).Append("\r\n");
            if (f.IsTruncated) sb.Append("[truncated: file is ").Append(DirectoryTree.FormatSize(f.Size)).Append("]\r\n");
        }
    }

    private static void AppendBudget(TextOutput sb, DumpModel model, DumpConfig cfg)
    {
        if (cfg.MaxTokens > 0)
            sb.Append($"Token budget: {model.TotalTokens:N0} / {cfg.MaxTokens:N0}")
                .Append(model.TotalTokens > cfg.MaxTokens ? " — OVER BUDGET\r\n" : " — within budget\r\n");
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
