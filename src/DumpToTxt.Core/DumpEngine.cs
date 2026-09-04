using System.Text;
using System.Text.RegularExpressions;

namespace DumpToTxt.Core;

public sealed class DumpResult
{
    /// <summary>Path of the written file, or null when the target was not <see cref="OutputTarget.File"/>.</summary>
    public string? OutputPath { get; init; }
    /// <summary>The rendered dump text (no BOM), regardless of output target.</summary>
    public string Text { get; init; } = "";
    /// <summary>Binary output for package formats such as Word.</summary>
    public byte[]? BinaryContent { get; init; }
    public int FilesIncluded { get; init; }
    /// <summary>Total secret findings across the dump (0 when scanning is off / none found).</summary>
    public int SecretFindingCount { get; init; }
    /// <summary>Number of files that carried at least one secret finding.</summary>
    public int FilesWithSecrets { get; init; }
    /// <summary>Number of files whose ENTIRE content was omitted from the output under Skip mode (a
    /// high-confidence secret was present). 0 for Classic and for the other modes.</summary>
    public int FilesContentOmitted { get; init; }
}

/// <summary>
/// Core dumper. Walks the target once into a <see cref="DumpModel"/> (a single pruning traversal
/// that feeds both the directory listing and the file contents), then hands it to the
/// <see cref="IDumpFormatter"/> for the configured <see cref="OutputStyle"/>. The Classic style
/// reproduces the legacy .txt layout; the others are repomix-inspired.
/// </summary>
public sealed class DumpEngine
{
    private static readonly IReadOnlyDictionary<OutputStyle, IDumpFormatter> Formatters =
        new Dictionary<OutputStyle, IDumpFormatter>
        {
            [OutputStyle.Classic] = new ClassicFormatter(),
            [OutputStyle.Plain] = new PlainFormatter(),
            [OutputStyle.Markdown] = new MarkdownFormatter(),
            [OutputStyle.MarkdownAi] = new MarkdownFormatter(OutputStyle.MarkdownAi),
            [OutputStyle.MarkdownCompact] = new MarkdownFormatter(OutputStyle.MarkdownCompact),
            [OutputStyle.Xml] = new XmlFormatter(),
            [OutputStyle.XmlCompact] = new XmlFormatter(OutputStyle.XmlCompact),
            [OutputStyle.Json] = new JsonFormatter(),
            [OutputStyle.JsonCompact] = new JsonFormatter(OutputStyle.JsonCompact),
        };

    /// <summary>
    /// Runs a dump for <paramref name="targetPath"/> (file or folder). Writes a file when the
    /// configured target is <see cref="OutputTarget.File"/>; otherwise just returns the text for
    /// the caller to route (clipboard/stdout). <paramref name="outputDir"/> overrides the config
    /// output dir (defaults to the config value, then the Desktop).
    /// </summary>
    public DumpResult Run(string targetPath, DumpConfig cfg, string? outputDir = null,
        DumpContentSelection? contentSelection = null)
    {
        if (OutputStyleCatalog.IsWord(cfg.Style) && cfg.OutputTarget != OutputTarget.File)
            throw new InvalidOperationException("Word documents must be saved as a file.");
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
            outPath = MakeOutputPath(dir, SafeName(baseNameRaw), OutputStyleCatalog.Extension(cfg.Style));
            outNameOnly = Path.GetFileName(outPath);
        }

        var model = Gather(targetPath, isFile, root, cfg, outNameOnly, contentSelection);
        byte[]? binary = null;
        string text;
        if (cfg.Style == OutputStyle.Docx)
        {
            binary = DocxFormatter.RenderPackage(model, cfg);
            text = "";
        }
        else text = SelectFormatter(cfg.Style).Render(model, cfg);

        if (outPath != null)
        {
            if (binary is not null) File.WriteAllBytes(outPath, binary);
            else File.WriteAllText(outPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        // How many files had their whole content omitted (Skip + a high-confidence secret) — surfaced in the
        // post-dump notice so the loss isn't silent. Never happens for Classic (it doesn't sanitize).
        int filesContentOmitted = 0;
        if (cfg.Style != OutputStyle.Classic && cfg.SecretScan == SecretScanMode.Skip)
            foreach (var f in model.Files)
                if (SecretScanner.OmitsWholeFile(f, cfg.SecretScan)) filesContentOmitted++;

        return new DumpResult
        {
            OutputPath = outPath,
            Text = text,
            BinaryContent = binary,
            FilesIncluded = model.Files.Count,
            SecretFindingCount = model.SecretFindingCount,
            FilesWithSecrets = model.FilesWithSecrets,
            FilesContentOmitted = filesContentOmitted,
        };
    }

    private static IDumpFormatter SelectFormatter(OutputStyle style) =>
        Formatters.TryGetValue(style, out var f) ? f : Formatters[OutputStyle.Classic];

    private static DumpModel Gather(string targetPath, bool isFile, string root, DumpConfig cfg,
        string outNameOnly, DumpContentSelection? contentSelection)
    {
        var matcher = IgnoreMatcher.Build(root, cfg);
        var extSet = new HashSet<string>(
            cfg.ExtSet.Select(e => e.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
        var dotAllow = new HashSet<string>(cfg.DotFilesAllow, StringComparer.OrdinalIgnoreCase);

        var entries = new List<ListedEntry>();
        var candidates = new List<FileInfo>();

        // ONE traversal: ignored directories are pruned (never descended), and the same pass collects
        // the full surviving structure (entries) and the content-eligible files (candidates).
        Walk(root, matcher, extSet, dotAllow, outNameOnly, entries, candidates, contentSelection);

        entries.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));
        candidates.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));

        // "Changed files" mode (git): keep only files changed since HEAD, plus their ancestor dirs in the
        // listing. Outside a git repo / no git on PATH the set is null → fall through to a normal dump.
        HashSet<string>? changed = cfg.OnlyGitChanged ? GitChanges.ChangedFiles(targetPath) : null;
        if (changed is not null)
        {
            candidates = candidates.Where(fi => changed.Contains(fi.FullName)).ToList();
            var keepDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // A transient type choice controls CONTENT only. Build its changed-file directory listing
            // from all changed entries; legacy calls keep their candidate-derived behavior byte-for-byte.
            IEnumerable<string> filesForTree = contentSelection is null
                ? candidates.Select(fi => fi.FullName)
                : entries.Where(e => !e.IsDirectory && changed.Contains(e.FullName)).Select(e => e.FullName);
            foreach (string file in filesForTree)
            {
                string? d = Path.GetDirectoryName(file);
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
        // Compile the secret allowlist once for the whole dump (skipped entirely when scanning is off).
        var allowlist = cfg.SecretScan != SecretScanMode.Off
            ? SecretScanner.CompileAllowlist(cfg.SecretAllowlist) : null;
        List<DumpFile> files;
        string? skipped = null;
        bool skippedUnchanged = false;

        if (isFile)
        {
            // Single-file target: content is just that file (legacy parity — the .min filter is not applied here).
            var fi = new FileInfo(targetPath);
            string rel = Path.GetRelativePath(root, fi.FullName);
            files = new List<DumpFile>();
            bool legible = IsLegible(fi, root, extSet, dotAllow, contentSelection)
                && !matcher.IsExcluded(fi.FullName, rel, isDir: false)
                && matcher.MatchesInclude(rel);
            if (legible && (changed is null || changed.Contains(fi.FullName)))
                files.Add(ReadDumpFile(fi, root, cfg, ref budget, allowlist, contentSelection is not null));
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
                files.Add(ReadDumpFile(fi, root, cfg, ref budget, allowlist, contentSelection is not null));
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
        HashSet<string> dotAllow, string outNameOnly, List<ListedEntry> entries, List<FileInfo> candidates,
        DumpContentSelection? contentSelection)
    {
        foreach (var entry in FileDiscovery.Enumerate(root, matcher))
        {
            entries.Add(new ListedEntry
            {
                FullName = entry.FullName,
                RelativePath = entry.RelativePath,
                IsDirectory = entry.IsDirectory,
            });
            if (!entry.IsDirectory && entry.Info is FileInfo fi
                && IsLegible(fi, root, extSet, dotAllow, contentSelection)
                && !Regex.IsMatch(fi.Name, @"\.min\.", RegexOptions.IgnoreCase)
                && !string.Equals(fi.Name, outNameOnly, StringComparison.OrdinalIgnoreCase)
                && matcher.MatchesInclude(entry.RelativePath)) candidates.Add(fi);
        }
    }

    /// <summary>Reads a candidate file applying binary detection and per-file + total size caps.</summary>
    private static DumpFile ReadDumpFile(FileInfo fi, string root, DumpConfig cfg, ref long totalBudget,
        IReadOnlyList<System.Text.RegularExpressions.Regex>? secretAllowlist, bool requireTextLike)
    {
        string rel = Path.GetRelativePath(root, fi.FullName);
        long size;
        try { size = fi.Length; } catch { size = 0; }

        if (requireTextLike ? !TextFileClassifier.IsTextLike(fi.FullName)
            : cfg.DetectBinary && TextFileClassifier.IsBinaryForDump(fi.FullName))
            return new DumpFile { FullName = fi.FullName, RelativePath = rel, Content = "", Size = size, IsBinary = true };

        long perFile = cfg.MaxFileSizeBytes > 0 ? cfg.MaxFileSizeBytes : long.MaxValue;
        long allowed = Math.Min(perFile, totalBudget);

        if (allowed <= 0)
            // Total cap already spent: list the file but skip its content.
            return new DumpFile { FullName = fi.FullName, RelativePath = rel, Content = "", Size = size, IsTruncated = true };

        bool truncated = size > allowed;
        string content = truncated ? SafeReadText(fi.FullName, allowed) : SafeReadText(fi.FullName);
        if (totalBudget != long.MaxValue) totalBudget -= truncated ? allowed : size;

        // Scan the dumped (post-truncation) content for secrets. Runs regardless of style when enabled so the
        // count surfaces in the post-dump notice even for Classic; Classic output itself is never altered
        // (redact/skip is a render-time transform the non-Classic formatters apply — see SecretScanner).
        // Post-truncation scanning under-detects a secret straddling the cap boundary (its clipped prefix can
        // survive Redact) — accepted: a partial token is unusable and PEM keys redact via the END-less fallback.
        var secrets = cfg.SecretScan != SecretScanMode.Off
            ? SecretScanner.Scan(content, cfg.SecretScanEntropy, secretAllowlist)
            : Array.Empty<SecretFinding>();

        // Token count must describe what's actually EMITTED. For non-Classic Redact/Skip the emitted body
        // differs from the source (spans redacted, or the whole file omitted), so count the sanitized body —
        // otherwise the per-file/total/over-budget figures would describe content the dump never contains.
        // Classic never sanitizes; Off/Warn emit as-is. Skipped for Classic-with-no-budget (counts unused there).
        string emitted = (secrets.Count > 0 && cfg.Style != OutputStyle.Classic
            && cfg.SecretScan is SecretScanMode.Redact or SecretScanMode.Skip)
            ? SecretScanner.ContentForOutput(content, secrets, cfg.SecretScan, out _)
            : content;
        int tokens = (cfg.Style != OutputStyle.Classic || cfg.MaxTokens > 0)
            ? TokenCounter.Count(emitted, cfg.TokenEncoding) : 0;

        return new DumpFile { FullName = fi.FullName, RelativePath = rel, Content = content, Size = size, IsTruncated = truncated, TokenCount = tokens, Secrets = secrets };
    }

    private static string MakeOutputPath(string dir, string safeBase, string extension)
    {
        string timeTag = DateTime.Now.ToString("HH-mm");
        string candidate = Path.Combine(dir, $"{safeBase}-dump-{timeTag}{extension}");
        if (!File.Exists(candidate)) return candidate;
        for (int i = 1; ; i++)
        {
            candidate = Path.Combine(dir, $"{safeBase}-dump-{timeTag}-{i:00}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static bool IsLegible(FileInfo fi, string root, HashSet<string> extSet, HashSet<string> dotAllow,
        DumpContentSelection? contentSelection)
    {
        if (contentSelection is not null) return contentSelection.Allows(fi, root);
        var ext = fi.Extension.ToLowerInvariant();
        if (!string.IsNullOrEmpty(ext) && extSet.Contains(ext)) return true;
        if (dotAllow.Contains(fi.Name)) return true;
        return false;
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
