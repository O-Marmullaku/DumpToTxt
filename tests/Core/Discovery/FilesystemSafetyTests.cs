using DumpToTxt.Core;
using System.Diagnostics;
using System.Text;

namespace DumpToTxt.Tests;

public class FilesystemSafetyTests
{
    [Fact]
    public void UnreadableTextIsNotReportedAsBinary()
    {
        Assert.Throws<FileNotFoundException>(() => TextFileClassifier.IsTextLike(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void InvalidExclusionCannotSilentlyDisableFiltering()
    {
        var config = DumpConfig.CreateDefault();
        config.ExcludeRegex = "[";
        Assert.ThrowsAny<ArgumentException>(() => IgnoreMatcher.FromConfig(config));
    }

    [Fact]
    public void LegacyRegexHasABoundedMatchTime()
    {
        var config = DumpConfig.CreateDefault();
        config.ExcludeRegex = "^(a+)+$";
        var matcher = IgnoreMatcher.FromConfig(config);
        Assert.Throws<System.Text.RegularExpressions.RegexMatchTimeoutException>(() =>
            matcher.IsExcluded(new string('a', 100_000) + "!", "file.txt", false));
    }

    [Fact]
    public void AnsiTextGetsAnEncodingErrorInsteadOfBinaryOrReplacementCharacters()
    {
        string path = Path.Combine(Path.GetTempPath(), "dtt-ansi-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(path, new byte[] { 99, 97, 102, 233, 32, 116, 101, 120, 116 });
            Assert.Throws<InvalidDataException>(() => TextFileClassifier.IsTextLike(path));
            var encoding = TextFileClassifier.DetectTextEncoding(new byte[] { 99, 97, 102, 233 }, out int bom);
            Assert.Equal(0, bom);
            Assert.Throws<DecoderFallbackException>(() => encoding.GetString(new byte[] { 233 }));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GitChangedFilesPreservesLeadingSpacesUnicodeAndRenameDestination()
    {
        string root = Path.Combine(Path.GetTempPath(), "dtt-git-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Git(root, "init");
            Git(root, "config", "user.name", "Fixture");
            Git(root, "config", "user.email", "fixture@example.invalid");
            File.WriteAllText(Path.Combine(root, "before.txt"), "old");
            Git(root, "add", ".");
            Git(root, "commit", "-m", "fixture");
            Git(root, "mv", "before.txt", " leading-é.txt");
            File.WriteAllText(Path.Combine(root, " untracked-é.txt"), "new");
            var changed = GitChanges.ChangedFiles(root);
            Assert.NotNull(changed);
            Assert.Contains(Path.Combine(root, " leading-é.txt"), changed);
            Assert.Contains(Path.Combine(root, " untracked-é.txt"), changed);
            Assert.DoesNotContain(Path.Combine(root, "before.txt"), changed);
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DirectoryLinksAreInventoriedWithoutTraversingExternalContentOrCycles()
    {
        string fixture = Path.Combine(Path.GetTempPath(), "dtt-links-audit-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(fixture, "root"), external = Path.Combine(fixture, "external");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(external, "private.txt"), "must not enter inventory");
        string outside = Path.Combine(root, "outside"), cycle = Path.Combine(root, "cycle");
        try
        {
            Link(outside, external);
            Link(cycle, root);
            var state = new DumpPreviewScanState();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await DumpPreviewScanner.ScanAsync(root, DumpConfig.CreateDefault(), state, timeout.Token);
            var snapshot = state.Snapshot();
            Assert.Contains(snapshot.Paths, p => p.RelativePath == "outside");
            Assert.Contains(snapshot.Paths, p => p.RelativePath == "cycle");
            Assert.DoesNotContain(snapshot.Paths, p => p.RelativePath.Contains("private.txt"));
            Assert.Equal(2, snapshot.Paths.Count);
            // Authoritative exports refuse traversal gaps even when only the map was selected.
            // Preview inventory is diagnostic, not permission to silently omit linked contents.
            var config = DumpConfig.CreateDefault();
            config.OutputTarget = OutputTarget.Stdout;
            var error = Assert.Throws<IOException>(() => new DumpEngine().Run(root, config,
                contentSelection: DumpContentSelection.Empty, textOutput: TextWriter.Null));
            Assert.Contains("Directory link was listed but not traversed", error.Message);
        }
        finally
        {
            // Delete the link itself first; never recurse across a fixture reparse point.
            if (Directory.Exists(outside)) Directory.Delete(outside);
            if (Directory.Exists(cycle)) Directory.Delete(cycle);
            Directory.Delete(fixture, true);
        }
    }

    [Fact]
    public async Task SelectedLinksAndLinkedAncestorsAreRefusedBeforeReadingPoliciesOrContent()
    {
        string fixture = Path.Combine(Path.GetTempPath(), "dtt-root-link-" + Guid.NewGuid().ToString("N"));
        string external = Path.Combine(fixture, "external"), link = Path.Combine(fixture, "selected");
        string child = Path.Combine(external, "child");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(child, "private.txt"), "external private content");
        // Invalid policy would produce InvalidDataException if read before the link refusal.
        File.WriteAllText(Path.Combine(external, ConfigStore.FolderConfigName), "{");
        try
        {
            Link(link, external);
            foreach (string target in new[] { link, link + Path.DirectorySeparatorChar,
                         Path.Combine(link, "child"), Path.Combine(link, "child", "private.txt") })
            {
                var config = DumpConfig.CreateDefault();
                string output = Path.Combine(fixture, "output");
                var error = Assert.Throws<IOException>(() => new DumpEngine().Run(target, config,
                    outputDir: output, contentSelection: DumpContentSelection.Empty));
                Assert.Contains("Directory link was not traversed", error.Message);
                Assert.False(Directory.Exists(output));
                var state = new DumpPreviewScanState();
                await Assert.ThrowsAsync<IOException>(() => DumpPreviewScanner.ScanAsync(
                    target, config, state, CancellationToken.None));
                Assert.Empty(state.Snapshot().Paths);
                error = Assert.Throws<IOException>(() => ConfigStore.Resolve(target, "", ""));
                Assert.IsNotType<InvalidDataException>(error);
                Assert.Throws<IOException>(() => GitChanges.ChangedFiles(target));
            }
            Assert.Throws<IOException>(() => TextFileClassifier.IsTextLike(
                Path.Combine(link, "child", "private.txt")));
            // The original directory remains usable.
            var ordinary = DumpConfig.CreateDefault();
            ordinary.OutputTarget = OutputTarget.Stdout;
            using var writer = new StringWriter();
            new DumpEngine().Run(child, ordinary, textOutput: writer);
            Assert.Contains("external private content", writer.ToString());
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(fixture, true);
        }
    }

    [Fact]
    public async Task IgnoredChildLinkDoesNotPreventOrdinaryExport()
    {
        string fixture = Path.Combine(Path.GetTempPath(), "dtt-ignored-link-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(fixture, "root"), external = Path.Combine(fixture, "external");
        string link = Path.Combine(root, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(root, "ordinary.txt"), "ordinary content");
        File.WriteAllText(Path.Combine(root, ".dumptotxtignore"), "outside/\n");
        File.WriteAllText(Path.Combine(external, "private.txt"), "external private content");
        try
        {
            Link(link, external);
            var config = DumpConfig.CreateDefault();
            config.OutputTarget = OutputTarget.Stdout;
            using var writer = new StringWriter();
            new DumpEngine().Run(root, config, textOutput: writer);
            Assert.Contains("ordinary content", writer.ToString());
            Assert.DoesNotContain("external private content", writer.ToString());
            var state = new DumpPreviewScanState();
            await DumpPreviewScanner.ScanAsync(root, config, state, CancellationToken.None);
            Assert.DoesNotContain(state.Snapshot().Paths, p => p.RelativePath.StartsWith("outside"));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(fixture, true);
        }
    }

    private static void Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("safe.directory=" + root.Replace('\\', '/'));
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(10_000));
        Assert.True(process.ExitCode == 0, error.GetAwaiter().GetResult());
        output.GetAwaiter().GetResult();
    }

    private static void Link(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J");
        start.ArgumentList.Add(link); start.ArgumentList.Add(target);
        using var process = Process.Start(start)!;
        Assert.True(process.WaitForExit(10_000));
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }
}
