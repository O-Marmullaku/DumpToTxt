namespace DumpToTxt.Core;

public enum DumpSelectionMode { Basic, Thorough, None }
public sealed record DumpFileTypeChoice(
    DumpFileTypeKey Type, int Count, bool IsBasic, bool Selected);

/// <summary>UI-independent shortcut/manual-selection state.</summary>
public sealed class DumpSelectionModel
{
    private sealed class Item
    {
        public int Count;
        public bool Basic;
        public bool Selected;
        public bool Manual;
    }

    private readonly Dictionary<DumpFileTypeKey, Item> _items = new();
    private DumpSelectionMode _mode = DumpSelectionMode.None;

    public IReadOnlyList<DumpFileTypeChoice> Choices => _items
        .Select(x => new DumpFileTypeChoice(x.Key, x.Value.Count, x.Value.Basic, x.Value.Selected))
        .OrderBy(x => x.Type.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();

    public void Apply(DumpPreviewSnapshot snapshot)
    {
        foreach (var incoming in snapshot.Types)
        {
            if (!_items.TryGetValue(incoming.Type, out var item))
            {
                item = new Item
                {
                    Selected = _mode == DumpSelectionMode.Thorough
                        || _mode == DumpSelectionMode.Basic && incoming.IsBasic,
                };
                _items.Add(incoming.Type, item);
            }

            bool becameBasic = !item.Basic && incoming.IsBasic;
            item.Count = incoming.Count;
            item.Basic |= incoming.IsBasic;
            if (becameBasic && _mode == DumpSelectionMode.Basic && !item.Manual) item.Selected = true;
        }
    }

    public void SelectMode(DumpSelectionMode mode)
    {
        _mode = mode;
        foreach (var item in _items.Values)
        {
            item.Manual = false;
            item.Selected = mode == DumpSelectionMode.Thorough
                || mode == DumpSelectionMode.Basic && item.Basic;
        }
    }

    public void SetSelected(DumpFileTypeKey type, bool selected)
    {
        if (!_items.TryGetValue(type, out var item)) return;
        item.Selected = selected;
        item.Manual = true;
    }

    public DumpContentSelection Capture() =>
        DumpContentSelection.FromTypes(_items.Where(x => x.Value.Selected).Select(x => x.Key));

    public DumpContentSelection Capture(DumpPreviewSnapshot latestSnapshot)
    {
        Apply(latestSnapshot);
        return Capture();
    }
}
