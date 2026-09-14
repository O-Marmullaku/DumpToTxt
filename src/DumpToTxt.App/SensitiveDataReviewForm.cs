using System.Runtime.InteropServices;
using DumpToTxt.Core;

namespace DumpToTxt.App;

/// <summary>One shared pre-output confirmation for automatically detected sensitive data.</summary>
public sealed class SensitiveDataReviewForm : Form
{
    private const int VisibleFindingLimit = 10;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    public SensitiveDataReviewForm(SensitiveDataReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        Text = "DumpToTxt";
        ShowIcon = false;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 560);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = UiTheme.UiFont();
        BackColor = UiTheme.Window;
        ForeColor = UiTheme.Text;

        BuildUi(review);
    }

    public SensitiveDataDecision Decision { get; private set; } = SensitiveDataDecision.Cancel;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int caption = ColorTranslator.ToWin32(UiTheme.Window);
        int captionText = ColorTranslator.ToWin32(UiTheme.Text);
        DwmSetWindowAttribute(Handle, DwmwaCaptionColor, ref caption, sizeof(int));
        DwmSetWindowAttribute(Handle, DwmwaTextColor, ref captionText, sizeof(int));
    }

    private void BuildUi(SensitiveDataReview review)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            BackColor = UiTheme.Window,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.Controls.Add(BuildHeader(review), 0, 0);
        root.Controls.Add(BuildFindings(review), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);
        Controls.Add(root);
    }

    private static Control BuildHeader(SensitiveDataReview review)
    {
        var header = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Window };
        header.Controls.Add(new PictureBox
        {
            Image = SystemIcons.Warning.ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(24, 24),
            Size = new Size(32, 32),
            AccessibleName = "Warning",
        });
        header.Controls.Add(new Label
        {
            Text = "Sensitive data found",
            AutoSize = true,
            Location = new Point(72, 20),
            Font = UiTheme.UiFont(13f, FontStyle.Bold),
            ForeColor = UiTheme.Text,
        });
        header.Controls.Add(new Label
        {
            Text = "This dump may contain sensitive information. Review it before continuing.",
            AutoSize = false,
            Location = new Point(72, 49),
            Size = new Size(520, 22),
            ForeColor = UiTheme.Muted,
        });
        header.Controls.Add(new Label
        {
            Text = $"{review.Items.Count} finding(s) across {review.FileCount} file(s)",
            AutoSize = false,
            Location = new Point(72, 72),
            Size = new Size(520, 20),
            ForeColor = UiTheme.Muted,
        });
        return header;
    }

    private static Control BuildFindings(SensitiveDataReview review)
    {
        var frame = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 0, 24, 0),
            BackColor = UiTheme.Window,
        };
        var surface = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = UiTheme.Surface,
            Padding = new Padding(16, 12, 16, 12),
            Margin = new Padding(0),
        };
        surface.Paint += (_, e) =>
        {
            using var border = new Pen(UiTheme.Border);
            e.Graphics.DrawRectangle(border, 0, 0, surface.ClientSize.Width - 1, surface.ClientSize.Height - 1);
        };

        var visible = VisibleItems(review.Items).ToList();
        foreach (var group in visible.GroupBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            surface.Controls.Add(new Label
            {
                Text = group.Key,
                AutoSize = false,
                Width = 530,
                Height = 27,
                Margin = new Padding(0, 3, 0, 2),
                Font = UiTheme.UiFont(9f, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                AutoEllipsis = true,
            });
            foreach (var finding in group)
            {
                string text = $"{finding.RuleName}, line {finding.Line}: {finding.Preview}";
                surface.Controls.Add(new Label
                {
                    Text = text,
                    AccessibleName = text,
                    AutoSize = false,
                    Width = 530,
                    Height = 26,
                    Margin = new Padding(0),
                    Padding = new Padding(12, 3, 0, 0),
                    ForeColor = UiTheme.Text,
                    AutoEllipsis = true,
                });
            }
        }

        int remaining = review.Items.Count - visible.Count;
        if (remaining > 0)
            surface.Controls.Add(new Label
            {
                Text = $"…and {remaining} more",
                AutoSize = false,
                Width = 530,
                Height = 30,
                Margin = new Padding(0, 7, 0, 0),
                Padding = new Padding(0, 3, 0, 0),
                Font = UiTheme.UiFont(9f, FontStyle.Bold),
                ForeColor = UiTheme.Muted,
            });

        frame.Controls.Add(surface);
        return frame;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            Padding = new Padding(24, 15, 24, 15),
            BackColor = UiTheme.Window,
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Paint += (_, e) =>
        {
            using var divider = new Pen(UiTheme.Border);
            e.Graphics.DrawLine(divider, 24, 0, footer.ClientSize.Width - 24, 0);
        };

        var cancel = new Button { Text = "Cancel", Width = 82, Height = 34, DialogResult = DialogResult.Cancel };
        var keep = new Button { Text = "Keep anyway", Width = 110, Height = 34 };
        var hide = new Button { Text = "Hide and continue", Width = 142, Height = 34 };
        UiTheme.StyleSecondary(cancel);
        UiTheme.StyleSecondary(keep);
        UiTheme.StylePrimary(hide);
        keep.Click += (_, _) => Complete(SensitiveDataDecision.Keep);
        hide.Click += (_, _) => Complete(SensitiveDataDecision.Redact);
        footer.Controls.Add(cancel, 1, 0);
        footer.Controls.Add(keep, 2, 0);
        footer.Controls.Add(hide, 3, 0);
        AcceptButton = hide;
        CancelButton = cancel;
        return footer;
    }

    private void Complete(SensitiveDataDecision decision)
    {
        Decision = decision;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static IEnumerable<SensitiveDataReviewItem> VisibleItems(
        IReadOnlyList<SensitiveDataReviewItem> items)
    {
        var groups = items.GroupBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        if (groups.Count == 0) yield break;
        int perFile = Math.Max(1, VisibleFindingLimit / groups.Count);
        var selected = groups.SelectMany(group => group.Take(perFile)).Take(VisibleFindingLimit).ToList();
        if (selected.Count < VisibleFindingLimit)
        {
            var alreadySelected = new HashSet<SensitiveDataReviewItem>(selected);
            selected.AddRange(items.Where(item => !alreadySelected.Contains(item))
                .Take(VisibleFindingLimit - selected.Count));
        }
        foreach (var item in selected.OrderBy(item =>
            groups.FindIndex(group => string.Equals(group.Key, item.RelativePath, StringComparison.OrdinalIgnoreCase))))
            yield return item;
    }
}
