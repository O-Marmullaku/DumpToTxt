namespace DumpToTxt.Core;

public enum DumpFileTypeKind { Extension, DotFile, Extensionless }

/// <summary>A normalized preview row key.</summary>
public readonly record struct DumpFileTypeKey(DumpFileTypeKind Kind, string Value)
{
    public const string NoExtensionDisplayName = "(no extension)";
    public string DisplayName => Kind == DumpFileTypeKind.Extensionless ? NoExtensionDisplayName : Value;

    public static DumpFileTypeKey FromFileName(string fileName)
    {
        string name = Path.GetFileName(fileName);
        if (name.Length > 1 && name[0] == '.')
            return new(DumpFileTypeKind.DotFile, name.ToLowerInvariant());
        string extension = Path.GetExtension(name);
        return extension.Length == 0
            ? new(DumpFileTypeKind.Extensionless, "")
            : new(DumpFileTypeKind.Extension, extension.ToLowerInvariant());
    }
}

/// <summary>Per-run content choice. It is never serialized into <see cref="DumpConfig"/>.</summary>
public sealed class DumpContentSelection
{
    private readonly HashSet<string> _extensions;
    private readonly HashSet<string> _dotFiles;
    private readonly IReadOnlyDictionary<string, bool> _pathOverrides;

    public static DumpContentSelection Empty { get; } = FromTypes(Array.Empty<DumpFileTypeKey>());
    public IReadOnlySet<string> AllowedExtensions => _extensions;
    public IReadOnlySet<string> AllowedDotFileNames => _dotFiles;
    public bool IncludeExtensionless { get; }
    public bool IncludeAllText { get; }
    public IReadOnlyDictionary<string, bool> PathOverrides => _pathOverrides;

    private DumpContentSelection(HashSet<string> extensions, HashSet<string> dotFiles, bool extensionless,
        bool includeAllText = false, IReadOnlyDictionary<string, bool>? pathOverrides = null)
    {
        _extensions = extensions;
        _dotFiles = dotFiles;
        IncludeExtensionless = extensionless;
        IncludeAllText = includeAllText;
        _pathOverrides = pathOverrides ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    }

    public static DumpContentSelection FromTypes(IEnumerable<DumpFileTypeKey> types)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dotFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool extensionless = false;
        foreach (var type in types)
        {
            if (type.Kind == DumpFileTypeKind.Extension) extensions.Add(type.Value);
            else if (type.Kind == DumpFileTypeKind.DotFile) dotFiles.Add(type.Value);
            else extensionless = true;
        }
        return new(extensions, dotFiles, extensionless);
    }

    public static DumpContentSelection FromMode(DumpSelectionMode mode, DumpConfig cfg) => mode switch
    {
        DumpSelectionMode.Thorough => new(new(StringComparer.OrdinalIgnoreCase),
            new(StringComparer.OrdinalIgnoreCase), false, includeAllText: true),
        DumpSelectionMode.None => Empty,
        _ => FromTypes(
            cfg.ExtSet.Select(x => new DumpFileTypeKey(DumpFileTypeKind.Extension, x.ToLowerInvariant()))
                .Concat(cfg.DotFilesAllow.Select(x => new DumpFileTypeKey(DumpFileTypeKind.DotFile, x.ToLowerInvariant())))),
    };

    public DumpContentSelection WithPathOverrides(IReadOnlyDictionary<string, bool> overrides) =>
        new(new HashSet<string>(_extensions, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(_dotFiles, StringComparer.OrdinalIgnoreCase), IncludeExtensionless,
            IncludeAllText, overrides.ToDictionary(pair => DumpContributionTree.Normalize(pair.Key),
                pair => pair.Value, StringComparer.OrdinalIgnoreCase));

    internal bool Allows(FileInfo file)
    {
        var type = DumpFileTypeKey.FromFileName(file.Name);
        return IncludeAllText || type.Kind switch
        {
            DumpFileTypeKind.Extension => _extensions.Contains(type.Value),
            DumpFileTypeKind.DotFile => _dotFiles.Contains(type.Value),
            _ => IncludeExtensionless,
        };
    }

    public bool AllowsType(DumpFileTypeKey type) => IncludeAllText || type.Kind switch
    {
        DumpFileTypeKind.Extension => _extensions.Contains(type.Value),
        DumpFileTypeKind.DotFile => _dotFiles.Contains(type.Value),
        _ => IncludeExtensionless,
    };

    internal bool Allows(FileInfo file, string root)
    {
        bool included = Allows(file);
        string relative = DumpContributionTree.Normalize(Path.GetRelativePath(root, file.FullName));
        while (true)
        {
            if (_pathOverrides.TryGetValue(relative, out bool selected)) return selected;
            if (relative.Length == 0) return included;
            int slash = relative.LastIndexOf('/');
            relative = slash < 0 ? "" : relative[..slash];
        }
    }
}

internal static class DumpContentRules
{
    public static bool IsConfigured(FileInfo file, HashSet<string> extensions, HashSet<string> dotFiles)
    {
        var type = DumpFileTypeKey.FromFileName(file.Name);
        return type.Kind switch
        {
            DumpFileTypeKind.Extension => extensions.Contains(type.Value),
            DumpFileTypeKind.DotFile => dotFiles.Contains(type.Value),
            _ => false,
        };
    }
}
