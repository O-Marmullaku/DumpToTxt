namespace DumpToTxt.Core;

public enum DumpNodeSelectionState { Excluded, Included, Mixed }

/// <summary>Transient nearest-ancestor include/exclude overrides for one review run.</summary>
public sealed class DumpPathSelectionModel
{
    private readonly Dictionary<string, bool> _overrides = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, bool> Overrides => _overrides;

    public void Set(string path, bool isDirectory, bool included)
    {
        string normalized = DumpContributionTree.Normalize(path);
        if (isDirectory)
        {
            string prefix = normalized.Length == 0 ? "" : normalized + "/";
            foreach (string key in _overrides.Keys.Where(x =>
                         prefix.Length == 0 || x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
                _overrides.Remove(key);
        }
        _overrides[normalized] = included;
    }

    public void Clear() => _overrides.Clear();

    public bool IsIncluded(string path, bool defaultIncluded)
    {
        string normalized = DumpContributionTree.Normalize(path);
        bool result = defaultIncluded;
        int bestLength = -1;
        foreach (var pair in _overrides)
        {
            bool matches = pair.Key.Length == 0
                || string.Equals(normalized, pair.Key, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(pair.Key + "/", StringComparison.OrdinalIgnoreCase);
            if (matches && pair.Key.Length > bestLength)
            {
                bestLength = pair.Key.Length;
                result = pair.Value;
            }
        }
        return result;
    }

    public DumpNodeSelectionState State(
        string path,
        IEnumerable<(string Path, bool DefaultIncluded)> descendants)
    {
        var states = descendants.Select(x => IsIncluded(x.Path, x.DefaultIncluded)).Distinct().Take(2).ToArray();
        if (states.Length == 0) return IsIncluded(path, true)
            ? DumpNodeSelectionState.Included : DumpNodeSelectionState.Excluded;
        if (states.Length > 1) return DumpNodeSelectionState.Mixed;
        return states[0] ? DumpNodeSelectionState.Included : DumpNodeSelectionState.Excluded;
    }
}
