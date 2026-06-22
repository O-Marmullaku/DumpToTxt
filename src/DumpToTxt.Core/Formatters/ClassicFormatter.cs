using System.Text;

namespace DumpToTxt.Core;

/// <summary>
/// The original DumpToTxt layout: a flat "DIRECTORY LIST (filtered)" section followed by
/// "FILE CONTENTS (LEGIBLE ONLY)" blocks. Uniform CRLF for structure; source line-endings
/// preserved inside each file's content (intentionally normalized vs the legacy PowerShell
/// tool's mixed LF/CRLF). Each file's content block renders byte-identically to the v2 P1 engine,
/// and the DIRECTORY LIST is now deterministically sorted OrdinalIgnoreCase (P1 emitted raw
/// enumeration order) — pinned by ClassicGoldenTests. NOTE: <em>which</em> files survive is an
/// engine concern — P3's ignore engine (.gitignore/.dumptotxtignore aware) and default-on binary
/// detection change the surviving set vs P1, so whole-output parity with P1 is not guaranteed on a
/// real repo (see <see cref="DumpEngine"/>).
/// </summary>
public sealed class ClassicFormatter : IDumpFormatter
{
    private const string Sep = "==============================";

    public OutputStyle Style => OutputStyle.Classic;

    public string Render(DumpModel model, DumpConfig cfg)
    {
        var sb = new StringBuilder();
        sb.Append("===== DIRECTORY LIST (filtered) =====\r\n");
        sb.Append("ROOT: ").Append(model.Root).Append("\r\n");
        sb.Append("\r\n");

        foreach (var entry in model.Entries)
            sb.Append(entry.FullName).Append("\r\n");

        sb.Append("\r\n\r\n===== FILE CONTENTS (LEGIBLE ONLY) =====\r\n");

        if (model.SkippedSingleFile is { } skipped)
        {
            sb.Append(model.SingleFileSkippedUnchanged
                ? "\r\n[Skipped: file has no changes since HEAD]\r\n"
                : "\r\n[Skipped: file not considered legible or is excluded]\r\n");
            sb.Append(skipped).Append("\r\n");
        }
        else
        {
            foreach (var f in model.Files)
                AppendFileBlock(sb, f);
        }

        return sb.ToString();
    }

    private static void AppendFileBlock(StringBuilder sb, DumpFile f)
    {
        sb.Append("\r\n").Append(Sep).Append("\r\n");
        sb.Append(f.FullName).Append("\r\n");
        sb.Append(Sep).Append("\r\n");
        if (f.IsBinary)
        {
            sb.Append("[binary file — content skipped]\r\n");
            return;
        }
        sb.Append(f.Content);
        if (!f.Content.EndsWith('\n')) sb.Append("\r\n");
        if (f.IsTruncated)
            sb.Append("[truncated: file is ").Append(DirectoryTree.FormatSize(f.Size)).Append("]\r\n");
    }
}
