namespace DumpToTxt.Core;

/// <summary>
/// Gitingest's <c>digest.txt</c>: <c>Directory structure:</c> with a box-drawn tree rooted at the folder
/// name, a blank line, then one 48-<c>=</c> framed <c>FILE:</c> block per file in tree order. Each folder lists
/// README first, then files, hidden files, folders and hidden folders. The summary Gitingest prints to the
/// console is not part of its file, so it is not part of this one either.
/// </summary>
public sealed class GitingestFormatter : IDumpFormatter
{
    private static readonly string Separator = new('=', 48);

    public OutputStyle Style => OutputStyle.Gitingest;

    public string Render(DumpModel model, DumpConfig cfg)
    {
        using var writer = new BoundedTextWriter(DumpEngine.MaxMaterializedCharacters);
        Write(model, cfg, writer);
        return writer.ToString();
    }

    private sealed class Node
    {
        public readonly List<(string Name, Node Child)> Children = new();
        public readonly Dictionary<string, int> Index = new(StringComparer.Ordinal);
        public bool IsDirectory;
        public string Path = "";
    }

    public void Write(DumpModel model, DumpConfig cfg, TextWriter writer)
    {
        var root = new Node { IsDirectory = true };
        foreach (var entry in model.Entries.Where(e => !e.IsDirectory))
        {
            string rel = entry.RelativePath.Replace('\\', '/');
            var cur = root;
            string[] parts = rel.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                if (!cur.Index.TryGetValue(parts[i], out int at))
                {
                    cur.Children.Add((parts[i], new Node
                    {
                        IsDirectory = i < parts.Length - 1, Path = string.Join('/', parts, 0, i + 1),
                    }));
                    at = cur.Children.Count - 1;
                    cur.Index[parts[i]] = at;
                }
                cur = cur.Children[at].Child;
            }
        }
        Sort(root);

        writer.Write("Directory structure:\n");
        var order = new List<string>();
        if (model.IsSingleFile)
            foreach (var (name, child) in root.Children) WriteTree(name, child, "", true, writer, order);
        else
        {
            string name = System.IO.Path.GetFileName(model.Root.TrimEnd('\\', '/'));
            WriteTree(name.Length == 0 ? model.Root : name, root, "", true, writer, order);
        }
        writer.Write('\n');

        var files = model.Files.ToDictionary(f => f.RelativePath.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
        bool first = true;
        foreach (string path in order)
        {
            if (!files.TryGetValue(path, out var f)) continue;   // listed, but its content was not selected
            if (!first) writer.Write('\n');
            first = false;
            writer.Write(Separator + "\nFILE: " + path + "\n" + Separator + "\n");
            WriteContent(f, cfg, writer);
            writer.Write("\n\n");
        }
    }

    private static void WriteContent(DumpFile f, DumpConfig cfg, TextWriter writer)
    {
        if (f.IsBinary) { writer.Write("[Binary file]"); return; }
        if (f.Size == 0) { writer.Write("[Empty file]"); return; }
        var body = OutputBody.For(f, cfg.SecretScan, out bool skipped);
        if (skipped) { writer.Write($"[content skipped: {f.Secrets.Count} secret(s) detected]"); return; }
        body.WriteTo(writer);
        if (f.IsTruncated) writer.Write("\n[truncated: file is " + DirectoryTree.FormatSize(f.Size) + "]");
    }

    /// <summary>Gitingest's <c>sort_children</c>: (group, lowercased name) with groups README, file,
    /// hidden file, directory, hidden directory.</summary>
    private static void Sort(Node node)
    {
        static (int, string) Key((string Name, Node Child) c)
        {
            string name = c.Name.ToLowerInvariant();
            if (!c.Child.IsDirectory)
                return (name == "readme" || name.StartsWith("readme.", StringComparison.Ordinal) ? 0
                    : name.StartsWith('.') ? 2 : 1, name);
            return (name.StartsWith('.') ? 4 : 3, name);
        }
        node.Children.Sort((a, b) =>
        {
            var (ga, na) = Key(a);
            var (gb, nb) = Key(b);
            return ga != gb ? ga.CompareTo(gb) : string.CompareOrdinal(na, nb);
        });
        foreach (var (_, child) in node.Children) Sort(child);
    }

    private static void WriteTree(string name, Node node, string prefix, bool last, TextWriter writer, List<string> order)
    {
        writer.Write(prefix + (last ? "└── " : "├── ") + name + (node.IsDirectory ? "/" : "") + "\n");
        if (!node.IsDirectory) { order.Add(node.Path); return; }
        prefix += last ? "    " : "│   ";
        for (int i = 0; i < node.Children.Count; i++)
            WriteTree(node.Children[i].Name, node.Children[i].Child, prefix, i == node.Children.Count - 1, writer, order);
    }
}
