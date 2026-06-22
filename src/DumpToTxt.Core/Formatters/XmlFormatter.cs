using System.Text;
using System.Xml;

namespace DumpToTxt.Core;

/// <summary>XML pack (repomix-style): a root element with summary attributes, the directory
/// structure, and one &lt;file&gt; element per included file. Content is XML-escaped and
/// stripped of characters that are illegal in XML 1.0 so the output always parses.</summary>
public sealed class XmlFormatter : IDumpFormatter
{
    public OutputStyle Style => OutputStyle.Xml;

    public string Render(DumpModel model, DumpConfig cfg)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
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

            w.WriteStartElement("directoryStructure");
            w.WriteString("\r\n" + Sanitize(DirectoryTree.RenderOrNote(model.Files)) + "\r\n");
            w.WriteEndElement();

            w.WriteStartElement("files");
            foreach (var f in model.Files)
            {
                w.WriteStartElement("file");
                w.WriteAttributeString("path", f.RelativePath);
                w.WriteString(Sanitize(f.Content));
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
