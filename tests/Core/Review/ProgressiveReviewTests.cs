using DumpToTxt.Core;

namespace DumpToTxt.Tests;

public sealed class ProgressiveReviewTests
{
    private static DumpPreviewEntry Entry(string path, long size = 1, bool basic = true) =>
        new(path, path, DumpFileTypeKey.FromFileName(path), size, size / 4, basic);

    [Fact]
    public void LazySelection_PreservesNestedOverridesAndFutureDiscoveries()
    {
        var tree = new DumpReviewTree(DumpSelectionMode.Basic);
        tree.AddText(Entry("src/a.cs", 10));
        tree.AddText(Entry("src/nested/b.txt", 20, false));
        tree.AddText(Entry("else/c.cs", 30));
        var source = tree.Find("src")!;
        Assert.Equal(40, tree.SelectedBytes);
        Assert.Equal(DumpNodeSelectionState.Mixed, tree.State(source));
        tree.Select(source, DumpSelectionMode.None);
        tree.Select(tree.Find("src/nested")!, DumpSelectionMode.Thorough);
        tree.AddText(Entry("src/nested/new.txt", 50, false));
        tree.AddText(Entry("src/later.cs", 60));
        Assert.Equal(100, tree.SelectedBytes);
        Assert.Equal(DumpNodeSelectionState.Excluded, tree.State(tree.Find("src/later.cs")!));
        tree.Select(source, DumpSelectionMode.Thorough);
        Assert.Equal(170, tree.SelectedBytes);
        Assert.Equal(DumpNodeSelectionState.Included, tree.State(tree.Find("src/later.cs")!));
        tree.Select(tree.Root, DumpSelectionMode.Basic);
        Assert.Equal(100, tree.SelectedBytes);
        Assert.Equal(DumpNodeSelectionState.Excluded, tree.State(tree.Find("src/nested/b.txt")!));
    }

    [Fact]
    public void WideInventory_IsCompleteAndPagesStaySizeSorted()
    {
        var tree = new DumpReviewTree(DumpSelectionMode.Thorough);
        for (int i = 0; i < 10_000; i++) tree.AddText(Entry($"wide/{i:D5}.txt", i));
        tree.AddPath(new("empty", "empty", true));
        tree.AddPath(new("binary", "binary", false));
        var wide = tree.Find("wide")!;
        Assert.Equal(10_000, wide.ChildCount);
        Assert.Equal(10_003, tree.PathCount);
        Assert.Equal(Enumerable.Range(9800, 200).Reverse().Select(i => $"wide/{i:D5}.txt"),
            wide.Page(0, 200).Select(node => node.RelativePath));
        tree.Select(wide, DumpSelectionMode.None);
        Assert.Equal(0, tree.SelectedBytes);
        Assert.Equal(10_000, wide.ChildCount);
        Assert.Equal(DumpNodeSelectionState.Excluded, tree.State(wide.Page(9000, 1).Single()));
    }

    [Fact]
    public void ReviewSorting_NameAndContributionAreDirectionalAndDeterministic()
    {
        var tree = new DumpReviewTree(DumpSelectionMode.Thorough);
        tree.AddText(Entry("zeta.txt", 10));
        tree.AddText(Entry("Alpha.txt", 20));
        tree.AddText(Entry("beta.txt", 20));
        tree.AddText(Entry("gamma.txt", 5));

        Assert.Equal(new[] { "Alpha.txt", "beta.txt", "gamma.txt", "zeta.txt" },
            tree.Root.Page(0, 10, DumpReviewSort.NameAscending).Select(node => node.RelativePath));
        Assert.Equal(new[] { "zeta.txt", "gamma.txt", "beta.txt", "Alpha.txt" },
            tree.Root.Page(0, 10, DumpReviewSort.NameDescending).Select(node => node.RelativePath));
        Assert.Equal(new[] { "Alpha.txt", "beta.txt", "zeta.txt", "gamma.txt" },
            tree.Root.Page(0, 10, DumpReviewSort.ContributionDescending).Select(node => node.RelativePath));
        Assert.Equal(new[] { "gamma.txt", "zeta.txt", "Alpha.txt", "beta.txt" },
            tree.Root.Page(0, 10, DumpReviewSort.ContributionAscending).Select(node => node.RelativePath));
    }

    [Fact]
    public void ReviewSorting_DiscoveryOrderDoesNotMoveWhenContributionChanges()
    {
        var tree = new DumpReviewTree(DumpSelectionMode.Thorough);
        tree.AddPath(new("", "folder/b.txt", false));
        tree.AddPath(new("", "folder/a.txt", false));
        var folder = tree.Find("folder")!;
        Assert.Equal(new[] { "folder/b.txt", "folder/a.txt" },
            folder.Page(0, 10, DumpReviewSort.DiscoveryAscending).Select(node => node.RelativePath));

        tree.AddText(Entry("folder/a.txt", 500));
        tree.AddText(Entry("folder/b.txt", 1));

        Assert.Equal(new[] { "folder/b.txt", "folder/a.txt" },
            folder.Page(0, 10, DumpReviewSort.DiscoveryAscending).Select(node => node.RelativePath));
        Assert.Equal(new[] { "folder/a.txt", "folder/b.txt" },
            folder.Page(0, 10, DumpReviewSort.ContributionDescending).Select(node => node.RelativePath));
    }

    [Fact]
    public void LargeDirectorySelection_RemainsLazyAndFutureChildrenInheritTheParentChoice()
    {
        var tree = new DumpReviewTree(DumpSelectionMode.Thorough);
        for (int i = 0; i < 20_000; i++) tree.AddText(Entry($"bulk/{i:D5}.txt", i + 1));
        var bulk = tree.Find("bulk")!;

        tree.Select(bulk, DumpSelectionMode.None);
        Assert.Equal(0, tree.SelectedBytes);
        Assert.Equal(DumpNodeSelectionState.Excluded, tree.State(tree.Find("bulk/00000.txt")!));
        Assert.Equal(DumpNodeSelectionState.Excluded, tree.State(tree.Find("bulk/19999.txt")!));

        tree.AddText(Entry("bulk/later.txt", 25));
        Assert.Equal(DumpNodeSelectionState.Excluded, tree.State(tree.Find("bulk/later.txt")!));
        tree.Select(tree.Find("bulk/later.txt")!, DumpSelectionMode.Thorough);
        Assert.Equal(DumpNodeSelectionState.Mixed, tree.State(bulk));
    }

    [Fact]
    public void RandomizedLazyAggregates_MatchFlatNearestAncestorSelection()
    {
        var tree = new DumpReviewTree(DumpSelectionMode.Basic);
        var flat = new DumpPathSelectionModel();
        var entries = Enumerable.Range(0, 500).Select(i => Entry($"d{i % 10}/sub{i % 7}/{i}.txt", i + 1, i % 3 == 0)).ToList();
        foreach (var entry in entries) tree.AddText(entry);
        var random = new Random(431);
        for (int i = 0; i < 200; i++)
        {
            string path = i % 2 == 0 ? $"d{random.Next(10)}" : entries[random.Next(entries.Count)].RelativePath;
            bool included = random.Next(2) == 1;
            var node = tree.Find(path)!;
            flat.Set(path, node.IsDirectory, included);
            tree.Select(node, included ? DumpSelectionMode.Thorough : DumpSelectionMode.None);
            Assert.Equal(entries.Where(e => flat.IsIncluded(e.RelativePath, e.IsBasic)).Sum(e => e.TextBytes), tree.SelectedBytes);
            foreach (var directory in Enumerable.Range(0, 10).Select(d => tree.Find($"d{d}")!))
                Assert.Equal(flat.State(directory.RelativePath, entries.Where(e => e.RelativePath.StartsWith(directory.RelativePath + "/"))
                    .Select(e => (e.RelativePath, e.IsBasic))), tree.State(directory));
        }
    }

    [Fact]
    public void Preview_AlwaysHideIsAppliedBeforeTruncatingVisibleText()
    {
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, new string('x', 3990) + "BEGIN_PRIVATE_" + new string('q', 40) + "_END_PRIVATE");
            var cfg = DumpConfig.CreateDefault();
            cfg.SecretScan = SecretScanMode.Off;
            cfg.SensitiveValuePatterns = new() { "BEGIN_PRIVATE_.*_END_PRIVATE" };
            var entry = new DumpPreviewEntry(file, Path.GetFileName(file), DumpFileTypeKey.FromFileName(file), new FileInfo(file).Length, 0, true);
            var snapshot = new DumpPreviewSnapshot(Array.Empty<DumpPreviewTypeCount>(), new[] { entry }, 1, true);
            string preview = DumpPreviewRenderer.Render(snapshot, cfg, DumpContentSelection.FromTypes(new[] { entry.Type }));
            Assert.DoesNotContain("BEGIN_PRIVATE", preview);
            Assert.DoesNotContain(new string('q', 10), preview);
            cfg.MaxFileSizeBytes = 3995;
            Assert.Contains("Preview withheld", DumpPreviewRenderer.Render(snapshot, cfg, DumpContentSelection.FromTypes(new[] { entry.Type })));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task ScanBatches_AreBoundedAndCompleteOnlyAfterInventoryIsDrained()
    {
        string root = Path.Combine(Path.GetTempPath(), "DumpReviewTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (int i = 0; i < 1_000; i++) File.WriteAllText(Path.Combine(root, $"{i}.txt"), "plain content");
            var state = new DumpPreviewScanState();
            await DumpPreviewScanner.ScanAsync(root, DumpConfig.CreateDefault(), state, CancellationToken.None);
            Assert.True(state.Snapshot().Completed);
            Assert.Equal(1_000, state.Snapshot().Paths.Count);
            var paths = new HashSet<string>();
            int texts = 0;
            DumpPreviewBatch batch;
            do
            {
                batch = state.Drain(17);
                Assert.InRange(batch.Changes.Count, 1, 17);
                foreach (var change in batch.Changes)
                {
                    if (change.Path is { } path) Assert.True(paths.Add(path.RelativePath));
                    if (change.Entry is { } entry)
                    {
                        Assert.Contains(entry.RelativePath, paths);
                        texts++;
                    }
                }
            } while (!batch.Completed);
            Assert.Equal(1_000, paths.Count);
            Assert.Equal(1_000, texts);
            Assert.Empty(state.Drain().Changes);
            Assert.Equal(1_000, state.Snapshot().Paths.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Preview_TotalBudgetIncludesEarlierSelectedFilesAndSupportsCancellation()
    {
        string root = Path.Combine(Path.GetTempPath(), "DumpReviewTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "first file");
            File.WriteAllText(Path.Combine(root, "b.txt"), "second file");
            var entries = new[] { "a.txt", "b.txt" }.Select(name => new DumpPreviewEntry(
                Path.Combine(root, name), name, DumpFileTypeKey.FromFileName(name), new FileInfo(Path.Combine(root, name)).Length, 0, true)).ToArray();
            var cfg = DumpConfig.CreateDefault();
            cfg.MaxTotalSizeBytes = entries[0].TextBytes;
            var snapshot = new DumpPreviewSnapshot(Array.Empty<DumpPreviewTypeCount>(), entries, 2, true);
            var selection = DumpContentSelection.FromTypes(entries.Select(entry => entry.Type));
            string preview = DumpPreviewRenderer.Render(snapshot, cfg, selection, "b.txt");
            Assert.Contains("total size limit", preview);
            Assert.DoesNotContain("second file", preview);
            Assert.ThrowsAny<OperationCanceledException>(() => DumpPreviewRenderer.Render(snapshot, cfg, selection,
                cancellationToken: new CancellationToken(true)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(OutputStyle.Xml)]
    [InlineData(OutputStyle.XmlCompact)]
    [InlineData(OutputStyle.Docx)]
    public void Preview_XmlFamiliesSanitizeControlsAndPreserveUnicode(OutputStyle style)
    {
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "hello 😀\u0001 world\n");
            var cfg = DumpConfig.CreateDefault();
            cfg.Style = style;
            cfg.SecretScan = SecretScanMode.Off;
            var entry = new DumpPreviewEntry(file, Path.GetFileName(file), DumpFileTypeKey.FromFileName(file), new FileInfo(file).Length, 0, true);
            var snapshot = new DumpPreviewSnapshot(Array.Empty<DumpPreviewTypeCount>(), new[] { entry }, 1, true);
            var selection = DumpContentSelection.FromTypes(new[] { entry.Type });
            string preview = DumpPreviewRenderer.Render(snapshot, cfg, selection);
            Assert.Contains("hello 😀 world", preview);
            Assert.DoesNotContain('\u0001', preview);
            if (style != OutputStyle.Docx) System.Xml.Linq.XDocument.Parse(preview);
            File.WriteAllText(file, new string('x', 3999) + "😀tail");
            preview = DumpPreviewRenderer.Render(snapshot, cfg, selection);
            Assert.DoesNotContain('\uD83D', preview);
            Assert.DoesNotContain('\uDE00', preview);
            if (style != OutputStyle.Docx) System.Xml.Linq.XDocument.Parse(preview);
        }
        finally { File.Delete(file); }
    }
}
