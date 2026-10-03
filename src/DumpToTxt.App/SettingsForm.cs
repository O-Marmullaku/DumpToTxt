using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DumpToTxt.Core;

namespace DumpToTxt.App;

/// <summary>
/// Task-oriented settings surface arranged into flat General, Files, and Safety tabs. Saving writes
/// the complete user config without rewriting untouched lossy fields.
/// </summary>
public sealed class SettingsForm : Form
{
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    private sealed class SettingsTabControl : TabControl
    {
        private const int WmPaint = 0x000F;
        private const int WmPrintClient = 0x0318;
        private const int WmChangeUiState = 0x0127;
        private const int UisSet = 1;
        private const int UisfHideFocus = 1;

        [DllImport("user32.dll")]
        private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            SendMessage(Handle, WmChangeUiState, UisSet | (UisfHideFocus << 16), 0);
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == WmPaint)
            {
                using Graphics graphics = CreateGraphics();
                CoverPageFrame(graphics);
            }
            else if (message.Msg == WmPrintClient)
            {
                using Graphics graphics = Graphics.FromHdc(message.WParam);
                CoverPageFrame(graphics);
            }
        }

        private void CoverPageFrame(Graphics graphics)
        {
            Rectangle page = DisplayRectangle;
            using var background = new SolidBrush(UiTheme.Window);
            graphics.FillRectangle(background, 0, page.Top, page.Left, ClientSize.Height - page.Top);
            graphics.FillRectangle(background, page.Right, page.Top, ClientSize.Width - page.Right, ClientSize.Height - page.Top);
            graphics.FillRectangle(background, 0, page.Bottom, ClientSize.Width, ClientSize.Height - page.Bottom);
        }
    }

    private sealed record ModeOption(string Name, DumpSelectionMode Mode)
    {
        public override string ToString() => Name;
    }

    private static readonly string[] KnownExt =
    {
        ".html", ".css", ".js", ".ts", ".json", ".md", ".txt",
        ".yml", ".yaml", ".xml", ".toml", ".ini",
        ".py", ".php", ".cs", ".java", ".kt", ".go", ".rs", ".rb", ".cpp", ".c", ".h", ".sql",
    };

    private static readonly string[] KnownExcl =
    {
        @"\.git", @"\.vs", "node_modules", "dist", "build", "bin", "obj",
        @"\.vscode", @"\.idea", "coverage", @"\.next", @"\.nuxt", "out",
    };

    // File types
    private readonly CheckedListBox _clbExt = new() { CheckOnClick = true };
    private readonly TextBox _tbExtSearch = new() { PlaceholderText = "Search file types" };
    private readonly Panel _fileTypePicker = new()
    {
        AccessibleName = "File type filter and list",
        AccessibleRole = AccessibleRole.Grouping,
    };
    private readonly TextBox _tbExtCustom = new();
    private readonly TextBox _tbDot = new();
    private readonly HashSet<string> _checkedKnownExt = new(StringComparer.OrdinalIgnoreCase);
    private bool _filteringExtensions;

    // Ignore & globs
    private readonly CheckBox _chkGitignore = new() { Text = "Follow .gitignore" };
    private readonly CheckBox _chkDumpignore = new() { Text = "Follow .dumptotxtignore" };
    private readonly CheckedListBox _clbExcl = new() { CheckOnClick = true };
    private readonly TextBox _tbExclCustom = new();
    private readonly TextBox _tbInclude = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _tbExclude = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };

    // Caps & binary
    private readonly CheckBox _chkDetectBinary = new() { Text = "Skip binary contents" };
    private readonly NumericUpDown _numMaxFile = new() { Maximum = 4_000_000, ThousandsSeparator = true, Increment = 64 };
    private readonly NumericUpDown _numMaxTotal = new() { Maximum = 4_000_000, ThousandsSeparator = true, Increment = 64 };

    // Output
    private readonly ComboBox _cmbFormat = new() { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(OutputFormatOption.DisplayName) };
    private readonly ComboBox _cmbStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cmbTarget = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _tbOutDir = new();
    private Label? _folderLabel;
    private readonly Button _btnBrowse = new() { Text = "Browse…", Width = 88, Height = 28 };
    private readonly ComboBox _cmbMode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cmbTheme = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _chkShowReview = new() { Text = "Review before creating a dump", AutoSize = true };
    private readonly CheckBox _chkCompletionSound = new() { Text = "Play sound when done", AutoSize = true };
    private readonly TabControl _tabs = new SettingsTabControl();
    private bool _syncingStyle;
    private readonly UiThemeKind _initialTheme;
    private bool _themeAccepted;

    // Tokens
    private readonly ComboBox _cmbTokenEnc = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _numMaxTokens = new() { Maximum = 2_000_000_000, ThousandsSeparator = true, Increment = 1000 };

    // Secrets
    private readonly ComboBox _cmbSecretScan = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _chkSecretEntropy = new() { Text = "Also check random-looking strings (more false alarms)", AutoSize = true };
    private readonly TextBox _tbSensitiveValues = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _tbSecretAllow = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };

    // Presets
    private readonly ComboBox _cmbPreset = new() { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name" };
    private readonly Button _pathRulesButton = new() { Text = "Advanced path rules" };
    private readonly TableLayoutPanel _pathRulesPanel = new() { Visible = false };

    private readonly Label _status = new();

    // Round-trip preservation: the GUI reconstructs ExcludeRegex from checkbox tokens and caps from KB,
    // both lossy. To stop a plain open→Save from silently rewriting a custom regex or collapsing a sub-KB
    // cap, we stash the loaded values and only regenerate from the controls the user actually edited.
    private bool _loading;
    private string? _loadedExcludeRegex;
    private bool _excludeUiDirty;
    private long _loadedMaxFileBytes;
    private long _loadedMaxTotalBytes;
    private bool _maxFileDirty;
    private bool _maxTotalDirty;
    private long _loadedMaxTokens;
    private bool _maxTokensDirty;
    private readonly Action<DumpConfig> _saveConfig;
    private string? _opaqueExcludeRegex;

    public SettingsForm() : this(ConfigStore.Load(), cfg =>
    {
        ConfigStore.Save(cfg);
        ContextMenu.TrySyncLabels(cfg);
    }) { }

    /// <summary>Explicit configuration/persistence seam for isolated native verification.</summary>
    public SettingsForm(DumpConfig config, Action<DumpConfig> saveConfig)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(saveConfig);
        _saveConfig = saveConfig;
        SuspendLayout();
        _initialTheme = UiTheme.CurrentKind;
        UiTheme.Apply(config.Theme);
        Text = "DumpToTxt";
        ShowIcon = false;
        Width = 860;
        Height = 570;
        MinimumSize = new Size(820, 550);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Font = UiTheme.UiFont();
        BackColor = UiTheme.Window;
        ForeColor = UiTheme.Text;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* no icon */ }

        BuildUi();
        LoadFromConfig(config);
        FormClosing += (_, _) =>
        {
            if (!_themeAccepted) UiTheme.Apply(_initialTheme);
        };
        ResumeLayout(true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyCaptionTheme();
    }

    private void ApplyCaptionTheme()
    {
        if (!IsHandleCreated) return;
        int captionColor = ColorTranslator.ToWin32(UiTheme.Window);
        int captionTextColor = ColorTranslator.ToWin32(UiTheme.Text);
        DwmSetWindowAttribute(Handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
        DwmSetWindowAttribute(Handle, DwmwaTextColor, ref captionTextColor, sizeof(int));
    }

    private void BuildUi()
    {
        PopulateControlChoices();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.Controls.Add(BuildSettingsHeader(), 0, 0);

        root.Controls.Add(BuildSettingsTabs(), 0, 1);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(20, 8, 20, 8), BackColor = UiTheme.Window };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var btnReset = new Button { Text = "Reset to defaults", Width = 126, Height = 34 };
        UiTheme.StyleSecondary(btnReset);
        btnReset.Click += (_, _) => OnReset();
        footer.Controls.Add(btnReset, 0, 0);
        _status.Anchor = AnchorStyles.Left;
        _status.AutoSize = true;
        footer.Controls.Add(_status, 1, 0);
        var btnCancel = new Button { Text = "Cancel", Width = 88, Height = 34, DialogResult = DialogResult.Cancel };
        UiTheme.StyleSecondary(btnCancel);
        btnCancel.Click += (_, _) => Close();
        footer.Controls.Add(btnCancel, 2, 0);
        var btnSave = new Button { Text = "Save and close", Width = 132, Height = 34 };
        UiTheme.StylePrimary(btnSave);
        btnSave.Click += (_, _) => OnSave();
        footer.Controls.Add(btnSave, 3, 0);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
        SizeChanged += (_, _) =>
        {
            root.Bounds = ClientRectangle;
            root.PerformLayout();
        };
        AcceptButton = btnSave;
        CancelButton = btnCancel;

        WireDirtyTracking();
    }

    private void PopulateControlChoices()
    {
        _cmbFormat.AccessibleName = "Format";
        _cmbStyle.AccessibleName = "Layout";
        _cmbTarget.AccessibleName = "Destination";
        _tbOutDir.AccessibleName = "Folder";
        _cmbMode.AccessibleName = "Included content";
        _cmbTheme.AccessibleName = "Theme";
        _cmbPreset.AccessibleName = "Quick setup";
        _cmbSecretScan.AccessibleName = "When sensitive data is found";
        _clbExt.AccessibleName = "Essential file types";
        _tbExtSearch.AccessibleName = "Search file types";
        _tbExtCustom.AccessibleName = "Other extensions";
        _tbDot.AccessibleName = "Specific file names";
        _clbExcl.AccessibleName = "Folders to skip";
        _tbInclude.AccessibleName = "Only scan these paths";
        _tbExclude.AccessibleName = "Skip these paths";
        _tbSensitiveValues.AccessibleName = "Always hide these values";
        _tbSecretAllow.AccessibleName = "Sensitive values that should not trigger a warning";
        foreach (ComboBox combo in new[] { _cmbFormat, _cmbStyle, _cmbTarget, _cmbMode, _cmbTheme, _cmbSecretScan, _cmbPreset, _cmbTokenEnc })
            UiTheme.StyleChoiceCombo(combo);
        foreach (var format in OutputStyleCatalog.Formats) _cmbFormat.Items.Add(format);
        _cmbFormat.SelectedIndexChanged += (_, _) => PopulateSettingsLayouts();
        _cmbStyle.Format += (_, e) => { if (e.ListItem is OutputStyle style) e.Value = OutputStyleCatalog.LayoutName(style); };
        foreach (OutputTarget target in Enum.GetValues<OutputTarget>()) _cmbTarget.Items.Add(target);
        _cmbTarget.FormattingEnabled = true;
        _cmbTarget.Format += (_, e) => e.Value = e.ListItem switch
        {
            OutputTarget.File => "File",
            OutputTarget.Clipboard => "Clipboard",
            OutputTarget.Stdout => "Console",
            _ => e.ListItem?.ToString(),
        };
        _cmbTarget.SelectedIndexChanged += (_, _) => SettingsDestinationChanged();
        _cmbMode.Items.Add(new ModeOption("Essential files", DumpSelectionMode.Basic));
        _cmbMode.Items.Add(new ModeOption("All text files", DumpSelectionMode.Thorough));
        _cmbMode.Items.Add(new ModeOption("File list only", DumpSelectionMode.None));
        foreach (UiThemeKind theme in Enum.GetValues<UiThemeKind>()) _cmbTheme.Items.Add(theme);
        _cmbTheme.SelectedIndexChanged += (_, _) => PreviewSelectedTheme();
        foreach (TokenEncoding encoding in Enum.GetValues<TokenEncoding>()) _cmbTokenEnc.Items.Add(encoding);
        _cmbTokenEnc.FormattingEnabled = true;
        _cmbTokenEnc.Format += (_, e) => e.Value = e.ListItem switch
        {
            TokenEncoding.O200kBase => "o200k (recommended)",
            TokenEncoding.Cl100kBase => "cl100k (legacy)",
            _ => e.ListItem?.ToString(),
        };
        foreach (SecretScanMode mode in Enum.GetValues<SecretScanMode>()) _cmbSecretScan.Items.Add(mode);
        _cmbSecretScan.FormattingEnabled = true;
        _cmbSecretScan.Format += (_, e) => e.Value = e.ListItem switch
        {
            SecretScanMode.Off => "Do not scan",
            SecretScanMode.Warn => "Ask before keeping",
            SecretScanMode.Redact => "Hide sensitive values",
            SecretScanMode.Skip => "Skip file contents",
            _ => e.ListItem?.ToString(),
        };
        _clbExt.ItemCheck += (_, e) =>
        {
            if (_filteringExtensions || _clbExt.Items[e.Index] is not string extension) return;
            if (e.NewValue == CheckState.Checked) _checkedKnownExt.Add(extension);
            else _checkedKnownExt.Remove(extension);
        };
        _tbExtSearch.TextChanged += (_, _) => ApplyExtensionFilter();
        ApplyExtensionFilter();
        foreach (string folder in KnownExcl) _clbExcl.Items.Add(folder);
        foreach (var preset in Presets.All.Where(p => p != Presets.ChangedFiles)) _cmbPreset.Items.Add(preset);
        if (_cmbPreset.Items.Count > 0) _cmbPreset.SelectedIndex = 0;
    }

    private Control BuildSettingsHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Window, Padding = new Padding(20, 10, 20, 6) };
        panel.Controls.Add(new Label
        {
            Text = "Settings",
            AutoSize = true,
            Location = new Point(20, 10),
            Font = UiTheme.UiFont(13f, FontStyle.Bold),
            ForeColor = UiTheme.Text,
        });
        var description = new Label
        {
            Text = "Defaults for new dumps.",
            AutoSize = false,
            AutoEllipsis = true,
            Location = new Point(20, 36),
            Height = 22,
            ForeColor = UiTheme.Muted,
            Width = 800,
        };
        panel.Controls.Add(description);
        var divider = new Panel { Height = 1, BackColor = UiTheme.Border, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        panel.Controls.Add(divider);
        panel.Resize += (_, _) =>
        {
            int Px(int value) => UiTheme.Px(panel, value);
            description.Width = Math.Max(Px(100), panel.ClientSize.Width - Px(40));
            divider.SetBounds(Px(20), panel.ClientSize.Height - Px(1), Math.Max(0, panel.ClientSize.Width - Px(40)), Px(1));
        };
        return panel;
    }

    private Control BuildSettingsTabs()
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Window, Padding = new Padding(20, 8, 20, 10) };
        _tabs.Dock = DockStyle.Fill;
        _tabs.Appearance = TabAppearance.FlatButtons;
        _tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        _tabs.Multiline = true;
        _tabs.SizeMode = TabSizeMode.Fixed;
        _tabs.ItemSize = new Size(150, 34);
        _tabs.DrawItem += (_, e) =>
        {
            bool selected = e.Index == _tabs.SelectedIndex;
            Rectangle bounds = e.Bounds;
            Color backgroundColor = selected && _tabs.Focused ? UiTheme.Surface : selected ? UiTheme.Window : UiTheme.Rail;
            using var background = new SolidBrush(backgroundColor);
            using var tabFont = UiTheme.UiFont(9f, selected ? FontStyle.Bold : FontStyle.Regular);
            e.Graphics.FillRectangle(background, bounds);
            TextRenderer.DrawText(e.Graphics, _tabs.TabPages[e.Index].Text, tabFont,
                bounds, selected ? UiTheme.Text : UiTheme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (selected)
            {
                using var accent = new SolidBrush(UiTheme.Accent);
                e.Graphics.FillRectangle(accent, bounds.Left + 12, bounds.Bottom - 3, bounds.Width - 24, 3);
            }
        };
        _tabs.GotFocus += (_, _) => _tabs.Invalidate();
        _tabs.LostFocus += (_, _) => _tabs.Invalidate();
        _tabs.TabPages.Add(BuildGeneralTab());
        _tabs.TabPages.Add(BuildContentTab());
        _tabs.TabPages.Add(BuildSafetyTab());
        host.Controls.Add(_tabs);
        _tabs.Resize += (_, _) =>
        {
            int Px(int value) => UiTheme.Px(_tabs, value);
            int width = Math.Max(Px(120), (_tabs.ClientSize.Width - Px(48)) / Math.Max(1, _tabs.TabCount));
            var size = new Size(width, Px(34));
            if (_tabs.ItemSize != size) _tabs.ItemSize = size;
        };
        return host;
    }

    private TabPage BuildGeneralTab()
    {
        var page = SettingsTab("General");
        var layout = SettingsTabLayout(330, 2);
        layout.ColumnCount = 2;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(BuildRunDefaultsSection(), 0, 0);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 0)!, 2);
        var review = BuildContentDefaultsSection();
        review.Margin = new Padding(0, 12, 12, 0);
        var quickSetup = BuildStartingPointSection();
        quickSetup.Margin = new Padding(12, 12, 0, 0);
        layout.Controls.Add(review, 0, 1);
        layout.Controls.Add(quickSetup, 1, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildContentTab()
    {
        var page = SettingsTab("Files");
        var layout = SettingsTabLayout(380, 2);
        page.Resize += (_, _) => layout.Height = Math.Max(layout.MinimumSize.Height, page.ClientSize.Height);
        layout.ColumnCount = 2;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var types = BuildTextTypesSection();
        types.Margin = new Padding(0, 0, 12, 10);
        var folders = BuildExcludedFoldersSection();
        folders.Margin = new Padding(12, 0, 0, 10);
        layout.Controls.Add(types, 0, 0);
        layout.Controls.Add(folders, 1, 0);
        layout.Controls.Add(BuildPathRulesDisclosure(layout), 0, 1);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 1)!, 2);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildSafetyTab()
    {
        var page = SettingsTab("Safety & limits");
        var layout = SettingsTabLayout(330, 1);
        layout.ColumnCount = 2;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = UiTheme.Window,
            Margin = new Padding(0, 0, 12, 0),
        };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        left.Controls.Add(BuildSafetySection(), 0, 0);
        left.Controls.Add(BuildLimitsSection(), 0, 1);
        var ai = BuildAiSection();
        ai.Margin = new Padding(12, 0, 0, 0);
        layout.Controls.Add(left, 0, 0);
        layout.Controls.Add(ai, 1, 0);
        page.Controls.Add(layout);
        return page;
    }

    private static TabPage SettingsTab(string text) => new(text)
    {
        BackColor = UiTheme.Window,
        ForeColor = UiTheme.Text,
        AutoScroll = true,
    };

    private static TableLayoutPanel SettingsTabLayout(int minimumHeight, int rows) => new()
    {
        Dock = DockStyle.Top,
        Height = minimumHeight,
        MinimumSize = new Size(0, minimumHeight),
        RowCount = rows,
        ColumnCount = 1,
        Padding = new Padding(16),
        BackColor = UiTheme.Window,
    };

    private Control BuildRunDefaultsSection()
    {
        var section = FlatSection("Output", "Format, layout, and destination.");
        AddFieldLabel(section, "Format", 62, width: 84);
        AddFieldLabel(section, "Layout", 96, width: 84);
        var destinationLabel = AddFieldLabel(section, "Destination", 62, width: 84);
        _folderLabel = AddFieldLabel(section, "Folder", 96, width: 84);
        _cmbFormat.SetBounds(88, 58, 250, 31);
        _cmbStyle.SetBounds(88, 92, 250, 31);
        _cmbTarget.SetBounds(492, 58, 220, 31);
        _tbOutDir.SetBounds(492, 92, 130, 27);
        _tbOutDir.PlaceholderText = "Desktop";
        _btnBrowse.Location = new Point(630, 91);
        UiTheme.StyleSecondary(_btnBrowse);
        _btnBrowse.Click += (_, _) => OnBrowse();
        section.Controls.AddRange(new Control[] { _cmbFormat, _cmbStyle, _cmbTarget, _tbOutDir, _btnBrowse });
        section.Resize += (_, _) =>
        {
            int Px(int value) => UiTheme.Px(section, value);
            int gap = Px(28);
            int half = Math.Max(Px(280), (section.ClientSize.Width - gap) / 2);
            int right = half + gap;
            int leftControlWidth = Math.Max(Px(180), half - Px(88));
            int rightControlWidth = Math.Max(Px(180), section.ClientSize.Width - right - Px(88));
            _cmbFormat.SetBounds(Px(88), Px(58), leftControlWidth, Px(31));
            _cmbStyle.SetBounds(Px(88), Px(92), leftControlWidth, Px(31));
            destinationLabel.Left = right;
            _folderLabel.Left = right;
            _cmbTarget.SetBounds(right + Px(88), Px(58), rightControlWidth, Px(31));
            _btnBrowse.Left = section.ClientSize.Width - _btnBrowse.Width;
            _tbOutDir.SetBounds(right + Px(88), Px(92),
                Math.Max(Px(80), _btnBrowse.Left - (right + Px(88)) - Px(8)), Px(27));
        };
        return section;
    }

    private Control BuildContentDefaultsSection()
    {
        var section = FlatSection("Review", "Choose what is selected when review opens.");
        AddFieldLabel(section, "Start with", 62, width: 100);
        _cmbMode.SetBounds(110, 58, 250, 31);
        _chkShowReview.Location = new Point(110, 96);
        _chkCompletionSound.Location = new Point(110, 120);
        section.Controls.AddRange(new Control[] { _cmbMode, _chkShowReview, _chkCompletionSound });
        section.Resize += (_, _) => _cmbMode.Width = Math.Max(UiTheme.Px(section, 160), section.ClientSize.Width - _cmbMode.Left);
        return section;
    }

    private Control BuildStartingPointSection()
    {
        var section = FlatSection("Quick setup", "Apply a starting point, then fine-tune it.");
        AddFieldLabel(section, "Preset", 62, width: 58);
        _cmbPreset.SetBounds(66, 58, 190, 31);
        var apply = new Button { Text = "Use this setup", Location = new Point(268, 58), Width = 120, Height = 31 };
        UiTheme.StyleSecondary(apply);
        apply.Click += (_, _) => OnLoadPreset();
        AddFieldLabel(section, "Theme", 98, width: 58);
        _cmbTheme.SetBounds(66, 94, 160, 31);
        section.Controls.AddRange(new Control[] { _cmbPreset, apply, _cmbTheme });
        section.Resize += (_, _) =>
        {
            apply.Left = section.ClientSize.Width - apply.Width;
            int Px(int value) => UiTheme.Px(section, value);
            _cmbPreset.Width = Math.Max(Px(140), apply.Left - _cmbPreset.Left - Px(12));
            _cmbTheme.Width = Math.Min(Px(190), Math.Max(Px(140), section.ClientSize.Width - _cmbTheme.Left));
        };
        return section;
    }

    private Control BuildTextTypesSection()
    {
        var section = FlatSection("File types", "Used by Essential.");
        _fileTypePicker.SetBounds(0, 58, 320, 154);
        _fileTypePicker.BackColor = UiTheme.Surface;
        _fileTypePicker.BorderStyle = BorderStyle.FixedSingle;
        _tbExtSearch.BorderStyle = BorderStyle.None;
        _tbExtSearch.SetBounds(8, 7, 302, 23);
        _clbExt.BorderStyle = BorderStyle.None;
        _clbExt.SetBounds(1, 35, 318, 118);
        var searchDivider = new Panel { BackColor = UiTheme.Border, Height = 1 };
        _fileTypePicker.Controls.AddRange(new Control[] { _tbExtSearch, searchDivider, _clbExt });
        _fileTypePicker.Resize += (_, _) =>
        {
            int Px(int value) => UiTheme.Px(_fileTypePicker, value);
            _tbExtSearch.Width = Math.Max(Px(80), _fileTypePicker.ClientSize.Width - Px(16));
            searchDivider.SetBounds(0, Px(34), _fileTypePicker.ClientSize.Width, Px(1));
            _clbExt.SetBounds(Px(1), Px(35), Math.Max(Px(80), _fileTypePicker.ClientSize.Width - Px(2)),
                Math.Max(Px(60), _fileTypePicker.ClientSize.Height - Px(36)));
        };
        var custom = FieldHint("Other extensions", 220);
        _tbExtCustom.SetBounds(0, 241, 320, 27);
        var named = FieldHint("Specific file names", 276);
        _tbDot.SetBounds(0, 297, 320, 27);
        section.Controls.AddRange(new Control[] { _fileTypePicker, custom, _tbExtCustom, named, _tbDot });
        section.Resize += (_, _) =>
        {
            int Px(int value) => UiTheme.Px(section, value);
            int width = Math.Max(Px(180), section.ClientSize.Width);
            int listBottom = Math.Max(Px(180), section.ClientSize.Height - Px(130));
            _fileTypePicker.Width = width;
            _fileTypePicker.Height = Math.Max(Px(122), listBottom - _fileTypePicker.Top);
            custom.Top = listBottom + Px(8);
            _tbExtCustom.Top = listBottom + Px(29);
            named.Top = listBottom + Px(64);
            _tbDot.Top = listBottom + Px(85);
            _tbExtCustom.Width = width;
            _tbDot.Width = width;
        };
        return section;
    }

    private Control BuildExcludedFoldersSection()
    {
        var section = FlatSection("Folders to skip", "Common folders and ignore rules.");
        _chkGitignore.Text = "Follow .gitignore";
        _chkDumpignore.Text = "Follow .dumptotxtignore";
        _chkGitignore.SetBounds(0, 58, 280, 24);
        _chkDumpignore.SetBounds(0, 82, 280, 24);
        _clbExcl.SetBounds(0, 108, 320, 80);
        var additional = FieldHint("Other folder names", 196);
        _tbExclCustom.SetBounds(0, 217, 320, 27);
        section.Controls.AddRange(new Control[] { _chkGitignore, _chkDumpignore, _clbExcl, additional, _tbExclCustom });
        section.Resize += (_, _) =>
        {
            int Px(int value) => UiTheme.Px(section, value);
            int width = Math.Max(Px(180), section.ClientSize.Width);
            _clbExcl.Width = width;
            _clbExcl.Height = Math.Max(Px(100), section.ClientSize.Height - Px(168));
            additional.Top = _clbExcl.Bottom + Px(8);
            _tbExclCustom.Top = additional.Bottom + Px(4);
            _tbExclCustom.Width = width;
        };
        return section;
    }

    private Control BuildPathRulesDisclosure(TableLayoutPanel filesLayout)
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = UiTheme.Window,
            Margin = new Padding(0, 8, 0, 0),
        };
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        UiTheme.StyleLinkButton(_pathRulesButton);
        _pathRulesButton.BackColor = UiTheme.Window;
        _pathRulesButton.Dock = DockStyle.Fill;
        _pathRulesButton.TextAlign = ContentAlignment.MiddleLeft;
        _pathRulesButton.Click += (_, _) =>
        {
            _pathRulesPanel.Visible = !_pathRulesPanel.Visible;
            filesLayout.RowStyles[1].Height = UiTheme.Px(filesLayout, _pathRulesPanel.Visible ? 190 : 40);
            filesLayout.MinimumSize = new Size(0, UiTheme.Px(filesLayout, _pathRulesPanel.Visible ? 530 : 380));
            filesLayout.Height = Math.Max(filesLayout.MinimumSize.Height, filesLayout.Parent!.ClientSize.Height);
        };
        host.Controls.Add(_pathRulesButton, 0, 0);

        _pathRulesPanel.Dock = DockStyle.Fill;
        _pathRulesPanel.ColumnCount = 2;
        _pathRulesPanel.RowCount = 1;
        _pathRulesPanel.BackColor = UiTheme.Window;
        _pathRulesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _pathRulesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var included = BuildPathPatternsSection("Only scan these paths", "Empty = everything.", _tbInclude);
        included.Margin = new Padding(0, 8, 12, 0);
        var excluded = BuildPathPatternsSection("Skip these paths", "Example: logs/** or *.min.js", _tbExclude);
        excluded.Margin = new Padding(12, 8, 0, 0);
        _pathRulesPanel.Controls.Add(included, 0, 0);
        _pathRulesPanel.Controls.Add(excluded, 1, 0);
        host.Controls.Add(_pathRulesPanel, 0, 1);
        return host;
    }

    private Control BuildPathPatternsSection(string title, string description, TextBox textBox)
    {
        var section = FlatSection(title, description);
        textBox.SetBounds(0, 58, 320, 100);
        section.Controls.Add(textBox);
        section.Resize += (_, _) => textBox.SetBounds(0, UiTheme.Px(section, 58), Math.Max(UiTheme.Px(section, 180), section.ClientSize.Width), Math.Max(UiTheme.Px(section, 52), section.ClientSize.Height - UiTheme.Px(section, 58)));
        return section;
    }

    private Control BuildSafetySection()
    {
        var section = FlatSection("Sensitive data", "What to do when a likely sensitive value is found.");
        AddFieldLabel(section, "When found", 62);
        _cmbSecretScan.SetBounds(140, 58, 240, 31);
        _chkSecretEntropy.Location = new Point(0, 96);
        section.Controls.AddRange(new Control[] { _cmbSecretScan, _chkSecretEntropy });
        section.Resize += (_, _) => _cmbSecretScan.Width = Math.Max(UiTheme.Px(section, 160), section.ClientSize.Width - _cmbSecretScan.Left);
        return section;
    }

    private Control BuildLimitsSection()
    {
        var section = FlatSection("File limits", "0 means unlimited. Binary files stay in the list.");
        _chkDetectBinary.SetBounds(0, 58, 300, 24);
        AddFieldLabel(section, "Max file size (KB)", 94);
        _numMaxFile.SetBounds(160, 90, 160, 27);
        AddFieldLabel(section, "Max dump size (KB)", 128);
        _numMaxTotal.SetBounds(160, 124, 160, 27);
        section.Controls.AddRange(new Control[] { _chkDetectBinary, _numMaxFile, _numMaxTotal });
        return section;
    }

    private Control BuildAiSection()
    {
        var section = FlatSection("Token estimate", "Estimate size for AI tools.");
        AddFieldLabel(section, "Model", 62);
        _cmbTokenEnc.SetBounds(160, 58, 240, 31);
        AddFieldLabel(section, "Warn above", 96);
        _numMaxTokens.SetBounds(160, 92, 240, 27);
        var hideHint = FieldHint("Always hide these values (one regex per line)", 132);
        _tbSensitiveValues.SetBounds(0, 153, 680, 58);
        var allowHint = FieldHint("Do not warn about these values (one regex per line)", 218);
        _tbSecretAllow.SetBounds(0, 239, 680, 54);
        section.Controls.AddRange(new Control[]
        {
            _cmbTokenEnc, _numMaxTokens, hideHint, _tbSensitiveValues, allowHint, _tbSecretAllow,
        });
        section.Resize += (_, _) =>
        {
            int Px(int value) => UiTheme.Px(section, value);
            int choiceWidth = Math.Max(Px(160), section.ClientSize.Width - Px(160));
            _cmbTokenEnc.Width = choiceWidth;
            _numMaxTokens.Width = choiceWidth;
            int width = Math.Max(Px(240), section.ClientSize.Width);
            _tbSensitiveValues.SetBounds(0, Px(153), width, Px(58));
            _tbSecretAllow.SetBounds(0, Px(239), width, Math.Max(Px(42), section.ClientSize.Height - Px(239)));
        };
        return section;
    }

    private static Panel FlatSection(string title, string description)
    {
        var section = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Window, Margin = new Padding(0) };
        section.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Location = new Point(0, 0),
            Font = UiTheme.UiFont(10f, FontStyle.Bold),
            ForeColor = UiTheme.Text,
        });
        var helper = new Label
        {
            Text = description,
            AutoEllipsis = true,
            Location = new Point(0, 24),
            Height = 20,
            ForeColor = UiTheme.Muted,
        };
        var divider = new Panel { Height = 1, BackColor = UiTheme.Border };
        section.Controls.AddRange(new Control[] { helper, divider });
        section.Resize += (_, _) =>
        {
            helper.Width = Math.Max(UiTheme.Px(section, 100), section.ClientSize.Width);
            divider.SetBounds(0, UiTheme.Px(section, 49), Math.Max(0, section.ClientSize.Width), UiTheme.Px(section, 1));
        };
        return section;
    }

    private static Label FieldHint(string text, int top) => new()
    {
        Text = text,
        Location = new Point(0, top),
        AutoSize = true,
        ForeColor = UiTheme.Muted,
    };

    private static Label AddFieldLabel(Control parent, string text, int top, int left = 0, int width = 132)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(left, top),
            Width = width,
            Height = 22,
            ForeColor = UiTheme.Muted,
        };
        parent.Controls.Add(label);
        return label;
    }

    private void PopulateSettingsLayouts()
    {
        if (_syncingStyle || _cmbFormat.SelectedItem is not OutputFormatOption format) return;
        OutputStyle old = _cmbStyle.SelectedItem is OutputStyle selected ? selected : OutputStyle.Classic;
        _syncingStyle = true;
        _cmbStyle.Items.Clear();
        foreach (OutputStyle style in OutputStyleCatalog.Layouts(format.Kind)) _cmbStyle.Items.Add(style);
        _cmbStyle.SelectedItem = OutputStyleCatalog.Format(old) == format.Kind
            ? old : OutputStyleCatalog.DefaultStyle(format.Kind);
        _syncingStyle = false;
        bool word = format.Kind == OutputFormatKind.Word;
        if (word) _cmbTarget.SelectedItem = OutputTarget.File;
        SettingsDestinationChanged();
    }

    private void SettingsDestinationChanged()
    {
        bool word = _cmbStyle.SelectedItem is OutputStyle style && OutputStyleCatalog.IsWord(style);
        if (word && _cmbTarget.SelectedItem is not OutputTarget.File)
            _cmbTarget.SelectedItem = OutputTarget.File;
        _cmbTarget.Enabled = !word;
        bool file = _cmbTarget.SelectedItem is not OutputTarget target || target == OutputTarget.File;
        _tbOutDir.Enabled = file;
        _btnBrowse.Enabled = file;
        if (_folderLabel is not null) _folderLabel.Visible = file;
        _tbOutDir.Visible = file;
        _btnBrowse.Visible = file;
    }

    /// <summary>Marks the exclude-folder UI / cap spinners dirty when the USER edits them (guarded against
    /// the programmatic population in <see cref="LoadFromConfig"/>), so a save only regenerates the lossy
    /// fields the user actually touched and otherwise preserves the loaded values verbatim.</summary>
    private void WireDirtyTracking()
    {
        _clbExcl.ItemCheck += (_, _) => { if (!_loading) _excludeUiDirty = true; };
        _tbExclCustom.TextChanged += (_, _) => { if (!_loading) _excludeUiDirty = true; };
        _numMaxFile.ValueChanged += (_, _) => { if (!_loading) _maxFileDirty = true; };
        _numMaxTotal.ValueChanged += (_, _) => { if (!_loading) _maxTotalDirty = true; };
        _numMaxTokens.ValueChanged += (_, _) => { if (!_loading) _maxTokensDirty = true; };
    }

    private void ApplyExtensionFilter()
    {
        string search = _tbExtSearch.Text.Trim();
        _filteringExtensions = true;
        _clbExt.BeginUpdate();
        try
        {
            _clbExt.Items.Clear();
            foreach (string extension in KnownExt.Where(extension =>
                         search.Length == 0 || extension.Contains(search, StringComparison.OrdinalIgnoreCase)))
                _clbExt.Items.Add(extension, _checkedKnownExt.Contains(extension));
        }
        finally
        {
            _clbExt.EndUpdate();
            _filteringExtensions = false;
        }
    }

    private void LoadFromConfig(DumpConfig cfg)
    {
        _loading = true;
        try
        {
            var cur = new HashSet<string>(cfg.ExtSet.Select(e => e.ToLowerInvariant()));
            _checkedKnownExt.Clear();
            foreach (string extension in KnownExt.Where(cur.Contains)) _checkedKnownExt.Add(extension);
            ApplyExtensionFilter();
            // Surface custom (non-listed) extensions so a save round-trips them instead of dropping them.
            _tbExtCustom.Text = string.Join(", ", cur.Where(e => !KnownExt.Contains(e)));

            var folderTokens = ParseFolderTokens(cfg.ExcludeRegex);
            _opaqueExcludeRegex = folderTokens is null ? cfg.ExcludeRegex : null;
            for (int i = 0; i < _clbExcl.Items.Count; i++)
            {
                var tok = _clbExcl.Items[i]!.ToString()!;
                _clbExcl.SetItemChecked(i, folderTokens?.Contains(tok, StringComparer.OrdinalIgnoreCase) == true);
            }
            _tbExclCustom.Text = folderTokens is null ? "" : string.Join(", ",
                folderTokens.Where(t => !KnownExcl.Contains(t)).Select(Regex.Unescape));

            _tbDot.Text = string.Join(", ", cfg.DotFilesAllow);
            _chkGitignore.Checked = cfg.RespectGitignore;
            _chkDumpignore.Checked = cfg.UseDumpToTxtIgnore;
            _tbInclude.Text = string.Join("\r\n", cfg.IncludeGlobs);
            _tbExclude.Text = string.Join("\r\n", cfg.ExcludeGlobs);
            _chkDetectBinary.Checked = cfg.DetectBinary;
            _numMaxFile.Value = ClampKb(cfg.MaxFileSizeBytes);
            _numMaxTotal.Value = ClampKb(cfg.MaxTotalSizeBytes);
            _syncingStyle = true;
            _cmbFormat.SelectedItem = _cmbFormat.Items.Cast<OutputFormatOption>()
                .First(x => x.Kind == OutputStyleCatalog.Format(cfg.Style));
            _cmbStyle.Items.Clear();
            foreach (OutputStyle style in OutputStyleCatalog.Layouts(OutputStyleCatalog.Format(cfg.Style)))
                _cmbStyle.Items.Add(style);
            _cmbStyle.SelectedItem = cfg.Style;
            _syncingStyle = false;
            _cmbTarget.SelectedItem = cfg.OutputTarget;
            _tbOutDir.Text = cfg.OutputDir ?? "";
            _cmbMode.SelectedItem = _cmbMode.Items.Cast<ModeOption>()
                .First(x => x.Mode == cfg.LastSelectionMode);
            _cmbTheme.SelectedItem = cfg.Theme;
            _chkShowReview.Checked = cfg.ShowReviewBeforeDump;
            _chkCompletionSound.Checked = cfg.PlayCompletionSound;
            _cmbTokenEnc.SelectedItem = cfg.TokenEncoding;
            _numMaxTokens.Value = ClampTokens(cfg.MaxTokens);
            _cmbSecretScan.SelectedItem = cfg.SecretScan;
            _chkSecretEntropy.Checked = cfg.SecretScanEntropy;
            _tbSensitiveValues.Text = string.Join("\r\n", cfg.SensitiveValuePatterns);
            _tbSecretAllow.Text = string.Join("\r\n", cfg.SecretAllowlist);
            SettingsDestinationChanged();

            // Stash the exact loaded values + reset dirty so an untouched save preserves them verbatim.
            _loadedExcludeRegex = cfg.ExcludeRegex;
            _loadedMaxFileBytes = cfg.MaxFileSizeBytes;
            _loadedMaxTotalBytes = cfg.MaxTotalSizeBytes;
            _loadedMaxTokens = cfg.MaxTokens;
        }
        finally { _loading = false; }

        _excludeUiDirty = false;
        _maxFileDirty = false;
        _maxTotalDirty = false;
        _maxTokensDirty = false;
    }

    /// <summary>Reads every control into a complete config (so a save never drops a field).</summary>
    private DumpConfig BuildConfigFromForm() => new()
    {
        ExtSet = BuildExtSet(),
        DotFilesAllow = SplitCsv(_tbDot.Text),
        // Preserve the loaded regex unless the user actually edited the exclude-folder UI (otherwise a plain
        // open→Save would replace any custom/non-token-shaped regex with the generated one).
        ExcludeRegex = (!_excludeUiDirty && _loadedExcludeRegex is not null) ? _loadedExcludeRegex : BuildExcludeRegex(),
        RespectGitignore = _chkGitignore.Checked,
        UseDumpToTxtIgnore = _chkDumpignore.Checked,
        IncludeGlobs = SplitLines(_tbInclude.Text),
        ExcludeGlobs = SplitLines(_tbExclude.Text),
        DetectBinary = _chkDetectBinary.Checked,
        // Preserve the exact loaded byte caps unless the user touched the (KB-granular) spinner — a sub-KB
        // cap (e.g. 500) would otherwise truncate to 0 KB = unlimited, silently removing the cap.
        MaxFileSizeBytes = _maxFileDirty ? (long)_numMaxFile.Value * 1024 : _loadedMaxFileBytes,
        MaxTotalSizeBytes = _maxTotalDirty ? (long)_numMaxTotal.Value * 1024 : _loadedMaxTotalBytes,
        Style = _cmbStyle.SelectedItem is OutputStyle s ? s : OutputStyle.Classic,
        OutputTarget = _cmbTarget.SelectedItem is OutputTarget t ? t : OutputTarget.File,
        OutputDir = _tbOutDir.Text.Trim().Length == 0 ? null : _tbOutDir.Text.Trim(),
        LastSelectionMode = _cmbMode.SelectedItem is ModeOption option ? option.Mode : DumpSelectionMode.Basic,
        ShowReviewBeforeDump = _chkShowReview.Checked,
        PlayCompletionSound = _chkCompletionSound.Checked,
        Theme = _cmbTheme.SelectedItem is UiThemeKind theme ? theme : UiThemeKind.Graphite,
        TokenEncoding = _cmbTokenEnc.SelectedItem is TokenEncoding te ? te : TokenEncoding.O200kBase,
        // Preserve the exact loaded budget unless the user touched the spinner, so a value above the
        // spinner's display ceiling isn't silently clamped on an untouched save (mirrors the byte caps).
        MaxTokens = _maxTokensDirty ? (long)_numMaxTokens.Value : _loadedMaxTokens,
        SecretScan = _cmbSecretScan.SelectedItem is SecretScanMode sm ? sm : SecretScanMode.Warn,
        SecretScanEntropy = _chkSecretEntropy.Checked,
        SensitiveValuePatterns = SplitRegexLines(_tbSensitiveValues.Text),
        SecretAllowlist = SplitRegexLines(_tbSecretAllow.Text),
    };

    private List<string> BuildExtSet()
    {
        var picked = _checkedKnownExt.Select(extension => extension.ToLowerInvariant());
        var custom = _tbExtCustom.Text.Split(',')
            .Select(s => s.Trim().ToLowerInvariant())
            .Where(s => s.StartsWith('.') && s.Length > 1);
        return picked.Concat(custom).Distinct().ToList();
    }

    private string BuildExcludeRegex()
    {
        var alts = _clbExcl.CheckedItems.Cast<object>().Select(o => o.ToString()!).ToList();
        alts.AddRange(_tbExclCustom.Text.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Select(Regex.Escape));
        alts = alts.Where(s => s.Length > 0).Distinct().ToList();
        string editable = alts.Count == 0 ? "(?!)" : @"\\(" + string.Join("|", alts) + @")(\\|$)";
        if (_opaqueExcludeRegex is null) return editable;
        // Preserve opaque backreferences and trailing IgnorePatternWhitespace comments.
        string literal = alts.Count == 0 ? "(?!)" : @"\\(?:" + string.Join("|", alts) + @")(?:\\|$)";
        return $"(?:{literal})|{_opaqueExcludeRegex}";
    }

    private static string[]? ParseFolderTokens(string pattern)
    {
        if (pattern == "(?!)") return Array.Empty<string>();
        const string prefix = @"\\(";
        const string suffix = @")(\\|$)";
        if (!pattern.StartsWith(prefix, StringComparison.Ordinal) || !pattern.EndsWith(suffix, StringComparison.Ordinal)) return null;
        string[] tokens = pattern[prefix.Length..^suffix.Length].Split('|');
        try { return tokens.All(t => Regex.Escape(Regex.Unescape(t)) == t) ? tokens : null; }
        catch (ArgumentException) { return null; }
    }

    private static List<string> SplitCsv(string s) =>
        s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

    private static List<string> SplitLines(string s) =>
        s.Split('\r', '\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

    private static List<string> SplitRegexLines(string s) =>
        s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

    private static decimal ClampKb(long bytes)
    {
        if (bytes <= 0) return 0;
        // Round a non-zero byte cap UP so a sub-KB cap shows as ≥1 KB, never 0 (which reads as "unlimited").
        // The exact byte value is preserved on save via the stash unless the user edits the spinner.
        long kb = (long)Math.Ceiling(bytes / 1024.0);
        if (kb > 4_000_000) kb = 4_000_000;
        return kb;
    }

    private static decimal ClampTokens(long v) => v < 0 ? 0 : (v > 2_000_000_000 ? 2_000_000_000 : v);

    private void OnBrowse()
    {
        using var dlg = new FolderBrowserDialog { Description = "Choose where to save dumps" };
        if (!string.IsNullOrWhiteSpace(_tbOutDir.Text) && Directory.Exists(_tbOutDir.Text))
            dlg.SelectedPath = _tbOutDir.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _tbOutDir.Text = dlg.SelectedPath;
    }

    private void OnLoadPreset()
    {
        if (_cmbPreset.SelectedItem is not Preset p) { _status.Text = "Choose a quick setup."; return; }
        var cfg = BuildConfigFromForm();
        p.Apply(cfg);
        LoadFromConfig(cfg);
        _status.Text = $"{p.Name} applied. Save to keep it.";
    }

    private void OnSave()
    {
        var cfg = BuildConfigFromForm();
        if (cfg.ExtSet.Count == 0)
        {
            _tabs.SelectedIndex = 1;
            _status.ForeColor = UiTheme.Error;
            _status.Text = "Choose at least one file type.";
            return;
        }
        try { _ = new Regex(cfg.ExcludeRegex, RegexOptions.None, TimeSpan.FromMilliseconds(250)); }
        catch
        {
            _tabs.SelectedIndex = 1;
            _status.ForeColor = UiTheme.Error;
            _status.Text = "The folder exclusion pattern is invalid.";
            return;
        }

        foreach (var (name, source) in new[]
        {
            ("Always hide", _tbSensitiveValues.Text), ("Do not warn", _tbSecretAllow.Text),
        })
        {
            string[] patterns = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < patterns.Length; i++)
            {
                try { _ = new Regex(patterns[i], RegexOptions.None, TimeSpan.FromMilliseconds(250)); }
                catch (ArgumentException)
                {
                    _tabs.SelectedIndex = 2;
                    _status.ForeColor = UiTheme.Error;
                    _status.Text = $"{name}: invalid pattern on line {i + 1}.";
                    return;
                }
            }
        }
        try { _saveConfig(cfg); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _status.ForeColor = UiTheme.Error;
            _status.Text = ex is System.Text.Json.JsonException
                ? "Settings file is invalid. Repair it before saving."
                : "Could not save settings. Check folder access.";
            return;
        }
        _themeAccepted = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnReset()
    {
        LoadFromConfig(DumpConfig.CreateDefault());
        PreviewSelectedTheme();
        _tbExtCustom.Text = "";
        _tbExclCustom.Text = "";
        _status.ForeColor = UiTheme.Warning;
        _status.Text = "Defaults restored. Save to keep them.";
    }

    private void PreviewSelectedTheme()
    {
        if (_loading || _cmbTheme.SelectedItem is not UiThemeKind theme) return;
        UiTheme.Apply(theme, this);
        ApplyCaptionTheme();
        _tabs.Invalidate(true);
    }
}
