using System.Text;
using System.Xml;

namespace DumpToTxt.Core;

/// <summary>XML pack (repomix-style): a root element with summary attributes, the directory
/// structure, and one &lt;file&gt; element per included file. Content is XML-escaped and
/// stripped of characters that are illegal in XML 1.0 so the output always parses.</summary>
public sealed class XmlFormatter : IDumpFormatter
{
    public OutputStyle Style { get; }
    private readonly bool _compact;

    public XmlFormatter(OutputStyle style = OutputStyle.Xml)
    {
        if (style is not (OutputStyle.Xml or OutputStyle.XmlCompact))
            throw new ArgumentOutOfRangeException(nameof(style));
        Style = style;
        _compact = style == OutputStyle.XmlCompact;
    }

    public string Render(DumpModel model, DumpConfig cfg)
    {
        var settings = new XmlWriterSettings
        {
            Indent = !_compact,
            IndentChars = "  ",
            NewLineChars = "\r\n",
            NewLineHandling = NewLineHandling.None, // don't rewrite line-endings inside file content
            Encoding = new UTF8Encoding(false),     // makes the <?xml ... encoding="utf-8"?> declaration
        };

        var sw = new Utf8StringWriter();
        using (var w = XmlWriter.Create(sw, settings))
        {
            w.WriteStartDocument();
            w.WriteStartElement("dump");
            w.WriteAttributeString("root", model.Root);
            w.WriteAttributeString("files", model.Files.Count.ToString());
            w.WriteAttributeString("totalSize", model.TotalSize.ToString());
            w.WriteAttributeString("totalTokens", model.TotalTokens.ToString());
            w.WriteAttributeString("tokenEncoding", TokenCounter.EncodingName(cfg.TokenEncoding));
            if (cfg.MaxTokens > 0)
            {
                w.WriteAttributeString("maxTokens", cfg.MaxTokens.ToString());
                w.WriteAttributeString("overBudget", model.TotalTokens > cfg.MaxTokens ? "true" : "false");
            }
            if (cfg.SecretScan != SecretScanMode.Off)
            {
                w.WriteAttributeString("secretScan", cfg.SecretScan.ToString());
                w.WriteAttributeString("secretFindings", model.SecretFindingCount.ToString());
                w.WriteAttributeString("filesWithSecrets", model.FilesWithSecrets.ToString());
            }

            w.WriteStartElement("directoryStructure");
            w.WriteString("\r\n" + Sanitize(DirectoryTree.RenderStructureOrNote(model.Entries)) + "\r\n");
            w.WriteEndElement();

            if (DirectoryTree.TopByTokens(model.Files, 1).Count > 0)
            {
                w.WriteStartElement("tokenTree");
                w.WriteString("\r\n" + Sanitize(DirectoryTree.RenderTokenTree(model.Files)) + "\r\n");
                w.WriteEndElement();
            }

            if (cfg.SecretScan != SecretScanMode.Off && model.SecretFindingCount > 0)
            {
                w.WriteStartElement("secretScan");
                w.WriteAttributeString("mode", cfg.SecretScan.ToString());
                foreach (var kv in SecretScanner.RuleTally(model.Files))
                {
                    w.WriteStartElement("rule");
                    w.WriteAttributeString("id", kv.Key);
                    w.WriteAttributeString("count", kv.Value.ToString());
                    w.WriteEndElement();
                }
                w.WriteEndElement(); // secretScan
            }

            w.WriteStartElement("files");
            foreach (var f in model.Files)
            {
                w.WriteStartElement("file");
                w.WriteAttributeString("path", f.RelativePath);
                w.WriteAttributeString("tokens", f.TokenCount.ToString());
                if (f.IsBinary) w.WriteAttributeString("binary", "true");
                if (f.IsTruncated)
                {
                    w.WriteAttributeString("truncated", "true");
                    w.WriteAttributeString("size", f.Size.ToString());
                }

                // Per-mode body (Off/Warn = original, Redact = redacted, Skip = omitted). Binary stays empty.
                bool secretSkipped = false;
                string body = f.IsBinary ? "" : SecretScanner.ContentForOutput(f, cfg.SecretScan, out secretSkipped);
                if (f.Secrets.Count > 0)
                {
                    w.WriteAttributeString("secrets", f.Secrets.Count.ToString());
                    if (secretSkipped) w.WriteAttributeString("secretsSkipped", "true");
                }
                // All attributes are written; now child <secret> elements (masked preview — never the raw secret).
                foreach (var s in f.Secrets)
                {
                    w.WriteStartElement("secret");
                    w.WriteAttributeString("rule", s.RuleId);
                    w.WriteAttributeString("line", s.Line.ToString());
                    w.WriteAttributeString("preview", Sanitize(s.Preview));
                    w.WriteEndElement();
                }
                if (!f.IsBinary && !secretSkipped) w.WriteString(Sanitize(body));
                w.WriteEndElement();
            }
            w.WriteEndElement(); // files

            w.WriteEndElement(); // dump
            w.WriteEndDocument();
        }
        return sw.ToString();
    }

    /// <summary>StringWriter that reports UTF-8 so XmlWriter emits encoding="utf-8" in the declaration.</summary>
    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }

    /// <summary>Drops characters illegal in XML 1.0 so XmlWriter never throws on file content.</summary>
    private static string Sanitize(string s)
    {
        if (s.Length == 0) return s;
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            if (XmlConvert.IsXmlChar(c)) sb.Append(c);
        return sb.ToString();
    }
}
