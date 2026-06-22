using System.Diagnostics;

namespace DumpToTxt.Core;

/// <summary>
/// Lists the files changed vs the last commit, using the <c>git</c> CLI. Returns the set of absolute
/// paths considered "changed" (staged + unstaged + untracked), or <c>null</c> when the target is not
/// inside a git repository or git is unavailable — in which case the caller falls back to a full dump.
/// </summary>
public static class GitChanges
{
    /// <summary>Absolute paths changed since HEAD, or null when not a git repo / git missing.</summary>
    public static HashSet<string>? ChangedFiles(string targetPathOrDir)
    {
        try
        {
            string dir = File.Exists(targetPathOrDir)
                ? Path.GetDirectoryName(Path.GetFullPath(targetPathOrDir))!
                : Path.GetFullPath(targetPathOrDir);

            string? top = Run(dir, "rev-parse --show-toplevel");
            if (string.IsNullOrWhiteSpace(top)) return null;     // not a git repo, or git not on PATH
            top = top.Trim();

            // Porcelain v1: 2 status chars + space + path, stable across git versions and locales.
            // core.quotepath=false keeps non-ASCII paths literal (UTF-8) instead of C-quoted/escaped,
            // so they still match FileInfo.FullName.
            string? status = Run(dir, "-c core.quotepath=false status --porcelain --untracked-files=all");
            if (status is null) return null;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in status.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length < 4) continue;
                string path = line.Substring(3);                 // drop the "XY " status prefix

                // Renames/copies render as "old -> new"; keep the new path.
                int arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrow >= 0) path = path.Substring(arrow + 4);

                path = path.Trim().Trim('"');                    // git quotes paths with odd chars
                if (path.Length == 0) continue;

                string abs = Path.GetFullPath(
                    Path.Combine(top, path.Replace('/', Path.DirectorySeparatorChar)));
                set.Add(abs);
            }
            return set;
        }
        catch { return null; }
    }

    private static string? Run(string workingDir, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("git", args)
            {
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null) return null;
            string output = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();          // drain so the child never blocks on a full pipe
            if (!p.WaitForExit(5000)) { try { p.Kill(true); } catch { } return null; }
            return p.ExitCode == 0 ? output : null;
        }
        catch { return null; }                    // git not installed / spawn failure
    }
}
