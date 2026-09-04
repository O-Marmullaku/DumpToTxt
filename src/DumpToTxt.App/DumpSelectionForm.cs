/*
THESIS: Review contribution before generation; refuse the type-grid wizard that hides impact and output decisions.
OWN-WORLD: Native Windows utility surfaces, quiet cool neutrals, semantic blue, dense tree data, standard controls.
STORY: Choose representation, inspect the largest contributors, exclude noise, preview the result, create the dump.
FIRST VIEWPORT: Remembered run choices stay in a narrow left rail; the live content map owns the wide workspace; Create dump anchors the lower-right.
FORM: A · Review workspace, selected in the interactive prototype. FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance.
*/
using System.Drawing;
using DumpToTxt.Core;

namespace DumpToTxt.App;

/// <summary>One-window, progressively updated review workspace shown before the authoritative dump.</summary>
public sealed class DumpSelectionForm : Form
{
    private const int RailContentWidth = 252;

    private sealed record NodeTag(DumpContributionNode Node, IReadOnlyList<DumpPreviewEntry> Entries);
    private sealed record ModeOption(DumpSelectionMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly string _targetPath;
    private readonly DumpConfig _config;
    private readonly DumpPreviewScanState _scanState = new();
    private readonly DumpSelectionModel _selectionModel = new();
    private readonly DumpPathSelectionModel _pathSelection = new();
    private readonly CancellationTokenSource _scanCancellation = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 120 };
    private readonly ComboBox _formatCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _layoutCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly RadioButton _clipboard = new() { Text = "Copy to clipboard", AutoSize = true };
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
    private readonly Button _create = new() { Text = "Create dump", Width = 118, Height = 34 };
    private readonly Button _cancel = new() { Text = "Cancel", Width = 88, Height = 34 };

    private DumpPreviewSnapshot _snapshot = new(Array.Empty<DumpPreviewTypeCount>(), 0, false);
    private Task? _scanTask;
    private string? _previewPath;
    private Control? _previewReturnFocus;
    private bool _ending;
    private bool _allowClose;
    private bool _syncingFormat;
    private bool _syncingDestination;
    private bool _syncingMode;
    private bool _initialTreeExpansionApplied;

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
        _scanStatus.AccessibleName = "Scan progress";
        _selectionSummary.AccessibleName = "Current selection summary";
        _validation.AccessibleName = "Selection error";
        _validation.AccessibleRole = AccessibleRole.Alert;
        LoadRunChoices();
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
        _clipboard.CheckedChanged += (_, _) => DestinationChoiceChanged(_clipboard, _file);
        _file.CheckedChanged += (_, _) => DestinationChoiceChanged(_file, _clipboard);
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
            SecretScanMode.Warn => "Warn only",
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
        mapLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        mapLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        mapLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mapLayout.Controls.Add(BuildMapToolbar(), 0, 0);
        mapLayout.Controls.Add(BuildTreeHeader(), 0, 1);
        _tree.AccessibleName = "Included file and folder contribution map";
        _tree.ToggleRequested += (_, node) => ToggleNode(node);
        _tree.PreviewRequested += (_, node) => PreviewNode(node);
        _tree.AfterSelect += (_, e) =>
        {
            if (e.Node?.Tag is not NodeTag) return;
            string state = e.Node.StateImageIndex switch { 1 => "Included", 2 => "Partly included", _ => "Excluded" };
            _tree.AccessibleDescription = $"{state}: {e.Node.Text}. Press Space to change inclusion or Enter to preview.";
            _tree.AnnounceDescription();
        };
        _tree.NodeMouseDoubleClick += (_, e) => PreviewNode(e.Node);
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
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(18, 12, 18, 8) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
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
        return panel;
    }

    private static Control BuildTreeHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            BackColor = Color.FromArgb(247, 249, 251),
            Padding = new Padding(16, 0, 16, 0),
            Margin = new Padding(0),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContributionTreeView.SizeColumnWidth));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContributionTreeView.ShareColumnWidth));
        header.Controls.Add(TreeHeaderLabel("NAME", "Name", ContentAlignment.MiddleLeft), 0, 0);
        header.Controls.Add(TreeHeaderLabel("TEXT SIZE", "Text size", ContentAlignment.MiddleRight), 1, 0);
        header.Controls.Add(TreeHeaderLabel("SHARE", "Share", ContentAlignment.MiddleRight), 2, 0);
        return header;
    }

    private static Label TreeHeaderLabel(string text, string accessibleName, ContentAlignment alignment) => new()
    {
        Text = text,
        AccessibleName = accessibleName,
        Dock = DockStyle.Fill,
        TextAlign = alignment,
        Font = UiTheme.UiFont(8f, FontStyle.Bold),
        ForeColor = UiTheme.Muted,
        Margin = new Padding(0),
    };

    private Control BuildFooter()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Rail };
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
        _clipboard.Checked = _config.OutputTarget == OutputTarget.Clipboard;
        _file.Checked = !_clipboard.Checked;
        _outputFolder.Text = _config.OutputDir ?? "";
        _respectGitignore.Checked = _config.RespectGitignore;
        _respectDumpignore.Checked = _config.UseDumpToTxtIgnore;
        _sensitive.SelectedItem = _config.SecretScan;
        _skipNext.Checked = !_config.ShowReviewBeforeDump;
        _selectionModel.SelectMode(_config.LastSelectionMode);
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
        _clipboard.Enabled = !word;
        if (word) _file.Checked = true;
        RefreshPreview();
    }

    private void ModeChanged()
    {
        if (_syncingMode || _modeCombo.SelectedItem is not ModeOption option) return;
        _config.LastSelectionMode = option.Mode;
        _selectionModel.SelectMode(option.Mode);
        _pathSelection.Clear();
        RefreshTree();
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
        _clipboard.Enabled = !word;
        if (word) _file.Checked = true;
        RefreshPreview();
    }

    private void DestinationChanged()
    {
        bool save = _file.Checked || OutputStyleCatalog.IsWord(_config.Style);
        _outputFolder.Enabled = save;
        _chooseFolder.Enabled = save;
        _config.OutputTarget = save ? OutputTarget.File : OutputTarget.Clipboard;
    }

    private void DestinationChoiceChanged(RadioButton selected, RadioButton other)
    {
        if (_syncingDestination) return;
        _syncingDestination = true;
        try
        {
            if (selected.Checked) other.Checked = false;
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
        _scanStatus.Text = "Scanning files…";
        _refreshTimer.Start();
        _scanTask = DumpPreviewScanner.ScanAsync(_targetPath, _config, _scanState, _scanCancellation.Token);
    }

    private void RefreshFromScan()
    {
        if (_ending) return;
        _snapshot = _scanState.Snapshot();
        _selectionModel.Apply(_snapshot);
        RefreshTree();
        _scanStatus.Text = _snapshot.Completed
            ? "Scan complete"
            : "Scanning project…";

        if (_scanTask is { IsFaulted: true })
        {
            _refreshTimer.Stop();
            _scanStatus.Text = "Scan could not be completed";
            _validation.Text = _scanTask.Exception?.GetBaseException().Message ?? "Unknown scan error.";
        }
        else if (_snapshot.Completed)
        {
            _refreshTimer.Stop();
            RefreshPreview();
        }
    }

    private void RefreshTree()
    {
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TreeNode node in Flatten(_tree.Nodes))
            if (node.IsExpanded && node.Tag is NodeTag tag) expanded.Add(tag.Node.RelativePath);
        string? selected = _tree.SelectedNode?.Tag is NodeTag selectedTag
            ? selectedTag.Node.RelativePath : null;

        DumpContentSelection baseSelection = _selectionModel.Capture();
        DumpContributionNode contribution = DumpContributionTree.Build(_snapshot.Paths, _snapshot.Entries);
        long total = contribution.TextBytes;

        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            _tree.ClearMetrics();
            foreach (DumpContributionNode child in contribution.Children)
                _tree.Nodes.Add(BuildTreeNode(child, baseSelection, total));
            foreach (TreeNode node in Flatten(_tree.Nodes))
            {
                if (node.Tag is not NodeTag tag) continue;
                if (expanded.Contains(tag.Node.RelativePath)) node.Expand();
                if (string.Equals(selected, tag.Node.RelativePath, StringComparison.OrdinalIgnoreCase))
                    _tree.SelectedNode = node;
            }
            if (_snapshot.Completed && !_initialTreeExpansionApplied)
            {
                TreeNode? firstDirectory = _tree.Nodes.Cast<TreeNode>()
                    .FirstOrDefault(node => node.Tag is NodeTag tag && tag.Node.IsDirectory);
                firstDirectory?.Expand();
                if (firstDirectory is not null)
                {
                    foreach (TreeNode child in firstDirectory.Nodes)
                        if (child.Tag is NodeTag tag && tag.Node.IsDirectory) child.Expand();
                }
                _initialTreeExpansionApplied = true;
            }
        }
        finally { _tree.EndUpdate(); }

        var included = _snapshot.Entries.Where(entry => _pathSelection.IsIncluded(
            entry.RelativePath, baseSelection.AllowsType(entry.Type))).ToArray();
        _selectionSummary.Text = $"{FormatBytes(included.Sum(x => x.TextBytes))} selected";
        _create.Enabled = _snapshot.Completed
            && (_snapshot.Paths.Count > 0 || _config.LastSelectionMode == DumpSelectionMode.None);
    }

    private TreeNode BuildTreeNode(DumpContributionNode node, DumpContentSelection baseSelection, long total)
    {
        IReadOnlyList<DumpPreviewEntry> descendants = _snapshot.Entries.Where(entry =>
        {
            string path = NormalizePath(entry.RelativePath);
            string nodePath = NormalizePath(node.RelativePath);
            return nodePath.Length == 0 || string.Equals(path, nodePath, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(nodePath + "/", StringComparison.OrdinalIgnoreCase);
        }).ToArray();
        DumpNodeSelectionState state = _pathSelection.State(node.RelativePath,
            descendants.Select(x => (x.RelativePath, baseSelection.AllowsType(x.Type))));
        long value = node.TextBytes;
        double share = total > 0 ? 100d * value / total : 0;
        var treeNode = new TreeNode(node.Name)
        {
            Name = node.RelativePath,
            StateImageIndex = ContributionTreeView.StateIndex(state),
            ImageIndex = node.IsDirectory ? ContributionTreeView.FolderImageIndex : ContributionTreeView.FileImageIndex,
            SelectedImageIndex = node.IsDirectory ? ContributionTreeView.FolderImageIndex : ContributionTreeView.FileImageIndex,
            ForeColor = state == DumpNodeSelectionState.Excluded ? UiTheme.DisabledText : UiTheme.Text,
            Tag = new NodeTag(node, descendants),
            ToolTipText = $"{state}: " + (node.IsDirectory
                ? "Space toggles this folder and all files below it; Enter previews its contents"
                : $"{node.RelativePath}; press Enter to preview"),
        };
        _tree.SetMetrics(treeNode, FormatBytes(value), share);
        foreach (var child in node.Children) treeNode.Nodes.Add(BuildTreeNode(child, baseSelection, total));
        return treeNode;
    }

    private void ToggleNode(TreeNode treeNode)
    {
        if (treeNode.Tag is not NodeTag tag) return;
        DumpNodeSelectionState state = treeNode.StateImageIndex switch
        {
            1 => DumpNodeSelectionState.Included,
            2 => DumpNodeSelectionState.Mixed,
            _ => DumpNodeSelectionState.Excluded,
        };
        _pathSelection.Set(tag.Node.RelativePath, tag.Node.IsDirectory,
            state != DumpNodeSelectionState.Included);
        RefreshTree();
        RefreshPreview();
    }

    private void PreviewNode(TreeNode treeNode)
    {
        if (treeNode.Tag is not NodeTag tag) return;
        ShowPreview(tag.Node.IsDirectory ? null : tag.Node.RelativePath, _tree);
    }

    private void ShowPreview(string? relativePath = null, Control? returnFocus = null)
    {
        _previewPath = relativePath;
        _previewReturnFocus = returnFocus;
        RefreshPreview();
        _mapWorkspace.Visible = false;
        _previewWorkspace.Visible = true;
        _previewWorkspace.BringToFront();
        _previewBack.Focus();
    }

    private void ShowMap()
    {
        _previewWorkspace.Visible = false;
        _mapWorkspace.Visible = true;
        _mapWorkspace.BringToFront();
        Control target = _previewReturnFocus is { CanFocus: true } ? _previewReturnFocus : _previewButton;
        target.Focus();
    }

    private void RefreshPreview()
    {
        DumpContentSelection selection = _selectionModel.Capture().WithPathOverrides(_pathSelection.Overrides);
        _preview.Text = _snapshot.Paths.Count == 0
            ? "The output preview will appear as files are found."
            : DumpPreviewRenderer.Render(_snapshot, _config, selection, _previewPath);
        _preview.SelectionStart = 0;
        _preview.ScrollToCaret();
    }

    private void AcceptSelection()
    {
        _validation.Text = "";
        if (!_snapshot.Completed)
        {
            _validation.Text = "Wait for the scan to finish.";
            return;
        }

        DumpContentSelection selection = _selectionModel.Capture(_snapshot)
            .WithPathOverrides(_pathSelection.Overrides);
        if (_config.LastSelectionMode != DumpSelectionMode.None && _snapshot.Entries.Count > 0
            && !_snapshot.Entries.Any(entry => _pathSelection.IsIncluded(
                entry.RelativePath, selection.AllowsType(entry.Type))))
        {
            _validation.Text = "Include at least one text file.";
            return;
        }

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

        _config.OutputTarget = _file.Checked ? OutputTarget.File : OutputTarget.Clipboard;
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
        if (_scanTask is not null)
        {
            try { await _scanTask.ConfigureAwait(true); }
            catch (OperationCanceledException) { }
            catch { }
        }
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

    private static string NormalizePath(string path) => path.Replace('\\', '/').Trim('/');

    private static IEnumerable<TreeNode> Flatten(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            yield return node;
            foreach (TreeNode child in Flatten(node.Nodes)) yield return child;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && !_ending)
        {
            e.Cancel = true;
            BeginClose(DialogResult.Cancel);
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _scanCancellation.Dispose();
        }
        base.Dispose(disposing);
    }
}
