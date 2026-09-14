namespace DumpToTxt.Core;

/// <summary>Complete logical inventory; native rows are only a paged view of this tree.</summary>
public sealed class DumpReviewNode
{
    internal readonly SortedSet<DumpReviewNode> SortedChildren = new(Comparer<DumpReviewNode>.Create((a, b) =>
    {
        int size = b.TextBytes.CompareTo(a.TextBytes);
        return size != 0 ? size : StringComparer.OrdinalIgnoreCase.Compare(a.RelativePath, b.RelativePath);
    }));
    public required string Name { get; init; }
    public required string RelativePath { get; init; }
    public bool IsDirectory { get; internal set; }
    public DumpReviewNode? Parent { get; init; }
    public long TextBytes { get; internal set; }
    public int TextCount { get; internal set; }
    internal long BasicBytes, SelectedBytes;
    internal int BasicCount, SelectedCount;
    internal long AssignmentEpoch, AggregateEpoch;
    internal DumpSelectionMode Assignment;
    internal bool HasText;
    public int ChildCount => SortedChildren.Count;
    public IEnumerable<DumpReviewNode> Page(int offset, int count) => SortedChildren.Skip(offset).Take(count);
}

/// <summary>Incremental aggregates with lazy subtree assignments. A toggle touches only its ancestors,
/// including when the subtree has never been expanded or is still being discovered.</summary>
public sealed class DumpReviewTree
{
    private readonly Dictionary<string, DumpReviewNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private long _epoch;
    public DumpReviewNode Root { get; } = new() { Name = "All discovered files", RelativePath = "", IsDirectory = true };
    public int PathCount => _nodes.Count - 1;

    public DumpReviewTree(DumpSelectionMode mode)
    {
        _nodes.Add("", Root);
        Select(Root, mode);
    }

    public DumpReviewNode? Find(string path) => _nodes.GetValueOrDefault(DumpContributionTree.Normalize(path));

    public DumpReviewNode AddPath(DumpPreviewPath path)
    {
        string normalized = DumpContributionTree.Normalize(path.RelativePath);
        if (_nodes.TryGetValue(normalized, out var known)) return known;
        int slash = normalized.LastIndexOf('/');
        string parentPath = slash < 0 ? "" : normalized[..slash];
        var parent = _nodes.GetValueOrDefault(parentPath) ?? AddPath(new("", parentPath, true));
        var node = new DumpReviewNode
        {
            Name = slash < 0 ? normalized : normalized[(slash + 1)..],
            RelativePath = normalized,
            IsDirectory = path.IsDirectory,
            Parent = parent,
        };
        _nodes.Add(normalized, node);
        parent.SortedChildren.Add(node);
        return node;
    }

    public void AddText(DumpPreviewEntry entry)
    {
        var node = AddPath(new(entry.FullPath, entry.RelativePath, false));
        if (node.HasText) return;
        node.HasText = true;
        var chain = Ancestors(node).Reverse().ToArray();
        foreach (var ancestor in chain) Ensure(ancestor);
        var mode = Effective(node).Mode;
        bool selected = mode == DumpSelectionMode.Thorough || mode == DumpSelectionMode.Basic && entry.IsBasic;
        foreach (var ancestor in chain) ancestor.Parent?.SortedChildren.Remove(ancestor);
        foreach (var ancestor in chain)
        {
            ancestor.TextBytes += entry.TextBytes;
            ancestor.TextCount++;
            if (entry.IsBasic) { ancestor.BasicBytes += entry.TextBytes; ancestor.BasicCount++; }
            if (selected) { ancestor.SelectedBytes += entry.TextBytes; ancestor.SelectedCount++; }
        }
        foreach (var ancestor in chain) ancestor.Parent?.SortedChildren.Add(ancestor);
    }

    public void Select(DumpReviewNode node, DumpSelectionMode mode)
    {
        var chain = Ancestors(node).Reverse().ToArray();
        foreach (var ancestor in chain) Ensure(ancestor);
        long oldBytes = node.SelectedBytes;
        int oldCount = node.SelectedCount;
        node.AssignmentEpoch = ++_epoch;
        node.Assignment = mode;
        Ensure(node);
        foreach (var ancestor in chain)
        {
            if (ancestor == node) continue;
            ancestor.SelectedBytes += node.SelectedBytes - oldBytes;
            ancestor.SelectedCount += node.SelectedCount - oldCount;
        }
    }

    public long SelectedBytes { get { Ensure(Root); return Root.SelectedBytes; } }
    public int SelectedCount { get { Ensure(Root); return Root.SelectedCount; } }

    public DumpNodeSelectionState State(DumpReviewNode node)
    {
        Ensure(node);
        if (node.TextCount == 0)
            return Effective(node).Mode == DumpSelectionMode.None && Effective(node).Epoch != Root.AssignmentEpoch
                ? DumpNodeSelectionState.Excluded : DumpNodeSelectionState.Included;
        return node.SelectedCount == 0 ? DumpNodeSelectionState.Excluded
            : node.SelectedCount == node.TextCount ? DumpNodeSelectionState.Included : DumpNodeSelectionState.Mixed;
    }

    private static IEnumerable<DumpReviewNode> Ancestors(DumpReviewNode node)
    {
        for (var current = node; current is not null; current = current.Parent) yield return current;
    }

    private static (long Epoch, DumpSelectionMode Mode) Effective(DumpReviewNode node)
    {
        long epoch = 0;
        var mode = DumpSelectionMode.None;
        foreach (var ancestor in Ancestors(node))
            if (ancestor.AssignmentEpoch > epoch) { epoch = ancestor.AssignmentEpoch; mode = ancestor.Assignment; }
        return (epoch, mode);
    }

    private static void Ensure(DumpReviewNode node)
    {
        var assignment = Effective(node);
        if (assignment.Epoch <= node.AggregateEpoch) return;
        node.AggregateEpoch = assignment.Epoch;
        node.SelectedBytes = assignment.Mode == DumpSelectionMode.Thorough ? node.TextBytes
            : assignment.Mode == DumpSelectionMode.Basic ? node.BasicBytes : 0;
        node.SelectedCount = assignment.Mode == DumpSelectionMode.Thorough ? node.TextCount
            : assignment.Mode == DumpSelectionMode.Basic ? node.BasicCount : 0;
    }
}
