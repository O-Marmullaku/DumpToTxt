namespace DumpToTxt.Core;

internal readonly record struct DiscoveredEntry(
    FileSystemInfo Info, string FullName, string RelativePath, bool IsDirectory);

/// <summary>Shared pruning filesystem walk used by the preview and authoritative dump.</summary>
internal static class FileDiscovery
{
    private static readonly EnumerationOptions TopLevel = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
    };

    public static IEnumerable<DiscoveredEntry> Enumerate(
        string root, IgnoreMatcher matcher, CancellationToken cancellationToken = default)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dir = stack.Pop();
            IEnumerator<FileSystemInfo> children;
            try { children = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", TopLevel).GetEnumerator(); }
            catch { continue; }

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
                    catch { break; }

                    bool isDirectory;
                    try { isDirectory = (info.Attributes & FileAttributes.Directory) != 0; }
                    catch { continue; }

                    string full = info.FullName;
                    string relative = Path.GetRelativePath(root, full);
                    if (matcher.IsExcluded(full, relative, isDirectory)) continue;

                    yield return new DiscoveredEntry(info, full, relative, isDirectory);
                    if (isDirectory) stack.Push(full);
                }
            }
        }
    }
}
