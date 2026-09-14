using DumpToTxt.Core;

namespace DumpToTxt.Tests;

public sealed class CancellationBoundaryTests
{
    [Fact]
    public void CanceledSnapshotDoesNotContinuePreparingPreview()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new DumpPreviewScanState().Snapshot(cancellation.Token));
    }

    [Fact]
    public void CanceledClassificationDoesNotTouchAMissingSource()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        string missing = Path.Combine(Path.GetTempPath(), "dtt-cancelled-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<OperationCanceledException>(() => TextFileClassifier.IsTextLike(missing, cancellation.Token));
    }
}
