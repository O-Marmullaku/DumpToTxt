using DumpToTxt.Core;

namespace DumpToTxt.App;

/// <summary>Keeps the native message loop available throughout authoritative generation.</summary>
public sealed class ExportProgressForm : Form
{
    private readonly string _target;
    private readonly DumpConfig _config;
    private readonly DumpContentSelection _selection;
    private readonly TextWriter? _textOutput;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Preparing dump…" };
    private readonly Button _cancel = new() { Text = "Cancel", Width = 100, Height = 34 };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 100 };
    private readonly LatestProgress _progress = new();
    private bool _finished;

    public DumpResult? Result { get; private set; }
    public Exception? Failure { get; private set; }

    public ExportProgressForm(string target, DumpConfig config, DumpContentSelection selection, TextWriter? textOutput = null)
    {
        _target = target;
        SuspendLayout();
        _config = config.Clone();
        _selection = selection;
        _textOutput = textOutput;
        Text = "Creating dump — DumpToTxt";
        ClientSize = new Size(560, 180);
        MinimumSize = new Size(440, 215);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Font = UiTheme.UiFont();
        BackColor = UiTheme.Window;
        ForeColor = UiTheme.Text;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.Controls.Add(_status, 0, 0);
        layout.Controls.Add(new ProgressBar { Dock = DockStyle.Fill, Style = UiTheme.ActivityStyle, AccessibleName = "Export activity" }, 0, 1);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        UiTheme.StyleSecondary(_cancel);
        footer.Controls.Add(_cancel);
        layout.Controls.Add(footer, 0, 2);
        Controls.Add(layout);
        CancelButton = _cancel;
        _cancel.Click += (_, _) => RequestCancellation();
        _refresh.Tick += (_, _) =>
        {
            if (_cancellation.IsCancellationRequested) return;
            var update = _progress.Take();
            if (update is not null)
                _status.Text = $"{update.Phase}\r\n{update.FilesProcessed:N0} file(s) · {update.BytesProcessed:N0} bytes\r\n{update.Path}";
        };
        Shown += RunExport;
        ResumeLayout(true);
    }

    private async void RunExport(object? sender, EventArgs e)
    {
        _refresh.Start();
        try
        {
            Result = await Task.Run(() => new DumpEngine().Run(_target, _config,
                contentSelection: _selection, reviewSensitiveData: ReviewSensitiveData,
                cancellationToken: _cancellation.Token, progress: _progress, textOutput: _textOutput));
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            Result = new DumpResult { Cancelled = true };
        }
        catch (Exception ex) { Failure = ex; }
        finally
        {
            _finished = true;
            _refresh.Stop();
            if (!IsDisposed) Close();
        }
    }

    private SensitiveDataDecision ReviewSensitiveData(SensitiveDataReview review)
    {
        if (_cancellation.IsCancellationRequested || IsDisposed) return SensitiveDataDecision.Cancel;
        try
        {
            return (SensitiveDataDecision)Invoke(new Func<SensitiveDataDecision>(() =>
            {
                if (_cancellation.IsCancellationRequested) return SensitiveDataDecision.Cancel;
                using var dialog = new SensitiveDataReviewForm(review);
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Decision : SensitiveDataDecision.Cancel;
            }));
        }
        catch (InvalidOperationException) when (_cancellation.IsCancellationRequested || IsDisposed)
        {
            return SensitiveDataDecision.Cancel;
        }
    }

    private void RequestCancellation()
    {
        if (_finished || _cancellation.IsCancellationRequested) return;
        _cancellation.Cancel();
        _cancel.Enabled = false;
        _status.Text = "Cancelling… Waiting for the current operation to stop.";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_finished) { e.Cancel = true; RequestCancellation(); }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _refresh.Dispose(); _cancellation.Dispose(); }
        base.Dispose(disposing);
    }

    // Progress replaces one slot; a busy worker cannot enqueue stale UI callbacks.
    private sealed class LatestProgress : IProgress<DumpProgress>
    {
        private DumpProgress? _latest;
        public void Report(DumpProgress value) => Interlocked.Exchange(ref _latest, value);
        public DumpProgress? Take() => Interlocked.Exchange(ref _latest, null);
    }
}
