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
/// Core dumper. Walks the target once into a <see cref="DumpModel"/>, then hands it to the
/// <see cref="IDumpFormatter"/> for the configured <see cref="OutputStyle"/>. The Classic
/// style reproduces the legacy .txt layout; the others are repomix-inspired.
/// </summary>
public sealed class DumpEngine
{
    // Recurse, include hidden/system, swallow access errors (matches the legacy -Recurse -Force).
    private static readonly EnumerationOptions EnumOpts = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
    };

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
        var rx = new Regex(cfg.ExcludeRegex, RegexOptions.IgnoreCase);
        var extSet = new HashSet<string>(
            cfg.ExtSet.Select(e => e.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
        var dotAllow = new HashSet<string>(cfg.DotFilesAllow, StringComparer.OrdinalIgnoreCase);

        // DIRECTORY LIST: every non-excluded entry, sorted by full path for cross-machine determinism.
        var listed = SafeEnumerate(root, filesOnly: false)
            .Where(p => !rx.IsMatch(p))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var files = new List<DumpFile>();
        string? skipped = null;

        if (isFile)
        {
            var fi = new FileInfo(targetPath);
            if (IsLegible(fi, extSet, dotAllow) && !rx.IsMatch(fi.FullName))
                files.Add(ToDumpFile(fi, root));
            else
                skipped = fi.FullName;
        }
        else
        {
            files = SafeEnumerate(root, filesOnly: true)
                .Select(p => new FileInfo(p))
                .Where(f => !rx.IsMatch(f.FullName)
                            && !Regex.IsMatch(f.Name, @"\.min\.", RegexOptions.IgnoreCase)
                            && !string.Equals(f.Name, outNameOnly, StringComparison.OrdinalIgnoreCase)
                            && IsLegible(f, extSet, dotAllow))
                .OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(f => ToDumpFile(f, root))
                .ToList();
        }

        return new DumpModel
        {
            Root = root,
            TargetPath = targetPath,
            IsSingleFile = isFile,
            ListedEntries = listed,
            Files = files,
            SkippedSingleFile = skipped,
        };
    }

    private static DumpFile ToDumpFile(FileInfo fi, string root)
    {
        string content = SafeReadText(fi.FullName);
        long size;
        try { size = fi.Length; } catch { size = content.Length; }
        return new DumpFile
        {
            FullName = fi.FullName,
            RelativePath = Path.GetRelativePath(root, fi.FullName),
            Content = content,
            Size = size,
        };
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
