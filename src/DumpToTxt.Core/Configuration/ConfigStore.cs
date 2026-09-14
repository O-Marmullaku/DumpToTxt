using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace DumpToTxt.Core;

/// <summary>
/// Loads JSON by overlaying machine settings and then user settings on defaults. Resolve also
/// overlays ancestor folder configuration for a target. The GUI saves to the user path while
/// preserving unowned fields. LoadFrom is a separate first-readable-file compatibility helper.
/// </summary>
public static class ConfigStore
{
    public static string UserDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DumpToTxt");

    public static string UserPath => Path.Combine(UserDir, "settings.json");

    public static string MachineDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DumpToTxt");

    public static string MachinePath => Path.Combine(MachineDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static DumpConfig Load()
    {
        var cfg = DumpConfig.CreateDefault();
        OverlayFile(cfg, MachinePath);
        OverlayFile(cfg, UserPath);
        return cfg;
    }

    /// <summary>Per-folder config file name (gitignore-syntax-free JSON, same shape as settings.json).</summary>
    public const string FolderConfigName = ".dumptotxt.json";

    /// <summary>Resolves the effective config for a dump target by MERGING layers, nearest-wins:
    /// built-in defaults → machine → user → every <c>.dumptotxt.json</c> from the volume root down to the
    /// target's own folder. Each layer overrides only the fields it specifies. The engine uses this at
    /// dump time; the GUI keeps editing the user file via <see cref="Load"/> / <see cref="Save"/>.</summary>
    public static DumpConfig Resolve(string targetPath) => Resolve(targetPath, MachinePath, UserPath);

    /// <summary>Testable seam: <see cref="Resolve(string)"/> with explicit machine/user paths.</summary>
    public static DumpConfig Resolve(string targetPath, string machinePath, string userPath)
    {
        var cfg = DumpConfig.CreateDefault();
        OverlayFile(cfg, machinePath);
        OverlayFile(cfg, userPath);
        foreach (var folderFile in FolderConfigChain(targetPath))   // farthest ancestor → nearest (nearest wins)
            OverlayFile(cfg, folderFile);
        return cfg;
    }

    private static void OverlayFile(DumpConfig acc, string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { ParseDto(File.ReadAllText(path)).Apply(acc); }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (JsonException) { throw new InvalidDataException($"Configuration is invalid: '{path}'. Repair this file before creating a dump."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new IOException($"Configuration could not be read: '{path}'. Check file access before creating a dump."); }
    }

    /// <summary>The <c>.dumptotxt.json</c> files from the volume root down to the target's own directory
    /// (farthest first, nearest last) so a later <see cref="ConfigDto.Apply"/> lets the nearest file win.</summary>
    private static IEnumerable<string> FolderConfigChain(string targetPath)
    {
        string startDir;
        try
        {
            string full = Path.GetFullPath(targetPath);
            startDir = File.Exists(full) ? Path.GetDirectoryName(full)! : full;
        }
        catch { yield break; }

        var dirs = new List<string>();
        string? d = startDir;
        while (!string.IsNullOrEmpty(d))
        {
            dirs.Add(d);
            d = Path.GetDirectoryName(d);
        }
        dirs.Reverse();   // volume root .. target dir
        foreach (var dir in dirs)
        {
            string p = Path.Combine(dir, FolderConfigName);
            yield return p;
        }
    }

    /// <summary>Loads from the given paths in precedence order; falls back to defaults. Testable seam.</summary>
    public static DumpConfig LoadFrom(params string[] paths)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            try { return Parse(File.ReadAllText(path)); }
            catch
            {
                // Corrupt/locked file: fall through to the next source, then defaults.
            }
        }
        return DumpConfig.CreateDefault();
    }

    public static void Save(DumpConfig cfg) => SaveTo(UserPath, cfg);

    /// <summary>Persists only the choices made in the review workspace.</summary>
    public static void SaveRunPreferences(DumpConfig used) => SaveRunPreferences(used, UserPath);

    public static void SaveRunPreferences(DumpConfig used, string userPath)
    {
        PatchAndSave(userPath, new JsonObject
        {
            [nameof(DumpConfig.Style)] = used.Style.ToString(),
            [nameof(DumpConfig.OutputTarget)] = used.OutputTarget.ToString(),
            [nameof(DumpConfig.OutputDir)] = used.OutputDir,
            [nameof(DumpConfig.ShowReviewBeforeDump)] = used.ShowReviewBeforeDump,
            [nameof(DumpConfig.LastSelectionMode)] = used.LastSelectionMode.ToString(),
        });
    }

    /// <summary>Saves to a specific path. Testable seam used by <see cref="Save"/>.</summary>
    public static void SaveTo(string path, DumpConfig cfg) =>
        PatchAndSave(path, JsonNode.Parse(Serialize(cfg))!.AsObject());

    private static void PatchAndSave(string path, JsonObject values)
    {
        var document = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
            { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
            ?? throw new JsonException("Settings must contain a JSON object.")
            : new JsonObject();
        foreach (var value in values)
        {
            foreach (string existing in document.Select(p => p.Key).Where(key =>
                string.Equals(key, value.Key, StringComparison.OrdinalIgnoreCase)).ToArray()) document.Remove(existing);
            document[value.Key] = value.Value?.DeepClone();
        }
        WriteAtomically(path, document.ToJsonString(JsonOpts));
    }

    private static void WriteAtomically(string path, string content)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Parses a settings.json string into a config. Pure (no IO) — used by Load and tests.</summary>
    public static DumpConfig Parse(string json) => ParseDto(json).ToConfig();

    private static ConfigDto ParseDto(string json) =>
        JsonSerializer.Deserialize<ConfigDto>(json, JsonOpts)
            ?? throw new JsonException("Configuration must be a JSON object.");

    /// <summary>Serializes a config to settings.json text. Pure — used by Save and tests.</summary>
    public static string Serialize(DumpConfig cfg) =>
        JsonSerializer.Serialize(ConfigDto.FromConfig(cfg), JsonOpts);

    /// <summary>DTO matching the legacy settings.json shape, plus the new output fields.</summary>
    private sealed class ConfigDto
    {
        public string? Theme { get; set; }
        public List<string>? ExtSet { get; set; }
        public List<string>? DotFilesAllow { get; set; }
        public string? ExcludeRegex { get; set; }
        public string? Style { get; set; }
        public string? OutputTarget { get; set; }
        private string? _outputDir;
        private bool _hasOutputDir;
        public string? OutputDir { get => _outputDir; set { _outputDir = value; _hasOutputDir = true; } }
        public bool? ShowReviewBeforeDump { get; set; }
        public bool? PlayCompletionSound { get; set; }
        public string? LastSelectionMode { get; set; }
        public bool? RespectGitignore { get; set; }
        public bool? UseDumpToTxtIgnore { get; set; }
        public List<string>? IncludeGlobs { get; set; }
        public List<string>? ExcludeGlobs { get; set; }
        public bool? DetectBinary { get; set; }
        public long? MaxFileSizeBytes { get; set; }
        public long? MaxTotalSizeBytes { get; set; }
        public string? TokenEncoding { get; set; }
        public long? MaxTokens { get; set; }
        public string? SecretScan { get; set; }
        public bool? SecretScanEntropy { get; set; }
        public List<string>? SecretAllowlist { get; set; }
        public List<string>? SensitiveValuePatterns { get; set; }

        public DumpConfig ToConfig() => Apply(DumpConfig.CreateDefault());

        /// <summary>Overlays the fields PRESENT in this DTO onto <paramref name="acc"/> (absent fields are
        /// left untouched). The single merge primitive used by both <see cref="ToConfig"/> (defaults + one
        /// file) and <see cref="ConfigStore.Resolve(string)"/> (layering machine/user/per-folder).
        /// <para>Exception to the present-overrides rule: a present-but-EMPTY <see cref="ExtSet"/> is treated as
        /// absent (kept), preserving the legacy "empty ExtSet ⇒ defaults" contract (the GUI enforces ≥1 type).
        /// The other lists (DotFilesAllow/IncludeGlobs/ExcludeGlobs) DO clear to empty when present-empty.</para></summary>
        public DumpConfig Apply(DumpConfig acc)
        {
            if (TryParseEnum(Theme, out UiThemeKind theme)) acc.Theme = theme;
            if (ExtSet is { Count: > 0 })
            {
                // Null-safe + null/blank-pruned so a stray null array element can't NRE (which the narrowed
                // OverlayFile catch would no longer mask); an all-empty list still falls back to defaults.
                var exts = ExtSet.Where(e => !string.IsNullOrEmpty(e)).Select(e => e.ToLowerInvariant()).ToList();
                if (exts.Count > 0) acc.ExtSet = exts;
            }
            if (DotFilesAllow is not null) acc.DotFilesAllow = DotFilesAllow;
            if (!string.IsNullOrWhiteSpace(ExcludeRegex)) acc.ExcludeRegex = ExcludeRegex;
            if (TryParseEnum(Style, out OutputStyle st)) acc.Style = st;
            if (TryParseEnum(OutputTarget, out Core.OutputTarget tg)) acc.OutputTarget = tg;
            if (_hasOutputDir) acc.OutputDir = string.IsNullOrWhiteSpace(OutputDir) ? null : OutputDir;
            if (ShowReviewBeforeDump is { } review) acc.ShowReviewBeforeDump = review;
            if (PlayCompletionSound is { } sound) acc.PlayCompletionSound = sound;
            if (TryParseEnum(LastSelectionMode, out DumpSelectionMode mode)) acc.LastSelectionMode = mode;
            if (RespectGitignore is { } rg) acc.RespectGitignore = rg;
            if (UseDumpToTxtIgnore is { } ud) acc.UseDumpToTxtIgnore = ud;
            if (IncludeGlobs is not null) acc.IncludeGlobs = IncludeGlobs;
            if (ExcludeGlobs is not null) acc.ExcludeGlobs = ExcludeGlobs;
            if (DetectBinary is { } db) acc.DetectBinary = db;
            if (MaxFileSizeBytes is { } mf) acc.MaxFileSizeBytes = mf > 0 ? mf : 0;
            if (MaxTotalSizeBytes is { } mt) acc.MaxTotalSizeBytes = mt > 0 ? mt : 0;
            if (TryParseEnum(TokenEncoding, out Core.TokenEncoding te)) acc.TokenEncoding = te;
            if (MaxTokens is { } mx) acc.MaxTokens = mx > 0 ? mx : 0;
            if (TryParseEnum(SecretScan, out Core.SecretScanMode ss)) acc.SecretScan = ss;
            if (SecretScanEntropy is { } se) acc.SecretScanEntropy = se;
            if (SensitiveValuePatterns is not null)
            {
                // New shape: the two lists are explicit and independent.
                acc.SensitiveValuePatterns = SensitiveValuePatterns;
                if (SecretAllowlist is not null) acc.SecretAllowlist = SecretAllowlist;
            }
            else if (SecretAllowlist is not null)
            {
                // Before SensitiveValuePatterns existed, the UI called this list "Ignore these sensitive
                // values" and users reasonably treated it as an exclusion/redaction list. Migrate old files
                // to the safer meaning once; new serialized files always carry SensitiveValuePatterns.
                acc.SensitiveValuePatterns = SecretAllowlist;
                acc.SecretAllowlist = new();
            }
            return acc;
        }

        // Accept only defined enum names; anything else (numbers, typos, garbage, absent) -> not present.
        private static bool TryParseEnum<TEnum>(string? raw, out TEnum value) where TEnum : struct, Enum
        {
            if (Enum.TryParse<TEnum>(raw, ignoreCase: true, out var v) && Enum.IsDefined(v)) { value = v; return true; }
            value = default;
            return false;
        }

        public static ConfigDto FromConfig(DumpConfig c) => new()
        {
            Theme = c.Theme.ToString(),
            ExtSet = c.ExtSet,
            DotFilesAllow = c.DotFilesAllow,
            ExcludeRegex = c.ExcludeRegex,
            Style = c.Style.ToString(),
            OutputTarget = c.OutputTarget.ToString(),
            OutputDir = c.OutputDir,
            ShowReviewBeforeDump = c.ShowReviewBeforeDump,
            PlayCompletionSound = c.PlayCompletionSound,
            LastSelectionMode = c.LastSelectionMode.ToString(),
            RespectGitignore = c.RespectGitignore,
            UseDumpToTxtIgnore = c.UseDumpToTxtIgnore,
            IncludeGlobs = c.IncludeGlobs,
            ExcludeGlobs = c.ExcludeGlobs,
            DetectBinary = c.DetectBinary,
            MaxFileSizeBytes = c.MaxFileSizeBytes,
            MaxTotalSizeBytes = c.MaxTotalSizeBytes,
            TokenEncoding = c.TokenEncoding.ToString(),
            MaxTokens = c.MaxTokens,
            SecretScan = c.SecretScan.ToString(),
            SecretScanEntropy = c.SecretScanEntropy,
            SecretAllowlist = c.SecretAllowlist,
            SensitiveValuePatterns = c.SensitiveValuePatterns,
        };
    }
}
