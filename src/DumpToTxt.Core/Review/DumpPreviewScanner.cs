using System.Text.RegularExpressions;

namespace DumpToTxt.Core;

public sealed record DumpPreviewTypeCount(DumpFileTypeKey Type, int Count, bool IsBasic);
public sealed record DumpPreviewPath(string FullPath, string RelativePath, bool IsDirectory);
public sealed record DumpPreviewEntry(
    string FullPath, string RelativePath, DumpFileTypeKey Type, long TextBytes,
    long EstimatedTokens, bool IsBasic);
public sealed record DumpPreviewChange(DumpPreviewPath? Path = null, DumpPreviewEntry? Entry = null);
public sealed record DumpPreviewBatch(IReadOnlyList<DumpPreviewChange> Changes, int ScannedFiles, bool Completed,
    int DiagnosticCount, string? LastDiagnostic);

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
    // The scanner records each surviving path once. These are the inventory itself, not
    // a second event backlog. Snapshot-only callers need no consumer to keep scanning.
    private readonly List<(DumpPreviewEntry Entry, int PathIndex)> _entries = new();
    private readonly List<DumpPreviewPath> _paths = new();
    private int _scannedFiles;
    private bool _completed;
    private bool _accepting = true;
    private int _pathCursor, _entryCursor;
    private int _currentFilePathIndex;
    private int _diagnosticCount;
    private string? _lastDiagnostic;

    internal void RecordDiagnostic(string message)
    {
        lock (_gate) { _diagnosticCount++; _lastDiagnostic = message; }
    }

    public DumpPreviewBatch Drain(int limit = 512)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        lock (_gate)
        {
            var changes = new List<DumpPreviewChange>(Math.Min(limit,
                _paths.Count - _pathCursor + _entries.Count - _entryCursor));
            while (changes.Count < limit)
            {
                bool readyText = _entryCursor < _entries.Count && _entries[_entryCursor].PathIndex < _pathCursor;
                if (readyText) changes.Add(new(Entry: _entries[_entryCursor++].Entry));
                else if (_pathCursor < _paths.Count) changes.Add(new(Path: _paths[_pathCursor++]));
                else break;
            }
            return new(changes, _scannedFiles,
                _completed && _pathCursor == _paths.Count && _entryCursor == _entries.Count,
                _diagnosticCount, _lastDiagnostic);
        }
    }

    internal void RecordPath(FileSystemInfo info, string root, bool isDirectory)
    {
        lock (_gate)
        {
            if (!_accepting) return;
            string relative = Path.GetRelativePath(root, info.FullName);
            _paths.Add(new DumpPreviewPath(info.FullName, relative, isDirectory));
            if (!isDirectory) { _currentFilePathIndex = _paths.Count - 1; _scannedFiles++; }
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
            _entries.Add((new DumpPreviewEntry(
                file.FullName, relative, key, size, Math.Max(0, (size + 3) / 4), isBasic), _currentFilePathIndex));
        }
    }

    internal void Complete() { lock (_gate) _completed = true; }

    public DumpPreviewSnapshot Snapshot(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DumpPreviewSnapshot copy;
        lock (_gate) copy = CopySnapshotLocked();
        return SortSnapshot(copy, cancellationToken);
    }

    /// <summary>Atomically freezes future records and returns the final selection snapshot.</summary>
    public DumpPreviewSnapshot StopAndSnapshot()
    {
        DumpPreviewSnapshot copy;
        lock (_gate)
        {
            _accepting = false;
            copy = CopySnapshotLocked();
        }
        return SortSnapshot(copy, default);
    }

    private DumpPreviewSnapshot CopySnapshotLocked()
    {
        var types = _types.Select(x => new DumpPreviewTypeCount(x.Key, x.Value.Count, x.Value.Basic))
            .ToArray();
        var entries = _entries.Select(item => item.Entry).ToArray();
        var paths = _paths.ToArray();
        return new(types, entries, _scannedFiles, _completed) { Paths = paths };
    }

    private static DumpPreviewSnapshot SortSnapshot(DumpPreviewSnapshot copy, CancellationToken cancellationToken)
    {
        // Never hold the publication lock while ordering the complete inventory.
        Sort((DumpPreviewTypeCount[])copy.Types, item => item.Type.DisplayName, cancellationToken);
        Sort((DumpPreviewEntry[])copy.Entries, item => item.RelativePath, cancellationToken);
        Sort((DumpPreviewPath[])copy.Paths, item => item.RelativePath, cancellationToken);
        return copy;
    }

    private static void Sort<T>(T[] items, Func<T, string> key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Array.Sort(items, (a, b) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return StringComparer.OrdinalIgnoreCase.Compare(key(a), key(b));
            });
        }
        catch (InvalidOperationException error) when (error.InnerException is OperationCanceledException)
        {
            // Array.Sort wraps comparer exceptions; preserve cooperative cancellation.
            throw new OperationCanceledException(cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
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
            foreach (var entry in FileDiscovery.Enumerate(root, matcher, cancellationToken, state.RecordDiagnostic))
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
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { state.RecordDiagnostic($"Text check timed out: {relative}"); isText = false; }
            catch (InvalidDataException)
            { state.RecordDiagnostic($"Content unavailable: {relative}. The file may be binary or use an unsupported text encoding."); isText = false; }
            catch (IOException ex) { state.RecordDiagnostic($"{relative}: {ex.Message}"); isText = false; }
            catch (UnauthorizedAccessException ex) { state.RecordDiagnostic($"{relative}: {ex.Message}"); isText = false; }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (isText) state.RecordText(file, root, basic);
    }
}
