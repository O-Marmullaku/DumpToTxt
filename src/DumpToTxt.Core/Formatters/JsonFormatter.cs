using System.Text.Json;
using System.Text.Json.Serialization;

namespace DumpToTxt.Core;

/// <summary>JSON pack: a single object with summary fields, the directory tree as text,
/// and a fileList array of {path, size, content}. Serialized via System.Text.Json so all
/// escaping is correct and the output always parses.</summary>
public sealed class JsonFormatter : IDumpFormatter
{
    public OutputStyle Style => OutputStyle.Json;

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Render(DumpModel model, DumpConfig cfg)
    {
        var top = DirectoryTree.TopByTokens(model.Files, 10);
        bool scanOn = cfg.SecretScan != SecretScanMode.Off;
        var dto = new DumpJson
        {
            Root = model.Root,
            Files = model.Files.Count,
            TotalSize = model.TotalSize,
            TotalTokens = model.TotalTokens,
            TokenEncoding = TokenCounter.EncodingName(cfg.TokenEncoding),
            MaxTokens = cfg.MaxTokens > 0 ? cfg.MaxTokens : null,
            OverBudget = cfg.MaxTokens > 0 ? model.TotalTokens > cfg.MaxTokens : null,
            SecretScan = scanOn ? cfg.SecretScan.ToString() : null,
            SecretFindings = scanOn ? model.SecretFindingCount : null,
            FilesWithSecrets = scanOn ? model.FilesWithSecrets : null,
            SecretRuleTally = scanOn && model.SecretFindingCount > 0
                ? SecretScanner.RuleTally(model.Files).Select(kv => new SecretRuleJson { Rule = kv.Key, Count = kv.Value }).ToList()
                : null,
            DirectoryStructure = DirectoryTree.RenderStructureOrNote(model.Entries),
            TokenTree = top.Count > 0 ? DirectoryTree.RenderTokenTree(model.Files) : null,
            TopFilesByTokens = top.Count > 0
                ? top.Select(f => new TokenFileJson { Path = f.RelativePath, Tokens = f.TokenCount }).ToList()
                : null,
            FileList = model.Files.Select(f =>
            {
                bool secretSkipped = false;
                string body = f.IsBinary ? "" : SecretScanner.ContentForOutput(f, cfg.SecretScan, out secretSkipped);
                return new FileJson
                {
                    Path = f.RelativePath,
                    Size = f.Size,
                    Tokens = f.TokenCount,
                    Content = body,
                    Binary = f.IsBinary ? true : null,
                    Truncated = f.IsTruncated ? true : null,
                    Secrets = f.Secrets.Count > 0
                        ? f.Secrets.Select(s => new SecretJson { Rule = s.RuleId, Line = s.Line, Preview = s.Preview }).ToList()
                        : null,
                    SecretsSkipped = secretSkipped ? true : null,
                };
            }).ToList(),
        };
        return JsonSerializer.Serialize(dto, Opts);
    }

    private sealed class DumpJson
    {
        [JsonPropertyName("root")] public required string Root { get; init; }
        [JsonPropertyName("files")] public int Files { get; init; }
        [JsonPropertyName("totalSize")] public long TotalSize { get; init; }
        [JsonPropertyName("totalTokens")] public long TotalTokens { get; init; }
        [JsonPropertyName("tokenEncoding")] public required string TokenEncoding { get; init; }

        [JsonPropertyName("maxTokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? MaxTokens { get; init; }

        [JsonPropertyName("overBudget")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? OverBudget { get; init; }

        [JsonPropertyName("secretScan")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SecretScan { get; init; }

        [JsonPropertyName("secretFindings")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? SecretFindings { get; init; }

        [JsonPropertyName("filesWithSecrets")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? FilesWithSecrets { get; init; }

        [JsonPropertyName("secretRuleTally")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<SecretRuleJson>? SecretRuleTally { get; init; }

        [JsonPropertyName("directoryStructure")] public required string DirectoryStructure { get; init; }

        [JsonPropertyName("tokenTree")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TokenTree { get; init; }

        [JsonPropertyName("topFilesByTokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<TokenFileJson>? TopFilesByTokens { get; init; }

        [JsonPropertyName("fileList")] public required List<FileJson> FileList { get; init; }
    }

    private sealed class TokenFileJson
    {
        [JsonPropertyName("path")] public required string Path { get; init; }
        [JsonPropertyName("tokens")] public int Tokens { get; init; }
    }

    private sealed class SecretRuleJson
    {
        [JsonPropertyName("rule")] public required string Rule { get; init; }
        [JsonPropertyName("count")] public int Count { get; init; }
    }

    private sealed class SecretJson
    {
        [JsonPropertyName("rule")] public required string Rule { get; init; }
        [JsonPropertyName("line")] public int Line { get; init; }
        [JsonPropertyName("preview")] public required string Preview { get; init; }
    }

    private sealed class FileJson
    {
        [JsonPropertyName("path")] public required string Path { get; init; }
        [JsonPropertyName("size")] public long Size { get; init; }
        [JsonPropertyName("tokens")] public int Tokens { get; init; }
        [JsonPropertyName("content")] public required string Content { get; init; }

        [JsonPropertyName("binary")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Binary { get; init; }

        [JsonPropertyName("truncated")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Truncated { get; init; }

        [JsonPropertyName("secrets")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<SecretJson>? Secrets { get; init; }

        [JsonPropertyName("secretsSkipped")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? SecretsSkipped { get; init; }
    }
}
