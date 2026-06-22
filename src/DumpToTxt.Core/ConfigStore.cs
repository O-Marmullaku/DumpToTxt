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

    public static DumpConfig Load()
    {
        foreach (var path in new[] { UserPath, MachinePath })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var dto = JsonSerializer.Deserialize<ConfigDto>(File.ReadAllText(path), JsonOpts);
                if (dto != null) return dto.ToConfig();
            }
            catch
            {
                // Corrupt/locked file: fall through to the next source, then defaults.
            }
        }
        return DumpConfig.CreateDefault();
    }

    public static void Save(DumpConfig cfg)
    {
        Directory.CreateDirectory(UserDir);
        File.WriteAllText(UserPath, JsonSerializer.Serialize(ConfigDto.FromConfig(cfg), JsonOpts));
    }

    /// <summary>DTO matching the legacy settings.json shape, plus the new Style field.</summary>
    private sealed class ConfigDto
    {
        public List<string>? ExtSet { get; set; }
        public List<string>? DotFilesAllow { get; set; }
        public string? ExcludeRegex { get; set; }
        public string? Style { get; set; }

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
                Style = Enum.TryParse<OutputStyle>(Style, ignoreCase: true, out var s) ? s : OutputStyle.Classic,
            };
        }

        public static ConfigDto FromConfig(DumpConfig c) => new()
        {
            ExtSet = c.ExtSet,
            DotFilesAllow = c.DotFilesAllow,
            ExcludeRegex = c.ExcludeRegex,
            Style = c.Style.ToString(),
        };
    }
}
