using System.Text;
using System.Text.RegularExpressions;

namespace DumpToTxt.Core;

public sealed class DumpResult
{
    /// <summary>Path of the written file, or null when the target was not <see cref="OutputTarget.File"/>.</summary>
    public string? OutputPath { get; init; }
    /// <summary>The rendered dump text (no BOM), regardless of output target.</summary>
    public string Text { get; init; } = "";
    public int FilesIncluded { get; init; }
}

/// <summary>
/// Core dumper. Walks the target once into a <see cref="DumpModel"/> (a single pruning traversal
/// that feeds both the directory listing and the file contents), then hands it to the
/// <see cref="IDumpFormatter"/> for the configured <see cref="OutputStyle"/>. The Classic style
/// reproduces the legacy .txt layout; the others are repomix-inspired.
/// </summary>
public sealed class DumpEngine
{
    // Single-directory listing; recursion is driven manually so ignored directories are pruned.
    private static readonly EnumerationOptions TopLevel = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
    };

    private const int BinarySniffBytes = 8000;

    private static readonly IReadOnlyDictionary<OutputStyle, IDumpFormatter> Formatters =
        new Dictionary<OutputStyle, IDumpFormatter>
        {
            [OutputStyle.Classic] = new ClassicFormatter(),
            [OutputStyle.Plain] = new PlainFormatter(),
            [OutputStyle.Markdown] = new MarkdownFormatter(),
            [OutputStyle.Xml] = new XmlFormatter(),
            [OutputStyle.Json] = new JsonFormatter(),
        };

    /// <summary>
    /// Runs a dump for <paramref name="targetPath"/> (file or folder). Writes a file when the
    /// configured target is <see cref="OutputTarget.File"/>; otherwise just returns the text for
    /// the caller to route (clipboard/stdout). <paramref name="outputDir"/> overrides the config
    /// output dir (defaults to the config value, then the Desktop).
    /// </summary>
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

        // Resolve the output file path up front (File target only) so the dump can exclude itself.
        string? outPath = null;
        string outNameOnly = "\0";   // sentinel that can never equal a real file name
        if (cfg.OutputTarget == OutputTarget.File)
        {
            string dir = outputDir ?? cfg.OutputDir
                ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            Directory.CreateDirectory(dir);
            outPath = MakeOutputPath(dir, SafeName(baseNameRaw));
            outNameOnly = Path.GetFileName(outPath);
        }

        var model = Gather(targetPath, isFile, root, cfg, outNameOnly);
        string text = SelectFormatter(cfg.Style).Render(model, cfg);

        if (outPath != null)
            File.WriteAllText(outPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        return new DumpResult { OutputPath = outPath, Text = text, FilesIncluded = model.Files.Count };
    }

    private static IDumpFormatter SelectFormatter(OutputStyle style) =>
        Formatters.TryGetValue(style, out var f) ? f : Formatters[OutputStyle.Classic];

    private static DumpModel Gather(string targetPath, bool isFile, string root, DumpConfig cfg, string outNameOnly)
    {
        var matcher = IgnoreMatcher.Build(root, cfg);
        var extSet = new HashSet<string>(
            cfg.ExtSet.Select(e => e.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
        var dotAllow = new HashSet<string>(cfg.DotFilesAllow, StringComparer.OrdinalIgnoreCase);

        var entries = new List<ListedEntry>();
        var candidates = new List<FileInfo>();

        // ONE traversal: ignored directories are pruned (never descended), and the same pass collects
        // the full surviving structure (entries) and the content-eligible files (candidates).
        Walk(root, matcher, extSet, dotAllow, outNameOnly, entries, candidates);

        entries.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));
        candidates.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));

        // "Changed files" mode (git): keep only files changed since HEAD, plus their ancestor dirs in the
        // listing. Outside a git repo / no git on PATH the set is null → fall through to a normal dump.
        HashSet<string>? changed = cfg.OnlyGitChanged ? GitChanges.ChangedFiles(targetPath) : null;
        if (changed is not null)
        {
            candidates = candidates.Where(fi => changed.Contains(fi.FullName)).ToList();
            var keepDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fi in candidates)
            {
                string? d = Path.GetDirectoryName(fi.FullName);
                while (!string.IsNullOrEmpty(d))
                {
                    keepDirs.Add(d!);
                    if (string.Equals(d, root, StringComparison.OrdinalIgnoreCase)) break;
                    d = Path.GetDirectoryName(d);
                }
            }
            entries = entries.Where(e =>
                e.IsDirectory ? keepDirs.Contains(e.FullName) : changed.Contains(e.FullName)).ToList();
        }

        long budget = cfg.MaxTotalSizeBytes > 0 ? cfg.MaxTotalSizeBytes : long.MaxValue;
        List<DumpFile> files;
        string? skipped = null;
        bool skippedUnchanged = false;

        if (isFile)
        {
            // Single-file target: content is just that file (legacy parity — the .min filter is not applied here).
            var fi = new FileInfo(targetPath);
            string rel = Path.GetRelativePath(root, fi.FullName);
            files = new List<DumpFile>();
            bool legible = IsLegible(fi, extSet, dotAllow)
                && !matcher.IsExcluded(fi.FullName, rel, isDir: false)
                && matcher.MatchesInclude(rel);
            if (legible && (changed is null || changed.Contains(fi.FullName)))
                files.Add(ReadDumpFile(fi, root, cfg, ref budget));
            else if (legible)                       // filtered out only because it has no git changes
            {
                skipped = fi.FullName;
                skippedUnchanged = true;
            }
            else
                skipped = fi.FullName;
        }
        else
        {
            files = new List<DumpFile>(candidates.Count);
            foreach (var fi in candidates)
                files.Add(ReadDumpFile(fi, root, cfg, ref budget));
        }

        return new DumpModel
        {
            Root = root,
            TargetPath = targetPath,
            IsSingleFile = isFile,
            Entries = entries,
            Files = files,
            SkippedSingleFile = skipped,
            SingleFileSkippedUnchanged = skippedUnchanged,
        };
    }

    /// <summary>Manual recursive walk that prunes ignored directories and collects entries + content candidates.</summary>
    private static void Walk(string root, IgnoreMatcher matcher, HashSet<string> extSet,
        HashSet<string> dotAllow, string outNameOnly, List<ListedEntry> entries, List<FileInfo> candidates)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            string dir = stack.Pop();
            IEnumerable<FileSystemInfo> children;
            try { children = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", TopLevel); }
            catch { continue; } // inaccessible directory: skip

            foreach (var info in children)
            {
                bool entryIsDir;
                try { entryIsDir = (info.Attributes & FileAttributes.Directory) != 0; }
                catch { continue; }

                string full = info.FullName;
                string rel = Path.GetRelativePath(root, full);

                // Excluded: dropped from BOTH the listing and the content. A directory match prunes its subtree.
                if (matcher.IsExcluded(full, rel, entryIsDir)) continue;

                entries.Add(new ListedEntry { FullName = full, RelativePath = rel, IsDirectory = entryIsDir });

                if (entryIsDir)
                {
                    stack.Push(full);
                }
                else if (info is FileInfo fi
                         && IsLegible(fi, extSet, dotAllow)
                         && !Regex.IsMatch(fi.Name, @"\.min\.", RegexOptions.IgnoreCase)
                         && !string.Equals(fi.Name, outNameOnly, StringComparison.OrdinalIgnoreCase)
                         && matcher.MatchesInclude(rel))
                {
                    candidates.Add(fi);
                }
            }
        }
    }

    /// <summary>Reads a candidate file applying binary detection and per-file + total size caps.</summary>
    private static DumpFile ReadDumpFile(FileInfo fi, string root, DumpConfig cfg, ref long totalBudget)
    {
        string rel = Path.GetRelativePath(root, fi.FullName);
        long size;
        try { size = fi.Length; } catch { size = 0; }

        if (cfg.DetectBinary && IsBinary(fi.FullName))
            return new DumpFile { FullName = fi.FullName, RelativePath = rel, Content = "", Size = size, IsBinary = true };

        long perFile = cfg.MaxFileSizeBytes > 0 ? cfg.MaxFileSizeBytes : long.MaxValue;
        long allowed = Math.Min(perFile, totalBudget);

        if (allowed <= 0)
            // Total cap already spent: list the file but skip its content.
            return new DumpFile { FullName = fi.FullName, RelativePath = rel, Content = "", Size = size, IsTruncated = true };

        bool truncated = size > allowed;
        string content = truncated ? SafeReadText(fi.FullName, allowed) : SafeReadText(fi.FullName);
        if (totalBudget != long.MaxValue) totalBudget -= truncated ? allowed : size;

        return new DumpFile { FullName = fi.FullName, RelativePath = rel, Content = content, Size = size, IsTruncated = truncated };
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

    private static bool IsLegible(FileInfo fi, HashSet<string> extSet, HashSet<string> dotAllow)
    {
        var ext = fi.Extension.ToLowerInvariant();
        if (!string.IsNullOrEmpty(ext) && extSet.Contains(ext)) return true;
        if (dotAllow.Contains(fi.Name)) return true;
        return false;
    }

    /// <summary>NUL-byte sniff over the file head. UTF-16/32 BOM ⇒ treat as text.</summary>
    private static bool IsBinary(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            int n = (int)Math.Min(fs.Length, BinarySniffBytes);
            if (n == 0) return false;
            var buf = new byte[n];
            int read = fs.Read(buf, 0, n);
            if (read >= 2 && ((buf[0] == 0xFF && buf[1] == 0xFE) || (buf[0] == 0xFE && buf[1] == 0xFF)))
                return false; // UTF-16 BOM — its NULs are expected
            for (int i = 0; i < read; i++) if (buf[i] == 0) return true;
            return false;
        }
        catch { return false; } // unreadable: let SafeReadText surface it
    }

    /// <summary>Full read, preserving the original encoding/BOM detection (byte-identical to v2-P1).</summary>
    private static string SafeReadText(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return "[unreadable]"; }
    }

    /// <summary>Capped read of the first <paramref name="maxBytes"/> bytes. Decodes BOM-aware (UTF-8/16/32),
    /// mirroring <see cref="File.ReadAllText(string)"/> so truncated content decodes the same way the full
    /// read would — a UTF-16/32 file no longer turns to mojibake when a size cap clips it. A multibyte
    /// sequence split at the byte boundary degrades to a single replacement char (lenient decode).</summary>
    private static string SafeReadText(string path, long maxBytes)
    {
        try
        {
            // Clamp to the max array length so a multi-GB cap can't request a >2 GB byte[] (deterministic
            // OutOfMemoryException). File.ReadAllText (the uncapped path) has the same ~2 GB string ceiling.
            int cap = (int)Math.Min(maxBytes, Array.MaxLength);
            using var fs = File.OpenRead(path);
            int n = (int)Math.Min(fs.Length, cap);
            if (n == 0) return "";
            var buf = new byte[n];
            // Stream.Read may return fewer bytes than requested; loop until the buffer is full (or EOF) so
            // the decoded content isn't silently short and the budget debit (by `allowed`) stays accurate.
            int read = 0, r;
            while (read < n && (r = fs.Read(buf, read, n - read)) > 0) read += r;
            using var ms = new MemoryStream(buf, 0, read);
            using var sr = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return sr.ReadToEnd();
        }
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
