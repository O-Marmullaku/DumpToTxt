using System.Diagnostics;
using System.Text;
using DumpToTxt.Core;

namespace DumpToTxt.Tests;

public sealed class DumpPreviewTests
{
    [Fact]
    public async Task Scan_GroupsExtensionsCaseInsensitively_AndFindsUnknownText()
    {
        using var tree = new TempTree();
        tree.Text("a.txt", "one");
        tree.Text("b.TXT", "two");
        tree.Text("animation.funscript", "{}");

        var snapshot = await Scan(tree.Root);

        Assert.Equal(2, Count(snapshot, ".txt"));
        Assert.Equal(1, Count(snapshot, ".funscript"));
        Assert.Equal(3, snapshot.Entries.Count);
        Assert.Contains(snapshot.Entries, x => x.RelativePath == "animation.funscript" && x.TextBytes > 0);
    }

    [Fact]
    public async Task ContributionTree_AggregatesAndSortsLargestFolderFirst()
    {
        using var tree = new TempTree();
        Directory.CreateDirectory(tree.Path("small"));
        Directory.CreateDirectory(tree.Path("large"));
        tree.Text("small\\one.txt", "1234");
        tree.Text("large\\one.txt", new string('a', 100));
        tree.Text("large\\two.txt", new string('b', 50));

        var snapshot = await Scan(tree.Root);
        var root = DumpContributionTree.Build(snapshot.Entries);

        Assert.Equal(3, root.FileCount);
        Assert.Equal("large", root.Children[0].Name);
        Assert.Equal(2, root.Children[0].FileCount);
        Assert.True(root.Children[0].TextBytes > root.Children[1].TextBytes);
    }

    [Fact]
    public void PathSelection_UsesNearestOverrideAndReportsMixedFolder()
    {
        var model = new DumpPathSelectionModel();
        model.Set("src", isDirectory: true, included: false);
        Assert.False(model.IsIncluded("src\\future.cs", defaultIncluded: true));

        model.Set("src\\keep.cs", isDirectory: false, included: true);

        Assert.True(model.IsIncluded("src\\keep.cs", defaultIncluded: true));
        Assert.False(model.IsIncluded("src\\drop.cs", defaultIncluded: true));
        Assert.Equal(DumpNodeSelectionState.Mixed, model.State(
            "src", new[] { (Path: "src\\keep.cs", DefaultIncluded: true), (Path: "src\\drop.cs", DefaultIncluded: true) }));
    }

    [Fact]
    public void Engine_PathOverrideExcludesOnlyThatSubtree()
    {
        using var tree = new TempTree();
        Directory.CreateDirectory(tree.Path("keep"));
        Directory.CreateDirectory(tree.Path("drop"));
        tree.Text("keep\\a.txt", "KEEP-CONTENT");
        tree.Text("drop\\b.txt", "DROP-CONTENT");
        var cfg = DumpConfig.CreateDefault();
        cfg.Style = OutputStyle.Plain;
        cfg.OutputTarget = OutputTarget.Stdout;
        cfg.SecretScan = SecretScanMode.Off;
        var selection = DumpContentSelection.FromMode(DumpSelectionMode.Thorough, cfg)
            .WithPathOverrides(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["drop"] = false,
            });

        var result = new DumpEngine().Run(tree.Root, cfg, contentSelection: selection);

        Assert.Contains("KEEP-CONTENT", result.Text);
        Assert.DoesNotContain("DROP-CONTENT", result.Text);
        Assert.Contains("b.txt", result.Text); // remains visible in the structure map
    }

    [Fact]
    public async Task PreviewRenderer_ChangesWithStyleAndCanFocusAFile()
    {
        using var tree = new TempTree();
        tree.Text("a.cs", "class PreviewA {}");
        tree.Text("b.txt", "PREVIEW-B");
        var snapshot = await Scan(tree.Root);
        var cfg = DumpConfig.CreateDefault();
        cfg.Style = OutputStyle.MarkdownAi;
        var selection = DumpContentSelection.FromMode(DumpSelectionMode.Thorough, cfg);

        string preview = DumpPreviewRenderer.Render(snapshot, cfg, selection, "b.txt");

        Assert.Contains("AI-friendly Markdown", preview);
        Assert.Contains("b.txt", preview);
        Assert.Contains("PREVIEW-B", preview);
        Assert.DoesNotContain("class PreviewA", preview);
    }

    [Fact]
    public async Task Scan_OmitsMp4AndBinaryDataRegardlessOfExtension()
    {
        using var tree = new TempTree();
        tree.Bytes("video.mp4", 0, 0, 0, 24, 102, 116, 121, 112, 105, 115, 111, 109);
        tree.Bytes("payload.custom", 1, 2, 3, 4, 5, 6, 7, 8, 11, 12, 14, 15, 16, 17, 18, 19);
        tree.Text("good.custom", "plain text");

        var snapshot = await Scan(tree.Root);

        Assert.DoesNotContain(snapshot.Types, x => x.Type.DisplayName == ".mp4");
        Assert.Equal(1, Count(snapshot, ".custom"));
    }

    [Fact]
    public async Task Scan_InventoryKeepsBinaryFilesAndEmptyFoldersAtZeroTextBytes()
    {
        using var tree = new TempTree();
        Directory.CreateDirectory(tree.Path("compact"));
        Directory.CreateDirectory(tree.Path("empty"));
        tree.Bytes("compact\\DumpToTxt.exe", 0, 1, 2, 3, 4, 5);
        tree.Bytes("setup.exe", 0, 1, 2, 3, 4, 5);

        var snapshot = await Scan(tree.Root);
        var contribution = DumpContributionTree.Build(snapshot.Paths, snapshot.Entries);

        Assert.Empty(snapshot.Entries);
        Assert.Contains(snapshot.Paths, x => x.IsDirectory && x.RelativePath == "compact");
        Assert.Contains(snapshot.Paths, x => x.IsDirectory && x.RelativePath == "empty");
        Assert.Contains(snapshot.Paths, x => !x.IsDirectory && x.RelativePath == "compact\\DumpToTxt.exe");
        Assert.Contains(snapshot.Paths, x => !x.IsDirectory && x.RelativePath == "setup.exe");
        Assert.Equal(2, contribution.FileCount);
        Assert.Equal(0, contribution.TextBytes);
        Assert.Equal(new[] { "compact", "empty", "setup.exe" },
            contribution.Children.Select(x => x.Name).ToArray());
    }

    [Fact]
    public async Task Scan_SelectedRootNamedDistIsNotExcludedByItsOwnDefaultRule()
    {
        using var tree = new TempTree();
        string dist = tree.Path("dist");
        Directory.CreateDirectory(Path.Combine(dist, "full"));
        File.WriteAllBytes(Path.Combine(dist, "full", "DumpToTxt.exe"), [0, 1, 2, 3]);

        var snapshot = await Scan(dist);

        Assert.Contains(snapshot.Paths, x => x.IsDirectory && x.RelativePath == "full");
        Assert.Contains(snapshot.Paths, x => !x.IsDirectory && x.RelativePath == "full\\DumpToTxt.exe");
    }

    [Fact]
    public async Task Scan_AcceptsBomEncodedText()
    {
        using var tree = new TempTree();
        File.WriteAllText(tree.Path("utf16.unknown"), "hello", new UnicodeEncoding(false, true));

        var snapshot = await Scan(tree.Root);

        Assert.Equal(1, Count(snapshot, ".unknown"));
    }

    [Fact]
    public async Task Scan_AcceptsLargeUtf8TextWhenSniffEndsMidCharacter()
    {
        using var tree = new TempTree();
        tree.Text("international.custom", string.Concat(Enumerable.Repeat("界", 3_000)));

        var snapshot = await Scan(tree.Root);

        Assert.Equal(1, Count(snapshot, ".custom"));
    }

    [Fact]
    public async Task Scan_RespectsGitignore()
    {
        using var tree = new TempTree();
        tree.Text(".gitignore", "ignored.txt\n");
        tree.Text("ignored.txt", "ignored");
        tree.Text("kept.txt", "kept");

        var snapshot = await Scan(tree.Root);

        Assert.Equal(1, Count(snapshot, ".txt"));
    }

    [Fact]
    public async Task Scan_RespectsExcludeGlobs()
    {
        using var tree = new TempTree();
        tree.Text("kept.txt", "kept");
        tree.Text("excluded.txt", "excluded");
        var cfg = DumpConfig.CreateDefault();
        cfg.ExcludeGlobs = new() { "excluded.txt" };

        var snapshot = await Scan(tree.Root, cfg);

        Assert.Equal(1, Count(snapshot, ".txt"));
    }

    [Fact]
    public async Task Scan_RespectsIncludeGlobs()
    {
        using var tree = new TempTree();
        tree.Text("kept.txt", "kept");
        tree.Text("excluded.cs", "class Excluded {}");
        var cfg = DumpConfig.CreateDefault();
        cfg.IncludeGlobs = new() { "*.txt" };

        var snapshot = await Scan(tree.Root, cfg);

        Assert.Equal(1, Count(snapshot, ".txt"));
        Assert.DoesNotContain(snapshot.Types, x => x.Type.DisplayName == ".cs");
    }

    [Fact]
    public async Task Scan_GroupsDotfilesAndExtensionlessFilesSeparately()
    {
        using var tree = new TempTree();
        tree.Text(".env", "A=1");
        tree.Text(".editorconfig", "root=true");
        tree.Text("LICENSE", "license text");

        var snapshot = await Scan(tree.Root);

        Assert.Equal(1, Count(snapshot, ".env"));
        Assert.Equal(1, Count(snapshot, ".editorconfig"));
        Assert.Equal(1, Count(snapshot, DumpFileTypeKey.NoExtensionDisplayName));
    }

    [Fact]
    public async Task Scan_BasicEligibilityUsesTheDisplayedDotfileIdentity()
    {
        using var tree = new TempTree();
        tree.Text(".dumptotxt.json", "{}");
        tree.Text(".env", "A=1");
        tree.Text("plain.json", "{}");
        var cfg = DumpConfig.CreateDefault();
        cfg.ExtSet = new() { ".json" };
        cfg.DotFilesAllow = new() { ".env" };

        var snapshot = await Scan(tree.Root, cfg);

        Assert.False(snapshot.Types.Single(x => x.Type.DisplayName == ".dumptotxt.json").IsBasic);
        Assert.True(snapshot.Types.Single(x => x.Type.DisplayName == ".env").IsBasic);
        Assert.True(snapshot.Types.Single(x => x.Type.DisplayName == ".json").IsBasic);
    }

    [Fact]
    public async Task Scan_ChangedModeCountsOnlyChangedFiles()
    {
        using var tree = new TempTree();
        tree.Text("changed.txt", "before");
        tree.Text("unchanged.cs", "class Stable {}");
        Git(tree.Root, "init");
        Git(tree.Root, "config user.email test@example.com");
        Git(tree.Root, "config user.name Test");
        Git(tree.Root, "add .");
        Git(tree.Root, "commit -m initial");
        tree.Text("changed.txt", "after");
        var cfg = DumpConfig.CreateDefault();
        cfg.OnlyGitChanged = true;

        var snapshot = await Scan(tree.Root, cfg);

        Assert.Equal(1, Count(snapshot, ".txt"));
        Assert.DoesNotContain(snapshot.Types, x => x.Type.DisplayName == ".cs");
    }

    [Fact]
    public async Task Scan_CancellationStopsCleanly()
    {
        using var tree = new TempTree();
        tree.Text("a.txt", "text");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var state = new DumpPreviewScanState();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DumpPreviewScanner.ScanAsync(tree.Root, DumpConfig.CreateDefault(), state, cts.Token));

        Assert.False(state.Snapshot().Completed);
    }

    [Fact]
    public async Task Scan_StopSnapshotPreventsRecordsAfterInProgressCancellation()
    {
        using var tree = new TempTree();
        for (int i = 0; i < 4_000; i++) tree.Text($"file-{i:D4}.txt", "text");
        using var cts = new CancellationTokenSource();
        var state = new DumpPreviewScanState();
        var scan = DumpPreviewScanner.ScanAsync(tree.Root, DumpConfig.CreateDefault(), state, cts.Token);
        Assert.True(SpinWait.SpinUntil(() => state.Snapshot().ScannedFiles > 0, TimeSpan.FromSeconds(5)));

        var frozen = state.StopAndSnapshot();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);

        Assert.Equal(frozen.ScannedFiles, state.Snapshot().ScannedFiles);
        Assert.False(state.Snapshot().Completed);
    }

    [Fact]
    public void Selection_BasicUsesResolvedExtensionsAndDotfiles()
    {
        var cfg = DumpConfig.CreateDefault();
        cfg.ExtSet = new() { ".txt" };
        cfg.DotFilesAllow = new() { ".env" };
        var model = new DumpSelectionModel();
        model.Apply(Snapshot(
            ("a.txt", 2, true),
            ("animation.funscript", 1, false),
            (".env", 1, true),
            ("LICENSE", 1, false)));

        model.SelectMode(DumpSelectionMode.Basic);

        Assert.Equal(new[] { ".env", ".txt" }, Selected(model));
    }

    [Fact]
    public void Selection_ThoroughSelectsCurrentAndFutureTypes_WithoutOverwritingManualChoice()
    {
        var model = new DumpSelectionModel();
        model.Apply(Snapshot(("a.txt", 1, true)));
        model.SelectMode(DumpSelectionMode.Thorough);
        var txt = DumpFileTypeKey.FromFileName("a.txt");
        model.SetSelected(txt, false);

        model.Apply(Snapshot(("a.txt", 2, true), ("data.json", 1, true)));

        Assert.False(model.Choices.Single(x => x.Type == txt).Selected);
        Assert.True(model.Choices.Single(x => x.Type.DisplayName == ".json").Selected);
        model.SelectMode(DumpSelectionMode.Thorough);
        Assert.All(model.Choices, x => Assert.True(x.Selected));
    }

    [Fact]
    public void Selection_NoneClearsCurrentAndFutureTypes()
    {
        var model = new DumpSelectionModel();
        model.Apply(Snapshot(("a.txt", 1, true)));
        model.SelectMode(DumpSelectionMode.Thorough);
        model.SelectMode(DumpSelectionMode.None);
        model.Apply(Snapshot(("a.txt", 2, true), ("readme.md", 1, true)));

        Assert.All(model.Choices, x => Assert.False(x.Selected));
    }

    [Fact]
    public void Selection_CaptureAppliesTypesRecordedAfterTheLastVisualRefresh()
    {
        var model = new DumpSelectionModel();
        model.Apply(Snapshot(("a.txt", 1, true)));
        model.SelectMode(DumpSelectionMode.Thorough);

        var selection = model.Capture(Snapshot(("a.txt", 1, true), ("late.json", 1, true)));

        Assert.Contains(".txt", selection.AllowedExtensions);
        Assert.Contains(".json", selection.AllowedExtensions);
    }

    [Fact]
    public void Engine_TransientSelectionControlsContentsWithoutMutatingConfig()
    {
        using var tree = new TempTree();
        tree.Text("chosen.txt", "CHOSEN-CONTENT");
        tree.Text("chosen-late.txt", "LATE-CHOSEN-CONTENT");
        tree.Text("configured.cs", "CONFIGURED-CONTENT");
        var cfg = DumpConfig.CreateDefault();
        cfg.ExtSet = new() { ".cs" };
        cfg.Style = OutputStyle.Plain;
        cfg.OutputTarget = OutputTarget.Stdout;
        cfg.SecretScan = SecretScanMode.Off;
        var selection = DumpContentSelection.FromTypes(new[] { DumpFileTypeKey.FromFileName("chosen.txt") });

        var result = new DumpEngine().Run(tree.Root, cfg, contentSelection: selection);

        Assert.Contains("CHOSEN-CONTENT", result.Text);
        Assert.Contains("LATE-CHOSEN-CONTENT", result.Text);
        Assert.DoesNotContain("CONFIGURED-CONTENT", result.Text);
        Assert.Equal(new[] { ".cs" }, cfg.ExtSet);
    }

    [Fact]
    public void Engine_TransientSelectionNeverLeaksBinaryContentFromATextualType()
    {
        using var tree = new TempTree();
        tree.Text("good.custom", "GOOD-TEXT");
        tree.Bytes("bad.custom", 1, 2, 3, 4, 5, 6, 7, 8, 11, 12, 14, 15, 16, 17, 18, 19);
        var cfg = DumpConfig.CreateDefault();
        cfg.Style = OutputStyle.Plain;
        cfg.OutputTarget = OutputTarget.Stdout;
        cfg.SecretScan = SecretScanMode.Off;
        cfg.DetectBinary = false;
        var selection = DumpContentSelection.FromTypes(new[] { DumpFileTypeKey.FromFileName("good.custom") });
        Assert.False(TextFileClassifier.IsTextLike(tree.Path("bad.custom")));

        var result = new DumpEngine().Run(tree.Root, cfg, contentSelection: selection);

        Assert.Contains("GOOD-TEXT", result.Text);
        Assert.Contains("[binary file — content skipped]", result.Text);
        int leaked = result.Text.IndexOf("\u0001\u0002\u0003", StringComparison.Ordinal);
        string context = leaked < 0 ? "" : Convert.ToHexString(Encoding.UTF8.GetBytes(
            result.Text.Substring(Math.Max(0, leaked - 24), Math.Min(72, result.Text.Length - Math.Max(0, leaked - 24)))));
        Assert.True(leaked < 0, $"binary bytes leaked at {leaked}; UTF-8 context: {context}");
    }

    [Fact]
    public void Engine_EmptySelectionPreservesStructureButEmitsNoContentBlocks()
    {
        using var tree = new TempTree();
        tree.Text("a.txt", "TXT-CONTENT");
        tree.Text("b.cs", "CS-CONTENT");
        var cfg = DumpConfig.CreateDefault();
        cfg.Style = OutputStyle.Plain;
        cfg.OutputTarget = OutputTarget.Stdout;
        cfg.SecretScan = SecretScanMode.Off;

        var result = new DumpEngine().Run(tree.Root, cfg, contentSelection: DumpContentSelection.Empty);

        Assert.Contains("a.txt", result.Text);
        Assert.Contains("b.cs", result.Text);
        Assert.Contains("Files: 0", result.Text);
        Assert.DoesNotContain("TXT-CONTENT", result.Text);
        Assert.DoesNotContain("CS-CONTENT", result.Text);
    }

    [Fact]
    public void Engine_BinaryOnlyFolderProducesACompleteStructureOnlyDump()
    {
        using var tree = new TempTree();
        string dist = tree.Path("dist");
        Directory.CreateDirectory(Path.Combine(dist, "compact"));
        Directory.CreateDirectory(Path.Combine(dist, "empty"));
        File.WriteAllBytes(Path.Combine(dist, "compact", "DumpToTxt.exe"), [0, 1, 2, 3, 4, 5]);
        File.WriteAllBytes(Path.Combine(dist, "setup.exe"), [0, 1, 2, 3, 4, 5]);
        var cfg = DumpConfig.CreateDefault();
        cfg.Style = OutputStyle.Plain;
        cfg.OutputTarget = OutputTarget.Stdout;
        cfg.SecretScan = SecretScanMode.Off;

        var result = new DumpEngine().Run(dist, cfg, contentSelection: DumpContentSelection.Empty);

        Assert.Equal(0, result.FilesIncluded);
        Assert.Contains("compact", result.Text);
        Assert.Contains("empty", result.Text);
        Assert.Contains("DumpToTxt.exe", result.Text);
        Assert.Contains("setup.exe", result.Text);
    }

    [Fact]
    public void Engine_ChangedModeSelectionDoesNotRemoveAncestorDirectoriesFromClassicListing()
    {
        using var tree = new TempTree();
        Directory.CreateDirectory(tree.Path("sub"));
        tree.Text("sub\\changed.txt", "before");
        Git(tree.Root, "init");
        Git(tree.Root, "config user.email test@example.com");
        Git(tree.Root, "config user.name Test");
        Git(tree.Root, "add .");
        Git(tree.Root, "commit -m initial");
        tree.Text("sub\\changed.txt", "after");
        var cfg = DumpConfig.CreateDefault();
        cfg.OutputTarget = OutputTarget.Stdout;
        cfg.SecretScan = SecretScanMode.Off;
        cfg.OnlyGitChanged = true;

        var result = new DumpEngine().Run(tree.Root, cfg, contentSelection: DumpContentSelection.Empty);

        Assert.Contains(tree.Path("sub") + "\r\n", result.Text);
        Assert.Contains(tree.Path("sub\\changed.txt") + "\r\n", result.Text);
        Assert.Equal(0, result.FilesIncluded);
    }

    private static async Task<DumpPreviewSnapshot> Scan(string root, DumpConfig? cfg = null)
    {
        var state = new DumpPreviewScanState();
        await DumpPreviewScanner.ScanAsync(root, cfg ?? DumpConfig.CreateDefault(), state, CancellationToken.None);
        return state.Snapshot();
    }

    private static int Count(DumpPreviewSnapshot snapshot, string displayName) =>
        snapshot.Types.SingleOrDefault(x => x.Type.DisplayName == displayName)?.Count ?? 0;

    private static DumpPreviewSnapshot Snapshot(params (string FileName, int Count, bool Basic)[] items) =>
        new(items.Select(x => new DumpPreviewTypeCount(
            DumpFileTypeKey.FromFileName(x.FileName), x.Count, x.Basic)).ToArray(), 0, true);

    private static string[] Selected(DumpSelectionModel model) => model.Choices
        .Where(x => x.Selected).Select(x => x.Type.DisplayName).OrderBy(x => x).ToArray();

    private static void Git(string workingDirectory, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }

    private sealed class TempTree : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "dtt-preview-" + Guid.NewGuid().ToString("N"));

        public TempTree() => Directory.CreateDirectory(Root);
        public string Path(string relative) => System.IO.Path.Combine(Root, relative);
        public void Text(string relative, string content) => File.WriteAllText(Path(relative), content);
        public void Bytes(string relative, params byte[] bytes) => File.WriteAllBytes(Path(relative), bytes);
        public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
    }
}
