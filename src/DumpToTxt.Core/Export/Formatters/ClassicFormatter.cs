using System.Text;

namespace DumpToTxt.Core;

/// <summary>
/// Classic's flat directory list and legible file blocks use CRLF structure and preserve source
/// line endings within bodies. ClassicGoldenTests pins the C# output contract; this is not byte
/// parity with the PowerShell edition. Inventory is sorted OrdinalIgnoreCase by the engine.
/// Discovery filters and binary detection determine the surviving set; explicit Redact/Skip and
/// user-declared always-hide values protect content in this format too.
/// </summary>
public sealed class ClassicFormatter : IDumpFormatter
{
    private const string Sep = "==============================";

    public OutputStyle Style => OutputStyle.Classic;

    public string Render(DumpModel model, DumpConfig cfg)
    {
        using var writer = new BoundedTextWriter(DumpEngine.MaxMaterializedCharacters);
        Write(model, cfg, writer);
        return writer.ToString();
    }

    public void Write(DumpModel model, DumpConfig cfg, TextWriter writer)
    {
        var sb = new TextOutput(writer);
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
                AppendFileBlock(sb, f, cfg.SecretScan);
        }

    }

    private static void AppendFileBlock(TextOutput sb, DumpFile f, SecretScanMode mode)
    {
        sb.Append("\r\n").Append(Sep).Append("\r\n");
        sb.Append(f.FullName).Append("\r\n");
        sb.Append(Sep).Append("\r\n");
        if (f.IsBinary)
        {
            sb.Append("[binary file — content skipped]\r\n");
            return;
        }
        var content = OutputBody.For(f, mode, out bool skipped);
        if (skipped)
            sb.Append("[content skipped: ").Append(f.Secrets.Count).Append(" sensitive value(s) detected]\r\n");
        else
        {
            sb.Append(content);
            if (!content.EndsWith('\n')) sb.Append("\r\n");
        }
        if (f.IsTruncated)
            sb.Append("[truncated: file is ").Append(DirectoryTree.FormatSize(f.Size)).Append("]\r\n");
    }
}
