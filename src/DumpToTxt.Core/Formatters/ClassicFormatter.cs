using System.Text;

namespace DumpToTxt.Core;

/// <summary>
/// The original DumpToTxt layout: a flat "DIRECTORY LIST (filtered)" section followed by
/// "FILE CONTENTS (LEGIBLE ONLY)" blocks. Output is byte-identical to the v2 P1 engine
/// (uniform CRLF for structure; source line-endings preserved inside each file's content),
/// which is intentionally normalized vs the legacy PowerShell tool's mixed LF/CRLF.
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

        foreach (var entry in model.ListedEntries)
            sb.Append(entry).Append("\r\n");

        sb.Append("\r\n\r\n===== FILE CONTENTS (LEGIBLE ONLY) =====\r\n");

        if (model.SkippedSingleFile is { } skipped)
        {
            sb.Append("\r\n[Skipped: file not considered legible or is excluded]\r\n");
            sb.Append(skipped).Append("\r\n");
        }
        else
        {
            foreach (var f in model.Files)
                AppendFileBlock(sb, f.FullName, f.Content);
        }

        return sb.ToString();
    }

    private static void AppendFileBlock(StringBuilder sb, string fullName, string content)
    {
        sb.Append("\r\n").Append(Sep).Append("\r\n");
        sb.Append(fullName).Append("\r\n");
        sb.Append(Sep).Append("\r\n");
        sb.Append(content);
        if (!content.EndsWith('\n')) sb.Append("\r\n");
    }
}
