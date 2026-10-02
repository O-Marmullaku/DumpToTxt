using System.Diagnostics;
using System.Text;

namespace DumpToTxt.Core;

/// <summary>
/// Lists the files changed vs the last commit, using the <c>git</c> CLI. Returns the set of absolute
/// paths considered "changed" (staged + unstaged + untracked), or <c>null</c> when the target is not
/// inside a git repository or git is unavailable — in which case the caller falls back to a full dump.
/// Repository hooks and filters are disabled: filtered files may conservatively appear modified.
/// Submodule worktree dirtiness is ignored; changes to their recorded commits remain visible.
/// </summary>
public static class GitChanges
{
    /// <summary>Absolute paths changed since HEAD, or null when not a git repo / git missing.</summary>
    public static HashSet<string>? ChangedFiles(string targetPathOrDir,
        CancellationToken cancellationToken = default)
    {
        FilesystemSafety.EnsureTargetPath(targetPathOrDir);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dir = File.Exists(targetPathOrDir)
                ? Path.GetDirectoryName(Path.GetFullPath(targetPathOrDir))!
                : Path.GetFullPath(targetPathOrDir);

            string? executable = FindGitExecutable();
            if (executable is null) return null;
            string? top = Run(executable, dir, ["rev-parse", "--show-toplevel"], [], cancellationToken);
            if (string.IsNullOrWhiteSpace(top)) return null;     // not a git repo, or git not on PATH
            top = top.Trim();

            // Status can execute configured clean/process filters while comparing tracked contents.
            // Discover driver names without running them, then disable every effective driver.
            string? filterNames = Run(executable, dir,
                ["config", "--null", "--name-only", "--get-regexp", @"^filter\..*\.(clean|process|required)$"],
                [], cancellationToken, allowNoMatch: true);
            if (filterNames is null) return null;
            var filterOverrides = new List<string>();
            foreach (string key in filterNames.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.Ordinal))
            {
                filterOverrides.Add("-c");
                filterOverrides.Add(key + (key.EndsWith(".required", StringComparison.OrdinalIgnoreCase)
                    ? "=false" : "="));
            }
            // Avoid recursively running status inside submodules. Changed gitlink commits remain visible.
            string? status = Run(executable, dir,
                ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=dirty"],
                filterOverrides, cancellationToken);
            if (status is null) return null;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] records = status.Split('\0');
            for (int i = 0; i < records.Length; i++)
            {
                string line = records[i];
                if (line.Length < 4) continue;
                string path = line.Substring(3);                 // drop the "XY " status prefix

                // With -z, destination comes first and an additional NUL record contains the source.
                if (line[0] is 'R' or 'C' || line[1] is 'R' or 'C') i++;
                if (path.Length == 0) continue;

                string abs = Path.GetFullPath(
                    Path.Combine(top, path.Replace('/', Path.DirectorySeparatorChar)));
                set.Add(abs);
            }
            return set;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private static string? FindGitExecutable()
    {
        // Never let the selected repository (or a relative PATH entry) supply our executable.
        foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string directory = entry.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(directory)) continue;
            string candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? "git.exe" : "git");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string? Run(string executable, string workingDir, IEnumerable<string> args,
        IEnumerable<string> configOverrides, CancellationToken cancellationToken, bool allowNoMatch = false)
    {
        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                // Pin UTF-8 so git's porcelain bytes (core.quotepath=false ⇒ literal UTF-8 paths) decode
                // correctly regardless of the host console codepage. Without this, .NET decodes via
                // Console.OutputEncoding — an OEM page (437/1252) on a stock Windows install — turning a
                // non-ASCII name to mojibake so it never equals FileInfo.FullName and is silently dropped.
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };
            // These command-line settings take precedence over system/global/repository configuration.
            // Clear inherited command-scope configuration so it cannot supersede these protections.
            foreach (string key in startInfo.Environment.Keys.Where(key =>
                key.Equals("GIT_CONFIG", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("GIT_CONFIG_PARAMETERS", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("GIT_CONFIG_COUNT", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("GIT_CONFIG_KEY_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("GIT_CONFIG_VALUE_", StringComparison.OrdinalIgnoreCase)).ToArray())
                startInfo.Environment.Remove(key);
            foreach (string arg in new[] { "--no-pager", "--no-optional-locks", "-c", "core.fsmonitor=",
                "-c", "core.hooksPath=/dev/null", "-c", "hook.post-index-change.enabled=false" }
                .Concat(configOverrides).Concat(args)) startInfo.ArgumentList.Add(arg);
            using var p = Process.Start(startInfo);
            if (p is null) return null;
            // Drain BOTH pipes asynchronously BEFORE waiting. A synchronous ReadToEnd on stdout has no
            // timeout; if git fills its stderr buffer (~4 KB) before closing stdout the two pipes deadlock
            // and the 5 s cap (which only guards WaitForExit) is never reached. Reading concurrently lets
            // the timeout actually bound the read.
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            var elapsed = Stopwatch.StartNew();
            try
            {
                while (!p.WaitForExit(100) || !outTask.IsCompleted || !errTask.IsCompleted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (p.HasExited) Thread.Sleep(20); // A descendant may still hold a redirected pipe.
                    if (elapsed.ElapsedMilliseconds < 5000) continue;
                    try { p.Kill(true); } catch { }
                    return null;
                }
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(true); } catch { }
                throw;
            }
            string output = outTask.GetAwaiter().GetResult();
            errTask.GetAwaiter().GetResult();     // observe/drain stderr
            return p.ExitCode == 0 || (allowNoMatch && p.ExitCode == 1 && output.Length == 0) ? output : null;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }                    // git not installed / spawn failure
    }
}
