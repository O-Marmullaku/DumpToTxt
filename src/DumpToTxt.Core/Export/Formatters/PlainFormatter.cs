using System.Text;

namespace DumpToTxt.Core;

/// <summary>Plain-text pack: summary header, directory tree, then delimited file blocks.</summary>
public sealed class PlainFormatter : IDumpFormatter
{
    public OutputStyle Style => OutputStyle.Plain;

    public string Render(DumpModel model, DumpConfig cfg)
    {
        using var writer = new BoundedTextWriter(DumpEngine.MaxMaterializedCharacters);
        Write(model, cfg, writer);
        return writer.ToString();
    }

    public void Write(DumpModel model, DumpConfig cfg, TextWriter writer)
    {
        var sb = new TextOutput(writer);
        sb.Append("============================================================\r\n");
        sb.Append("DumpToTxt — Plain output\r\n");
        sb.Append("============================================================\r\n");
        sb.Append("Root: ").Append(model.Root).Append("\r\n");
        sb.Append("Files: ").Append(model.Files.Count).Append("\r\n");
        sb.Append("Total size: ").Append(DirectoryTree.FormatSize(model.TotalSize)).Append("\r\n");
        sb.Append("Total tokens: ").Append(model.TotalTokens.ToString("N0"))
          .Append(" (").Append(TokenCounter.EncodingName(cfg.TokenEncoding)).Append(")\r\n");
        if (cfg.MaxTokens > 0)
            sb.Append("Token budget: ").Append(cfg.MaxTokens.ToString("N0")).Append(
                model.TotalTokens > cfg.MaxTokens
                    ? $" — OVER BUDGET by {(model.TotalTokens - cfg.MaxTokens):N0}\r\n"
                    : " — within budget\r\n");
        sb.Append("\r\n");

        sb.Append("----- Directory structure -----\r\n");
        sb.Append(DirectoryTree.RenderStructureOrNote(model.Entries)).Append("\r\n\r\n");

        var top = DirectoryTree.TopByTokens(model.Files, 10);
        if (top.Count > 0)
        {
            sb.Append("----- Largest files by tokens -----\r\n");
            for (int i = 0; i < top.Count; i++)
                sb.Append($"{i + 1,2}. {top[i].RelativePath}  ({top[i].TokenCount:N0} tokens)\r\n");
            sb.Append("\r\n----- Token tree -----\r\n");
            sb.Append(DirectoryTree.RenderTokenTree(model.Files)).Append("\r\n\r\n");
        }

        if (cfg.SecretScan != SecretScanMode.Off)
        {
            sb.Append("----- Secret scan (").Append(cfg.SecretScan).Append(") -----\r\n");
            if (model.SecretFindingCount == 0)
                sb.Append("No secrets detected.\r\n\r\n");
            else
            {
                sb.Append(model.SecretFindingCount).Append(" secret(s) in ")
                  .Append(model.FilesWithSecrets).Append(" file(s).\r\n");
                foreach (var kv in SecretScanner.RuleTally(model.Files))
                    sb.Append("  ").Append(kv.Key).Append(": ").Append(kv.Value).Append("\r\n");
                foreach (var f in model.Files)
                {
                    if (f.Secrets.Count == 0) continue;
                    sb.Append(f.RelativePath).Append("\r\n");
                    foreach (var s in f.Secrets)
                        sb.Append("  line ").Append(s.Line).Append(" · ").Append(s.RuleName)
                          .Append(" · ").Append(s.Preview).Append("\r\n");
                }
                sb.Append("\r\n");
            }
        }

        sb.Append("----- Files -----\r\n");
        foreach (var f in model.Files)
        {
            sb.Append("\r\n================ File: ").Append(f.RelativePath)
              .Append(" (").Append(f.TokenCount.ToString("N0")).Append(" tokens)");
            if (f.Secrets.Count > 0) sb.Append(" [⚠ ").Append(f.Secrets.Count).Append(" secret(s)]");
            sb.Append(" ================\r\n");
            if (f.IsBinary)
            {
                sb.Append("[binary file — content skipped]\r\n");
                continue;
            }
            var body = OutputBody.For(f, cfg.SecretScan, out bool secretSkipped);
            if (secretSkipped)
            {
                sb.Append("[content skipped: ").Append(f.Secrets.Count).Append(" secret(s) detected]\r\n");
                continue;
            }
            sb.Append(body);
            if (!body.EndsWith('\n')) sb.Append("\r\n");
            if (f.IsTruncated)
                sb.Append("[truncated: file is ").Append(DirectoryTree.FormatSize(f.Size)).Append("]\r\n");
        }
    }
}
