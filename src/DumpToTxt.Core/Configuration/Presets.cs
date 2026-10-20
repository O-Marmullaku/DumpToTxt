namespace DumpToTxt.Core;

/// <summary>A named, partial config overlay applied on top of the resolved config at dump time
/// (e.g. via the <c>--preset</c> CLI flag or the GUI Presets tab). A preset only sets the few knobs
/// that define it; everything else flows through from the resolved config.</summary>
public sealed class Preset
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    /// <summary>Mutates the resolved config to express this preset. No IO.</summary>
    public required Action<DumpConfig> Apply { get; init; }
}

/// <summary>Built-in partial overlays exposed by settings and the --preset argument.
/// Explorer registration deliberately remains a single action.</summary>
public static class Presets
{
    /// <summary>The original tool's experience: Classic layout, strict legacy file selection
    /// (no .gitignore / .dumptotxtignore / binary skipping), default legible types.</summary>
    public static readonly Preset Classic = new()
    {
        Name = "Classic",
        Description = "Clean text · Classic layout · ignore and binary checks off.",
        Apply = c =>
        {
            c.Style = OutputStyle.Classic;
            c.LastSelectionMode = DumpSelectionMode.Basic;
            c.RespectGitignore = false;
            c.UseDumpToTxtIgnore = false;
            c.DetectBinary = false;
            c.IncludeGlobs = new();
            c.ExtSet = DumpConfig.CreateDefault().ExtSet;
        },
    };

    /// <summary>Web / UI source only — restricts legible types to the front-end stack.</summary>
    public static readonly Preset Frontend = new()
    {
        Name = "Frontend",
        Description = "Web project · HTML, CSS, JavaScript, TypeScript, and related files.",
        Apply = c =>
        {
            c.ExtSet = new() { ".js", ".jsx", ".ts", ".tsx", ".vue", ".svelte", ".css", ".scss", ".html", ".json" };
            c.LastSelectionMode = DumpSelectionMode.Basic;
            c.IncludeGlobs = new();
        },
    };

    /// <summary>Documentation only — restricts legible types to common doc formats.</summary>
    public static readonly Preset DocsOnly = new()
    {
        Name = "Docs only",
        Description = "Documentation · Markdown, text, reStructuredText, and AsciiDoc files.",
        Apply = c =>
        {
            c.ExtSet = new() { ".md", ".mdx", ".txt", ".rst", ".adoc" };
            c.LastSelectionMode = DumpSelectionMode.Basic;
            c.IncludeGlobs = new();
        },
    };

    /// <summary>Only files changed since the last git commit. Falls back to a normal dump outside a repo.</summary>
    public static readonly Preset ChangedFiles = new()
    {
        Name = "Changed files",
        Description = "Changed files only · Uses Git changes when available.",
        Apply = c => c.OnlyGitChanged = true,
    };

    /// <summary>Repomix's built-in ignore list (src/config/defaultIgnore.ts), rewritten from its globby rules
    /// into this matcher's gitignore syntax: a pattern without <c>**/</c> applies at the root only, and
    /// <c>dir/**</c> becomes <c>dir</c>, which (like picomatch) also matches a file of that name and lets
    /// discovery skip the folder rather than walk it. That keeps a worktree's <c>.git</c> file out too.</summary>
    public static readonly IReadOnlyList<string> RepomixIgnore = new[]
    {
        "/.git", "/.hg", "/.hgignore", "/.svn", "**/node_modules", "**/bower_components",
        "**/jspm_packages", "/vendor", "**/.bundle", "**/.gradle", "/target", "/logs", "**/*.log",
        "**/npm-debug.log*", "**/yarn-debug.log*", "**/yarn-error.log*", "/pids", "/*.pid", "/*.seed",
        "/*.pid.lock", "/lib-cov", "/coverage", "/.nyc_output", "/.grunt", "/.lock-wscript",
        "/build/Release", "/typings", "**/.npm", "/.eslintcache", "/.rollup.cache", "/.webpack.cache",
        "/.parcel-cache", "/.sass-cache", "/*.cache", "/.node_repl_history", "/*.tgz", "**/.yarn",
        "**/.yarn-integrity", "/.env", "/.next", "/.nuxt", "/.vuepress/dist", "/.serverless",
        "/.fusebox", "/.dynamodb", "/dist", "**/.DS_Store", "**/Thumbs.db", "/.idea", "/.vscode",
        "**/*.swp", "**/*.swo", "**/*.swn", "**/*.bak", "/build", "/out", "/tmp", "/temp",
        "**/repomix-output.*", "**/repopack-output.*", "**/package-lock.json", "**/yarn-error.log",
        "**/yarn.lock", "**/pnpm-lock.yaml", "**/bun.lockb", "**/bun.lock", "**/__pycache__",
        "**/*.py[cod]", "**/venv", "**/.venv", "**/.pytest_cache", "**/.mypy_cache",
        "**/.ipynb_checkpoints", "**/Pipfile.lock", "**/poetry.lock", "**/uv.lock", "**/Cargo.lock",
        "**/Cargo.toml.orig", "**/target", "**/*.rs.bk", "**/composer.lock", "**/Gemfile.lock", "**/go.sum",
        "**/mix.lock", "**/stack.yaml.lock", "**/cabal.project.freeze",
    };

    /// <summary>Gitingest's built-in ignore set (src/gitingest/utils/ignore_patterns.py), already gitignore syntax.</summary>
    public static readonly IReadOnlyList<string> GitingestIgnore = new[]
    {
        "*.pyc", "*.pyo", "*.pyd", "__pycache__", ".pytest_cache", ".coverage", ".tox", ".nox",
        ".mypy_cache", ".ruff_cache", ".hypothesis", "poetry.lock", "Pipfile.lock", "node_modules",
        "bower_components", "package-lock.json", "yarn.lock", ".npm", ".yarn", ".pnpm-store", "bun.lock",
        "bun.lockb", "*.class", "*.jar", "*.war", "*.ear", "*.nar", ".gradle/", "build/", ".settings/",
        ".classpath", "gradle-app.setting", "*.gradle", ".project", "*.o", "*.obj", "*.dll", "*.dylib",
        "*.exe", "*.lib", "*.out", "*.a", "*.pdb", "*.bin", ".build/", "*.xcodeproj/", "*.xcworkspace/",
        "*.pbxuser", "*.mode1v3", "*.mode2v3", "*.perspectivev3", "*.xcuserstate", "xcuserdata/",
        ".swiftpm/", "*.gem", ".bundle/", "vendor/bundle", "Gemfile.lock", ".ruby-version", ".ruby-gemset",
        ".rvmrc", "Cargo.lock", "**/*.rs.bk", "target/", "pkg/", "obj/", "*.suo", "*.user", "*.userosscache",
        "*.sln.docstates", "*.nupkg", "bin/", ".git", ".svn", ".hg", ".gitignore", ".gitattributes",
        ".gitmodules", "*.svg", "*.png", "*.jpg", "*.jpeg", "*.gif", "*.ico", "*.pdf", "*.mov", "*.mp4",
        "*.mp3", "*.wav", "venv", ".venv", "env", ".env", "virtualenv", ".idea", ".vscode", ".vs", "*.swo",
        "*.swn", ".settings", "*.sublime-*", "*.log", "*.bak", "*.swp", "*.tmp", "*.temp", ".cache",
        ".sass-cache", ".eslintcache", ".DS_Store", "Thumbs.db", "desktop.ini", "build", "dist", "target",
        "out", "*.egg-info", "*.egg", "*.whl", "*.so", "site-packages", ".docusaurus", ".next", ".nuxt",
        "*.db", "*.sqlite", "*.sqlite3", "*.min.js", "*.min.css", "*.map", "*.tfstate*", "vendor/",
        "digest.txt",
    };

    /// <summary>A pattern that never matches: turns off the legacy folder-name exclusion while still
    /// surviving a settings round trip (an empty value would restore the default).</summary>
    public const string NoExcludeRegex = "(?!)";

    /// <summary>What Repomix does with no flags: every text file, root .gitignore plus its default ignore
    /// list, binary files listed but not packed, and its XML-tagged layout.</summary>
    public static readonly Preset Repomix = new()
    {
        Name = "Repomix",
        Description = "Repomix defaults · every text file, its ignore list, Repomix-style layout.",
        Apply = c => ToolDefaults(c, OutputStyle.Repomix, RepomixIgnore),
    };

    /// <summary>What Gitingest does with no flags: every file, .gitignore plus its default ignore set,
    /// and its digest layout.</summary>
    public static readonly Preset Gitingest = new()
    {
        Name = "Gitingest",
        Description = "Gitingest defaults · every text file, its ignore list, Gitingest-style digest.",
        Apply = c => ToolDefaults(c, OutputStyle.Gitingest, GitingestIgnore),
    };

    private static void ToolDefaults(DumpConfig c, OutputStyle style, IReadOnlyList<string> ignore)
    {
        c.Style = style;
        c.LastSelectionMode = DumpSelectionMode.Thorough;
        c.RespectGitignore = true;
        c.DetectBinary = true;
        c.IncludeGlobs = new();
        c.ExcludeGlobs = ignore.ToList();
        c.ExcludeRegex = NoExcludeRegex;
        c.MaxFileSizeBytes = 0;
        c.MaxTotalSizeBytes = 0;
        c.TokenEncoding = TokenEncoding.O200kBase;
        // Sensitive-data protection is deliberately left as configured: matching another tool never weakens it.
    }

    public static readonly IReadOnlyList<Preset> All = new[] { Classic, Frontend, DocsOnly, Repomix, Gitingest, ChangedFiles };

    /// <summary>Looks up a preset by name (case-insensitive), or null if unknown.</summary>
    public static Preset? ByName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}
