using System.Globalization;
using System.Text;

namespace DumpToTxt.Core;

/// <summary>
/// Repomix's default output (its Handlebars XML template, <c>parsableStyle</c> off): file summary, an
/// indented directory structure of every listed file, then <c>&lt;file path&gt;</c> blocks with trimmed,
/// unescaped bodies in Repomix order. Binary files appear only in the structure, as in Repomix. Where
/// Repomix names itself, the text names DumpToTxt; notes reflect the settings actually used.
/// </summary>
public sealed class RepomixFormatter : IDumpFormatter
{
    public OutputStyle Style => OutputStyle.Repomix;

    public string Render(DumpModel model, DumpConfig cfg)
    {
        using var writer = new BoundedTextWriter(DumpEngine.MaxMaterializedCharacters);
        Write(model, cfg, writer);
        return writer.ToString();
    }

    public void Write(DumpModel model, DumpConfig cfg, TextWriter writer)
    {
        // Repomix distinguishes its default ignore list from user-supplied ignore patterns.
        bool included = cfg.IncludeGlobs.Count > 0;
        bool defaultIgnore = cfg.ExcludeGlobs.SequenceEqual(Presets.RepomixIgnore);
        bool customIgnore = cfg.ExcludeGlobs.Count > 0 && !defaultIgnore;
        bool entire = !included && !customIgnore;
        bool securityOff = cfg.SecretScan == SecretScanMode.Off;

        if (entire) writer.Write("This file is a merged representation of the entire codebase");
        else
        {
            var parts = new List<string>();
            if (included) parts.Add("specifically included files");
            if (customIgnore) parts.Add("files not matching ignore patterns");
            writer.Write("This file is a merged representation of a subset of the codebase, containing " + string.Join(" and ", parts));
        }
        writer.Write(", combined into a single document by DumpToTxt.");
        if (securityOff) writer.Write("\nThe content has been processed where security check has been disabled.");
        writer.Write("\n\n<file_summary>\nThis section contains a summary of this file.\n\n<purpose>\n");
        writer.Write("This file contains a packed representation of ");
        writer.Write(entire ? "the entire repository's contents"
            : "a subset of the repository's contents that is considered the most important context");
        writer.Write(".\nIt is designed to be easily consumable by AI systems for analysis, code review,\nor other automated processes.\n</purpose>\n\n");
        writer.Write("<file_format>\nThe content is organized as follows:\n1. This summary section\n2. Repository information\n3. Directory structure\n4. Repository files (if enabled)\n5. Multiple file entries, each consisting of:\n  - File path as an attribute\n  - Full contents of the file\n</file_format>\n\n");
        writer.Write("<usage_guidelines>\n- This file should be treated as read-only. Any changes should be made to the\n  original repository files, not this packed version.\n- When processing this file, use the file path to distinguish\n  between different files in the repository.\n- Be aware that this file may contain sensitive information. Handle it with\n  the same level of security as you would the original repository.\n</usage_guidelines>\n\n");

        writer.Write("<notes>\n- Some files may have been excluded based on .gitignore rules and DumpToTxt's configuration\n");
        writer.Write("- Binary files are not included in this packed representation. Please refer to the Repository Structure section for a complete list of file paths, including binary files");
        if (included) writer.Write("\n- Only files matching these patterns are included: " + string.Join(", ", cfg.IncludeGlobs));
        if (customIgnore) writer.Write("\n- Files matching these patterns are excluded: " + string.Join(", ", cfg.ExcludeGlobs));
        if (cfg.RespectGitignore) writer.Write("\n- Files matching patterns in .gitignore are excluded");
        if (defaultIgnore) writer.Write("\n- Files matching default ignore patterns are excluded");
        if (securityOff) writer.Write("\n- Security check has been disabled - content may contain sensitive information");
        if (model.GitChangeCounts is not null)
            writer.Write("\n- Files are sorted by Git change count (files with more changes are at the bottom)");
        writer.Write("\n</notes>\n\n</file_summary>\n\n");

        writer.Write("<directory_structure>\n");
        writer.Write(Tree(model.Entries.Where(e => !e.IsDirectory).Select(e => Slash(e.RelativePath))));
        writer.Write("\n</directory_structure>\n\n");

        writer.Write("<files>\nThis section contains the contents of the repository's files.\n\n");
        foreach (var f in Order(model))
        {
            // Repomix drops files its security check flags; Skip mode omitting a whole file is the analogue.
            var body = OutputBody.For(f, cfg.SecretScan, out bool skipped);
            if (skipped) continue;
            writer.Write("<file path=\"");
            writer.Write(Slash(f.RelativePath));
            writer.Write("\">\n");
            WriteTrimmed(body, writer);
            if (f.IsTruncated) writer.Write("\n[truncated: file is " + DirectoryTree.FormatSize(f.Size) + "]");
            writer.Write("\n</file>\n\n");
        }
        writer.Write("</files>\n");
    }

    private static string Slash(string path) => path.Replace('\\', '/');

    /// <summary>Repomix order: <c>sortPaths</c> (directories before files at the first differing segment,
    /// otherwise locale order), then a stable sort by ascending Git change count when available.</summary>
    internal static IEnumerable<DumpFile> Order(DumpModel model)
    {
        var files = model.Files.Where(f => !f.IsBinary)
            .Select(f => (File: f, Parts: Slash(f.RelativePath).Split('/'))).ToList();
        files.Sort((a, b) => ComparePaths(a.Parts, b.Parts));
        if (model.GitChangeCounts is not { } counts) return files.Select(x => x.File);
        return files.OrderBy(x => counts.TryGetValue(string.Join('/', x.Parts), out int n) ? n : 0).Select(x => x.File);
    }

    private static int ComparePaths(string[] a, string[] b)
    {
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            if (a[i] == b[i]) continue;
            bool lastA = i == a.Length - 1, lastB = i == b.Length - 1;
            if (!lastA && lastB) return -1;
            if (lastA && !lastB) return 1;
            return Locale(a[i], b[i]);
        }
        return a.Length - b.Length;
    }

    // ponytail: invariant-culture compare stands in for Node's ICU localeCompare; identical for ordinary
    // file names, may differ on exotic Unicode ordering.
    private static int Locale(string a, string b) =>
        CultureInfo.InvariantCulture.CompareInfo.Compare(a, b, CompareOptions.None);

    private sealed class Node
    {
        public readonly List<(string Name, Node Child)> Children = new();
        public readonly Dictionary<string, int> Index = new(StringComparer.Ordinal);
        public bool IsDirectory;
    }

    /// <summary>Repomix's <c>generateTreeString</c>: directories first, then locale order, two-space indent.</summary>
    internal static string Tree(IEnumerable<string> files)
    {
        var root = new Node { IsDirectory = true };
        foreach (string file in files)
        {
            var cur = root;
            string[] parts = file.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                if (!cur.Index.TryGetValue(parts[i], out int at))
                {
                    cur.Children.Add((parts[i], new Node { IsDirectory = i < parts.Length - 1 }));
                    at = cur.Children.Count - 1;
                    cur.Index[parts[i]] = at;
                }
                cur = cur.Children[at].Child;
            }
        }
        var sb = new StringBuilder();
        Append(root, "", sb);
        return sb.ToString().Trim();
    }

    private static void Append(Node node, string prefix, StringBuilder sb)
    {
        node.Children.Sort((a, b) => a.Child.IsDirectory == b.Child.IsDirectory
            ? Locale(a.Name, b.Name) : a.Child.IsDirectory ? -1 : 1);
        foreach (var (name, child) in node.Children)
        {
            sb.Append(prefix).Append(name).Append(child.IsDirectory ? "/" : "").Append('\n');
            if (child.IsDirectory) Append(child, prefix + "  ", sb);
        }
    }

    /// <summary>Repomix always <c>trim()</c>s file content. Two passes over the re-readable body keep memory
    /// constant: the first finds where the last non-whitespace character ends, the second writes from the
    /// first non-whitespace character up to there.</summary>
    private static void WriteTrimmed(OutputBody body, TextWriter writer)
    {
        var buffer = new char[32768];
        long end = 0, position = 0;
        int count;
        using (var reader = body.Open())
            while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
                for (int i = 0; i < count; i++, position++)
                    if (!char.IsWhiteSpace(buffer[i])) end = position + 1;

        using var second = body.Open();
        bool started = false;
        position = 0;
        while (position < end && (count = second.Read(buffer, 0, buffer.Length)) > 0)
        {
            int start = 0, take = (int)Math.Min(count, end - position);
            if (!started)
            {
                while (start < take && char.IsWhiteSpace(buffer[start])) start++;
                started = start < take;
            }
            if (take > start) writer.Write(buffer, start, take - start);
            position += count;
        }
    }
}
