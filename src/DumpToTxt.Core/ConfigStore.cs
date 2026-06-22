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
    public static DumpConfig Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<ConfigDto>(json, JsonOpts);
        return (dto ?? new ConfigDto()).ToConfig();
    }

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

        public DumpConfig ToConfig()
        {
            var def = DumpConfig.CreateDefault();
            return new DumpConfig
            {
                ExtSet = (ExtSet is { Count: > 0 })
                    ? ExtSet.Select(e => e.ToLowerInvariant()).ToList()
                    : def.ExtSet,
                DotFilesAllow = DotFilesAllow ?? def.DotFilesAllow,
                ExcludeRegex = string.IsNullOrWhiteSpace(ExcludeRegex) ? def.ExcludeRegex : ExcludeRegex,
                Style = ParseEnum(Style, OutputStyle.Classic),
                OutputTarget = ParseEnum(OutputTarget, Core.OutputTarget.File),
                OutputDir = string.IsNullOrWhiteSpace(OutputDir) ? null : OutputDir,
            };
        }

        // Accept only defined enum names; anything else (numbers, typos, garbage) -> fallback.
        private static TEnum ParseEnum<TEnum>(string? raw, TEnum fallback) where TEnum : struct, Enum =>
            Enum.TryParse<TEnum>(raw, ignoreCase: true, out var v) && Enum.IsDefined(v) ? v : fallback;

        public static ConfigDto FromConfig(DumpConfig c) => new()
        {
            ExtSet = c.ExtSet,
            DotFilesAllow = c.DotFilesAllow,
            ExcludeRegex = c.ExcludeRegex,
            Style = c.Style.ToString(),
            OutputTarget = c.OutputTarget.ToString(),
            OutputDir = c.OutputDir,
        };
    }
}
