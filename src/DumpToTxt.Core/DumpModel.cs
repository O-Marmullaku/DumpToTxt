namespace DumpToTxt.Core;

/// <summary>A single file included in a dump, with its already-read text content.</summary>
public sealed class DumpFile
{
    public required string FullName { get; init; }
    /// <summary>Path relative to <see cref="DumpModel.Root"/>, using the OS separator.</summary>
    public required string RelativePath { get; init; }
    /// <summary>The text content to render. Empty for a binary file; truncated when <see cref="IsTruncated"/>.</summary>
    public required string Content { get; init; }
    /// <summary>On-disk size in bytes.</summary>
    public long Size { get; init; }
    /// <summary>True when the file was detected as binary; its content is skipped and marked.</summary>
    public bool IsBinary { get; init; }
    /// <summary>True when the content was cut to satisfy a per-file or total size cap.</summary>
    public bool IsTruncated { get; init; }
}

/// <summary>A filesystem entry (file or directory) that survived the ignore filters.</summary>
public sealed class ListedEntry
{
    public required string FullName { get; init; }
    /// <summary>Path relative to <see cref="DumpModel.Root"/>, using the OS separator.</summary>
    public required string RelativePath { get; init; }
    public required bool IsDirectory { get; init; }
}

/// <summary>
/// The gathered, formatter-agnostic result of walking a target: the filtered directory
/// listing plus the legible files with their contents. Every <see cref="IDumpFormatter"/>
/// renders from this same model, so the walk happens once and all styles see identical data.
/// </summary>
public sealed class DumpModel
{
    /// <summary>The folder the dump is rooted at (the target folder, or a file's parent).</summary>
    public required string Root { get; init; }

    /// <summary>The original target path (file or folder) the user asked to dump.</summary>
    public required string TargetPath { get; init; }

    /// <summary>True when the target was a single file rather than a folder.</summary>
    public required bool IsSingleFile { get; init; }

    /// <summary>
    /// Every filesystem entry (files and folders) under the root that survived the ignore filters,
    /// sorted by full path (OrdinalIgnoreCase) for cross-machine determinism. Drives the Classic
    /// "DIRECTORY LIST" section (full paths) and the non-Classic full-structure directory tree.
    /// </summary>
    public required IReadOnlyList<ListedEntry> Entries { get; init; }

    /// <summary>The legible files whose contents are included, in dump order.</summary>
    public required IReadOnlyList<DumpFile> Files { get; init; }

    /// <summary>
    /// Set only when the target was a single file that was filtered out (not legible / excluded);
    /// carries the path so Classic can emit its legacy "[Skipped: ...]" note. Null otherwise.
    /// </summary>
    public string? SkippedSingleFile { get; init; }

    public long TotalSize
    {
        get
        {
            long t = 0;
            foreach (var f in Files) t += f.Size;
            return t;
        }
    }
}
