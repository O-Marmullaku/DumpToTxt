using System.Text;

namespace DumpToTxt.Core;

/// <summary>Plain-text pack: summary header, directory tree, then delimited file blocks.</summary>
public sealed class PlainFormatter : IDumpFormatter
{
    public OutputStyle Style => OutputStyle.Plain;

    public string Render(DumpModel model, DumpConfig cfg)
    {
        var sb = new StringBuilder();
        sb.Append("============================================================\r\n");
        sb.Append("DumpToTxt — Plain output\r\n");
        sb.Append("============================================================\r\n");
        sb.Append("Root: ").Append(model.Root).Append("\r\n");
        sb.Append("Files: ").Append(model.Files.Count).Append("\r\n");
        sb.Append("Total size: ").Append(DirectoryTree.FormatSize(model.TotalSize)).Append("\r\n");
        sb.Append("\r\n");

        sb.Append("----- Directory structure -----\r\n");
        sb.Append(DirectoryTree.RenderOrNote(model.Files)).Append("\r\n\r\n");

        sb.Append("----- Files -----\r\n");
        foreach (var f in model.Files)
        {
            sb.Append("\r\n================ File: ").Append(f.RelativePath).Append(" ================\r\n");
            sb.Append(f.Content);
            if (!f.Content.EndsWith('\n')) sb.Append("\r\n");
        }
        return sb.ToString();
    }
}
