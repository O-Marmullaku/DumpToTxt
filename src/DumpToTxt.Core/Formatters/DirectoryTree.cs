using System.Text;

namespace DumpToTxt.Core;

/// <summary>
/// Shared helpers for the repomix-style preamble used by the non-Classic formatters:
/// an ASCII directory tree of the scanned (non-ignored) structure, plus small formatting utilities.
/// </summary>
public static class DirectoryTree
{
    private sealed class Node
    {
        public SortedDictionary<string, Node> Children { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public bool IsDir { get; set; }
    }

    /// <summary>Full-structure tree of the surviving entries, or a note when nothing matched the filters.</summary>
    public static string RenderStructureOrNote(IReadOnlyList<ListedEntry> entries)
    {
        var t = RenderStructure(entries);
        return t.Length == 0 ? "(no matching files)" : t;
    }

    /// <summary>Renders an ASCII tree of the entries' relative paths (directories included). Empty string if none.</summary>
    public static string RenderStructure(IReadOnlyList<ListedEntry> entries)
    {
        var root = new Node();
        foreach (var e in entries)
        {
            var parts = e.RelativePath.Split('\\', '/');
            var cur = root;
            for (int i = 0; i < parts.Length; i++)
            {
                var name = parts[i];
                if (name.Length == 0) continue;
                if (!cur.Children.TryGetValue(name, out var child))
                {
                    child = new Node();
                    cur.Children[name] = child;
                }
                // Intermediate path components are always directories; the leaf takes the entry's own type.
                child.IsDir = (i < parts.Length - 1) || e.IsDirectory || child.IsDir;
                cur = child;
            }
        }

        var sb = new StringBuilder();
        RenderChildren(root, "", sb);
        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static void RenderChildren(Node node, string prefix, StringBuilder sb)
    {
        // Directories first, then files; each group alphabetical (deterministic).
        var entries = new List<KeyValuePair<string, Node>>(node.Children);
        entries.Sort((a, b) =>
        {
            bool ad = a.Value.IsDir, bd = b.Value.IsDir;
            if (ad != bd) return ad ? -1 : 1;
            return string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
        });

        for (int i = 0; i < entries.Count; i++)
        {
            bool last = i == entries.Count - 1;
            var (name, child) = (entries[i].Key, entries[i].Value);
            sb.Append(prefix).Append(last ? "└── " : "├── ").Append(name);
            if (child.IsDir) sb.Append('/');
            sb.Append("\r\n");
            if (child.Children.Count > 0)
                RenderChildren(child, prefix + (last ? "    " : "│   "), sb);
        }
    }

    /// <summary>Human-readable byte size, e.g. "12.3 KB".</summary>
    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} {units[u]}" : $"{v:0.0} {units[u]}";
    }

    /// <summary>Language id for a Markdown fence, derived from a file extension. Empty if unknown.</summary>
    public static string LanguageFor(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "csharp",
            ".js" => "javascript",
            ".ts" => "typescript",
            ".jsx" => "jsx",
            ".tsx" => "tsx",
            ".py" => "python",
            ".rb" => "ruby",
            ".php" => "php",
            ".java" => "java",
            ".kt" => "kotlin",
            ".go" => "go",
            ".rs" => "rust",
            ".cpp" or ".cc" or ".cxx" => "cpp",
            ".c" or ".h" => "c",
            ".html" => "html",
            ".css" => "css",
            ".json" => "json",
            ".xml" => "xml",
            ".yml" or ".yaml" => "yaml",
            ".toml" => "toml",
            ".ini" => "ini",
            ".md" => "markdown",
            ".sh" => "bash",
            ".ps1" or ".psm1" or ".psd1" => "powershell",
            ".sql" => "sql",
            _ => ext.Length > 1 ? ext.Substring(1) : "",
        };
    }
}
