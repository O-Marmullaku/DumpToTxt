using System.Drawing;
using System.Runtime.InteropServices;
using DumpToTxt.Core;

namespace DumpToTxt.App;

internal sealed class ContributionTreeView : TreeView
{
    private sealed class RowMetrics
    {
        public required string DisplayName { get; set; }
        public required string Size { get; set; }
        public double Share { get; set; }
    }

    public const int SizeColumnWidth = 92;
    public const int ShareColumnWidth = 92;
    public const int FolderImageIndex = 0;
    public const int FileImageIndex = 1;

    public event EventHandler<TreeNode>? ToggleRequested;
    public event EventHandler<TreeNode>? PreviewRequested;

    private readonly Dictionary<TreeNode, RowMetrics> _metrics = new();
    private TreeNode? _hotNode;

    public Func<TreeNode, DumpNodeSelectionState>? StateProvider { get; set; }

    private const int VerticalScrollStyle = 0x00200000;
    private const int HorizontalScrollStyle = 0x00100000;
    private const int NoHorizontalScrollStyle = 0x8000;
    private bool _restoringScrollBar;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowStyle(IntPtr window, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowScrollBar(IntPtr window, int bar, [MarshalAs(UnmanagedType.Bool)] bool show);

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.Style = (parameters.Style | VerticalScrollStyle | NoHorizontalScrollStyle)
                & ~HorizontalScrollStyle;
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        const int styleChanging = 0x007c;
        const int windowStyle = -16;
        if (message.Msg == styleChanging && message.WParam == (IntPtr)windowStyle && message.LParam != IntPtr.Zero)
        {
            // STYLESTRUCT contains two DWORDs: old style, then proposed style.
            // Keep a permanent vertical gutter while native ranges grow/shrink.
            int proposed = Marshal.ReadInt32(message.LParam, sizeof(int));
            Marshal.WriteInt32(message.LParam, sizeof(int),
                (proposed | VerticalScrollStyle | NoHorizontalScrollStyle) & ~HorizontalScrollStyle);
        }
        base.WndProc(ref message);
        // Native range updates can hide a scrollbar without WM_STYLECHANGING.
        // Restore it before returning to the message loop; nested messages are guarded.
        if (!_restoringScrollBar && !Disposing && !IsDisposed && IsHandleCreated
            && message.Msg != 0x0002 && message.Msg != 0x0082
            && (GetWindowStyle(Handle, windowStyle) & VerticalScrollStyle) == 0)
        {
            _restoringScrollBar = true;
            try { ShowScrollBar(Handle, 1, true); }
            finally { _restoringScrollBar = false; }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr mask, IntPtr value);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Buffer the native common control, which owns TreeView painting.
        const uint setExtendedStyle = 0x1100 + 44;
        const int doubleBuffer = 0x0004;
        SendMessage(Handle, setExtendedStyle, (IntPtr)doubleBuffer, (IntPtr)doubleBuffer);
    }

    public ContributionTreeView()
    {
        BorderStyle = BorderStyle.None;
        DrawMode = TreeViewDrawMode.OwnerDrawAll;
        FullRowSelect = true;
        HideSelection = false;
        HotTracking = false;
        Indent = 20;
        ShowLines = false;
        ShowPlusMinus = false;
        ShowRootLines = false;
        ShowNodeToolTips = false;
        ItemHeight = 37;
        Font = UiTheme.UiFont(9f);
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Text;
        AccessibleName = "Content map";
        AccessibleDescription = "Files and folders with Included, Mixed, or Excluded selection state. Press Space to change a row.";
        ImageList = BuildNodeImages();
        MouseMove += (_, e) =>
        {
            TreeNode? next = NodeAtRow(e.Y);
            if (next == _hotNode) return;
            TreeNode? previous = _hotNode;
            _hotNode = next;
            InvalidateRow(previous);
            InvalidateRow(next);
        };
        MouseLeave += (_, _) =>
        {
            TreeNode? previous = _hotNode;
            _hotNode = null;
            InvalidateRow(previous);
        };
        MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) HandlePointer(e.Location);
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Space || SelectedNode is null) return;
            ToggleRequested?.Invoke(this, SelectedNode);
            e.Handled = true;
            e.SuppressKeyPress = true;
        };
    }

    public void SetMetrics(TreeNode node, string displayName, string size, double share, bool invalidate = true)
    {
        double value = Math.Round(Math.Clamp(share, 0, 100), 1, MidpointRounding.AwayFromZero);
        if (!_metrics.TryGetValue(node, out RowMetrics? metrics))
        {
            metrics = new RowMetrics { DisplayName = displayName, Size = size, Share = value };
            _metrics[node] = metrics;
        }
        else
        {
            if (metrics.DisplayName == displayName && metrics.Size == size && metrics.Share == value) return;
            metrics.DisplayName = displayName;
            metrics.Size = size;
            metrics.Share = value;
        }
        if (invalidate) InvalidateRow(node);
    }

    public DumpNodeSelectionState GetNodeState(TreeNode node) =>
        StateProvider?.Invoke(node) ?? DumpNodeSelectionState.Excluded;

    public void Forget(TreeNode node)
    {
        _metrics.Remove(node);
        if (_hotNode == node) _hotNode = null;
    }

    public void AnnounceDescription() => AccessibilityNotifyClients(AccessibleEvents.DescriptionChange, 0);

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Enter && SelectedNode is not null)
        {
            PreviewRequested?.Invoke(this, SelectedNode);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            base.OnMouseDoubleClick(e);
            return;
        }

        TreeNode? node = NodeAtRow(e.Y);
        if (node is null)
        {
            base.OnMouseDoubleClick(e);
            return;
        }

        SelectedNode = node;
        if (IsExpanderHit(node, e.Location) || IsToggleHit(node, e.Location)) return;
        PreviewRequested?.Invoke(this, node);
    }

    protected override void OnDrawNode(DrawTreeNodeEventArgs e)
    {
        if (e.Node is null || !_metrics.TryGetValue(e.Node, out RowMetrics? metrics))
        {
            e.DrawDefault = true;
            return;
        }

        var row = new Rectangle(0, e.Bounds.Top, ClientSize.Width, e.Bounds.Height);
        DumpNodeSelectionState state = GetNodeState(e.Node);
        bool selected = e.Node == SelectedNode;
        bool excluded = state == DumpNodeSelectionState.Excluded;
        Color background = selected ? UiTheme.AccentSoft
            : excluded ? UiTheme.Window
            : e.Node == _hotNode ? UiTheme.ControlHover
            : UiTheme.Surface;
        using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, row);
        using (var pen = new Pen(UiTheme.Rail))
            e.Graphics.DrawLine(pen, 0, row.Bottom - 1, row.Right, row.Bottom - 1);

        int branchX = 8 + (e.Node.Level * Indent);
        int checkX = branchX + 18;
        int iconX = checkX + 20;
        int nameX = iconX + 22;
        int shareX = Math.Max(nameX + 80, ClientSize.Width - ShareColumnWidth);
        int sizeX = Math.Max(nameX + 40, shareX - SizeColumnWidth);

        if (e.Node.Nodes.Count > 0) DrawExpander(e.Graphics, e.Node, branchX, row.Top + (row.Height / 2));
        DrawState(e.Graphics, state, new Rectangle(checkX, row.Top + ((row.Height - 16) / 2), 16, 16));
        ImageList?.Draw(e.Graphics, iconX, row.Top + ((row.Height - 16) / 2), 16, 16, e.Node.ImageIndex);

        Color text = excluded ? UiTheme.DisabledText : UiTheme.Text;
        TextRenderer.DrawText(e.Graphics, metrics.DisplayName, Font,
            new Rectangle(nameX, row.Top, Math.Max(20, sizeX - nameX - 8), row.Height), text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, metrics.Size, Font,
            new Rectangle(sizeX, row.Top, Math.Max(0, shareX - sizeX - 12), row.Height), text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix);

        int barWidth = 36;
        var bar = new Rectangle(shareX + 8, row.Top + ((row.Height - 4) / 2), barWidth, 4);
        using (var track = new SolidBrush(UiTheme.Border)) e.Graphics.FillRectangle(track, bar);
        if (!excluded && metrics.Share > 0)
        {
            var fill = new Rectangle(bar.X, bar.Y, Math.Max(1, (int)Math.Round(barWidth * metrics.Share / 100d)), bar.Height);
            using var accent = new SolidBrush(UiTheme.Accent);
            e.Graphics.FillRectangle(accent, fill);
        }
        TextRenderer.DrawText(e.Graphics, $"{metrics.Share:0.#}%", Font,
            new Rectangle(bar.Right + 5, row.Top, Math.Max(0, ClientSize.Width - bar.Right - 11), row.Height), text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix);

        if (selected && Focused)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(row, -2, -2), text, background);
    }

    private void HandlePointer(Point location)
    {
        TreeNode? node = NodeAtRow(location.Y);
        if (node is null) return;
        SelectedNode = node;
        if (IsExpanderHit(node, location)) node.Toggle();
        else if (IsToggleHit(node, location)) ToggleRequested?.Invoke(this, node);
        InvalidateRow(node);
    }

    private bool IsExpanderHit(TreeNode node, Point location) =>
        node.Nodes.Count > 0 && ExpanderBounds(node).Contains(location);

    private bool IsToggleHit(TreeNode node, Point location) => StateBounds(node).Contains(location);

    private Rectangle ExpanderBounds(TreeNode node)
    {
        int branchX = 8 + (node.Level * Indent);
        return new Rectangle(branchX, node.Bounds.Top + ((ItemHeight - 16) / 2), 16, 16);
    }

    private Rectangle StateBounds(TreeNode node)
    {
        int branchX = 8 + (node.Level * Indent);
        return new Rectangle(branchX + 18, node.Bounds.Top + ((ItemHeight - 16) / 2), 16, 16);
    }

    private TreeNode? NodeAtRow(int y)
    {
        for (TreeNode? node = TopNode; node is not null; node = node.NextVisibleNode)
        {
            if (y >= node.Bounds.Top && y < node.Bounds.Top + ItemHeight) return node;
            if (node.Bounds.Top > y) break;
        }
        return null;
    }

    private void InvalidateRow(TreeNode? node)
    {
        if (node is { IsVisible: true })
            Invalidate(new Rectangle(0, node.Bounds.Top, ClientSize.Width, node.Bounds.Height));
    }

    private static void DrawExpander(Graphics graphics, TreeNode node, int x, int centerY)
    {
        Point[] points = node.IsExpanded
            ? [new(x + 3, centerY - 3), new(x + 7, centerY + 1), new(x + 11, centerY - 3)]
            : [new(x + 4, centerY - 5), new(x + 9, centerY), new(x + 4, centerY + 5)];
        using var pen = new Pen(UiTheme.Muted, 1.4f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        graphics.DrawLines(pen, points);
    }

    private static void DrawState(Graphics graphics, DumpNodeSelectionState state, Rectangle bounds)
    {
        if (state == DumpNodeSelectionState.Excluded)
        {
            using var border = new Pen(UiTheme.DisabledText);
            using var fill = new SolidBrush(Color.White);
            graphics.FillRectangle(fill, bounds);
            graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
            return;
        }

        using (var fill = new SolidBrush(UiTheme.Accent)) graphics.FillRectangle(fill, bounds);
        if (state == DumpNodeSelectionState.Mixed)
        {
            using var dash = new Pen(Color.White, 2f);
            graphics.DrawLine(dash, bounds.X + 4, bounds.Y + 8, bounds.Right - 4, bounds.Y + 8);
            return;
        }

        using var check = new Pen(Color.White, 2f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
        };
        graphics.DrawLines(check, new Point[]
        {
            new Point(bounds.X + 4, bounds.Y + 8),
            new Point(bounds.X + 7, bounds.Y + 11),
            new Point(bounds.X + 12, bounds.Y + 5),
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ImageList?.Dispose();
            _metrics.Clear();
        }
        base.Dispose(disposing);
    }

    private static ImageList BuildNodeImages()
    {
        var list = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
        list.Images.Add(FolderImage());
        list.Images.Add(FileImage());
        return list;
    }

    private static Bitmap FolderImage()
    {
        var image = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.Transparent);
        using var back = new SolidBrush(Color.FromArgb(232, 172, 44));
        using var front = new SolidBrush(Color.FromArgb(242, 184, 55));
        graphics.FillRectangle(back, 1, 4, 14, 10);
        graphics.FillRectangle(front, 2, 3, 6, 3);
        graphics.FillRectangle(front, 2, 6, 12, 7);
        return image;
    }

    private static Bitmap FileImage()
    {
        var image = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.Transparent);
        using var fill = new SolidBrush(Color.White);
        using var border = new Pen(UiTheme.DisabledText);
        Point[] shape = [new(3, 1), new(10, 1), new(14, 5), new(14, 15), new(3, 15)];
        graphics.FillPolygon(fill, shape);
        graphics.DrawPolygon(border, shape);
        graphics.DrawLine(border, 10, 1, 10, 5);
        graphics.DrawLine(border, 10, 5, 14, 5);
        return image;
    }
}
