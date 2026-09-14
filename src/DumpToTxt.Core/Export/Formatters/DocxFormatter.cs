using System.IO.Compression;
using System.Text;
using System.Xml;

namespace DumpToTxt.Core;

/// <summary>Streams a WordprocessingML package with internal file navigation.</summary>
public static class DocxFormatter
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public static byte[] RenderPackage(DumpModel model, DumpConfig cfg)
    {
        if (model.TotalSize > DumpEngine.MaxMaterializedCharacters)
            throw new InvalidOperationException("Use streaming file output for a Word document this large.");
        using var buffer = new MemoryStream();
        WritePackage(model, cfg, buffer);
        return buffer.ToArray();
    }

    public static void WritePackage(DumpModel model, DumpConfig cfg, Stream output,
        CancellationToken cancellationToken = default)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        Write(zip, "[Content_Types].xml", ContentTypes);
        Write(zip, "_rels/.rels", RootRelationships);
        Write(zip, "word/styles.xml", Styles);
        Write(zip, "word/_rels/document.xml.rels", """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="styles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """);
        var entry = zip.CreateEntry("word/document.xml", CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), NewLineHandling = NewLineHandling.None });
        writer.WriteStartDocument(true);
        writer.WriteStartElement("w", "document", W);
        writer.WriteStartElement("w", "body", W);
        Bookmark(writer, "index", 0, "DumpToTxt file index");
        Paragraph(writer, "Root: " + model.Root);
        Paragraph(writer, $"Files: {model.Files.Count:N0} · Estimated tokens: {model.TotalTokens:N0}");
        if (cfg.MaxTokens > 0)
            Paragraph(writer, $"Token budget: {model.TotalTokens:N0} / {cfg.MaxTokens:N0}" +
                (model.TotalTokens > cfg.MaxTokens ? " — OVER BUDGET" : " — within budget"));
        Paragraph(writer, "Directory structure", "Heading2");
        using (var tree = new StringReader(DirectoryTree.RenderStructureOrNote(model.Entries)))
        {
            string? line;
            while ((line = tree.ReadLine()) is not null) { cancellationToken.ThrowIfCancellationRequested(); Paragraph(writer, line, "Code"); }
        }
        Paragraph(writer, "Files", "Heading2");
        for (int i = 0; i < model.Files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Hyperlink(writer, $"file_{i + 1}", model.Files[i].RelativePath);
        }
        for (int i = 0; i < model.Files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = model.Files[i];
            Bookmark(writer, $"file_{i + 1}", i + 1, file.RelativePath);
            Hyperlink(writer, "index", "Back to file index");
            if (file.IsBinary) { Paragraph(writer, "[binary file — content skipped]"); continue; }
            var body = OutputBody.For(file, cfg.SecretScan, out bool skipped);
            if (skipped) { Paragraph(writer, "[content skipped: sensitive information detected]"); continue; }
            using var reader = body.Open();
            WriteBody(writer, reader, cancellationToken);
            if (file.IsTruncated) Paragraph(writer, $"[truncated: file is {DirectoryTree.FormatSize(file.Size)}]");
        }
        writer.WriteStartElement("w", "sectPr", W);
        writer.WriteStartElement("w", "pgSz", W);
        Attribute(writer, "w", "12240"); Attribute(writer, "h", "15840"); writer.WriteEndElement();
        writer.WriteStartElement("w", "pgMar", W);
        foreach (string side in new[] { "top", "right", "bottom", "left" }) Attribute(writer, side, "1080");
        writer.WriteEndElement(); writer.WriteEndElement();
        writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndDocument();
    }

    private static void WriteBody(XmlWriter writer, TextReader reader, CancellationToken cancellationToken)
    {
        StartParagraph(writer, "Code"); StartText(writer);
        var buffer = new char[32768];
        bool previousCr = false;
        char pendingHigh = '\0';
        int count;
        while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int start = 0;
            if (pendingHigh != '\0') { writer.WriteString(new string([pendingHigh, buffer[0]])); pendingHigh = '\0'; start = 1; }
            for (int i = start; i < count; i++)
            {
                char c = buffer[i];
                if (c is not ('\r' or '\n')) { previousCr = false; continue; }
                if (i > start) writer.WriteChars(buffer, start, i - start);
                if (c != '\n' || !previousCr)
                {
                    writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement();
                    StartParagraph(writer, "Code"); StartText(writer);
                }
                previousCr = c == '\r'; start = i + 1;
            }
            if (count > start && char.IsHighSurrogate(buffer[count - 1])) pendingHigh = buffer[--count];
            if (count > start) writer.WriteChars(buffer, start, count - start);
        }
        writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement();
    }

    private static void Attribute(XmlWriter writer, string name, string value) => writer.WriteAttributeString("w", name, W, value);
    private static void StartParagraph(XmlWriter writer, string? style)
    {
        writer.WriteStartElement("w", "p", W);
        if (style is null) return;
        writer.WriteStartElement("w", "pPr", W); writer.WriteStartElement("w", "pStyle", W);
        Attribute(writer, "val", style); writer.WriteEndElement(); writer.WriteEndElement();
    }
    private static void StartText(XmlWriter writer)
    {
        writer.WriteStartElement("w", "r", W); writer.WriteStartElement("w", "t", W);
        writer.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
    }
    private static void Paragraph(XmlWriter writer, string text, string? style = null)
    {
        StartParagraph(writer, style); StartText(writer); writer.WriteString(Clean(text));
        writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement();
    }
    private static void Bookmark(XmlWriter writer, string name, int id, string text)
    {
        StartParagraph(writer, "Heading1");
        writer.WriteStartElement("w", "bookmarkStart", W); Attribute(writer, "id", id.ToString()); Attribute(writer, "name", name); writer.WriteEndElement();
        StartText(writer); writer.WriteString(Clean(text)); writer.WriteEndElement(); writer.WriteEndElement();
        writer.WriteStartElement("w", "bookmarkEnd", W); Attribute(writer, "id", id.ToString()); writer.WriteEndElement(); writer.WriteEndElement();
    }
    private static void Hyperlink(XmlWriter writer, string anchor, string text)
    {
        StartParagraph(writer, null);
        writer.WriteStartElement("w", "hyperlink", W); Attribute(writer, "anchor", anchor); Attribute(writer, "history", "1");
        writer.WriteStartElement("w", "r", W); writer.WriteStartElement("w", "rPr", W); writer.WriteStartElement("w", "rStyle", W);
        Attribute(writer, "val", "Hyperlink"); writer.WriteEndElement(); writer.WriteEndElement();
        writer.WriteStartElement("w", "t", W); writer.WriteString(Clean(text));
        writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement();
    }
    private static string Clean(string text)
    {
        using var writer = new StringWriter(); new XmlSafeWriter(writer).Write(text); return writer.ToString();
    }
    private static void Write(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
    private const string ContentTypes = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
          <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
        </Types>
        """;

    private const string RootRelationships = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>
        """;

    private const string Styles = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:rPr><w:sz w:val="20"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:rPr><w:b/><w:sz w:val="32"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:rPr><w:b/><w:sz w:val="26"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Code"><w:name w:val="Code"/><w:basedOn w:val="Normal"/><w:rPr><w:rFonts w:ascii="Consolas" w:hAnsi="Consolas"/><w:sz w:val="18"/></w:rPr></w:style>
          <w:style w:type="character" w:styleId="Hyperlink"><w:name w:val="Hyperlink"/><w:rPr><w:color w:val="0563C1"/><w:u w:val="single"/></w:rPr></w:style>
        </w:styles>
        """;
}
