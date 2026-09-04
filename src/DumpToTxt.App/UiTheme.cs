using System.Drawing;

namespace DumpToTxt.App;

internal static class UiTheme
{
    public static readonly Color Window = Color.FromArgb(246, 248, 251);
    public static readonly Color Surface = Color.White;
    public static readonly Color Rail = Color.FromArgb(239, 243, 248);
    public static readonly Color Border = Color.FromArgb(202, 210, 222);
    public static readonly Color Text = Color.FromArgb(28, 36, 48);
    public static readonly Color Muted = Color.FromArgb(78, 91, 108);
    public static readonly Color Accent = Color.FromArgb(25, 103, 210);
    public static readonly Color AccentHover = Color.FromArgb(18, 84, 176);
    public static readonly Color AccentSoft = Color.FromArgb(225, 236, 252);
    public static readonly Color Error = Color.FromArgb(176, 45, 45);
    public static readonly Color Warning = Color.FromArgb(145, 91, 0);
    public static readonly Color DisabledText = Color.FromArgb(121, 131, 145);

    public static Font UiFont(float size = 9f, FontStyle style = FontStyle.Regular) =>
        new("Segoe UI", size, style, GraphicsUnit.Point);

    public static void StylePrimary(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = Accent;
        button.ForeColor = Color.White;
        button.Font = UiFont(9f, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.MouseEnter += (_, _) => { if (button.Enabled) button.BackColor = AccentHover; };
        button.MouseLeave += (_, _) => { if (button.Enabled) button.BackColor = Accent; };
        button.EnabledChanged += (_, _) =>
        {
            button.BackColor = button.Enabled ? Accent : Color.FromArgb(185, 194, 206);
        };
    }

    public static void StyleSecondary(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = Surface;
        button.ForeColor = Text;
        button.Cursor = Cursors.Hand;
    }

    public static void StyleLinkButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = Rail;
        button.ForeColor = Accent;
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Padding = new Padding(0);
        button.Cursor = Cursors.Hand;
    }

    public static void StyleChoiceCombo(ComboBox combo)
    {
        combo.DrawMode = DrawMode.OwnerDrawFixed;
        combo.ItemHeight = 26;
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = Surface;
        combo.ForeColor = Text;
        combo.Font = UiFont(10f);
        combo.FormattingEnabled = true;
        combo.DrawItem += (_, e) =>
        {
            e.DrawBackground();
            object? item = e.Index >= 0 ? combo.Items[e.Index] : combo.SelectedItem;
            string text = item is null ? combo.Text : combo.GetItemText(item) ?? string.Empty;
            Color foreground = (e.State & DrawItemState.Disabled) != 0 ? DisabledText : Text;
            TextRenderer.DrawText(e.Graphics, text, combo.Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 12), e.Bounds.Height),
                foreground, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        };
    }

    public static Label SectionLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = UiFont(8f, FontStyle.Bold),
        ForeColor = Muted,
        Margin = new Padding(0, 14, 0, 6),
    };
}
