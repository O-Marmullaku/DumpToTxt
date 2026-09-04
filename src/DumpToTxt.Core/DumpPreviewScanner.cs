using System.Text.RegularExpressions;

namespace DumpToTxt.Core;

public sealed record DumpPreviewTypeCount(DumpFileTypeKey Type, int Count, bool IsBasic);
public sealed record DumpPreviewPath(string FullPath, string RelativePath, bool IsDirectory);
public sealed record DumpPreviewEntry(
    string FullPath, string RelativePath, DumpFileTypeKey Type, long TextBytes,
    long EstimatedTokens, bool IsBasic);

public sealed record DumpPreviewSnapshot(
    IReadOnlyList<DumpPreviewTypeCount> Types,
    IReadOnlyList<DumpPreviewEntry> Entries,
    int ScannedFiles,
    bool Completed)
{
    public IReadOnlyList<DumpPreviewPath> Paths { get; init; } = Array.Empty<DumpPreviewPath>();

    public DumpPreviewSnapshot(IReadOnlyList<DumpPreviewTypeCount> types, int scannedFiles, bool completed)
        : this(types, Array.Empty<DumpPreviewEntry>(), scannedFiles, completed) { }
}

/// <summary>Thread-safe progressively updated scan state read by the WinForms refresh timer.</summary>
public sealed class DumpPreviewScanState
{
    private readonly object _gate = new();
    private readonly Dictionary<DumpFileTypeKey, (int Count, bool Basic)> _types = new();
    private readonly Dictionary<string, DumpPreviewEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DumpPreviewPath> _paths = new(StringComparer.OrdinalIgnoreCase);
    private int _scannedFiles;
    private bool _completed;
    private bool _accepting = true;

    internal void RecordPath(FileSystemInfo info, string root, bool isDirectory)
    {
        lock (_gate)
        {
            if (!_accepting) return;
            string relative = Path.GetRelativePath(root, info.FullName);
            _paths[relative] = new DumpPreviewPath(info.FullName, relative, isDirectory);
            if (!isDirectory) _scannedFiles++;
        }
    }

    internal void RecordText(FileInfo file, string root, bool isBasic)
    {
        lock (_gate)
        {
            if (!_accepting) return;
            var key = DumpFileTypeKey.FromFileName(file.Name);
            if (_types.TryGetValue(key, out var current))
                _types[key] = (current.Count + 1, current.Basic || isBasic);
            else
                _types[key] = (1, isBasic);
            long size;
            try { size = file.Length; } catch { size = 0; }
            string relative = Path.GetRelativePath(root, file.FullName);
            _entries[relative] = new DumpPreviewEntry(
                file.FullName, relative, key, size, Math.Max(0, (size + 3) / 4), isBasic);
        }
    }

    internal void Complete() { lock (_gate) _completed = true; }

    public DumpPreviewSnapshot Snapshot()
    {
        lock (_gate) return SnapshotLocked();
    }

    /// <summary>Atomically freezes future records and returns the final selection snapshot.</summary>
    public DumpPreviewSnapshot StopAndSnapshot()
    {
        lock (_gate)
        {
            _accepting = false;
            return SnapshotLocked();
        }
    }

    private DumpPreviewSnapshot SnapshotLocked()
    {
        var types = _types.Select(x => new DumpPreviewTypeCount(x.Key, x.Value.Count, x.Value.Basic))
            .OrderBy(x => x.Type.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        var entries = _entries.Values
            .OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
        var paths = _paths.Values
            .OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(types, entries, _scannedFiles, _completed) { Paths = paths };
    }
}

/// <summary>Asynchronously discovers text-like content types without using the configured extension whitelist.</summary>
public static class DumpPreviewScanner
{
    public static Task ScanAsync(string targetPath, DumpConfig cfg, DumpPreviewScanState state,
        CancellationToken cancellationToken) =>
        Task.Run(() => Scan(targetPath, cfg, state, cancellationToken), cancellationToken);

    private static void Scan(string targetPath, DumpConfig cfg, DumpPreviewScanState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        targetPath = Path.GetFullPath(targetPath);
        bool isFile = File.Exists(targetPath);
        bool isDirectory = Directory.Exists(targetPath);
        if (!isFile && !isDirectory)
            throw new FileNotFoundException("Target path is missing or does not exist.", targetPath);

        string root = isFile ? Path.GetDirectoryName(targetPath)! : targetPath;
        var matcher = IgnoreMatcher.Build(root, cfg);
        var extensions = new HashSet<string>(cfg.ExtSet, StringComparer.OrdinalIgnoreCase);
        var dotFiles = new HashSet<string>(cfg.DotFilesAllow, StringComparer.OrdinalIgnoreCase);
        HashSet<string>? changed = cfg.OnlyGitChanged
            ? GitChanges.ChangedFiles(targetPath, cancellationToken) : null;

        if (isFile)
        {
            var file = new FileInfo(targetPath);
            state.RecordPath(file, root, isDirectory: false);
            ScanFile(file, root, matcher, extensions, dotFiles, changed, state,
                cancellationToken, excludeMinified: false);
        }
        else
        {
            foreach (var entry in FileDiscovery.Enumerate(root, matcher, cancellationToken))
            {
                state.RecordPath(entry.Info, root, entry.IsDirectory);
                if (!entry.IsDirectory && entry.Info is FileInfo file)
                    ScanFile(file, root, matcher, extensions, dotFiles, changed, state,
                        cancellationToken, excludeMinified: true);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        state.Complete();
    }

    private static void ScanFile(FileInfo file, string root, IgnoreMatcher matcher,
        HashSet<string> extensions, HashSet<string> dotFiles, HashSet<string>? changed,
        DumpPreviewScanState state, CancellationToken cancellationToken, bool excludeMinified)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string relative = Path.GetRelativePath(root, file.FullName);
        if (matcher.IsExcluded(file.FullName, relative, false)
            || !matcher.MatchesInclude(relative)
            || changed is not null && !changed.Contains(file.FullName)
            || excludeMinified && Regex.IsMatch(file.Name, @"\.min\.", RegexOptions.IgnoreCase)) return;

        bool basic = DumpContentRules.IsConfigured(file, extensions, dotFiles);
        bool isText;
        using (var sniffCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            sniffCancellation.CancelAfter(TimeSpan.FromSeconds(2));
            try { isText = TextFileClassifier.IsTextLike(file.FullName, sniffCancellation.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { isText = false; }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (isText) state.RecordText(file, root, basic);
    }
}
