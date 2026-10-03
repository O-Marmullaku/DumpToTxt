using System.Drawing;
using System.Runtime.InteropServices;
using DumpToTxt.Core;

namespace DumpToTxt.App;

internal static class UiTheme
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter,
        [MarshalAs(UnmanagedType.Bool)] out bool enabled, uint flags);

    public static ProgressBarStyle ActivityStyle =>
        SystemParametersInfo(0x1042, 0, out bool enabled, 0) && enabled
            ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;

    public static int Px(Control control, int logicalPixels) =>
        (int)Math.Round(logicalPixels * control.DeviceDpi / 96d);

    private sealed record ThemePalette(
        Color Window,
        Color Surface,
        Color Rail,
        Color Border,
        Color ControlHover,
        Color ControlPressed,
        Color Text,
        Color Muted,
        Color Action,
        Color ActionHover,
        Color Accent,
        Color AccentSoft,
        Color Error,
        Color Warning,
        Color DisabledText,
        Color DisabledFill);

    private static readonly ThemePalette Graphite = new(
        Color.FromArgb(247, 247, 247), Color.White,
        Color.FromArgb(240, 240, 240), Color.FromArgb(208, 208, 208),
        Color.FromArgb(242, 242, 242), Color.FromArgb(232, 232, 232),
        Color.FromArgb(32, 32, 32), Color.FromArgb(90, 90, 90),
        Color.FromArgb(45, 45, 45), Color.FromArgb(31, 31, 31),
        Color.FromArgb(25, 103, 210), Color.FromArgb(225, 236, 252),
        Color.FromArgb(176, 45, 45), Color.FromArgb(145, 91, 0),
        Color.FromArgb(118, 118, 118), Color.FromArgb(185, 185, 185));

    private static readonly ThemePalette Blue = new(
        Color.FromArgb(247, 249, 252), Color.White,
        Color.FromArgb(238, 243, 250), Color.FromArgb(200, 212, 227),
        Color.FromArgb(236, 243, 252), Color.FromArgb(220, 232, 247),
        Color.FromArgb(32, 37, 43), Color.FromArgb(86, 100, 115),
        Color.FromArgb(11, 87, 208), Color.FromArgb(8, 75, 178),
        Color.FromArgb(25, 103, 210), Color.FromArgb(225, 236, 252),
        Color.FromArgb(176, 45, 45), Color.FromArgb(145, 91, 0),
        Color.FromArgb(108, 119, 132), Color.FromArgb(174, 190, 210));

    private static ThemePalette _current = Graphite;

    public static UiThemeKind CurrentKind { get; private set; } = UiThemeKind.Graphite;
    public static Color Window => _current.Window;
    public static Color Surface => _current.Surface;
    public static Color Rail => _current.Rail;
    public static Color Border => _current.Border;
    public static Color ControlHover => _current.ControlHover;
    public static Color ControlPressed => _current.ControlPressed;
    public static Color Text => _current.Text;
    public static Color Muted => _current.Muted;
    public static Color Action => _current.Action;
    public static Color ActionHover => _current.ActionHover;
    public static Color Accent => _current.Accent;
    public static Color AccentSoft => _current.AccentSoft;
    public static Color Error => _current.Error;
    public static Color Warning => _current.Warning;
    public static Color DisabledText => _current.DisabledText;
    public static Color DisabledFill => _current.DisabledFill;

    public static void Apply(UiThemeKind kind, Control? root = null)
    {
        ThemePalette previous = _current;
        CurrentKind = kind;
        _current = kind == UiThemeKind.Blue ? Blue : Graphite;
        if (root is not null) Refresh(root, previous);
    }

    private static void Refresh(Control control, ThemePalette previous)
    {
        control.BackColor = Remap(control.BackColor, previous);
        control.ForeColor = Remap(control.ForeColor, previous);
        if (control is Button button)
        {
            button.FlatAppearance.BorderColor = Remap(button.FlatAppearance.BorderColor, previous);
            button.FlatAppearance.MouseOverBackColor = Remap(button.FlatAppearance.MouseOverBackColor, previous);
            button.FlatAppearance.MouseDownBackColor = Remap(button.FlatAppearance.MouseDownBackColor, previous);
        }
        foreach (Control child in control.Controls) Refresh(child, previous);
        control.Invalidate();
    }

    private static Color Remap(Color color, ThemePalette previous)
    {
        if (color.ToArgb() == previous.Window.ToArgb()) return Window;
        if (color.ToArgb() == previous.Surface.ToArgb()) return Surface;
        if (color.ToArgb() == previous.Rail.ToArgb()) return Rail;
        if (color.ToArgb() == previous.Border.ToArgb()) return Border;
        if (color.ToArgb() == previous.ControlHover.ToArgb()) return ControlHover;
        if (color.ToArgb() == previous.ControlPressed.ToArgb()) return ControlPressed;
        if (color.ToArgb() == previous.Text.ToArgb()) return Text;
        if (color.ToArgb() == previous.Muted.ToArgb()) return Muted;
        if (color.ToArgb() == previous.Action.ToArgb()) return Action;
        if (color.ToArgb() == previous.ActionHover.ToArgb()) return ActionHover;
        if (color.ToArgb() == previous.Accent.ToArgb()) return Accent;
        if (color.ToArgb() == previous.AccentSoft.ToArgb()) return AccentSoft;
        if (color.ToArgb() == previous.Error.ToArgb()) return Error;
        if (color.ToArgb() == previous.Warning.ToArgb()) return Warning;
        if (color.ToArgb() == previous.DisabledText.ToArgb()) return DisabledText;
        if (color.ToArgb() == previous.DisabledFill.ToArgb()) return DisabledFill;
        return color;
    }

    public static Font UiFont(float size = 9f, FontStyle style = FontStyle.Regular) =>
        new("Segoe UI", size, style, GraphicsUnit.Point);

    public static void StylePrimary(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = Action;
        button.ForeColor = Color.White;
        button.Font = UiFont(9f, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.MouseEnter += (_, _) => { if (button.Enabled) button.BackColor = ActionHover; };
        button.MouseLeave += (_, _) => { if (button.Enabled) button.BackColor = Action; };
        button.EnabledChanged += (_, _) =>
        {
            button.BackColor = button.Enabled ? Action : DisabledFill;
        };
    }

    public static void StyleSecondary(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = ControlHover;
        button.FlatAppearance.MouseDownBackColor = ControlPressed;
        button.BackColor = Surface;
        button.ForeColor = Text;
        button.Cursor = Cursors.Hand;
    }

    public static void StyleLinkButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = ControlPressed;
        button.FlatAppearance.MouseDownBackColor = Border;
        button.BackColor = Rail;
        button.ForeColor = Text;
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Padding = new Padding(0);
        button.Cursor = Cursors.Hand;
    }

    public static void StyleChoiceCombo(ComboBox combo)
    {
        combo.DrawMode = DrawMode.OwnerDrawFixed;
        combo.ItemHeight = 26;
        combo.FlatStyle = FlatStyle.Standard;
        combo.BackColor = Surface;
        combo.ForeColor = Text;
        combo.Font = UiFont(10f);
        combo.FormattingEnabled = true;
        combo.DrawItem += (_, e) =>
        {
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using var background = new SolidBrush(selected ? AccentSoft : combo.BackColor);
            e.Graphics.FillRectangle(background, e.Bounds);
            object? item = e.Index >= 0 ? combo.Items[e.Index] : combo.SelectedItem;
            string text = item is null ? combo.Text : combo.GetItemText(item) ?? string.Empty;
            Color foreground = (e.State & DrawItemState.Disabled) != 0 ? DisabledText : Text;
            TextRenderer.DrawText(e.Graphics, text, combo.Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 12), e.Bounds.Height),
                foreground, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
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
