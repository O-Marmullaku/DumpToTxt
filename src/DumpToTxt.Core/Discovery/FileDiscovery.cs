namespace DumpToTxt.Core;

internal readonly record struct DiscoveredEntry(
    FileSystemInfo Info, string FullName, string RelativePath, bool IsDirectory);

/// <summary>Shared pruning filesystem walk used by the preview and authoritative dump.</summary>
internal static class FileDiscovery
{
    private static readonly EnumerationOptions TopLevel = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
    };

    public static IEnumerable<DiscoveredEntry> Enumerate(
        string root, IgnoreMatcher matcher, CancellationToken cancellationToken = default,
        Action<string>? reportDiagnostic = null)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dir = stack.Pop();
            IEnumerator<FileSystemInfo> children;
            try { children = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", TopLevel).GetEnumerator(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Report($"Could not enumerate '{dir}': {ex.Message}", reportDiagnostic); continue; }

            using (children)
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileSystemInfo info;
                    try
                    {
                        if (!children.MoveNext()) break;
                        info = children.Current;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { Report($"Could not enumerate '{dir}': {ex.Message}", reportDiagnostic); break; }

                    FileAttributes attributes;
                    try { attributes = info.Attributes; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { Report($"Could not inspect '{info.FullName}': {ex.Message}", reportDiagnostic); continue; }
                    bool isDirectory = (attributes & FileAttributes.Directory) != 0;

                    string full = info.FullName;
                    string relative = Path.GetRelativePath(root, full);
                    if (matcher.IsExcluded(full, relative, isDirectory)) continue;

                    yield return new DiscoveredEntry(info, full, relative, isDirectory);
                    if (isDirectory)
                    {
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                            Report($"Directory link was listed but not traversed: '{full}'.", reportDiagnostic);
                        else stack.Push(full);
                    }
                }
            }
        }
    }

    private static void Report(string message, Action<string>? reportDiagnostic)
    {
        if (reportDiagnostic is null) throw new IOException(message);
        reportDiagnostic(message);
    }
}
