using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace DumpToTxt.Core;

/// <summary>Bounded real-file preview for the live workspace; never performs a second full traversal.</summary>
public static class DumpPreviewRenderer
{
    private const int MaxFileChars = 4_000;

    public static string Render(DumpPreviewSnapshot snapshot, DumpConfig cfg,
        DumpContentSelection selection, string? selectedPath = null)
    {
        IEnumerable<DumpPreviewEntry> chosen = snapshot.Entries.Where(e => Allows(e, cfg, selection));
        if (!string.IsNullOrWhiteSpace(selectedPath))
            chosen = chosen.Where(e => string.Equals(
                DumpContributionTree.Normalize(e.RelativePath),
                DumpContributionTree.Normalize(selectedPath), StringComparison.OrdinalIgnoreCase));
        var files = chosen.Take(string.IsNullOrWhiteSpace(selectedPath) ? 3 : 1)
            .Select(e => (Entry: e, Content: ReadBounded(e.FullPath))).ToArray();

        return OutputStyleCatalog.Format(cfg.Style) switch
        {
            OutputFormatKind.Markdown => Markdown(files, cfg.Style),
            OutputFormatKind.Json => Json(files, cfg.Style == OutputStyle.JsonCompact),
            OutputFormatKind.Xml => Xml(files, cfg.Style == OutputStyle.XmlCompact),
            OutputFormatKind.Word => Word(files),
            _ => Text(files, cfg.Style == OutputStyle.Classic),
        };
    }

    private static bool Allows(DumpPreviewEntry entry, DumpConfig cfg, DumpContentSelection selection)
    {
        var file = new FileInfo(entry.FullPath);
        string root = Path.GetDirectoryName(entry.FullPath)!;
        int segments = DumpContributionTree.Normalize(entry.RelativePath).Split('/').Length - 1;
        for (int i = 0; i < segments; i++) root = Path.GetDirectoryName(root)!;
        return selection.Allows(file, root);
    }

    private static string Text((DumpPreviewEntry Entry, string Content)[] files, bool classic)
    {
        var sb = new StringBuilder(classic ? "CLASSIC TEXT PREVIEW\r\n\r\n" : "CLEAN TEXT PREVIEW\r\n\r\n");
        foreach (var file in files)
            sb.Append("──── ").Append(file.Entry.RelativePath).Append(" ────\r\n")
              .Append(file.Content).Append("\r\n\r\n");
        return sb.ToString();
    }

    private static string Markdown((DumpPreviewEntry Entry, string Content)[] files, OutputStyle style)
    {
        var sb = new StringBuilder(style == OutputStyle.MarkdownAi
            ? "# AI-friendly Markdown preview\r\n" : "# Markdown preview\r\n");
        foreach (var file in files)
        {
            if (style == OutputStyle.MarkdownAi)
                sb.Append("\r\n<!-- file:start path=\"").Append(file.Entry.RelativePath).Append("\" -->");
            sb.Append("\r\n## File: `").Append(file.Entry.RelativePath).Append("`\r\n\r\n```")
              .Append(DirectoryTree.LanguageFor(file.Entry.FullPath)).Append("\r\n")
              .Append(file.Content).Append("\r\n```\r\n");
            if (style == OutputStyle.MarkdownAi)
                sb.Append("<!-- file:end path=\"").Append(file.Entry.RelativePath).Append("\" -->\r\n");
        }
        return sb.ToString();
    }

    private static string Json((DumpPreviewEntry Entry, string Content)[] files, bool compact) =>
        JsonSerializer.Serialize(new
        {
            preview = true,
            files = files.Select(x => new { path = x.Entry.RelativePath, content = x.Content }),
        }, new JsonSerializerOptions { WriteIndented = !compact });

    private static string Xml((DumpPreviewEntry Entry, string Content)[] files, bool compact)
    {
        var doc = new XDocument(new XElement("dumpPreview",
            files.Select(x => new XElement("file", new XAttribute("path", x.Entry.RelativePath), x.Content))));
        return doc.ToString(compact ? SaveOptions.DisableFormatting : SaveOptions.None);
    }

    private static string Word((DumpPreviewEntry Entry, string Content)[] files)
    {
        var sb = new StringBuilder("WORD DOCUMENT PREVIEW\r\n\r\nClickable file index\r\n");
        foreach (var file in files) sb.Append("• ").Append(file.Entry.RelativePath).Append("\r\n");
        foreach (var file in files)
            sb.Append("\r\n").Append(file.Entry.RelativePath).Append("\r\nBack to file index\r\n")
              .Append(file.Content).Append("\r\n");
        return sb.ToString();
    }

    private static string ReadBounded(string path)
    {
        try
        {
            using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
            var chars = new char[MaxFileChars];
            int count = reader.ReadBlock(chars, 0, chars.Length);
            string text = new(chars, 0, count);
            return count == MaxFileChars && !reader.EndOfStream ? text + "\r\n[…preview truncated…]" : text;
        }
        catch (Exception ex) { return $"[Preview unavailable: {ex.Message}]"; }
    }
}
