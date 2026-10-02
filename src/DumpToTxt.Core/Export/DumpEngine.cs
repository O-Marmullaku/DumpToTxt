using System.Text;
using System.Text.RegularExpressions;

namespace DumpToTxt.Core;

public sealed class DumpResult
{
    /// <summary>Path of the written file, or null when the target was not <see cref="OutputTarget.File"/>.</summary>
    public string? OutputPath { get; init; }
    /// <summary>Bounded text for materialized API and clipboard consumers. Empty for file or streamed stdout output.</summary>
    public string Text { get; init; } = "";
    /// <summary>Compatibility property; streaming file exports do not retain package bytes.</summary>
    public byte[]? BinaryContent { get; init; }
    public int FilesIncluded { get; init; }
    /// <summary>Exact token count of emitted file bodies when <see cref="TokensCounted"/> is true.</summary>
    public long TotalTokens { get; init; }
    public bool TokensCounted { get; init; }
    /// <summary>True when counted body tokens exceed the configured positive token budget.</summary>
    public bool TokenBudgetExceeded { get; init; }
    /// <summary>Total secret findings across the dump (0 when scanning is off / none found).</summary>
    public int SecretFindingCount { get; init; }
    /// <summary>Number of files that carried at least one secret finding.</summary>
    public int FilesWithSecrets { get; init; }
    /// <summary>Number of files whose ENTIRE content was omitted from the output under Skip mode (a
    /// high-confidence secret was present). 0 for the other modes.</summary>
    public int FilesContentOmitted { get; init; }
    /// <summary>True when the pre-output sensitive-data review cancelled the dump.</summary>
    public bool Cancelled { get; init; }
    /// <summary>The action actually used for rendering, including a one-run choice from the review dialog.</summary>
    public SecretScanMode EffectiveSecretScan { get; init; } = SecretScanMode.Warn;
}

/// <summary>
/// Core dumper. Traverses once, snapshots source text to one-run temporary files, checks it,
/// then streams the selected format. Publication follows the sensitive-data decision.
/// </summary>
public sealed class DumpEngine
{
    public const int MaxMaterializedCharacters = 16 * 1024 * 1024;
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
    /// configured target is <see cref="OutputTarget.File"/>; otherwise streams stdout through a supplied writer or returns bounded text
    /// for the caller to deliver. <paramref name="outputDir"/> overrides the config
    /// output dir (defaults to the config value, then the Desktop).
    /// </summary>
    public DumpResult Run(string targetPath, DumpConfig cfg, string? outputDir = null,
        DumpContentSelection? contentSelection = null,
        Func<SensitiveDataReview, SensitiveDataDecision>? reviewSensitiveData = null,
        CancellationToken cancellationToken = default, IProgress<DumpProgress>? progress = null,
        TextWriter? textOutput = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        cfg = cfg.Clone();
        using var staging = new StagedContent();
        if (OutputStyleCatalog.IsWord(cfg.Style) && cfg.OutputTarget != OutputTarget.File)
            throw new InvalidOperationException("Word documents must be saved as a file.");
        targetPath = Path.GetFullPath(targetPath);
        FilesystemSafety.EnsureTargetPath(targetPath);
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
        if (cfg.OutputTarget == OutputTarget.File)
        {
            string dir = outputDir ?? cfg.OutputDir
                ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            outPath = MakeOutputPath(dir, SafeName(baseNameRaw), OutputStyleCatalog.Extension(cfg.Style));
        }

        var model = Gather(targetPath, isFile, root, cfg, outPath, contentSelection,
            staging, cancellationToken, progress);
        var effectiveConfig = cfg;
        if (cfg.SecretScan == SecretScanMode.Warn && reviewSensitiveData is not null)
        {
            var review = SensitiveDataReview.FromModel(model);
            if (review.Items.Count > 0)
            {
                SensitiveDataDecision decision = reviewSensitiveData(review);
                if (decision == SensitiveDataDecision.Cancel)
                    return new DumpResult
                    {
                        Cancelled = true,
                        FilesIncluded = model.Files.Count,
                        SecretFindingCount = model.SecretFindingCount,
                        FilesWithSecrets = model.FilesWithSecrets,
                        EffectiveSecretScan = cfg.SecretScan,
                    };
                if (decision == SensitiveDataDecision.Redact)
                {
                    effectiveConfig = cfg.Clone();
                    effectiveConfig.SecretScan = SecretScanMode.Redact;
                }
            }
        }

        PrepareBodies(model, effectiveConfig, staging, cancellationToken, progress);
        string text = "";
        progress?.Report(new("Rendering", null, model.Files.Count, model.TotalSize));
        if (outPath is not null)
            outPath = Publish(model, effectiveConfig, outPath, cancellationToken);
        else if (textOutput is not null && cfg.OutputTarget == OutputTarget.Stdout)
            SelectFormatter(effectiveConfig.Style).Write(model, effectiveConfig,
                new CancellationTextWriter(textOutput, cancellationToken));
        else
        {
            using var writer = new BoundedTextWriter(MaxMaterializedCharacters);
            SelectFormatter(effectiveConfig.Style).Write(model, effectiveConfig,
                new CancellationTextWriter(writer, cancellationToken));
            text = writer.ToString();
        }

        // How many files had their whole content omitted (Skip + a high-confidence secret) — surfaced in the
        // post-dump notice so the loss isn't silent.
        int filesContentOmitted = 0;
        if (effectiveConfig.SecretScan == SecretScanMode.Skip)
            foreach (var f in model.Files)
                if (SecretScanner.OmitsWholeFile(f, effectiveConfig.SecretScan)) filesContentOmitted++;

        return new DumpResult
        {
            OutputPath = outPath,
            Text = text,
            FilesIncluded = model.Files.Count,
            TotalTokens = model.TotalTokens,
            TokensCounted = effectiveConfig.Style != OutputStyle.Classic || effectiveConfig.MaxTokens > 0,
            TokenBudgetExceeded = effectiveConfig.MaxTokens > 0 && model.TotalTokens > effectiveConfig.MaxTokens,
            SecretFindingCount = model.SecretFindingCount,
            FilesWithSecrets = model.FilesWithSecrets,
            FilesContentOmitted = filesContentOmitted,
            EffectiveSecretScan = effectiveConfig.SecretScan,
        };
    }

    private static void PrepareBodies(DumpModel model, DumpConfig cfg, StagedContent staging,
        CancellationToken cancellationToken, IProgress<DumpProgress>? progress)
    {
        int processed = 0;
        foreach (var file in model.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            file.ContentOmitted = SecretScanner.OmitsWholeFile(file, cfg.SecretScan);
            string output = staging.NewPath();
            using (var writer = StagedContent.CreateWriter(output))
            {
                if (!file.IsBinary && !file.ContentOmitted)
                {
                    using var reader = StagedContent.OpenReader(file.SourceContentPath!);
                    TextWriter canonical = cfg.Style is OutputStyle.Xml or OutputStyle.XmlCompact or OutputStyle.Docx
                        ? new XmlSafeWriter(writer) : writer;
                    var findings = cfg.SecretScan is SecretScanMode.Warn or SecretScanMode.Off
                        ? file.Secrets.Where(s => s.AlwaysRedact).ToArray() : file.Secrets;
                    SecretScanner.WriteRedacted(reader, canonical, findings, cancellationToken);
                }
            }
            file.OutputContentPath = output;
            using (var reader = StagedContent.OpenReader(output))
            {
                var buffer = new char[32768];
                int run = 0, count;
                bool needsFence = cfg.Style is OutputStyle.Markdown or OutputStyle.MarkdownAi or OutputStyle.MarkdownCompact;
                while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    file.LastCharacter = buffer[count - 1];
                    if (needsFence) foreach (char c in buffer.AsSpan(0, count))
                    {
                        run = c == '`' ? run + 1 : 0;
                        file.BacktickRun = Math.Max(file.BacktickRun, run);
                    }
                    if (file.BacktickRun > 1_000_000)
                        throw new InvalidOperationException("A Markdown fence would exceed 1,000,000 characters. No output was published.");
                }
            }
            if (cfg.Style != OutputStyle.Classic || cfg.MaxTokens > 0)
            {
                using var reader = StagedContent.OpenReader(output);
                file.TokenCount = TokenCounter.Count(reader, cfg.TokenEncoding, cancellationToken);
            }
            progress?.Report(new("Preparing output", file.RelativePath, ++processed, file.Size));
        }
    }

    private static IDumpFormatter SelectFormatter(OutputStyle style) =>
        Formatters.TryGetValue(style, out var f) ? f : Formatters[OutputStyle.Classic];

    private static DumpModel Gather(string targetPath, bool isFile, string root, DumpConfig cfg,
        string? outputPath, DumpContentSelection? contentSelection, StagedContent staging,
        CancellationToken cancellationToken, IProgress<DumpProgress>? progress)
    {
        var matcher = IgnoreMatcher.Build(root, cfg);
        var extSet = new HashSet<string>(
            cfg.ExtSet.Select(e => e.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
        var dotAllow = new HashSet<string>(cfg.DotFilesAllow, StringComparer.OrdinalIgnoreCase);

        var entries = new List<ListedEntry>();
        var candidates = new List<FileInfo>();

        // ONE traversal: ignored directories are pruned (never descended), and the same pass collects
        // the full surviving structure (entries) and the content-eligible files (candidates).
        progress?.Report(new("Discovering", root, 0, 0));
        if (isFile)
        {
            string relative = Path.GetRelativePath(root, targetPath);
            if (!matcher.IsExcluded(targetPath, relative, false))
                entries.Add(new ListedEntry { FullName = targetPath, RelativePath = relative, IsDirectory = false });
        }
        else Walk(root, matcher, extSet, dotAllow, outputPath, entries, candidates, contentSelection, cancellationToken);

        entries.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));
        candidates.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));

        // "Changed files" mode (git): keep only files changed since HEAD, plus their ancestor dirs in the
        // listing. Outside a git repo / no git on PATH the set is null → fall through to a normal dump.
        HashSet<string>? changed = cfg.OnlyGitChanged ? GitChanges.ChangedFiles(targetPath, cancellationToken) : null;
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
        var sensitiveValues = SecretScanner.CompilePatterns(cfg.SensitiveValuePatterns);
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
                files.Add(ReadDumpFile(fi, root, cfg, ref budget, allowlist, sensitiveValues, contentSelection is not null, staging, cancellationToken));
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
            int findingCount = 0;
            foreach (var fi in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new("Reading and checking", Path.GetRelativePath(root, fi.FullName), files.Count, 0));
                var file = ReadDumpFile(fi, root, cfg, ref budget, allowlist, sensitiveValues, contentSelection is not null, staging, cancellationToken);
                findingCount = checked(findingCount + file.Secrets.Count);
                if (findingCount > 100_000)
                    throw new InvalidOperationException("This dump exceeds the supported limit of 100,000 sensitive findings across all files. No output was published. Select fewer files and export them separately.");
                files.Add(file);
            }
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
        HashSet<string> dotAllow, string? outputPath, List<ListedEntry> entries, List<FileInfo> candidates,
        DumpContentSelection? contentSelection, CancellationToken cancellationToken)
    {
        foreach (var entry in FileDiscovery.Enumerate(root, matcher, cancellationToken))
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
                && !string.Equals(fi.FullName, outputPath, StringComparison.OrdinalIgnoreCase)
                && matcher.MatchesInclude(entry.RelativePath)) candidates.Add(fi);
        }
    }

    /// <summary>Reads a candidate file applying binary detection and per-file + total size caps.</summary>
    private static DumpFile ReadDumpFile(FileInfo fi, string root, DumpConfig cfg, ref long totalBudget,
        IReadOnlyList<System.Text.RegularExpressions.Regex>? secretAllowlist,
        IReadOnlyList<System.Text.RegularExpressions.Regex> sensitiveValues, bool requireTextLike,
        StagedContent staging, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string rel = Path.GetRelativePath(root, fi.FullName);
        TextFileClassifier.EnsureRegularFile(fi.FullName);
        long size = fi.Length;

        if (requireTextLike ? !TextFileClassifier.IsTextLike(fi.FullName, cancellationToken)
            : cfg.DetectBinary && TextFileClassifier.IsBinaryForDump(fi.FullName, cancellationToken))
            return new DumpFile { FullName = fi.FullName, RelativePath = rel, Content = "", Size = size, IsBinary = true };

        long perFile = cfg.MaxFileSizeBytes > 0 ? cfg.MaxFileSizeBytes : long.MaxValue;
        long allowed = Math.Min(perFile, totalBudget);

        bool scan = cfg.SecretScan != SecretScanMode.Off || sensitiveValues.Count > 0;
        var snapshot = staging.Snapshot(fi.FullName, allowed, scan, cancellationToken);
        if (totalBudget != long.MaxValue) totalBudget -= snapshot.Consumed;
        var secrets = new List<SecretFinding>();
        if (scan && snapshot.Characters > 0)
        {
            var seen = new HashSet<(int Start, int Length, string Rule, bool Required)>();
            string[] scanPaths = snapshot.ScanPath == snapshot.Path ? [snapshot.Path] : [snapshot.ScanPath, snapshot.Path];
            foreach (string scanPath in scanPaths)
            {
                using var reader = StagedContent.OpenReader(scanPath);
                long offset = 0;
                int lineOffset = 0;
                foreach (string region in ProcessingRegions.Read(reader, automaticSecrets: true,
                             wholeFileRequired: sensitiveValues.Count > 0, cancellationToken))
                {
                    var findings = SecretScanner.Scan(region, cfg.SecretScanEntropy, secretAllowlist,
                        sensitiveValues, includeAutomatic: cfg.SecretScan != SecretScanMode.Off);
                    foreach (var finding in findings)
                    {
                        long start = offset + finding.Start;
                        if (start >= snapshot.Characters) continue;
                        int length = (int)Math.Min(finding.Length, snapshot.Characters - start);
                        if (!seen.Add((checked((int)start), length, finding.RuleId, finding.AlwaysRedact))) continue;
                        secrets.Add(new SecretFinding
                        {
                            RuleId = finding.RuleId, RuleName = finding.RuleName,
                            Start = checked((int)start),
                            Length = length,
                            Line = checked(lineOffset + finding.Line), Preview = finding.Preview,
                            AlwaysRedact = finding.AlwaysRedact,
                        });
                        if (secrets.Count > 100_000)
                            throw new InvalidOperationException($"More than 100,000 sensitive findings in {rel}; no output was published.");
                    }
                    for (int i = 0; i < region.Length; i++)
                        if (region[i] == '\r' || region[i] == '\n' && (i == 0 || region[i - 1] != '\r')) lineOffset++;
                    offset += region.Length;
                }
                }
            secrets = secrets.OrderBy(s => s.Start).ThenByDescending(s => s.Length).ToList();
        }
        return new DumpFile
        {
            FullName = fi.FullName, RelativePath = rel, Content = "", SourceContentPath = snapshot.Path,
            Size = snapshot.Size, IsTruncated = snapshot.Truncated, Secrets = secrets,
        };
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

    private static string Publish(DumpModel model, DumpConfig cfg, string proposed,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetFullPath(Path.GetDirectoryName(proposed)!);
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, ".dumptotxt-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536))
            {
                if (cfg.Style == OutputStyle.Docx) DocxFormatter.WritePackage(model, cfg, stream, cancellationToken);
                else
                {
                    using var writer = new StreamWriter(stream, new UTF8Encoding(true), 65536, leaveOpen: true);
                    SelectFormatter(cfg.Style).Write(model, cfg, new CancellationTextWriter(writer, cancellationToken));
                    writer.Flush();
                }
                stream.Flush(flushToDisk: true);
            }
            string candidate = proposed;
            for (int suffix = 1; ; suffix++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { File.Move(temporary, candidate, overwrite: false); return candidate; }
                catch (IOException) when (File.Exists(candidate))
                {
                    candidate = Path.Combine(directory, Path.GetFileNameWithoutExtension(proposed)
                        + "-" + suffix.ToString("D2") + Path.GetExtension(proposed));
                }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Sanitizes a name for use in the output filename (mirrors legacy SafeName).</summary>
    public static string SafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "dump";
        var cleaned = Regex.Replace(name, "[\\\\/:*?\"<>|]+", "_");
        return string.IsNullOrWhiteSpace(cleaned) ? "dump" : cleaned;
    }
}
