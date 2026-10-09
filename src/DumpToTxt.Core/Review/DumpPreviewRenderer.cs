using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace DumpToTxt.Core;

/// <summary>Bounded real-file preview for the live workspace; never performs a second full traversal.</summary>
public static class DumpPreviewRenderer
{
    private const int MaxFileChars = 4_000;
    private const int MaxSafetyBytes = 64 * 1024;

    public static string Render(DumpPreviewSnapshot snapshot, DumpConfig cfg,
        DumpContentSelection selection, string? selectedPath = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = new List<(DumpPreviewEntry Entry, string Content)>();
        long remaining = cfg.MaxTotalSizeBytes > 0 ? cfg.MaxTotalSizeBytes : long.MaxValue;
        foreach (var entry in snapshot.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Allows(entry, cfg, selection)) continue;
            long allowed = Math.Min(remaining, cfg.MaxFileSizeBytes > 0 ? cfg.MaxFileSizeBytes : long.MaxValue);
            remaining -= Math.Min(entry.TextBytes, allowed);
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                string relative = DumpContributionTree.Normalize(entry.RelativePath);
                string focus = DumpContributionTree.Normalize(selectedPath);
                if (!relative.Equals(focus, StringComparison.OrdinalIgnoreCase)
                    && !relative.StartsWith(focus + "/", StringComparison.OrdinalIgnoreCase)) continue;
            }
            files.Add((entry, ReadBounded(entry.FullPath, allowed, cfg, cancellationToken)));
            if (files.Count == (string.IsNullOrWhiteSpace(selectedPath) ? 3 : 1)) break;
        }
        var fileArray = files.ToArray();

        return OutputStyleCatalog.Format(cfg.Style) switch
        {
            OutputFormatKind.Markdown => Markdown(fileArray, cfg.Style),
            OutputFormatKind.Json => Json(fileArray, cfg.Style == OutputStyle.JsonCompact),
            OutputFormatKind.Xml => Xml(fileArray, cfg.Style == OutputStyle.XmlCompact),
            OutputFormatKind.Word => Word(fileArray),
            _ => Text(fileArray, cfg.Style == OutputStyle.Classic),
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

    private static string ReadBounded(string path, long allowed, DumpConfig cfg, CancellationToken cancellationToken)
    {
        try
        {
            if (allowed <= 0) return "[Content omitted by the total size limit]";
            TextFileClassifier.EnsureRegularFile(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > allowed)
                return "[Preview withheld: this file crosses the output size limit. Create the dump to apply the limit and sensitive-data review.]";
            if (stream.Length > MaxSafetyBytes)
                return "[Preview withheld: this file is too large for a complete preview safety scan. Create the dump for full processing.]";
            var bytes = new byte[MaxSafetyBytes + 1];
            int read = 0;
            while (read < bytes.Length)
            {
                int count = stream.ReadAsync(bytes.AsMemory(read), cancellationToken).AsTask().GetAwaiter().GetResult();
                if (count == 0) break;
                read += count;
            }
            if (read > MaxSafetyBytes || read > allowed) return "[Preview withheld: file changed beyond the preview limit]";
            var encoding = TextFileClassifier.DetectTextEncoding(bytes.AsSpan(0, read), out int bomLength, path);
            string text = encoding.GetString(bytes, bomLength, read - bomLength);
            cancellationToken.ThrowIfCancellationRequested();
            var findings = SecretScanner.Scan(text, cfg.SecretScanEntropy,
                SecretScanner.CompileAllowlist(cfg.SecretAllowlist), SecretScanner.CompilePatterns(cfg.SensitiveValuePatterns),
                includeAutomatic: cfg.SecretScan != SecretScanMode.Off);
            // Warn decisions have not been made yet: preview masks findings until the export review.
            var mode = cfg.SecretScan == SecretScanMode.Warn ? SecretScanMode.Redact : cfg.SecretScan;
            text = SecretScanner.ContentForOutput(text, findings, mode, out bool skipped);
            if (skipped) return "[Content omitted: sensitive information detected]";
            if (OutputStyleCatalog.Format(cfg.Style) is OutputFormatKind.Xml or OutputFormatKind.Word)
            {
                using var cleaned = new StringWriter();
                new XmlSafeWriter(cleaned).Write(text);
                text = cleaned.ToString();
            }
            int visibleLength = Math.Min(text.Length, MaxFileChars);
            if (visibleLength < text.Length && visibleLength > 0 && char.IsHighSurrogate(text[visibleLength - 1]))
                visibleLength--;
            return text.Length > visibleLength ? text[..visibleLength] + "\r\n[…preview truncated…]" : text;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return $"[Preview unavailable: {ex.Message}]"; }
    }
}
