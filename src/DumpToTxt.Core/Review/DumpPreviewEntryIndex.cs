namespace DumpToTxt.Core;

/// <summary>Indexes preview entries once so live tree refreshes can fetch a subtree without rescanning every entry.</summary>
public sealed class DumpPreviewEntryIndex
{
    private static readonly IReadOnlyList<DumpPreviewEntry> Empty = Array.Empty<DumpPreviewEntry>();
    private readonly Dictionary<string, List<DumpPreviewEntry>> _byPath =
        new(StringComparer.OrdinalIgnoreCase);

    public DumpPreviewEntryIndex(IEnumerable<DumpPreviewEntry> entries)
    {
        foreach (DumpPreviewEntry entry in entries)
        {
            Add("", entry);
            string path = DumpContributionTree.Normalize(entry.RelativePath);
            for (int slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
                Add(path[..slash], entry);
            if (path.Length > 0) Add(path, entry);
        }
    }

    public IReadOnlyList<DumpPreviewEntry> Descendants(string relativePath) =>
        _byPath.TryGetValue(DumpContributionTree.Normalize(relativePath), out List<DumpPreviewEntry>? entries)
            ? entries
            : Empty;

    private void Add(string path, DumpPreviewEntry entry)
    {
        if (!_byPath.TryGetValue(path, out List<DumpPreviewEntry>? entries))
        {
            entries = new List<DumpPreviewEntry>();
            _byPath.Add(path, entries);
        }
        entries.Add(entry);
    }
}
