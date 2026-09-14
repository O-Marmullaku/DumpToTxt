namespace DumpToTxt.Core;

/// <summary>A single file included in a dump, with its already-read text content.</summary>
public sealed class DumpFile
{
    public required string FullName { get; init; }
    /// <summary>Path relative to <see cref="DumpModel.Root"/>, using the OS separator.</summary>
    public required string RelativePath { get; init; }
    /// <summary>The text content to render. Empty for a binary file; truncated when <see cref="IsTruncated"/>.</summary>
    public required string Content { get; init; }
    // Staged paths are valid only for the duration of DumpEngine.Run. Public formatter callers
    // may still supply Content directly; file exports never materialize these paths as strings.
    internal string? SourceContentPath { get; init; }
    internal string? OutputContentPath { get; set; }
    internal bool ContentOmitted { get; set; }
    internal char LastCharacter { get; set; }
    internal int BacktickRun { get; set; }
    /// <summary>On-disk size in bytes.</summary>
    public long Size { get; init; }
    /// <summary>True when the file was detected as binary; its content is skipped and marked.</summary>
    public bool IsBinary { get; init; }
    /// <summary>True when the content was cut to satisfy a per-file or total size cap.</summary>
    public bool IsTruncated { get; init; }

    /// <summary>BPE token count of <see cref="Content"/> (0 for binary/skipped, or when token counting was
    /// not run — the Classic style with no budget skips it for speed).</summary>
    public int TokenCount { get; set; }

    /// <summary>Secrets detected in <see cref="Content"/> (empty when scanning is off or none found).
    /// Findings are shared by every formatter, which applies warn/redact/skip per the config.</summary>
    public IReadOnlyList<SecretFinding> Secrets { get; init; } = Array.Empty<SecretFinding>();
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

    /// <summary>True when <see cref="SkippedSingleFile"/> was skipped solely because it had no git
    /// changes (OnlyGitChanged mode) rather than being illegible/excluded — selects the Classic note text.</summary>
    public bool SingleFileSkippedUnchanged { get; init; }

    public long TotalSize
    {
        get
        {
            long t = 0;
            foreach (var f in Files) t += f.Size;
            return t;
        }
    }

    /// <summary>Sum of per-file token counts (0 when token counting was not run for this dump).</summary>
    public long TotalTokens
    {
        get
        {
            long t = 0;
            foreach (var f in Files) t += f.TokenCount;
            return t;
        }
    }

    /// <summary>Total number of secret findings across all files (0 when scanning was off / none found).</summary>
    public int SecretFindingCount
    {
        get
        {
            int t = 0;
            foreach (var f in Files) t += f.Secrets.Count;
            return t;
        }
    }

    /// <summary>Number of files carrying at least one secret finding.</summary>
    public int FilesWithSecrets
    {
        get
        {
            int t = 0;
            foreach (var f in Files) if (f.Secrets.Count > 0) t++;
            return t;
        }
    }
}
