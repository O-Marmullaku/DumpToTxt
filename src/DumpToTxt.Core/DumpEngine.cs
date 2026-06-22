using System.Text;
using System.Text.RegularExpressions;

namespace DumpToTxt.Core;

public sealed class DumpResult
{
    public required string OutputPath { get; init; }
    public int FilesIncluded { get; init; }
}

/// <summary>
/// Core dumper. P1 implements the <see cref="OutputStyle.Classic"/> layout with
/// behavior matching the legacy PowerShell tool. Other styles are stubbed and
/// will be added in later phases.
/// </summary>
public sealed class DumpEngine
{
    private const string Sep = "==============================";

    // Mirror PowerShell's `Get-ChildItem -Recurse -Force -ErrorAction SilentlyContinue`:
    // recurse, include hidden/system, swallow access errors.
    private static readonly EnumerationOptions EnumOpts = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
    };

    /// <summary>
    /// Runs a dump for <paramref name="targetPath"/> (a file or folder) and writes the result.
    /// </summary>
    /// <param name="outputDir">Where to write the dump. Defaults to the Desktop (legacy behavior).</param>
    public DumpResult Run(string targetPath, DumpConfig cfg, string? outputDir = null)
    {
        targetPath = Path.GetFullPath(targetPath);
        bool isFile = File.Exists(targetPath);
        bool isDir = Directory.Exists(targetPath);
        if (!isFile && !isDir)
            throw new FileNotFoundException("Target path is missing or does not exist.", targetPath);

        string root;
        string baseNameRaw;
        if (isFile)
        {
            root = Path.GetDirectoryName(targetPath) ?? targetPath;
            baseNameRaw = Path.GetFileNameWithoutExtension(targetPath);
        }
        else
        {
            root = targetPath;
            baseNameRaw = new DirectoryInfo(targetPath).Name;
            if (string.IsNullOrWhiteSpace(baseNameRaw)) baseNameRaw = "folder";
        }

        outputDir ??= Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Directory.CreateDirectory(outputDir);
        string outPath = MakeOutputPath(outputDir, SafeName(baseNameRaw));

        if (cfg.Style != OutputStyle.Classic)
            throw new NotSupportedException($"Output style '{cfg.Style}' is not implemented yet (P2).");

        var (text, count) = BuildClassic(targetPath, isFile, root, cfg, Path.GetFileName(outPath));
        File.WriteAllText(outPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return new DumpResult { OutputPath = outPath, FilesIncluded = count };
    }

    private static string MakeOutputPath(string dir, string safeBase)
    {
        string timeTag = DateTime.Now.ToString("HH-mm");
        string candidate = Path.Combine(dir, $"{safeBase}-dump-{timeTag}.txt");
        if (!File.Exists(candidate)) return candidate;
        for (int i = 1; ; i++)
        {
            candidate = Path.Combine(dir, $"{safeBase}-dump-{timeTag}-{i:00}.txt");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static (string text, int count) BuildClassic(
        string targetPath, bool isFile, string root, DumpConfig cfg, string outNameOnly)
    {
        var rx = new Regex(cfg.ExcludeRegex, RegexOptions.IgnoreCase);
        var extSet = new HashSet<string>(
            cfg.ExtSet.Select(e => e.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
        var dotAllow = new HashSet<string>(cfg.DotFilesAllow, StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.Append("===== DIRECTORY LIST (filtered) =====\r\n");
        sb.Append("ROOT: ").Append(root).Append("\r\n");
        sb.Append("\r\n");

        // Directory listing: every entry (files + folders) under root, minus excluded paths.
        foreach (var entry in SafeEnumerate(root, filesOnly: false))
        {
            if (rx.IsMatch(entry)) continue;
            sb.Append(entry).Append("\r\n");
        }

        sb.Append("\r\n\r\n===== FILE CONTENTS (LEGIBLE ONLY) =====\r\n");

        int count = 0;
        if (isFile)
        {
            var fi = new FileInfo(targetPath);
            if (IsLegible(fi, extSet, dotAllow) && !rx.IsMatch(fi.FullName))
            {
                AppendFileBlock(sb, fi.FullName);
                count++;
            }
            else
            {
                sb.Append("\r\n[Skipped: file not considered legible or is excluded]\r\n");
                sb.Append(fi.FullName).Append("\r\n");
            }
        }
        else
        {
            var files = SafeEnumerate(root, filesOnly: true)
                .Select(p => new FileInfo(p))
                .Where(f => !rx.IsMatch(f.FullName)
                            && !Regex.IsMatch(f.Name, @"\.min\.", RegexOptions.IgnoreCase)
                            && !string.Equals(f.Name, outNameOnly, StringComparison.OrdinalIgnoreCase)
                            && IsLegible(f, extSet, dotAllow))
                .OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase);

            foreach (var f in files)
            {
                AppendFileBlock(sb, f.FullName);
                count++;
            }
        }

        return (sb.ToString(), count);
    }

    private static void AppendFileBlock(StringBuilder sb, string fullName)
    {
        sb.Append("\r\n").Append(Sep).Append("\r\n");
        sb.Append(fullName).Append("\r\n");
        sb.Append(Sep).Append("\r\n");
        string content = SafeReadText(fullName);
        sb.Append(content);
        if (!content.EndsWith('\n')) sb.Append("\r\n");
    }

    private static bool IsLegible(FileInfo fi, HashSet<string> extSet, HashSet<string> dotAllow)
    {
        var ext = fi.Extension.ToLowerInvariant();
        if (!string.IsNullOrEmpty(ext) && extSet.Contains(ext)) return true;
        if (dotAllow.Contains(fi.Name)) return true;
        return false;
    }

    private static IEnumerable<string> SafeEnumerate(string root, bool filesOnly)
    {
        try
        {
            return filesOnly
                ? Directory.EnumerateFiles(root, "*", EnumOpts)
                : Directory.EnumerateFileSystemEntries(root, "*", EnumOpts);
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    private static string SafeReadText(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return "[unreadable]"; }
    }

    /// <summary>Sanitizes a name for use in the output filename (mirrors legacy SafeName).</summary>
    public static string SafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "dump";
        var cleaned = Regex.Replace(name, "[\\\\/:*?\"<>|]+", "_");
        return string.IsNullOrWhiteSpace(cleaned) ? "dump" : cleaned;
    }
}
