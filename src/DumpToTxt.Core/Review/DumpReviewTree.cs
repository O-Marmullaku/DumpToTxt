namespace DumpToTxt.Core;

public enum DumpReviewSortKey { Discovery, Name, Contribution }
public enum DumpReviewSortDirection { Ascending, Descending }

public readonly record struct DumpReviewSort(DumpReviewSortKey Key, DumpReviewSortDirection Direction)
{
    public static DumpReviewSort DiscoveryAscending { get; } = new(DumpReviewSortKey.Discovery, DumpReviewSortDirection.Ascending);
    public static DumpReviewSort NameAscending { get; } = new(DumpReviewSortKey.Name, DumpReviewSortDirection.Ascending);
    public static DumpReviewSort NameDescending { get; } = new(DumpReviewSortKey.Name, DumpReviewSortDirection.Descending);
    public static DumpReviewSort ContributionAscending { get; } = new(DumpReviewSortKey.Contribution, DumpReviewSortDirection.Ascending);
    public static DumpReviewSort ContributionDescending { get; } = new(DumpReviewSortKey.Contribution, DumpReviewSortDirection.Descending);
}

/// <summary>Complete logical inventory; native rows are only a paged view of this tree.</summary>
public sealed class DumpReviewNode
{
    private readonly List<DumpReviewNode> _children = new();
    private readonly Dictionary<DumpReviewSort, (int Version, DumpReviewNode[] Nodes)> _ordered = new();
    private int _nameVersion;
    private int _contributionVersion;

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
    public int ChildCount => _children.Count;

    /// <summary>Compatibility page ordering: largest contribution first.</summary>
    public IEnumerable<DumpReviewNode> Page(int offset, int count) =>
        Page(offset, count, DumpReviewSort.ContributionDescending);

    public IEnumerable<DumpReviewNode> Page(int offset, int count, DumpReviewSort sort)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0 || offset >= _children.Count) return Array.Empty<DumpReviewNode>();

        if (sort.Key == DumpReviewSortKey.Discovery)
        {
            if (sort.Direction == DumpReviewSortDirection.Ascending)
                return _children.Skip(offset).Take(count);
            return _children.AsEnumerable().Reverse().Skip(offset).Take(count);
        }

        int version = sort.Key == DumpReviewSortKey.Name ? _nameVersion : _contributionVersion;
        if (!_ordered.TryGetValue(sort, out var cached) || cached.Version != version)
        {
            var nodes = _children.ToArray();
            Array.Sort(nodes, (left, right) => Compare(left, right, sort));
            cached = (version, nodes);
            _ordered[sort] = cached;
        }
        return cached.Nodes.Skip(offset).Take(count);
    }

    internal void AddChild(DumpReviewNode child)
    {
        _children.Add(child);
        _nameVersion++;
        _contributionVersion++;
        _ordered.Clear();
    }

    internal void ChildContributionChanged()
    {
        _contributionVersion++;
        _ordered.Remove(DumpReviewSort.ContributionAscending);
        _ordered.Remove(DumpReviewSort.ContributionDescending);
    }

    private static int Compare(DumpReviewNode left, DumpReviewNode right, DumpReviewSort sort)
    {
        if (sort.Key == DumpReviewSortKey.Contribution)
        {
            int size = left.TextBytes.CompareTo(right.TextBytes);
            if (sort.Direction == DumpReviewSortDirection.Descending) size = -size;
            if (size != 0) return size;
            return CompareName(left, right, DumpReviewSortDirection.Ascending);
        }
        return CompareName(left, right, sort.Direction);
    }

    private static int CompareName(DumpReviewNode left, DumpReviewNode right, DumpReviewSortDirection direction)
    {
        int result = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        if (result == 0) result = StringComparer.Ordinal.Compare(left.Name, right.Name);
        if (result == 0) result = StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath);
        if (result == 0) result = StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath);
        return direction == DumpReviewSortDirection.Ascending ? result : -result;
    }
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
        parent.AddChild(node);
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
        foreach (var ancestor in chain)
        {
            ancestor.TextBytes += entry.TextBytes;
            ancestor.TextCount++;
            if (entry.IsBasic) { ancestor.BasicBytes += entry.TextBytes; ancestor.BasicCount++; }
            if (selected) { ancestor.SelectedBytes += entry.TextBytes; ancestor.SelectedCount++; }
            ancestor.Parent?.ChildContributionChanged();
        }
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
        {
            var effective = Effective(node);
            return effective.Mode == DumpSelectionMode.None && effective.Epoch != Root.AssignmentEpoch
                ? DumpNodeSelectionState.Excluded : DumpNodeSelectionState.Included;
        }
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
