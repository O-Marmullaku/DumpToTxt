using System.Drawing;
using System.Text.RegularExpressions;
using DumpToTxt.Core;

namespace DumpToTxt.App;

/// <summary>
/// Settings GUI. Rebuilt in P4 into tabs (File types / Ignore &amp; globs / Caps &amp; binary / Output /
/// Presets) so the full config — including the P3 ignore/glob/caps knobs that were previously
/// settings.json-only — is reachable from the UI. Saving writes the COMPLETE config (fixing the prior
/// bug where a save erased the P3 fields) and re-syncs the right-click label. Saves to the user config
/// (%APPDATA%\DumpToTxt\settings.json).
/// </summary>
public sealed class SettingsForm : Form
{
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
    private readonly TextBox _tbExtCustom = new();
    private readonly TextBox _tbDot = new();

    // Ignore & globs
    private readonly CheckBox _chkGitignore = new() { Text = "Respect .gitignore" };
    private readonly CheckBox _chkDumpignore = new() { Text = "Respect .dumptotxtignore" };
    private readonly CheckedListBox _clbExcl = new() { CheckOnClick = true };
    private readonly TextBox _tbExclCustom = new();
    private readonly TextBox _tbInclude = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _tbExclude = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };

    // Caps & binary
    private readonly CheckBox _chkDetectBinary = new() { Text = "Detect and skip binary files" };
    private readonly NumericUpDown _numMaxFile = new() { Maximum = 4_000_000, ThousandsSeparator = true, Increment = 64 };
    private readonly NumericUpDown _numMaxTotal = new() { Maximum = 4_000_000, ThousandsSeparator = true, Increment = 64 };

    // Output
    private readonly ComboBox _cmbStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cmbTarget = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _tbOutDir = new();

    // Tokens
    private readonly ComboBox _cmbTokenEnc = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _numMaxTokens = new() { Maximum = 2_000_000_000, ThousandsSeparator = true, Increment = 1000 };

    // Secrets
    private readonly ComboBox _cmbSecretScan = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _chkSecretEntropy = new() { Text = "Also flag generic high-entropy strings (noisier)" };
    private readonly TextBox _tbSecretAllow = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };

    // Presets
    private readonly ComboBox _cmbPreset = new() { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name" };
    private readonly Label _lblPresetDesc = new();

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

    public SettingsForm()
    {
        Text = "DumpToTxt Settings";
        Width = 800;
        Height = 660;
        MinimumSize = new Size(800, 660);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* no icon */ }

        BuildUi();
        LoadFromConfig(ConfigStore.Load());
    }

    private void BuildUi()
    {
        var tabs = new TabControl { Left = 12, Top = 12, Width = 762, Height = 575 };
        tabs.TabPages.Add(BuildFileTypesTab());
        tabs.TabPages.Add(BuildIgnoreTab());
        tabs.TabPages.Add(BuildCapsTab());
        tabs.TabPages.Add(BuildOutputTab());
        tabs.TabPages.Add(BuildTokensTab());
        tabs.TabPages.Add(BuildSecretsTab());
        tabs.TabPages.Add(BuildPresetsTab());

        var btnSave = new Button { Text = "Save", Left = 12, Top = 595, Width = 120 };
        btnSave.Click += (_, _) => OnSave();
        var btnReset = new Button { Text = "Reset defaults", Left = 142, Top = 595, Width = 140 };
        btnReset.Click += (_, _) => OnReset();
        var btnClose = new Button { Text = "Close", Left = 654, Top = 595, Width = 120 };
        btnClose.Click += (_, _) => Close();
        _status.SetBounds(292, 600, 350, 23);

        Controls.Add(tabs);
        Controls.Add(btnSave);
        Controls.Add(btnReset);
        Controls.Add(btnClose);
        Controls.Add(_status);

        WireDirtyTracking();
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

    private TabPage BuildFileTypesTab()
    {
        var tab = new TabPage("File types");
        _clbExt.SetBounds(12, 12, 350, 470);
        foreach (var e in KnownExt) _clbExt.Items.Add(e);

        var lblCustom = new Label { Text = "Manual file types (comma-separated). Example: .ps1, .iss", Left = 12, Top = 488, Width = 350 };
        _tbExtCustom.SetBounds(12, 508, 350, 23);

        var lblDot = new Label { Text = "Allowed dotfiles (comma-separated):", Left = 384, Top = 12, Width = 350 };
        _tbDot.SetBounds(384, 35, 350, 23);
        var lblDotHint = new Label
        {
            Text = "Dotfiles are printed even without a whitelisted extension (matched by full name).",
            Left = 384, Top = 64, Width = 350, Height = 40,
        };

        tab.Controls.Add(_clbExt);
        tab.Controls.Add(lblCustom);
        tab.Controls.Add(_tbExtCustom);
        tab.Controls.Add(lblDot);
        tab.Controls.Add(_tbDot);
        tab.Controls.Add(lblDotHint);
        return tab;
    }

    private TabPage BuildIgnoreTab()
    {
        var tab = new TabPage("Ignore & globs");
        _chkGitignore.SetBounds(12, 12, 350, 24);
        _chkDumpignore.SetBounds(12, 38, 350, 24);

        var lblExcl = new Label { Text = "Excluded folders:", Left = 12, Top = 70, Width = 350 };
        _clbExcl.SetBounds(12, 92, 350, 300);
        foreach (var x in KnownExcl) _clbExcl.Items.Add(x);
        var lblExclCustom = new Label { Text = "Additional folders (comma-separated). Example: cache, temp", Left = 12, Top = 396, Width = 350 };
        _tbExclCustom.SetBounds(12, 416, 350, 23);

        var lblInc = new Label { Text = "Include globs — one per line (blank = all files):", Left = 384, Top = 12, Width = 350 };
        _tbInclude.SetBounds(384, 35, 350, 190);
        var lblExc = new Label { Text = "Exclude globs — one per line (gitignore syntax):", Left = 384, Top = 235, Width = 350 };
        _tbExclude.SetBounds(384, 258, 350, 181);

        tab.Controls.Add(_chkGitignore);
        tab.Controls.Add(_chkDumpignore);
        tab.Controls.Add(lblExcl);
        tab.Controls.Add(_clbExcl);
        tab.Controls.Add(lblExclCustom);
        tab.Controls.Add(_tbExclCustom);
        tab.Controls.Add(lblInc);
        tab.Controls.Add(_tbInclude);
        tab.Controls.Add(lblExc);
        tab.Controls.Add(_tbExclude);
        return tab;
    }

    private TabPage BuildCapsTab()
    {
        var tab = new TabPage("Caps & binary");
        _chkDetectBinary.SetBounds(12, 16, 400, 24);

        var lblFile = new Label { Text = "Max per-file size (KB, 0 = unlimited):", Left = 12, Top = 56, Width = 280 };
        _numMaxFile.SetBounds(300, 54, 130, 23);
        var lblTotal = new Label { Text = "Max total size (KB, 0 = unlimited):", Left = 12, Top = 92, Width = 280 };
        _numMaxTotal.SetBounds(300, 90, 130, 23);

        var lblHint = new Label
        {
            Text = "Content beyond a cap is truncated with a [truncated] marker. Binary files (NUL-byte sniff) "
                 + "are listed but their content is skipped with a marker.",
            Left = 12, Top = 132, Width = 700, Height = 50,
        };

        tab.Controls.Add(_chkDetectBinary);
        tab.Controls.Add(lblFile);
        tab.Controls.Add(_numMaxFile);
        tab.Controls.Add(lblTotal);
        tab.Controls.Add(_numMaxTotal);
        tab.Controls.Add(lblHint);
        return tab;
    }

    private TabPage BuildOutputTab()
    {
        var tab = new TabPage("Output");
        var lblStyle = new Label { Text = "Output style:", Left = 12, Top = 18, Width = 90 };
        foreach (OutputStyle s in Enum.GetValues<OutputStyle>()) _cmbStyle.Items.Add(s);
        _cmbStyle.SetBounds(108, 15, 160, 23);

        var lblTarget = new Label { Text = "Output to:", Left = 300, Top = 18, Width = 70 };
        foreach (OutputTarget t in Enum.GetValues<OutputTarget>()) _cmbTarget.Items.Add(t);
        _cmbTarget.SetBounds(372, 15, 160, 23);

        var lblDir = new Label { Text = "Output folder:", Left = 12, Top = 56, Width = 90 };
        _tbOutDir.SetBounds(108, 53, 500, 23);
        var btnBrowse = new Button { Text = "Browse…", Left = 616, Top = 52, Width = 110 };
        btnBrowse.Click += (_, _) => OnBrowse();

        var lblHint = new Label
        {
            Text = "Folder applies to file output only (blank = Desktop). Clipboard / Console ignore it.",
            Left = 12, Top = 88, Width = 700,
        };

        tab.Controls.Add(lblStyle);
        tab.Controls.Add(_cmbStyle);
        tab.Controls.Add(lblTarget);
        tab.Controls.Add(_cmbTarget);
        tab.Controls.Add(lblDir);
        tab.Controls.Add(_tbOutDir);
        tab.Controls.Add(btnBrowse);
        tab.Controls.Add(lblHint);
        return tab;
    }

    private TabPage BuildTokensTab()
    {
        var tab = new TabPage("Tokens");
        var lblEnc = new Label { Text = "Token encoding:", Left = 12, Top = 18, Width = 110 };
        foreach (TokenEncoding e in Enum.GetValues<TokenEncoding>()) _cmbTokenEnc.Items.Add(e);
        _cmbTokenEnc.SetBounds(128, 15, 200, 23);
        var lblEncHint = new Label
        {
            Text = "o200k_base = GPT-4o / o-series / current era (recommended). cl100k_base = legacy gpt-4 / 3.5-turbo.",
            Left = 12, Top = 46, Width = 700, Height = 40,
        };

        var lblMax = new Label { Text = "Token budget (0 = none):", Left = 12, Top = 96, Width = 280 };
        _numMaxTokens.SetBounds(300, 94, 160, 23);
        var lblMaxHint = new Label
        {
            Text = "When > 0, the dump is flagged once its total exceeds this — warn-only, no content is dropped. "
                 + "Token counts appear in the non-Classic output styles; Classic output is unchanged.",
            Left = 12, Top = 124, Width = 700, Height = 60,
        };

        tab.Controls.Add(lblEnc);
        tab.Controls.Add(_cmbTokenEnc);
        tab.Controls.Add(lblEncHint);
        tab.Controls.Add(lblMax);
        tab.Controls.Add(_numMaxTokens);
        tab.Controls.Add(lblMaxHint);
        return tab;
    }

    private TabPage BuildSecretsTab()
    {
        var tab = new TabPage("Secrets");
        var lblMode = new Label { Text = "On secret detected:", Left = 12, Top = 18, Width = 120 };
        foreach (SecretScanMode m in Enum.GetValues<SecretScanMode>()) _cmbSecretScan.Items.Add(m);
        _cmbSecretScan.SetBounds(138, 15, 180, 23);
        var lblModeHint = new Label
        {
            Text = "Off = no scan. Warn = flag + count, content untouched. Redact = replace each secret with "
                 + "[REDACTED]. Skip = drop the whole file's content. Redact/Skip apply to the non-Classic "
                 + "styles only — Classic output stays byte-identical; findings still appear in the post-dump notice.",
            Left = 12, Top = 46, Width = 720, Height = 60,
        };

        _chkSecretEntropy.SetBounds(12, 116, 500, 24);

        var lblAllow = new Label { Text = "Allowlist — one regex per line (a finding whose match text matches is dropped):", Left = 12, Top = 150, Width = 720 };
        _tbSecretAllow.SetBounds(12, 173, 720, 170);

        tab.Controls.Add(lblMode);
        tab.Controls.Add(_cmbSecretScan);
        tab.Controls.Add(lblModeHint);
        tab.Controls.Add(_chkSecretEntropy);
        tab.Controls.Add(lblAllow);
        tab.Controls.Add(_tbSecretAllow);
        return tab;
    }

    private TabPage BuildPresetsTab()
    {
        var tab = new TabPage("Presets");
        var lbl = new Label
        {
            Text = "Pick a preset, then “Load into form” to preview and tweak it before saving.",
            Left = 12, Top = 16, Width = 700,
        };
        foreach (var p in Presets.All) _cmbPreset.Items.Add(p);
        _cmbPreset.SetBounds(12, 48, 220, 23);
        _cmbPreset.SelectedIndexChanged += (_, _) =>
            _lblPresetDesc.Text = (_cmbPreset.SelectedItem as Preset)?.Description ?? "";

        var btnLoad = new Button { Text = "Load into form", Left = 244, Top = 47, Width = 140 };
        btnLoad.Click += (_, _) => OnLoadPreset();

        _lblPresetDesc.SetBounds(12, 84, 720, 60);

        tab.Controls.Add(lbl);
        tab.Controls.Add(_cmbPreset);
        tab.Controls.Add(btnLoad);
        tab.Controls.Add(_lblPresetDesc);
        return tab;
    }

    private void LoadFromConfig(DumpConfig cfg)
    {
        _loading = true;
        try
        {
            var cur = new HashSet<string>(cfg.ExtSet.Select(e => e.ToLowerInvariant()));
            for (int i = 0; i < _clbExt.Items.Count; i++)
                _clbExt.SetItemChecked(i, cur.Contains(_clbExt.Items[i]!.ToString()!.ToLowerInvariant()));
            // Surface custom (non-listed) extensions so a save round-trips them instead of dropping them.
            _tbExtCustom.Text = string.Join(", ", cur.Where(e => !KnownExt.Contains(e)));

            for (int i = 0; i < _clbExcl.Items.Count; i++)
            {
                var tok = _clbExcl.Items[i]!.ToString()!;
                _clbExcl.SetItemChecked(i, cfg.ExcludeRegex.Contains(tok, StringComparison.Ordinal));
            }
            _tbExclCustom.Text = "";

            _tbDot.Text = string.Join(", ", cfg.DotFilesAllow);
            _chkGitignore.Checked = cfg.RespectGitignore;
            _chkDumpignore.Checked = cfg.UseDumpToTxtIgnore;
            _tbInclude.Text = string.Join("\r\n", cfg.IncludeGlobs);
            _tbExclude.Text = string.Join("\r\n", cfg.ExcludeGlobs);
            _chkDetectBinary.Checked = cfg.DetectBinary;
            _numMaxFile.Value = ClampKb(cfg.MaxFileSizeBytes);
            _numMaxTotal.Value = ClampKb(cfg.MaxTotalSizeBytes);
            _cmbStyle.SelectedItem = cfg.Style;
            _cmbTarget.SelectedItem = cfg.OutputTarget;
            _tbOutDir.Text = cfg.OutputDir ?? "";
            _cmbTokenEnc.SelectedItem = cfg.TokenEncoding;
            _numMaxTokens.Value = ClampTokens(cfg.MaxTokens);
            _cmbSecretScan.SelectedItem = cfg.SecretScan;
            _chkSecretEntropy.Checked = cfg.SecretScanEntropy;
            _tbSecretAllow.Text = string.Join("\r\n", cfg.SecretAllowlist);

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
        TokenEncoding = _cmbTokenEnc.SelectedItem is TokenEncoding te ? te : TokenEncoding.O200kBase,
        // Preserve the exact loaded budget unless the user touched the spinner, so a value above the
        // spinner's display ceiling isn't silently clamped on an untouched save (mirrors the byte caps).
        MaxTokens = _maxTokensDirty ? (long)_numMaxTokens.Value : _loadedMaxTokens,
        SecretScan = _cmbSecretScan.SelectedItem is SecretScanMode sm ? sm : SecretScanMode.Warn,
        SecretScanEntropy = _chkSecretEntropy.Checked,
        SecretAllowlist = SplitLines(_tbSecretAllow.Text),
    };

    private List<string> BuildExtSet()
    {
        var picked = _clbExt.CheckedItems.Cast<object>().Select(o => o.ToString()!.ToLowerInvariant());
        var custom = _tbExtCustom.Text.Split(',')
            .Select(s => s.Trim().ToLowerInvariant())
            .Where(s => s.StartsWith('.') && s.Length > 1);
        return picked.Concat(custom).Distinct().ToList();
    }

    private string BuildExcludeRegex()
    {
        var alts = _clbExcl.CheckedItems.Cast<object>().Select(o => o.ToString()!).ToList();
        alts.AddRange(_tbExclCustom.Text.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
        alts = alts.Where(s => s.Length > 0).Distinct().ToList();
        if (alts.Count == 0) return DumpConfig.DefaultExcludeRegex;
        return @"\\(" + string.Join("|", alts) + @")(\\|$)";
    }

    private static List<string> SplitCsv(string s) =>
        s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

    private static List<string> SplitLines(string s) =>
        s.Split('\r', '\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

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
        using var dlg = new FolderBrowserDialog { Description = "Choose the output folder for dumps" };
        if (!string.IsNullOrWhiteSpace(_tbOutDir.Text) && Directory.Exists(_tbOutDir.Text))
            dlg.SelectedPath = _tbOutDir.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _tbOutDir.Text = dlg.SelectedPath;
    }

    private void OnLoadPreset()
    {
        if (_cmbPreset.SelectedItem is not Preset p) { _status.Text = "Pick a preset first."; return; }
        var cfg = BuildConfigFromForm();
        p.Apply(cfg);
        LoadFromConfig(cfg);
        _status.Text = $"Loaded preset ‘{p.Name}’ (not saved yet).";
    }

    private void OnSave()
    {
        var cfg = BuildConfigFromForm();
        if (cfg.ExtSet.Count == 0) { _status.Text = "❌ Pick at least 1 extension."; return; }
        try { _ = new Regex(cfg.ExcludeRegex); }
        catch { _status.Text = "❌ Exclude regex invalid."; return; }

        ConfigStore.Save(cfg);
        ContextMenu.TrySyncLabels(cfg);
        _status.Text = "✅ Saved.";
    }

    private void OnReset()
    {
        LoadFromConfig(DumpConfig.CreateDefault());
        // Legacy: excludes reset to "none checked" -> on save yields the default exclude regex.
        for (int i = 0; i < _clbExcl.Items.Count; i++) _clbExcl.SetItemChecked(i, false);
        _tbExtCustom.Text = "";
        _tbExclCustom.Text = "";
        _status.Text = "Defaults loaded (not saved yet).";
    }
}
