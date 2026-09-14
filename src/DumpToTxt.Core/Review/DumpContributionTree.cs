namespace DumpToTxt.Core;

public sealed class DumpContributionNode
{
    private readonly List<DumpContributionNode> _children = new();

    public required string Name { get; init; }
    public required string RelativePath { get; init; }
    public required bool IsDirectory { get; init; }
    public int FileCount { get; internal set; }
    public long TextBytes { get; internal set; }
    public long EstimatedTokens { get; internal set; }
    public IReadOnlyList<DumpContributionNode> Children => _children;
    internal List<DumpContributionNode> MutableChildren => _children;

}

public static class DumpContributionTree
{
    public static DumpContributionNode Build(IReadOnlyList<DumpPreviewEntry> entries)
    {
        var paths = entries.Select(x => new DumpPreviewPath(x.FullPath, x.RelativePath, false)).ToArray();
        return Build(paths, entries);
    }

    public static DumpContributionNode Build(
        IReadOnlyList<DumpPreviewPath> paths,
        IReadOnlyList<DumpPreviewEntry> entries)
    {
        var root = new DumpContributionNode
        {
            Name = "All discovered files",
            RelativePath = "",
            IsDirectory = true,
        };

        var textByPath = entries.ToDictionary(
            x => Normalize(x.RelativePath), StringComparer.OrdinalIgnoreCase);
        var nodesByPath = new Dictionary<string, DumpContributionNode>(StringComparer.OrdinalIgnoreCase)
        {
            [""] = root,
        };

        foreach (var pathEntry in paths
                     .OrderBy(x => Normalize(x.RelativePath).Count(c => c == '/'))
                     .ThenBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var current = root;
            string normalized = Normalize(pathEntry.RelativePath);
            string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string path = string.Join('/', parts.Take(i + 1));
                bool final = i == parts.Length - 1;
                bool isDirectory = !final || pathEntry.IsDirectory;
                if (!nodesByPath.TryGetValue(path, out DumpContributionNode? child))
                {
                    textByPath.TryGetValue(path, out DumpPreviewEntry? textEntry);
                    child = new DumpContributionNode
                    {
                        Name = parts[i],
                        RelativePath = path,
                        IsDirectory = isDirectory,
                        FileCount = isDirectory ? 0 : 1,
                        TextBytes = textEntry?.TextBytes ?? 0,
                        EstimatedTokens = textEntry?.EstimatedTokens ?? 0,
                    };
                    current.MutableChildren.Add(child);
                    nodesByPath.Add(path, child);
                }
                current = child;
            }
        }

        AggregateAndSort(root);
        return root;
    }

    private static void AggregateAndSort(DumpContributionNode node)
    {
        foreach (var child in node.MutableChildren) AggregateAndSort(child);
        if (node.IsDirectory)
        {
            node.FileCount = node.MutableChildren.Sum(x => x.FileCount);
            node.TextBytes = node.MutableChildren.Sum(x => x.TextBytes);
            node.EstimatedTokens = node.MutableChildren.Sum(x => x.EstimatedTokens);
        }
        node.MutableChildren.Sort((a, b) =>
        {
            int value = b.TextBytes.CompareTo(a.TextBytes);
            return value != 0 ? value : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        });
    }

    internal static string Normalize(string path) => path.Replace('\\', '/').Trim('/');
}
