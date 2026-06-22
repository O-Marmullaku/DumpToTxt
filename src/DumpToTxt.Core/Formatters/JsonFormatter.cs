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
        var dto = new DumpJson
        {
            Root = model.Root,
            Files = model.Files.Count,
            TotalSize = model.TotalSize,
            DirectoryStructure = DirectoryTree.RenderOrNote(model.Files),
            FileList = model.Files.Select(f => new FileJson
            {
                Path = f.RelativePath,
                Size = f.Size,
                Content = f.Content,
            }).ToList(),
        };
        return JsonSerializer.Serialize(dto, Opts);
    }

    private sealed class DumpJson
    {
        [JsonPropertyName("root")] public required string Root { get; init; }
        [JsonPropertyName("files")] public int Files { get; init; }
        [JsonPropertyName("totalSize")] public long TotalSize { get; init; }
        [JsonPropertyName("directoryStructure")] public required string DirectoryStructure { get; init; }
        [JsonPropertyName("fileList")] public required List<FileJson> FileList { get; init; }
    }

    private sealed class FileJson
    {
        [JsonPropertyName("path")] public required string Path { get; init; }
        [JsonPropertyName("size")] public long Size { get; init; }
        [JsonPropertyName("content")] public required string Content { get; init; }
    }
}
