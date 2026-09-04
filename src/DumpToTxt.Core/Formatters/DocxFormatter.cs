using System.IO.Compression;
using System.Security;
using System.Text;

namespace DumpToTxt.Core;

/// <summary>Minimal dependency-free WordprocessingML package with internal file navigation.</summary>
public static class DocxFormatter
{
    public static byte[] RenderPackage(DumpModel model, DumpConfig cfg)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "[Content_Types].xml", ContentTypes);
            Write(zip, "_rels/.rels", RootRelationships);
            Write(zip, "word/styles.xml", Styles);
            Write(zip, "word/document.xml", Document(model, cfg));
        }
        return buffer.ToArray();
    }

    private static string Document(DumpModel model, DumpConfig cfg)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
          .Append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">")
          .Append("<w:body>")
          .Append(BookmarkParagraph("index", "0", "DumpToTxt file index", "Heading1"))
          .Append(Paragraph($"Root: {model.Root}"))
          .Append(Paragraph($"Files: {model.Files.Count:N0} · Estimated tokens: {model.TotalTokens:N0}"))
          .Append(Paragraph("Directory structure", "Heading2"));
        foreach (string line in DirectoryTree.RenderStructureOrNote(model.Entries).Split('\n'))
            sb.Append(Paragraph(line.TrimEnd('\r'), "Code"));

        sb.Append(Paragraph("Files", "Heading2"));
        for (int i = 0; i < model.Files.Count; i++)
            sb.Append(Hyperlink($"file_{i + 1}", model.Files[i].RelativePath));

        for (int i = 0; i < model.Files.Count; i++)
        {
            var file = model.Files[i];
            sb.Append(BookmarkParagraph($"file_{i + 1}", (i + 1).ToString(), file.RelativePath, "Heading1"));
            sb.Append(Hyperlink("index", "Back to file index"));
            if (file.IsBinary)
            {
                sb.Append(Paragraph("[binary file — content skipped]"));
                continue;
            }
            string body = SecretScanner.ContentForOutput(file, cfg.SecretScan, out bool skipped);
            if (skipped)
            {
                sb.Append(Paragraph("[content skipped: sensitive information detected]"));
                continue;
            }
            foreach (string line in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                sb.Append(Paragraph(line, "Code"));
            if (file.IsTruncated) sb.Append(Paragraph($"[truncated: file is {DirectoryTree.FormatSize(file.Size)}]"));
        }

        sb.Append("<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1080\" w:right=\"1080\" w:bottom=\"1080\" w:left=\"1080\"/></w:sectPr>")
          .Append("</w:body></w:document>");
        return sb.ToString();
    }

    private static string Paragraph(string text, string? style = null)
    {
        string properties = style is null ? "" : $"<w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr>";
        return $"<w:p>{properties}<w:r><w:t xml:space=\"preserve\">{Escape(text)}</w:t></w:r></w:p>";
    }

    private static string BookmarkParagraph(string name, string id, string text, string style) =>
        $"<w:p><w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr><w:bookmarkStart w:id=\"{id}\" w:name=\"{name}\"/>" +
        $"<w:r><w:t>{Escape(text)}</w:t></w:r><w:bookmarkEnd w:id=\"{id}\"/></w:p>";

    private static string Hyperlink(string anchor, string text) =>
        $"<w:p><w:hyperlink w:anchor=\"{anchor}\" w:history=\"1\"><w:r><w:rPr><w:rStyle w:val=\"Hyperlink\"/></w:rPr>" +
        $"<w:t>{Escape(text)}</w:t></w:r></w:hyperlink></w:p>";

    private static string Escape(string value) => SecurityElement.Escape(value) ?? "";

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
