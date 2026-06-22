using System.Text.Json;
using System.Text.Json.Serialization;

namespace DumpToTxt.Core;

/// <summary>
/// Loads/saves <see cref="DumpConfig"/> as JSON. Read precedence mirrors the legacy
/// PowerShell tool: user config (%APPDATA%) first, then machine config (%PROGRAMDATA%),
/// else built-in defaults. The GUI saves to the user path.
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

    public static DumpConfig Load() => LoadFrom(UserPath, MachinePath);

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
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try { ParseDto(File.ReadAllText(path)).Apply(acc); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt/locked layer: skip it, keep what we have so far. Narrowed to the expected IO/parse
            // failures so an UNEXPECTED exception (a real bug) surfaces instead of being silently masked.
        }
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
            if (File.Exists(p)) yield return p;
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

    /// <summary>Saves to a specific path. Testable seam used by <see cref="Save"/>.</summary>
    public static void SaveTo(string path, DumpConfig cfg)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Serialize(cfg));
    }

    /// <summary>Parses a settings.json string into a config. Pure (no IO) — used by Load and tests.</summary>
    public static DumpConfig Parse(string json) => ParseDto(json).ToConfig();

    private static ConfigDto ParseDto(string json) =>
        JsonSerializer.Deserialize<ConfigDto>(json, JsonOpts) ?? new ConfigDto();

    /// <summary>Serializes a config to settings.json text. Pure — used by Save and tests.</summary>
    public static string Serialize(DumpConfig cfg) =>
        JsonSerializer.Serialize(ConfigDto.FromConfig(cfg), JsonOpts);

    /// <summary>DTO matching the legacy settings.json shape, plus the new output fields.</summary>
    private sealed class ConfigDto
    {
        public List<string>? ExtSet { get; set; }
        public List<string>? DotFilesAllow { get; set; }
        public string? ExcludeRegex { get; set; }
        public string? Style { get; set; }
        public string? OutputTarget { get; set; }
        public string? OutputDir { get; set; }
        public bool? RespectGitignore { get; set; }
        public bool? UseDumpToTxtIgnore { get; set; }
        public List<string>? IncludeGlobs { get; set; }
        public List<string>? ExcludeGlobs { get; set; }
        public bool? DetectBinary { get; set; }
        public long? MaxFileSizeBytes { get; set; }
        public long? MaxTotalSizeBytes { get; set; }

        public DumpConfig ToConfig() => Apply(DumpConfig.CreateDefault());

        /// <summary>Overlays the fields PRESENT in this DTO onto <paramref name="acc"/> (absent fields are
        /// left untouched). The single merge primitive used by both <see cref="ToConfig"/> (defaults + one
        /// file) and <see cref="ConfigStore.Resolve(string)"/> (layering machine/user/per-folder).
        /// <para>Exception to the present-overrides rule: a present-but-EMPTY <see cref="ExtSet"/> is treated as
        /// absent (kept), preserving the legacy "empty ExtSet ⇒ defaults" contract (the GUI enforces ≥1 type).
        /// The other lists (DotFilesAllow/IncludeGlobs/ExcludeGlobs) DO clear to empty when present-empty.</para></summary>
        public DumpConfig Apply(DumpConfig acc)
        {
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
            if (!string.IsNullOrWhiteSpace(OutputDir)) acc.OutputDir = OutputDir;
            if (RespectGitignore is { } rg) acc.RespectGitignore = rg;
            if (UseDumpToTxtIgnore is { } ud) acc.UseDumpToTxtIgnore = ud;
            if (IncludeGlobs is not null) acc.IncludeGlobs = IncludeGlobs;
            if (ExcludeGlobs is not null) acc.ExcludeGlobs = ExcludeGlobs;
            if (DetectBinary is { } db) acc.DetectBinary = db;
            if (MaxFileSizeBytes is { } mf) acc.MaxFileSizeBytes = mf > 0 ? mf : 0;
            if (MaxTotalSizeBytes is { } mt) acc.MaxTotalSizeBytes = mt > 0 ? mt : 0;
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
            ExtSet = c.ExtSet,
            DotFilesAllow = c.DotFilesAllow,
            ExcludeRegex = c.ExcludeRegex,
            Style = c.Style.ToString(),
            OutputTarget = c.OutputTarget.ToString(),
            OutputDir = c.OutputDir,
            RespectGitignore = c.RespectGitignore,
            UseDumpToTxtIgnore = c.UseDumpToTxtIgnore,
            IncludeGlobs = c.IncludeGlobs,
            ExcludeGlobs = c.ExcludeGlobs,
            DetectBinary = c.DetectBinary,
            MaxFileSizeBytes = c.MaxFileSizeBytes,
            MaxTotalSizeBytes = c.MaxTotalSizeBytes,
        };
    }
}
