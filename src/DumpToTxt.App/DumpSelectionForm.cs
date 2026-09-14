using System.Drawing;
using DumpToTxt.Core;

namespace DumpToTxt.App;

/// <summary>One-window, progressively updated review workspace shown before the authoritative dump.</summary>
public sealed class DumpSelectionForm : Form
{
    private const int RailContentWidth = 252;

    private sealed record NodeTag(DumpReviewNode Node);
    private sealed record PageTag(DumpReviewNode Parent, int Offset);
    private sealed record FolderViewTag(DumpReviewNode Folder);
    private enum SortColumn { Name, TextSize, Share }
    private const int PageSize = 64;
    private const int MaxRealizedNodes = 1600;
    private readonly Dictionary<string, TreeNode> _realized = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _compactedParents = new(StringComparer.OrdinalIgnoreCase);
    private DumpReviewTree _review = null!;
    private DumpReviewNode _viewRoot = null!;
    private bool _scanComplete, _refreshingNodes, _started;
    private int _scanGeneration, _previewGeneration;
    private CancellationTokenSource? _previewCancellation;
    private SortColumn _sortColumn = SortColumn.Name;
    private DumpReviewSortDirection _sortDirection = DumpReviewSortDirection.Ascending;
    private sealed record ModeOption(DumpSelectionMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly string _targetPath;
    private readonly DumpConfig _config;
    private DumpPreviewScanState _scanState = new();
    private readonly DumpPathSelectionModel _pathSelection = new();
    private CancellationTokenSource _scanCancellation = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 30 };
    private readonly ComboBox _formatCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _layoutCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly RadioButton _clipboard = new() { Text = "Copy to clipboard", AutoSize = true };
    private readonly RadioButton _console = new() { Text = "Write to console", AutoSize = true };
    private readonly RadioButton _file = new() { Text = "Save to file", AutoSize = true };
    private readonly TextBox _outputFolder = new();
    private readonly Button _chooseFolder = new() { Text = "Browse…" };
    private readonly ComboBox _modeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _advancedButton = new() { Text = "Advanced settings  ›" };
    private readonly Panel _advancedPanel = new() { Visible = false };
    private readonly CheckBox _respectGitignore = new() { Text = "Follow .gitignore", AutoSize = true };
    private readonly CheckBox _respectDumpignore = new() { Text = "Follow .dumptotxtignore", AutoSize = true };
    private readonly ComboBox _sensitive = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Panel _mapWorkspace = new();
    private readonly Panel _previewWorkspace = new();
    private readonly Button _previewButton = new() { Text = "Preview output…", AutoSize = true, Height = 32 };
    private readonly ProgressBar _scanProgress = new()
    {
        Style = ProgressBarStyle.Marquee,
        MarqueeAnimationSpeed = 24,
        AccessibleName = "Scanning activity",
    };
    private readonly Button _previewBack = new() { Text = "Back to content map", AutoSize = true, Height = 32 };
    private readonly ContributionTreeView _tree = new() { Dock = DockStyle.Fill };
    private readonly RichTextBox _preview = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BorderStyle = BorderStyle.None,
        BackColor = UiTheme.Surface,
        ForeColor = UiTheme.Text,
        Font = new Font("Consolas", 9f),
        WordWrap = false,
    };
    private readonly Label _scanStatus = new() { AutoSize = true, ForeColor = UiTheme.Muted };
    private readonly Label _selectionSummary = new() { AutoSize = true, ForeColor = UiTheme.Text };
    private readonly CheckBox _skipNext = new() { Text = "Skip this screen next time", AutoSize = true };
    private readonly Label _validation = new() { AutoSize = true, ForeColor = UiTheme.Error };
    private readonly ToolTip _details = new();
    private readonly Button _nameHeader = new();
    private readonly Button _sizeHeader = new();
    private readonly Button _shareHeader = new();
    private readonly Button _create = new() { Text = "Create dump", Width = 118, Height = 34 };
    private readonly Button _cancel = new() { Text = "Cancel", Width = 88, Height = 34 };

    private Task? _scanTask;
    private string? _previewPath;
    private Control? _previewReturnFocus;
    private bool _ending;
    private bool _allowClose;
    private bool _ownedResourcesDisposed;
    private bool _syncingFormat;
    private bool _syncingDestination;
    private bool _syncingMode;

    public DumpContentSelection? Selection { get; private set; }
    public DumpConfig? UpdatedConfig { get; private set; }

    public DumpSelectionForm(string targetPath, DumpConfig config)
    {
        _targetPath = Path.GetFullPath(targetPath);
        _config = config.Clone();
        Text = "Create dump — DumpToTxt";
        ClientSize = new Size(920, 620);
        MinimumSize = new Size(860, 580);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = UiTheme.UiFont();
        BackColor = UiTheme.Window;
        ForeColor = UiTheme.Text;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        BuildUi();
        _selectionSummary.AccessibleName = "Current selection summary";
        _validation.AccessibleName = "Selection error";
        _validation.AccessibleRole = AccessibleRole.Alert;
        LoadRunChoices();
        _review = new DumpReviewTree(_config.LastSelectionMode);
        _viewRoot = _review.Root;
        _tree.StateProvider = row => row.Tag is NodeTag tag
            ? _review.State(tag.Node) : DumpNodeSelectionState.Excluded;
        _respectGitignore.CheckedChanged += (_, _) => RestartScan();
        _respectDumpignore.CheckedChanged += (_, _) => RestartScan();
        _sensitive.SelectedIndexChanged += (_, _) => RefreshPreview();
        _refreshTimer.Tick += (_, _) => RefreshFromScan();
        Shown += (_, _) => { StartScan(); _tree.Focus(); };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = UiTheme.Window };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.Controls.Add(BuildMain(), 0, 0);
        root.Controls.Add(BuildFooter(), 0, 1);
        Controls.Add(root);
        SizeChanged += (_, _) =>
        {
            root.Bounds = ClientRectangle;
            root.PerformLayout();
        };
    }

    private Control BuildMain()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(780, 600),
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 306,
            SplitterWidth = 1,
            BackColor = UiTheme.Border,
            Panel1MinSize = 300,
            Panel2MinSize = 500,
            Margin = new Padding(0),
        };
        split.Panel1.BackColor = UiTheme.Rail;
        split.Panel2.BackColor = UiTheme.Surface;
        split.Panel1.Controls.Add(BuildRail());
        split.Panel2.Controls.Add(BuildWorkspace());
        return split;
    }

    private Control BuildRail()
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(18, 10, 17, 16),
            BackColor = UiTheme.Rail,
        };

        var project = new Panel { Size = new Size(RailContentWidth, 66), Margin = new Padding(0, 0, 0, 5), BackColor = UiTheme.Rail };
        var path = new Label
        {
            Text = _targetPath,
            AutoEllipsis = true,
            Font = UiTheme.UiFont(10f, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(0, 6),
            Size = new Size(RailContentWidth, 20),
        };
        var targetKind = new Label { Text = Directory.Exists(_targetPath) ? "Selected folder" : "Selected file", AutoSize = true, ForeColor = UiTheme.Muted, Location = new Point(0, 29) };
        var divider = new Panel { BackColor = UiTheme.Border, Location = new Point(0, 55), Size = new Size(RailContentWidth, 1) };
        project.Controls.AddRange(new Control[] { path, targetKind, divider });
        flow.Controls.Add(project);

        flow.Controls.Add(UiTheme.SectionLabel("SAVE AS"));
        UiTheme.StyleChoiceCombo(_formatCombo);
        _formatCombo.DisplayMember = nameof(OutputFormatOption.DisplayName);
        _formatCombo.AccessibleName = "Save as format";
        _formatCombo.SelectedIndexChanged += (_, _) => FormatChanged();
        flow.Controls.Add(BuildComboHost(_formatCombo));

        flow.Controls.Add(UiTheme.SectionLabel("LAYOUT"));
        UiTheme.StyleChoiceCombo(_layoutCombo);
        _layoutCombo.AccessibleName = "Output layout";
        _layoutCombo.Format += (_, e) => { if (e.ListItem is OutputStyle s) e.Value = OutputStyleCatalog.LayoutName(s); };
        _layoutCombo.SelectedIndexChanged += (_, _) => LayoutChanged();
        flow.Controls.Add(BuildComboHost(_layoutCombo));

        flow.Controls.Add(UiTheme.SectionLabel("SEND TO"));
        _clipboard.CheckedChanged += (_, _) => DestinationChoiceChanged(_clipboard);
        _file.CheckedChanged += (_, _) => DestinationChoiceChanged(_file);
        var destinationRow = new TableLayoutPanel { Width = RailContentWidth, Height = 32, ColumnCount = 3, Margin = new Padding(0, 0, 0, 3) };
        destinationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        destinationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        destinationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        _file.Dock = DockStyle.Fill;
        _file.Margin = new Padding(0);
        _outputFolder.Dock = DockStyle.Fill;
        _outputFolder.Margin = new Padding(4, 1, 4, 1);
        _outputFolder.PlaceholderText = "Desktop";
        _outputFolder.AccessibleName = "Save folder";
        _chooseFolder.Dock = DockStyle.Fill;
        _chooseFolder.Margin = new Padding(0, 1, 0, 1);
        UiTheme.StyleSecondary(_chooseFolder);
        _chooseFolder.Click += (_, _) => ChooseOutputFolder();
        destinationRow.Controls.Add(_file, 0, 0);
        destinationRow.Controls.Add(_outputFolder, 1, 0);
        destinationRow.Controls.Add(_chooseFolder, 2, 0);
        flow.Controls.Add(destinationRow);
        _clipboard.Margin = new Padding(0, 1, 0, 0);
        flow.Controls.Add(_clipboard);
        _console.CheckedChanged += (_, _) => DestinationChoiceChanged(_console);
        flow.Controls.Add(_console);

        flow.Controls.Add(UiTheme.SectionLabel("START WITH"));
        UiTheme.StyleChoiceCombo(_modeCombo);
        _modeCombo.AccessibleName = "Content starting point";
        _modeCombo.Items.AddRange(new object[]
        {
            new ModeOption(DumpSelectionMode.Basic, "Essential"),
            new ModeOption(DumpSelectionMode.Thorough, "All text"),
            new ModeOption(DumpSelectionMode.None, "Map only"),
        });
        _modeCombo.SelectedIndexChanged += (_, _) => ModeChanged();
        flow.Controls.Add(BuildComboHost(_modeCombo));

        _advancedButton.Size = new Size(RailContentWidth, 32);
        UiTheme.StyleLinkButton(_advancedButton);
        _advancedButton.Click += (_, _) =>
        {
            _advancedPanel.Visible = !_advancedPanel.Visible;
            _advancedButton.Text = _advancedPanel.Visible ? "Advanced settings  ⌄" : "Advanced settings  ›";
        };
        flow.Controls.Add(BuildAdvancedHost());
        BuildAdvancedPanel();
        flow.Controls.Add(_advancedPanel);
        return flow;
    }

    private static Control BuildComboHost(ComboBox combo)
    {
        var host = new Panel { Size = new Size(RailContentWidth, 37), Margin = new Padding(0) };
        combo.SetBounds(0, 2, RailContentWidth, 32);
        combo.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        host.Controls.Add(combo);
        return host;
    }

    private Control BuildAdvancedHost()
    {
        var host = new Panel { Size = new Size(RailContentWidth, 47), Margin = new Padding(0, 9, 0, 0), BackColor = UiTheme.Rail };
        host.Controls.Add(new Panel { BackColor = UiTheme.Border, Location = new Point(0, 0), Size = new Size(RailContentWidth, 1) });
        _advancedButton.Location = new Point(0, 8);
        host.Controls.Add(_advancedButton);
        return host;
    }

    private void BuildAdvancedPanel()
    {
        _advancedPanel.Size = new Size(RailContentWidth, 146);
        _advancedPanel.BackColor = UiTheme.Rail;
        _respectGitignore.Location = new Point(0, 4);
        _respectDumpignore.Location = new Point(0, 30);
        var label = new Label { Text = "Sensitive data", AutoSize = true, Location = new Point(0, 61), ForeColor = UiTheme.Muted };
        _sensitive.SetBounds(0, 83, RailContentWidth, 26);
        foreach (SecretScanMode mode in Enum.GetValues<SecretScanMode>()) _sensitive.Items.Add(mode);
        _sensitive.FormattingEnabled = true;
        _sensitive.Format += (_, e) => e.Value = e.ListItem switch
        {
            SecretScanMode.Off => "Do not scan",
            SecretScanMode.Warn => "Ask before keeping",
            SecretScanMode.Redact => "Hide sensitive values",
            SecretScanMode.Skip => "Skip file contents",
            _ => e.ListItem?.ToString(),
        };
        var hint = new Label { Text = "More file types and limits are in Settings.", AutoSize = false, ForeColor = UiTheme.Muted, Location = new Point(0, 113), Size = new Size(RailContentWidth, 32) };
        _advancedPanel.Controls.AddRange(new Control[] { _respectGitignore, _respectDumpignore, label, _sensitive, hint });
    }

    private Control BuildWorkspace()
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
        _mapWorkspace.Dock = DockStyle.Fill;
        _mapWorkspace.BackColor = UiTheme.Surface;
        var mapLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        mapLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        mapLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        mapLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mapLayout.Controls.Add(BuildMapToolbar(), 0, 0);
        mapLayout.Controls.Add(BuildTreeHeader(), 0, 1);
        _tree.AccessibleName = "Included file and folder contribution map";
        _tree.Margin = new Padding(0);
        _tree.BeforeExpand += (_, e) =>
        {
            if (_refreshingNodes || e.Node?.Tag is not NodeTag tag) return;
            int nextNameX = 68 + ((e.Node.Level + 1) * _tree.Indent);
            if (nextNameX + 120 + ContributionTreeView.SizeColumnWidth + ContributionTreeView.ShareColumnWidth > _tree.ClientSize.Width)
            {
                e.Cancel = true;
                BeginInvoke(new Action(() => ShowFolder(tag.Node)));
                return;
            }
            _tree.BeginUpdate();
            try
            {
                if (!EnsureRealizationRoom(e.Node))
                {
                    e.Cancel = true;
                    BeginInvoke(new Action(() => ShowFolder(tag.Node)));
                    return;
                }
                PopulatePage(e.Node.Nodes, tag.Node);
            }
            finally { _tree.EndUpdate(); }
        };
        _tree.AfterCollapse += (_, e) =>
        {
            if (_refreshingNodes || e.Node?.Tag is not NodeTag tag) return;
            ReleaseNodes(e.Node.Nodes);
            e.Node.Nodes.Clear();
            _compactedParents.Remove(tag.Node.RelativePath);
            if (tag.Node.ChildCount > 0) e.Node.Nodes.Add(new TreeNode("Expand to browse"));
        };
        _tree.ToggleRequested += (_, node) => ToggleNode(node);
        _tree.PreviewRequested += (_, node) => PreviewNode(node);
        _tree.AfterSelect += (_, e) =>
        {
            if (e.Node?.Tag is not NodeTag) return;
            UpdateSelectionRow(e.Node);
            string state = _tree.GetNodeState(e.Node) switch
            {
                DumpNodeSelectionState.Included => "Included",
                DumpNodeSelectionState.Mixed => "Partly included",
                _ => "Excluded",
            };
            _tree.AccessibleDescription = $"{state}: {e.Node.Text}. Press Space to change inclusion or Enter to preview.";
            _tree.AnnounceDescription();
        };
        mapLayout.Controls.Add(_tree, 0, 2);
        _mapWorkspace.Controls.Add(mapLayout);

        _previewWorkspace.Dock = DockStyle.Fill;
        _previewWorkspace.BackColor = UiTheme.Surface;
        _previewWorkspace.Visible = false;
        var previewLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var previewHeader = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 7, 14, 7), BackColor = UiTheme.Surface };
        var previewTitle = new Label { Text = "Output preview", AutoSize = true, Font = UiTheme.UiFont(10f, FontStyle.Bold), Location = new Point(14, 14) };
        UiTheme.StyleSecondary(_previewBack);
        _previewBack.Click += (_, _) => ShowMap();
        previewHeader.Controls.AddRange(new Control[] { previewTitle, _previewBack });
        previewHeader.Resize += (_, _) => _previewBack.Location = new Point(previewHeader.ClientSize.Width - _previewBack.Width - 14, 7);
        _preview.AccessibleName = "Output preview";
        previewLayout.Controls.Add(previewHeader, 0, 0);
        previewLayout.Controls.Add(_preview, 0, 1);
        _previewWorkspace.Controls.Add(previewLayout);

        host.Controls.Add(_previewWorkspace);
        host.Controls.Add(_mapWorkspace);
        return host;
    }

    private Control BuildMapToolbar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(18, 8, 18, 0),
            BackColor = UiTheme.Surface,
            Margin = new Padding(0),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 3));
        _scanStatus.Text = "Preparing scan…";
        _scanStatus.Font = UiTheme.UiFont(10f, FontStyle.Bold);
        _scanStatus.ForeColor = UiTheme.Text;
        _scanStatus.Anchor = AnchorStyles.Left;
        _selectionSummary.ForeColor = UiTheme.Muted;
        _selectionSummary.Anchor = AnchorStyles.Left;
        panel.Controls.Add(_scanStatus, 0, 0);
        panel.Controls.Add(_selectionSummary, 0, 1);
        UiTheme.StyleSecondary(_previewButton);
        _previewButton.Anchor = AnchorStyles.Right;
        _previewButton.Click += (_, _) => ShowPreview(returnFocus: _previewButton);
        panel.Controls.Add(_previewButton, 1, 0);
        panel.SetRowSpan(_previewButton, 2);
        _scanProgress.Dock = DockStyle.Fill;
        _scanProgress.Margin = new Padding(0);
        panel.Controls.Add(_scanProgress, 0, 2);
        panel.SetColumnSpan(_scanProgress, 2);
        var accent = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Accent, Margin = new Padding(0) };
        panel.Controls.Add(accent, 0, 3);
        panel.SetColumnSpan(accent, 2);
        return panel;
    }

    private Control BuildTreeHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            BackColor = UiTheme.Window,
            Padding = new Padding(16, 0, 16, 0),
            Margin = new Padding(0),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContributionTreeView.SizeColumnWidth));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContributionTreeView.ShareColumnWidth));
        ConfigureTreeHeader(_nameHeader, "NAME", "Name", ContentAlignment.MiddleLeft, SortColumn.Name);
        ConfigureTreeHeader(_sizeHeader, "TEXT SIZE", "Text size", ContentAlignment.MiddleRight, SortColumn.TextSize);
        ConfigureTreeHeader(_shareHeader, "SHARE", "Share", ContentAlignment.MiddleRight, SortColumn.Share);
        header.Controls.Add(_nameHeader, 0, 0);
        header.Controls.Add(_sizeHeader, 1, 0);
        header.Controls.Add(_shareHeader, 2, 0);
        UpdateSortHeaders();
        return header;
    }

    private void ConfigureTreeHeader(Button button, string text, string accessibleName,
        ContentAlignment alignment, SortColumn column)
    {
        button.Text = text;
        button.AccessibleName = accessibleName;
        button.AccessibleRole = AccessibleRole.PushButton;
        button.Dock = DockStyle.Fill;
        button.TextAlign = alignment;
        button.Font = UiTheme.UiFont(8f, FontStyle.Bold);
        button.ForeColor = UiTheme.Muted;
        button.BackColor = UiTheme.Window;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = UiTheme.ControlHover;
        button.FlatAppearance.MouseDownBackColor = UiTheme.AccentSoft;
        button.UseVisualStyleBackColor = false;
        button.Margin = new Padding(0);
        button.Cursor = Cursors.Hand;
        button.TabStop = true;
        button.Click += (_, _) => ChangeSort(column);
    }

    private Control BuildFooter()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Rail, Margin = new Padding(0) };
        panel.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = UiTheme.Border });
        _skipNext.Location = new Point(20, 20);
        _skipNext.AccessibleDescription = "Use the last saved choices automatically on future Explorer invocations.";
        panel.Controls.Add(_skipNext);
        _validation.AutoSize = false;
        _validation.Height = 34;
        _validation.TextAlign = ContentAlignment.MiddleRight;
        panel.Controls.Add(_validation);
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Height = 38 };
        UiTheme.StyleSecondary(_cancel);
        UiTheme.StylePrimary(_create);
        _cancel.Click += (_, _) => BeginClose(DialogResult.Cancel);
        _create.Click += (_, _) => AcceptSelection();
        actions.Controls.Add(_cancel);
        actions.Controls.Add(_create);
        panel.Controls.Add(actions);
        panel.Resize += (_, _) =>
        {
            actions.Location = new Point(panel.ClientSize.Width - actions.PreferredSize.Width - 14, 10);
            _validation.SetBounds(_skipNext.Right + 12, 10,
                Math.Max(0, actions.Left - _skipNext.Right - 22), 34);
        };
        AcceptButton = _create;
        CancelButton = _cancel;
        return panel;
    }

    private void LoadRunChoices()
    {
        foreach (var format in OutputStyleCatalog.Formats) _formatCombo.Items.Add(format);
        _syncingFormat = true;
        _formatCombo.SelectedItem = _formatCombo.Items.Cast<OutputFormatOption>()
            .First(x => x.Kind == OutputStyleCatalog.Format(_config.Style));
        PopulateLayouts(_config.Style);
        _syncingFormat = false;
        OutputTarget savedTarget = _config.OutputTarget;
        _syncingDestination = true;
        _clipboard.Checked = savedTarget == OutputTarget.Clipboard && !OutputStyleCatalog.IsWord(_config.Style);
        _console.Checked = savedTarget == OutputTarget.Stdout && !OutputStyleCatalog.IsWord(_config.Style);
        _file.Checked = savedTarget == OutputTarget.File || OutputStyleCatalog.IsWord(_config.Style);
        _clipboard.Enabled = _console.Enabled = !OutputStyleCatalog.IsWord(_config.Style);
        _syncingDestination = false;
        _outputFolder.Text = _config.OutputDir ?? "";
        _respectGitignore.Checked = _config.RespectGitignore;
        _respectDumpignore.Checked = _config.UseDumpToTxtIgnore;
        _sensitive.SelectedItem = _config.SecretScan;
        _skipNext.Checked = !_config.ShowReviewBeforeDump;
        _syncingMode = true;
        _modeCombo.SelectedItem = _modeCombo.Items.Cast<ModeOption>()
            .First(x => x.Mode == _config.LastSelectionMode);
        _syncingMode = false;
        DestinationChanged();
    }

    private void FormatChanged()
    {
        if (_syncingFormat || _formatCombo.SelectedItem is not OutputFormatOption format) return;
        OutputStyle next = OutputStyleCatalog.Format(_config.Style) == format.Kind
            ? _config.Style : OutputStyleCatalog.DefaultStyle(format.Kind);
        _config.Style = next;
        PopulateLayouts(next);
        bool word = OutputStyleCatalog.IsWord(next);
        _clipboard.Enabled = _console.Enabled = !word;
        if (word) _file.Checked = true;
        RefreshPreview();
    }

    private void ModeChanged()
    {
        if (_syncingMode || _modeCombo.SelectedItem is not ModeOption option) return;
        _config.LastSelectionMode = option.Mode;
        _pathSelection.Clear();
        _review.Select(_review.Root, option.Mode);
        RefreshVisibleSelectionRows();
        UpdateSelectionSummary();
        RefreshPreview();
    }

    private void PopulateLayouts(OutputStyle selected)
    {
        _syncingFormat = true;
        try
        {
            _layoutCombo.Items.Clear();
            foreach (var style in OutputStyleCatalog.Layouts(OutputStyleCatalog.Format(selected))) _layoutCombo.Items.Add(style);
            _layoutCombo.SelectedItem = selected;
        }
        finally { _syncingFormat = false; }
    }

    private void LayoutChanged()
    {
        if (_syncingFormat || _layoutCombo.SelectedItem is not OutputStyle style) return;
        _config.Style = style;
        bool word = OutputStyleCatalog.IsWord(style);
        _clipboard.Enabled = _console.Enabled = !word;
        if (word) _file.Checked = true;
        RefreshPreview();
    }

    private void DestinationChanged()
    {
        bool save = _file.Checked || OutputStyleCatalog.IsWord(_config.Style);
        _outputFolder.Enabled = save;
        _chooseFolder.Enabled = save;
        _config.OutputTarget = save ? OutputTarget.File : _console.Checked ? OutputTarget.Stdout : OutputTarget.Clipboard;
    }

    private void DestinationChoiceChanged(RadioButton selected)
    {
        if (_syncingDestination) return;
        _syncingDestination = true;
        try
        {
            if (selected.Checked)
                foreach (var radio in new[] { _file, _clipboard, _console })
                    if (radio != selected) radio.Checked = false;
            DestinationChanged();
        }
        finally
        {
            _syncingDestination = false;
        }
    }

    private void ChooseOutputFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where to save dumps",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_outputFolder.Text) ? _outputFolder.Text : "",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _outputFolder.Text = dialog.SelectedPath;
    }

    private void StartScan()
    {
        _started = true;
        _scanComplete = false;
        _create.Enabled = true;
        UpdateSortHeaders();
        SetScanStatus("Scanning files…");
        _scanProgress.Visible = true;
        _refreshTimer.Start();
        _scanTask = DumpPreviewScanner.ScanAsync(_targetPath, _config.Clone(), _scanState, _scanCancellation.Token);
    }

    private async void RestartScan()
    {
        if (!_started || _ending) return;
        int generation = ++_scanGeneration;
        _scanCancellation.Cancel();
        _previewCancellation?.Cancel();
        ++_previewGeneration;
        _refreshTimer.Stop();
        _scanComplete = false;
        UpdateSortHeaders();
        SetScanStatus("Updating file rules…");
        var previous = _scanTask;
        try { if (previous is not null) await previous; } catch { }
        if (_ending || IsDisposed || generation != _scanGeneration) return;
        _scanCancellation.Dispose();
        _scanCancellation = new();
        _scanState = new();
        _config.RespectGitignore = _respectGitignore.Checked;
        _config.UseDumpToTxtIgnore = _respectDumpignore.Checked;
        _review = new DumpReviewTree(_config.LastSelectionMode);
        _viewRoot = _review.Root;
        ReleaseNodes(_tree.Nodes);
        _tree.Nodes.Clear();
        _pages.Clear();
        _compactedParents.Clear();
        StartScan();
    }

    private void RefreshFromScan()
    {
        if (_ending) return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        bool changed = false;
        bool wasComplete = _scanComplete;
        DumpPreviewBatch batch;
        do
        {
            batch = _scanState.Drain(128);
            foreach (var change in batch.Changes)
            {
                if (change.Path is { } path)
                {
                    var node = _review.AddPath(path);
                    if (_pathSelection.Overrides.TryGetValue(node.RelativePath, out bool included))
                        _review.Select(node, included ? DumpSelectionMode.Thorough : DumpSelectionMode.None);
                }
                if (change.Entry is { } entry) _review.AddText(entry);
            }
            changed |= batch.Changes.Count > 0;
        } while (batch.Changes.Count > 0 && !batch.Completed && watch.ElapsedMilliseconds < 10);

        _scanComplete = batch.Completed;
        if (changed || _scanComplete != wasComplete) RefreshTree();
        if (_scanComplete != wasComplete) UpdateSortHeaders();

        if (batch.DiagnosticCount > 0)
        {
            _validation.Text = $"{batch.DiagnosticCount:N0} scan issue(s): {batch.LastDiagnostic}";
            _details.SetToolTip(_validation, batch.LastDiagnostic);
        }
        if (_scanTask is { IsFaulted: true })
        {
            _refreshTimer.Stop();
            _scanProgress.Visible = false;
            SetScanStatus("Scan could not be completed");
            _validation.Text = _scanTask.Exception?.GetBaseException().Message ?? "Unknown scan error.";
        }
        else if (batch.Completed)
        {
            _refreshTimer.Stop();
            _scanProgress.Visible = false;
            SetScanStatus("Scan complete");
            RefreshPreview();
        }
        else SetScanStatus($"Scanning project… {batch.ScannedFiles:N0} files found");
    }

    private void SetScanStatus(string status)
    {
        _scanStatus.Text = status;
        _scanStatus.AccessibleName = status;
    }

    private void ChangeSort(SortColumn column)
    {
        DumpReviewSortKey previousKey = SortKey(_sortColumn);
        DumpReviewSortKey nextKey = SortKey(column);
        if (column == _sortColumn)
            _sortDirection = _sortDirection == DumpReviewSortDirection.Ascending
                ? DumpReviewSortDirection.Descending : DumpReviewSortDirection.Ascending;
        else if (previousKey != nextKey)
            _sortDirection = nextKey == DumpReviewSortKey.Name
                ? DumpReviewSortDirection.Ascending : DumpReviewSortDirection.Descending;
        _sortColumn = column;
        _pages.Clear();
        UpdateSortHeaders();
        if (_scanComplete) RefreshTree();
    }

    private static DumpReviewSortKey SortKey(SortColumn column) =>
        column == SortColumn.Name ? DumpReviewSortKey.Name : DumpReviewSortKey.Contribution;

    private DumpReviewSort RequestedSort() => new(SortKey(_sortColumn), _sortDirection);

    private DumpReviewSort EffectiveSort() =>
        _scanComplete ? RequestedSort() : DumpReviewSort.DiscoveryAscending;

    private void UpdateSortHeaders()
    {
        UpdateSortHeader(_nameHeader, "NAME", SortColumn.Name);
        UpdateSortHeader(_sizeHeader, "TEXT SIZE", SortColumn.TextSize);
        UpdateSortHeader(_shareHeader, "SHARE", SortColumn.Share);
    }

    private void UpdateSortHeader(Button button, string label, SortColumn column)
    {
        bool active = _sortColumn == column;
        bool pending = active && !_scanComplete;
        string arrow = _sortDirection == DumpReviewSortDirection.Ascending ? "▲" : "▼";
        button.Text = active ? $"{label} {arrow}{(pending ? " *" : "")}" : label;
        button.ForeColor = active ? UiTheme.Text : UiTheme.Muted;
        string direction = _sortDirection == DumpReviewSortDirection.Ascending ? "ascending" : "descending";
        button.AccessibleDescription = active
            ? pending
                ? $"Requested {direction} sort. Discovery order stays stable until scanning completes."
                : $"Active {direction} sort. Activate again to reverse direction."
            : "Activate to sort this column.";
        _details.SetToolTip(button, pending
            ? "Discovery order stays stable while scanning; this sort is applied when discovery finishes."
            : active ? $"Sorted {direction}. Click again to reverse." : "Click to sort.");
    }

    private void RefreshTree()
    {
        _tree.BeginUpdate();
        _refreshingNodes = true;
        try
        {
            PopulatePage(_tree.Nodes, _viewRoot);
            foreach (var row in VisibleRows().ToArray())
                if (row.IsExpanded && row.Tag is NodeTag tag)
                    PopulatePage(row.Nodes, tag.Node);
            RefreshRealizedStructure();
            foreach (var row in VisibleRows().ToArray()) UpdateRow(row, invalidateMetrics: false);
        }
        finally { _refreshingNodes = false; _tree.EndUpdate(); }
        UpdateSelectionSummary();
        _tree.Invalidate();
    }

    private void RefreshRealizedStructure()
    {
        foreach (var row in _realized.Values)
        {
            if (row.TreeView is null || row.Tag is not NodeTag tag) continue;
            if (tag.Node.ChildCount > 0 && row.Nodes.Count == 0)
                row.Nodes.Add(new TreeNode("Expand to browse"));
        }
    }

    private IEnumerable<TreeNode> VisibleRows()
    {
        for (TreeNode? row = _tree.TopNode; row is not null; row = row.NextVisibleNode)
        {
            if (row.Bounds.Top >= _tree.ClientSize.Height) yield break;
            if (row.Bounds.Bottom > 0) yield return row;
        }
    }

    private void UpdateSelectionSummary()
    {
        _selectionSummary.Text = $"{FormatBytes(_review.SelectedBytes)} selected · {_review.PathCount:N0} paths";
        _selectionSummary.AccessibleDescription = _selectionSummary.Text;
    }

    private void UpdateRow(TreeNode row, bool invalidateMetrics = true)
    {
        if (row.Tag is not NodeTag tag) return;
        var node = tag.Node;
        UpdateSelectionRow(row);
        _tree.SetMetrics(row, node.Name, FormatBytes(node.TextBytes),
            Share(node.TextBytes, _review.Root.TextBytes), invalidateMetrics);
        if (node.ChildCount > 0 && row.Nodes.Count == 0) row.Nodes.Add(new TreeNode("Expand to browse"));
    }

    private void UpdateSelectionRow(TreeNode row)
    {
        if (row.Tag is not NodeTag tag) return;
        DumpNodeSelectionState state = _review.State(tag.Node);
        string text = AccessibleNodeText(tag.Node.Name, state);
        if (row.Text != text) row.Text = text;
        Color foreColor = state == DumpNodeSelectionState.Excluded ? UiTheme.DisabledText : UiTheme.Text;
        if (row.ForeColor != foreColor) row.ForeColor = foreColor;
    }

    private void RefreshVisibleSelectionRows(TreeNode? changedRow = null)
    {
        if (_ending || IsDisposed) return;
        var rows = new HashSet<TreeNode>(VisibleRows().Where(row => row.Tag is NodeTag));
        for (TreeNode? row = changedRow; row is not null; row = row.Parent)
            if (row.Tag is NodeTag) rows.Add(row);
        _tree.BeginUpdate();
        try
        {
            foreach (var row in rows) UpdateSelectionRow(row);
        }
        finally { _tree.EndUpdate(); }
        _tree.Invalidate();
    }

    private void PopulatePage(TreeNodeCollection rows, DumpReviewNode parent)
    {
        TreeNode? selected = _tree.SelectedNode;
        int offset = _pages.GetValueOrDefault(parent.RelativePath);
        offset = Math.Min(offset, Math.Max(0, ((parent.ChildCount - 1) / PageSize) * PageSize));
        int existingCount = rows.Cast<TreeNode>().Count(row => row.Tag is NodeTag);
        int count = Math.Min(PageSize, Math.Max(0, MaxRealizedNodes - _realized.Count + existingCount));
        bool compacted = _compactedParents.TryGetValue(parent.RelativePath, out string? retainedPath);
        var desired = compacted && _review.Find(retainedPath!) is { } retained
            ? new[] { retained } : parent.Page(offset, count, EffectiveSort()).ToArray();
        var wanted = desired.Select(node => node.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (TreeNode row in rows.Cast<TreeNode>().ToArray())
            if (row.Tag is NodeTag tag && !wanted.Contains(tag.Node.RelativePath))
            {
                ReleaseNode(row);
                row.Remove();
            }

        var navigationRows = new HashSet<TreeNode>();
        int index = 0;
        if (rows == _tree.Nodes && parent.Parent is { } outer)
            PlaceNavigationRow(rows, ref index, navigationRows,
                row => row.Tag is FolderViewTag view && view.Folder.RelativePath.Equals(outer.RelativePath, StringComparison.OrdinalIgnoreCase),
                $"Back to {outer.Name} — press Enter", new FolderViewTag(outer));
        if (!compacted && offset > 0)
            PlaceNavigationRow(rows, ref index, navigationRows,
                row => row.Tag is PageTag page && page.Parent.RelativePath.Equals(parent.RelativePath, StringComparison.OrdinalIgnoreCase)
                    && page.Offset == Math.Max(0, offset - PageSize),
                $"Previous {PageSize:N0} paths — press Enter", new PageTag(parent, Math.Max(0, offset - PageSize)));

        foreach (var node in desired)
        {
            if (!_realized.TryGetValue(node.RelativePath, out var row))
            {
                row = new TreeNode
                {
                    Name = node.RelativePath,
                    Tag = new NodeTag(node),
                    ImageIndex = node.IsDirectory ? ContributionTreeView.FolderImageIndex : ContributionTreeView.FileImageIndex,
                    SelectedImageIndex = node.IsDirectory ? ContributionTreeView.FolderImageIndex : ContributionTreeView.FileImageIndex,
                };
                _realized.Add(node.RelativePath, row);
                rows.Insert(Math.Min(index, rows.Count), row);
                UpdateRow(row, invalidateMetrics: false);
            }
            else if (row.Index != index)
            {
                bool expanded = row.IsExpanded;
                row.Remove();
                rows.Insert(Math.Min(index, rows.Count), row);
                if (expanded) row.Expand();
            }
            index++;
        }

        if (compacted)
            PlaceNavigationRow(rows, ref index, navigationRows,
                row => row.Tag is PageTag page && page.Parent.RelativePath.Equals(parent.RelativePath, StringComparison.OrdinalIgnoreCase)
                    && page.Offset == offset,
                $"Browse all {parent.ChildCount:N0} sibling paths — press Enter", new PageTag(parent, offset));
        else if (offset + desired.Length < parent.ChildCount)
        {
            int nextOffset = offset + desired.Length;
            string label = desired.Length == 0
                ? "Collapse another folder, then press Enter to browse"
                : $"Next paths ({nextOffset:N0} of {parent.ChildCount:N0} shown)";
            PlaceNavigationRow(rows, ref index, navigationRows,
                row => row.Tag is PageTag page && page.Parent.RelativePath.Equals(parent.RelativePath, StringComparison.OrdinalIgnoreCase)
                    && page.Offset == nextOffset,
                label + " — press Enter", new PageTag(parent, nextOffset));
        }

        foreach (TreeNode row in rows.Cast<TreeNode>().ToArray())
            if (row.Tag is not NodeTag && !navigationRows.Contains(row)) row.Remove();
        if (selected?.TreeView == _tree) _tree.SelectedNode = selected;
    }

    private static void PlaceNavigationRow(TreeNodeCollection rows, ref int index, HashSet<TreeNode> kept,
        Func<TreeNode, bool> matches, string text, object tag)
    {
        TreeNode? row = rows.Cast<TreeNode>().FirstOrDefault(matches);
        if (row is null)
        {
            row = new TreeNode(text) { Tag = tag };
            rows.Insert(Math.Min(index, rows.Count), row);
        }
        else
        {
            row.Text = text;
            row.Tag = tag;
            if (row.Index != index)
            {
                row.Remove();
                rows.Insert(Math.Min(index, rows.Count), row);
            }
        }
        kept.Add(row);
        index++;
    }

    private bool EnsureRealizationRoom(TreeNode expanding)
    {
        if (_realized.Count + PageSize <= MaxRealizedNodes) return true;
        var ancestors = new HashSet<TreeNode>();
        for (var node = expanding; node is not null; node = node.Parent) ancestors.Add(node);
        foreach (var row in _realized.Values.ToArray())
        {
            if (row.IsExpanded && !ancestors.Contains(row)) row.Collapse();
            if (_realized.Count + PageSize <= MaxRealizedNodes) break;
        }
        foreach (var branch in ancestors.Reverse())
        {
            if (_realized.Count + PageSize <= MaxRealizedNodes) break;
            if (branch.Tag is not NodeTag tag) continue;
            var parent = tag.Node.Parent!;
            var rows = branch.Parent?.Nodes ?? _tree.Nodes;
            _compactedParents[parent.RelativePath] = tag.Node.RelativePath;
            PopulatePage(rows, parent);
        }
        return _realized.Count + PageSize <= MaxRealizedNodes;
    }

    private void ShowFolder(DumpReviewNode folder)
    {
        if (_ending || IsDisposed) return;
        _viewRoot = folder;
        ReleaseNodes(_tree.Nodes);
        _tree.Nodes.Clear();
        _compactedParents.Clear();
        RefreshTree();
        _tree.SelectedNode = _tree.Nodes.Cast<TreeNode>().FirstOrDefault(row => row.Tag is NodeTag);
    }

    private void ReleaseNodes(TreeNodeCollection rows)
    {
        foreach (TreeNode row in rows) ReleaseNode(row);
    }

    private void ReleaseNode(TreeNode row)
    {
        ReleaseNodes(row.Nodes);
        if (row.Tag is NodeTag tag)
        {
            _realized.Remove(tag.Node.RelativePath);
            _compactedParents.Remove(tag.Node.RelativePath);
        }
        _tree.Forget(row);
    }

    private static string AccessibleNodeText(string name, DumpNodeSelectionState state) =>
        $"{name} - {state}";

    private static double Share(long value, long total) => total > 0 ? 100d * value / total : 0;

    private void ToggleNode(TreeNode treeNode)
    {
        if (treeNode.Tag is PageTag or FolderViewTag) { PreviewNode(treeNode); return; }
        if (treeNode.Tag is not NodeTag tag) return;
        DumpNodeSelectionState state = _review.State(tag.Node);
        _pathSelection.Set(tag.Node.RelativePath, tag.Node.IsDirectory,
            state != DumpNodeSelectionState.Included);
        _review.Select(tag.Node, state == DumpNodeSelectionState.Included ? DumpSelectionMode.None : DumpSelectionMode.Thorough);
        RefreshVisibleSelectionRows(treeNode);
        UpdateSelectionSummary();
        RefreshPreview();
    }

    private void PreviewNode(TreeNode treeNode)
    {
        if (treeNode.Tag is FolderViewTag view) { ShowFolder(view.Folder); return; }
        if (treeNode.Tag is PageTag page)
        {
            var rows = treeNode.Parent?.Nodes ?? _tree.Nodes;
            _pages[page.Parent.RelativePath] = page.Offset;
            _tree.BeginUpdate();
            try
            {
                if (_compactedParents.Remove(page.Parent.RelativePath))
                {
                    ReleaseNodes(rows);
                    rows.Clear();
                }
                PopulatePage(rows, page.Parent);
            }
            finally { _tree.EndUpdate(); }
            _tree.SelectedNode = rows.Cast<TreeNode>().FirstOrDefault(row => row.Tag is NodeTag);
            return;
        }
        if (treeNode.Tag is not NodeTag tag) return;
        ShowPreview(tag.Node.RelativePath, _tree);
    }

    private void ShowPreview(string? relativePath = null, Control? returnFocus = null)
    {
        _previewPath = relativePath;
        _previewReturnFocus = returnFocus;
        _mapWorkspace.Visible = false;
        _previewWorkspace.Visible = true;
        _previewWorkspace.BringToFront();
        RefreshPreview();
        _previewBack.Focus();
    }

    private void ShowMap()
    {
        _previewCancellation?.Cancel();
        ++_previewGeneration;
        _previewWorkspace.Visible = false;
        _mapWorkspace.Visible = true;
        _mapWorkspace.BringToFront();
        Control target = _previewReturnFocus is { CanFocus: true } ? _previewReturnFocus : _previewButton;
        target.Focus();
    }

    private async void RefreshPreview()
    {
        if (!_previewWorkspace.Visible || _ending) return;
        int generation = ++_previewGeneration;
        _previewCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        var selection = DumpContentSelection.FromMode(_config.LastSelectionMode, _config).WithPathOverrides(_pathSelection.Overrides);
        var config = _config.Clone();
        if (_sensitive.SelectedItem is SecretScanMode mode) config.SecretScan = mode;
        var state = _scanState;
        string? path = _previewPath;
        _preview.Text = "Preparing preview…";
        try
        {
            string text = await Task.Run(() => DumpPreviewRenderer.Render(state.Snapshot(cancellation.Token), config, selection, path, cancellation.Token), cancellation.Token);
            if (_ending || IsDisposed || generation != _previewGeneration || !_previewWorkspace.Visible) return;
            _preview.Text = text;
            _preview.SelectionStart = 0;
            _preview.ScrollToCaret();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_ending && !IsDisposed && generation == _previewGeneration) _preview.Text = $"Preview unavailable: {ex.Message}";
        }
        finally
        {
            if (_previewCancellation == cancellation) _previewCancellation = null;
            cancellation.Dispose();
        }
    }

    private void AcceptSelection()
    {
        _validation.Text = "";
        DumpContentSelection selection = DumpContentSelection.FromMode(_config.LastSelectionMode, _config)
            .WithPathOverrides(_pathSelection.Overrides);

        if (_file.Checked)
        {
            string folder = string.IsNullOrWhiteSpace(_outputFolder.Text)
                ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                : _outputFolder.Text.Trim();
            try { folder = Path.GetFullPath(Environment.ExpandEnvironmentVariables(folder)); }
            catch (Exception ex)
            {
                _validation.Text = $"Choose a valid save folder: {ex.Message}";
                return;
            }
            if (!Directory.Exists(folder))
            {
                _validation.Text = "The selected save folder does not exist.";
                return;
            }
            _config.OutputDir = folder;
        }
        else _config.OutputDir = null;

        _config.OutputTarget = _file.Checked ? OutputTarget.File : _console.Checked ? OutputTarget.Stdout : OutputTarget.Clipboard;
        _config.RespectGitignore = _respectGitignore.Checked;
        _config.UseDumpToTxtIgnore = _respectDumpignore.Checked;
        if (_sensitive.SelectedItem is SecretScanMode secretMode) _config.SecretScan = secretMode;
        _config.ShowReviewBeforeDump = !_skipNext.Checked;
        Selection = selection;
        UpdatedConfig = _config.Clone();
        BeginClose(DialogResult.OK);
    }

    private async void BeginClose(DialogResult result)
    {
        if (_ending) return;
        _ending = true;
        _refreshTimer.Stop();
        _scanCancellation.Cancel();
        _previewCancellation?.Cancel();
        _create.Enabled = _cancel.Enabled = false;
        if (_scanTask is not null)
        {
            try { await _scanTask.ConfigureAwait(true); }
            catch (OperationCanceledException) { }
            catch { }
        }
        if (IsDisposed) return;
        _allowClose = true;
        DialogResult = result;
        Close();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            BeginClose(DialogResult.Cancel);
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_ownedResourcesDisposed)
        {
            _ownedResourcesDisposed = true;
            _ending = true;
            _refreshTimer.Dispose();
            _details.Dispose();
            _scanCancellation.Cancel();
            var previewCancellation = _previewCancellation;
            _previewCancellation = null;
            previewCancellation?.Cancel();
            _scanCancellation.Dispose();
        }
        base.Dispose(disposing);
    }
}
