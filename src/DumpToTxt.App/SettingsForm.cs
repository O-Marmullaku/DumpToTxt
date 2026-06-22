using System.Drawing;
using System.Text.RegularExpressions;
using DumpToTxt.Core;

namespace DumpToTxt.App;

/// <summary>
/// Settings GUI, ported from the legacy PowerShell Show-SettingsGui. Lets the user pick
/// which file types are printed in full, which folders are excluded, and which dotfiles
/// are allowed. Saves to the user config (%APPDATA%\DumpToTxt\settings.json).
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

    private readonly CheckedListBox _clbExt = new() { CheckOnClick = true };
    private readonly TextBox _tbExtCustom = new();
    private readonly CheckedListBox _clbExcl = new() { CheckOnClick = true };
    private readonly TextBox _tbExclCustom = new();
    private readonly TextBox _tbDot = new();
    private readonly Label _status = new();

    public SettingsForm()
    {
        Text = "DumpToTxt Settings";
        Width = 860;
        Height = 600;
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* no icon */ }

        BuildUi();
        LoadFromConfig(ConfigStore.Load());
    }

    private void BuildUi()
    {
        var gExt = new GroupBox { Text = "Legible file types", Left = 12, Top = 12, Width = 400, Height = 410 };
        _clbExt.SetBounds(12, 22, 370, 300);
        foreach (var e in KnownExt) _clbExt.Items.Add(e);
        var lblExt = new Label
        {
            Text = "Enter manual file types (comma-separated). Example: .ps1, .iss",
            Left = 12, Top = 332, Width = 370,
        };
        _tbExtCustom.SetBounds(12, 352, 370, 23);
        gExt.Controls.Add(_clbExt);
        gExt.Controls.Add(lblExt);
        gExt.Controls.Add(_tbExtCustom);

        var gExcl = new GroupBox { Text = "Excluded folders", Left = 430, Top = 12, Width = 400, Height = 410 };
        _clbExcl.SetBounds(12, 22, 370, 300);
        foreach (var x in KnownExcl) _clbExcl.Items.Add(x);
        var lblExcl = new Label
        {
            Text = "Additional folders to skip (comma-separated). Example: cache, temp",
            Left = 12, Top = 332, Width = 370,
        };
        _tbExclCustom.SetBounds(12, 352, 370, 23);
        gExcl.Controls.Add(_clbExcl);
        gExcl.Controls.Add(lblExcl);
        gExcl.Controls.Add(_tbExclCustom);

        var lblDot = new Label { Text = "Allowed dotfiles (comma-separated):", Left = 12, Top = 430, Width = 500 };
        _tbDot.SetBounds(12, 452, 818, 23);

        var btnSave = new Button { Text = "Save", Left = 12, Top = 486, Width = 120 };
        btnSave.Click += (_, _) => OnSave();
        var btnReset = new Button { Text = "Reset defaults", Left = 142, Top = 486, Width = 140 };
        btnReset.Click += (_, _) => OnReset();
        var btnClose = new Button { Text = "Close", Left = 710, Top = 486, Width = 120 };
        btnClose.Click += (_, _) => Close();

        _status.SetBounds(300, 492, 380, 23);

        Controls.Add(gExt);
        Controls.Add(gExcl);
        Controls.Add(lblDot);
        Controls.Add(_tbDot);
        Controls.Add(btnSave);
        Controls.Add(btnReset);
        Controls.Add(btnClose);
        Controls.Add(_status);
    }

    private void LoadFromConfig(DumpConfig cfg)
    {
        var cur = new HashSet<string>(cfg.ExtSet.Select(e => e.ToLowerInvariant()));
        for (int i = 0; i < _clbExt.Items.Count; i++)
            _clbExt.SetItemChecked(i, cur.Contains(_clbExt.Items[i]!.ToString()!.ToLowerInvariant()));

        for (int i = 0; i < _clbExcl.Items.Count; i++)
        {
            var tok = _clbExcl.Items[i]!.ToString()!;
            _clbExcl.SetItemChecked(i, cfg.ExcludeRegex.Contains(tok, StringComparison.Ordinal));
        }

        _tbDot.Text = string.Join(", ", cfg.DotFilesAllow);
    }

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

    private void OnSave()
    {
        var ext = BuildExtSet();
        if (ext.Count == 0) { _status.Text = "❌ Pick at least 1 extension."; return; }

        var rx = BuildExcludeRegex();
        try { _ = new Regex(rx); }
        catch { _status.Text = "❌ Exclude regex invalid."; return; }

        var dot = _tbDot.Text.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        ConfigStore.Save(new DumpConfig
        {
            ExtSet = ext,
            DotFilesAllow = dot,
            ExcludeRegex = rx,
            Style = OutputStyle.Classic,
        });
        _status.Text = "✅ Saved.";
    }

    private void OnReset()
    {
        var def = DumpConfig.CreateDefault();
        for (int i = 0; i < _clbExt.Items.Count; i++) _clbExt.SetItemChecked(i, false);
        foreach (var e in def.ExtSet)
        {
            int idx = _clbExt.Items.IndexOf(e);
            if (idx >= 0) _clbExt.SetItemChecked(idx, true);
        }
        // Legacy resets excludes to "none checked"; on save that yields the default exclude regex.
        for (int i = 0; i < _clbExcl.Items.Count; i++) _clbExcl.SetItemChecked(i, false);
        _tbExtCustom.Text = "";
        _tbExclCustom.Text = "";
        _tbDot.Text = string.Join(", ", def.DotFilesAllow);
        _status.Text = "Defaults loaded (not saved yet).";
    }
}
