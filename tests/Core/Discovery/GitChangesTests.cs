using DumpToTxt.Core;
using System.Diagnostics;

namespace DumpToTxt.Tests;

[Collection("Git environment")]
public sealed class GitChangesTests : IDisposable
{
    private readonly string fixture = Path.Combine(Path.GetTempPath(), "dtt-passive-git-" + Guid.NewGuid().ToString("N"));
    private readonly string root;

    public GitChangesTests()
    {
        root = Path.Combine(fixture, "repository");
        Directory.CreateDirectory(root);
        Git("init");
        Git("config", "user.name", "Fixture");
        Git("config", "user.email", "fixture@example.invalid");
        File.WriteAllText(Path.Combine(root, "tracked.txt"), "initial content\n");
        Git("add", ".");
        Git("commit", "-m", "fixture");
    }

    [Fact]
    public void FsmonitorIsNeverExecuted()
    {
        string marker = Path.Combine(fixture, "fsmonitor-ran");
        string script = WriteScript("monitor", marker, "printf '\\0'\n");
        Git("config", "core.fsmonitor", script);
        Git("status", "--porcelain=v1");
        Assert.True(File.Exists(marker), "The harmless control must demonstrate Git invoking fsmonitor.");
        File.Delete(marker);
        File.WriteAllText(Path.Combine(root, "untracked.txt"), "new");

        var changed = GitChanges.ChangedFiles(root);

        Assert.NotNull(changed);
        Assert.Contains(Path.Combine(root, "untracked.txt"), changed);
        Assert.False(File.Exists(marker));
    }

    [Theory]
    [InlineData("clean")]
    [InlineData("process")]
    public void ConfiguredFiltersAreNeverExecuted(string filterKind)
    {
        string marker = Path.Combine(fixture, filterKind + "-ran");
        string script = WriteScript(filterKind, marker, "cat\n");
        File.WriteAllText(Path.Combine(root, ".gitattributes"), "tracked.txt filter=fixture\n");
        Git("config", "filter.fixture." + filterKind, script);
        Git("config", "filter.fixture.required", "true");
        File.WriteAllText(Path.Combine(root, "tracked.txt"), "changed content\n");
        // A clean filter completes normally. The deliberately incomplete process filter still records
        // its invocation before Git rejects its protocol; either is a valid harmless positive control.
        Git("status", "--porcelain=v1");
        Assert.True(File.Exists(marker), "The control must exercise the configured filter.");
        File.Delete(marker);

        var changed = GitChanges.ChangedFiles(root);

        Assert.NotNull(changed);
        Assert.Contains(Path.Combine(root, "tracked.txt"), changed);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void TraditionalAndConfiguredIndexHooksAreNeverExecuted()
    {
        string marker = Path.Combine(fixture, "index-hook-ran");
        string script = WriteScript("index-hook", marker, "exit 0\n");
        Directory.CreateDirectory(Path.Combine(root, ".git", "hooks"));
        File.Copy(script, Path.Combine(root, ".git", "hooks", "post-index-change"));
        Git("config", "hook.fixture.command", script);
        Git("config", "hook.fixture.event", "post-index-change");
        File.WriteAllText(Path.Combine(root, "tracked.txt"), "changed content\n");

        var changed = GitChanges.ChangedFiles(root);

        Assert.NotNull(changed);
        Assert.Contains(Path.Combine(root, "tracked.txt"), changed);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void CaseDistinctFilterDriversAreBothDisabled()
    {
        string marker = Path.Combine(fixture, "case-filter-ran");
        File.WriteAllText(Path.Combine(root, ".gitattributes"), "tracked.txt filter=fixture\n");
        string script = WriteScript("case-filter", marker, "cat\n");
        File.AppendAllText(Path.Combine(root, ".git", "config"),
            "\n[filter \"Fixture\"]\n\tclean = cat\n[filter \"fixture\"]\n\tclean = " + script + "\n");
        File.WriteAllText(Path.Combine(root, "tracked.txt"), "changed content\n");
        Git("status", "--porcelain=v1");
        Assert.True(File.Exists(marker));
        File.Delete(marker);

        Assert.Contains(Path.Combine(root, "tracked.txt"), GitChanges.ChangedFiles(root)!);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void StagedUnstagedUntrackedAndRenameDestinationRemainVisible()
    {
        Git("mv", "tracked.txt", " renamed-é.txt");
        File.WriteAllText(Path.Combine(root, "staged.txt"), "stage");
        Git("add", "staged.txt");
        File.WriteAllText(Path.Combine(root, "staged.txt"), "also unstaged");
        File.WriteAllText(Path.Combine(root, " untracked-é.txt"), "new");

        var changed = GitChanges.ChangedFiles(root);

        Assert.NotNull(changed);
        Assert.Contains(Path.Combine(root, " renamed-é.txt"), changed);
        Assert.Contains(Path.Combine(root, "staged.txt"), changed);
        Assert.Contains(Path.Combine(root, " untracked-é.txt"), changed);
        Assert.DoesNotContain(Path.Combine(root, "tracked.txt"), changed);
    }

    [Fact]
    public void InheritedConfigCannotHideEffectiveFilterDrivers()
    {
        string marker = Path.Combine(fixture, "inherited-filter-ran");
        File.WriteAllText(Path.Combine(root, ".gitattributes"), "tracked.txt filter=fixture\n");
        Git("config", "filter.fixture.clean", WriteScript("inherited-filter", marker, "cat\n"));
        File.WriteAllText(Path.Combine(root, "tracked.txt"), "changed content\n");
        Git("status", "--porcelain=v1");
        Assert.True(File.Exists(marker));
        File.Delete(marker);
        string foreignConfig = Path.Combine(fixture, "foreign.config");
        File.WriteAllText(foreignConfig, "[core]\n\tfsmonitor = false\n");
        string[] keys = ["GIT_CONFIG", "GIT_CONFIG_COUNT", "GIT_CONFIG_KEY_0", "GIT_CONFIG_VALUE_0"];
        string?[] previous = keys.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            Environment.SetEnvironmentVariable(keys[0], foreignConfig);
            Environment.SetEnvironmentVariable(keys[1], "1");
            Environment.SetEnvironmentVariable(keys[2], "core.fsmonitor");
            Environment.SetEnvironmentVariable(keys[3], WriteScript("inherited-monitor", marker, "printf '\\0'\n"));

            Assert.Contains(Path.Combine(root, "tracked.txt"), GitChanges.ChangedFiles(root)!);
            Assert.False(File.Exists(marker));
        }
        finally
        {
            for (int i = 0; i < keys.Length; i++) Environment.SetEnvironmentVariable(keys[i], previous[i]);
        }
    }

    [Fact]
    public void RepositoryLocalGitExecutableCannotReplaceTrustedGit()
    {
        File.WriteAllText(Path.Combine(root, "git.exe"), "not an executable");
        File.WriteAllText(Path.Combine(root, "git.cmd"), "@exit /b 1\n");
        Assert.Contains(Path.Combine(root, "git.exe"), GitChanges.ChangedFiles(root)!);
    }

    [Fact]
    public void SubmoduleWorktreesAreNotExecutedButChangedGitlinksRemainVisible()
    {
        string sub = Path.Combine(root, "sub");
        Directory.CreateDirectory(sub);
        GitIn(sub, "init");
        GitIn(sub, "config", "user.name", "Fixture");
        GitIn(sub, "config", "user.email", "fixture@example.invalid");
        File.WriteAllText(Path.Combine(sub, "nested.txt"), "initial");
        GitIn(sub, "add", ".");
        GitIn(sub, "commit", "-m", "nested fixture");
        Git("add", "sub");
        Git("commit", "-m", "gitlink fixture");
        string marker = Path.Combine(fixture, "nested-monitor-ran");
        GitIn(sub, "config", "core.fsmonitor", WriteScript("nested-monitor", marker, "printf '\\0'\n"));
        File.WriteAllText(Path.Combine(sub, "nested.txt"), "modified nested content");
        Git("status", "--porcelain=v1");
        Assert.True(File.Exists(marker), "Ordinary status must demonstrate nested monitor invocation.");
        File.Delete(marker);

        Assert.Empty(GitChanges.ChangedFiles(root)!);
        Assert.False(File.Exists(marker));

        GitIn(sub, "-c", "core.fsmonitor=", "add", ".");
        GitIn(sub, "-c", "core.fsmonitor=", "commit", "-m", "changed nested commit");
        Assert.Contains(sub, GitChanges.ChangedFiles(root)!);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void CleanRepositoryAndNonRepositoryRetainFallbackContracts()
    {
        Assert.Empty(GitChanges.ChangedFiles(root)!);
        Assert.Null(GitChanges.ChangedFiles(fixture));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => GitChanges.ChangedFiles(root, cancellation.Token));
    }

    private string WriteScript(string name, string marker, string body)
    {
        string path = Path.Combine(fixture, name + ".sh");
        File.WriteAllText(path, "#!/bin/sh\nprintf marker > '" + marker.Replace('\\', '/') + "'\n" + body);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path.Replace('\\', '/');
    }

    private void Git(params string[] arguments) => GitIn(root, arguments);

    private static void GitIn(string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (string arg in arguments) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(10000), "Fixture Git command timed out.");
        Task.WaitAll(stdout, stderr);
        // Process-filter positive controls intentionally use an invalid protocol.
        Assert.True(process.ExitCode == 0 || arguments[0] == "status", stderr.Result);
    }

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(fixture, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(fixture, recursive: true);
    }
}

[CollectionDefinition("Git environment", DisableParallelization = true)]
public sealed class GitEnvironmentCollection { }
