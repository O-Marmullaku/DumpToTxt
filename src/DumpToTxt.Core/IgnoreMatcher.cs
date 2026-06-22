using System.Text;
using System.Text.RegularExpressions;

namespace DumpToTxt.Core;

/// <summary>
/// Path filter for a dump. Layers, additively, a root <c>.gitignore</c> + <c>.dumptotxtignore</c>
/// (gitignore syntax), config exclude/include globs, and the legacy backslash <see cref="DumpConfig.ExcludeRegex"/>.
/// <para>
/// A path is EXCLUDED (dropped from both the listing and the content) when any ignore source matches,
/// with gitignore-style <c>!</c> negation applied last-match-wins. A FILE is eligible for content only
/// when it also matches an include glob — but only if any include globs are configured (otherwise all
/// non-excluded files are eligible). Gitignore/glob matching is on root-relative paths using <c>/</c>;
/// the legacy ExcludeRegex keeps its original semantics (full path, backslashes).
/// </para>
/// </summary>
public sealed class IgnoreMatcher
{
    private readonly List<GlobRule> _ignore;
    private readonly List<GlobRule> _include;
    private readonly Regex? _excludeRegex;

    private IgnoreMatcher(List<GlobRule> ignore, List<GlobRule> include, Regex? excludeRegex)
    {
        _ignore = ignore;
        _include = include;
        _excludeRegex = excludeRegex;
    }

    /// <summary>True when include globs are configured, so content is whitelisted to matching files.</summary>
    public bool HasIncludeGlobs => _include.Count > 0;

    /// <summary>Builds a matcher from the config plus any ignore files in <paramref name="root"/>.</summary>
    public static IgnoreMatcher Build(string root, DumpConfig cfg)
    {
        var ignore = new List<GlobRule>();
        if (cfg.RespectGitignore)
            AddLines(ignore, ReadIgnoreFile(Path.Combine(root, ".gitignore")));
        if (cfg.UseDumpToTxtIgnore)
            AddLines(ignore, ReadIgnoreFile(Path.Combine(root, ".dumptotxtignore")));
        AddLines(ignore, cfg.ExcludeGlobs);

        var include = new List<GlobRule>();
        AddLines(include, cfg.IncludeGlobs);

        Regex? rx = null;
        if (!string.IsNullOrWhiteSpace(cfg.ExcludeRegex))
        {
            try { rx = new Regex(cfg.ExcludeRegex, RegexOptions.IgnoreCase); }
            catch { rx = null; } // invalid regex: drop the legacy layer rather than throw
        }

        return new IgnoreMatcher(ignore, include, rx);
    }

    /// <summary>Builds a matcher with no ignore files read (config + regex only). Used in tests / single sources.</summary>
    public static IgnoreMatcher FromConfig(DumpConfig cfg) =>
        Build(Path.GetTempPath() + Guid.NewGuid().ToString("N"), cfg); // a dir with no ignore files

    /// <summary>True when the entry should be dropped entirely (listing + content).</summary>
    public bool IsExcluded(string fullPath, string relativePath, bool isDir)
    {
        string rel = Normalize(relativePath);

        bool ignored = false;
        foreach (var r in _ignore)
        {
            if (r.DirOnly && !isDir) continue;
            if (r.Rx.IsMatch(rel)) ignored = !r.Negate;
        }

        // Legacy ExcludeRegex is a hard layer (full path, backslashes); not subject to gitignore negation.
        if (_excludeRegex is not null && _excludeRegex.IsMatch(fullPath)) ignored = true;

        return ignored;
    }

    /// <summary>True when a (non-excluded) file is allowed to contribute content under the include globs.</summary>
    public bool MatchesInclude(string relativePath)
    {
        if (_include.Count == 0) return true;
        string rel = Normalize(relativePath);
        foreach (var r in _include)
            if (r.Rx.IsMatch(rel)) return true;
        return false;
    }

    private static string Normalize(string rel) => rel.Replace('\\', '/').TrimStart('/');

    private static IEnumerable<string> ReadIgnoreFile(string path)
    {
        try { return File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static void AddLines(List<GlobRule> rules, IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var rule = Translate(raw);
            if (rule is not null) rules.Add(rule);
        }
    }

    /// <summary>A single compiled gitignore/glob rule.</summary>
    private sealed class GlobRule
    {
        public required Regex Rx { get; init; }
        public bool Negate { get; init; }
        public bool DirOnly { get; init; }
    }

    /// <summary>Translates one gitignore/glob line into a compiled rule, or null for blank/comment lines.</summary>
    private static GlobRule? Translate(string line)
    {
        if (line is null) return null;
        string p = line.TrimEnd();          // gitignore ignores trailing whitespace (we don't support escaped trailing spaces)
        if (p.Length == 0 || p[0] == '#') return null;

        bool negate = p.StartsWith('!');
        if (negate) p = p[1..];
        if (p.Length == 0) return null;

        bool dirOnly = p.EndsWith('/');
        if (dirOnly) p = p[..^1];
        if (p.Length == 0) return null;

        // A separator at the start or middle anchors the pattern to the root; otherwise it matches at any depth.
        bool anchored = p.StartsWith('/') || p.IndexOf('/') >= 0;
        if (p.StartsWith('/')) p = p[1..];

        var sb = new StringBuilder("^");
        if (!anchored) sb.Append("(?:.*/)?");

        for (int i = 0; i < p.Length; i++)
        {
            char c = p[i];
            if (c == '*')
            {
                if (i + 1 < p.Length && p[i + 1] == '*')
                {
                    if (i + 2 < p.Length && p[i + 2] == '/') { sb.Append("(?:.*/)?"); i += 2; }
                    else { sb.Append(".*"); i += 1; }
                }
                else
                {
                    sb.Append("[^/]*");
                }
            }
            else if (c == '?') sb.Append("[^/]");
            else if (c == '/') sb.Append('/');
            else sb.Append(Regex.Escape(c.ToString()));
        }
        sb.Append('$');

        return new GlobRule
        {
            Rx = new Regex(sb.ToString(), RegexOptions.IgnoreCase),
            Negate = negate,
            DirOnly = dirOnly,
        };
    }
}
