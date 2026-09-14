using System.Text.Encodings.Web;
using System.Text.Json;

namespace DumpToTxt.Core;

/// <summary>JSON metadata and file bodies are emitted incrementally, including escaped strings.</summary>
public sealed class JsonFormatter : IDumpFormatter
{
    private static readonly JsonSerializerOptions Values = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public OutputStyle Style { get; }
    private bool Compact => Style == OutputStyle.JsonCompact;

    public JsonFormatter(OutputStyle style = OutputStyle.Json)
    {
        if (style is not (OutputStyle.Json or OutputStyle.JsonCompact)) throw new ArgumentOutOfRangeException(nameof(style));
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
        bool first = true;
        writer.Write('{');
        void Name(string name, int depth = 1)
        {
            if (!first) writer.Write(',');
            if (!Compact) { writer.Write("\r\n"); writer.Write(new string(' ', depth * 2)); }
            writer.Write(JsonSerializer.Serialize(name));
            writer.Write(Compact ? ":" : ": ");
            first = false;
        }
        void Property(string name, object? value, int depth = 1)
        {
            Name(name, depth);
            writer.Write(JsonSerializer.Serialize(value, Values));
        }
        Property("root", model.Root);
        Property("files", model.Files.Count);
        Property("totalSize", model.TotalSize);
        Property("totalTokens", model.TotalTokens);
        Property("tokenEncoding", TokenCounter.EncodingName(cfg.TokenEncoding));
        if (cfg.MaxTokens > 0)
        {
            Property("maxTokens", cfg.MaxTokens);
            Property("overBudget", model.TotalTokens > cfg.MaxTokens);
        }
        if (cfg.SecretScan != SecretScanMode.Off)
        {
            Property("secretScan", cfg.SecretScan.ToString());
            Property("secretFindings", model.SecretFindingCount);
            Property("filesWithSecrets", model.FilesWithSecrets);
            if (model.SecretFindingCount > 0)
                Property("secretRuleTally", SecretScanner.RuleTally(model.Files).Select(kv => new { rule = kv.Key, count = kv.Value }));
        }
        Property("directoryStructure", DirectoryTree.RenderStructureOrNote(model.Entries));
        var top = DirectoryTree.TopByTokens(model.Files, 10);
        if (top.Count > 0)
        {
            Property("tokenTree", DirectoryTree.RenderTokenTree(model.Files));
            Property("topFilesByTokens", top.Select(f => new { path = f.RelativePath, tokens = f.TokenCount }));
        }
        Name("fileList");
        writer.Write('[');
        bool firstFile = true;
        foreach (var file in model.Files)
        {
            if (!firstFile) writer.Write(',');
            firstFile = false;
            if (!Compact) writer.Write("\r\n    ");
            writer.Write('{'); first = true;
            Property("path", file.RelativePath, 3);
            Property("size", file.Size, 3);
            Property("tokens", file.TokenCount, 3);
            Name("content", 3);
            var body = OutputBody.For(file, cfg.SecretScan, out bool skipped);
            if (file.IsBinary || skipped) writer.Write("\"\"");
            else
            {
                using var reader = body.Open();
                WriteString(reader, writer);
            }
            if (file.IsBinary) Property("binary", true, 3);
            if (file.IsTruncated) Property("truncated", true, 3);
            if (file.Secrets.Count > 0)
                Property("secrets", file.Secrets.Select(s => new { rule = s.RuleId, line = s.Line, preview = s.Preview }), 3);
            if (skipped) Property("secretsSkipped", true, 3);
            if (!Compact) writer.Write("\r\n    ");
            writer.Write('}');
        }
        if (!Compact && !firstFile) writer.Write("\r\n  ");
        writer.Write(']');
        if (!Compact) writer.Write("\r\n");
        writer.Write('}');
    }

    private static void WriteString(TextReader reader, TextWriter writer)
    {
        writer.Write('"');
        var buffer = new char[32768];
        int read;
        char high = '\0';
        while ((read = reader.Read(buffer, high == '\0' ? 0 : 1, buffer.Length - (high == '\0' ? 0 : 1))) > 0)
        {
            if (high != '\0') { buffer[0] = high; read++; high = '\0'; }
            if (char.IsHighSurrogate(buffer[read - 1])) high = buffer[--read];
            string encoded = JsonSerializer.Serialize(new string(buffer, 0, read), Values);
            writer.Write(encoded.AsSpan(1, encoded.Length - 2));
        }
        if (high != '\0') writer.Write("\\uFFFD");
        writer.Write('"');
    }
}