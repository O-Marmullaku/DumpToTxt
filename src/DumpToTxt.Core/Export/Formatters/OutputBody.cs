using System.Text;
using System.Xml;

namespace DumpToTxt.Core;

internal sealed class OutputBody
{
    private readonly DumpFile _file;
    private readonly string? _text;
    public bool Skipped { get; }
    private OutputBody(DumpFile file, SecretScanMode mode)
    {
        _file = file;
        if (file.OutputContentPath is null)
        {
            _text = SecretScanner.ContentForOutput(file, mode, out bool skipped);
            Skipped = skipped;
        }
        else Skipped = file.ContentOmitted;
    }
    public static OutputBody For(DumpFile file, SecretScanMode mode, out bool skipped)
    {
        var body = new OutputBody(file, mode);
        skipped = body.Skipped;
        return body;
    }
    public TextReader Open() => _text is not null ? new StringReader(_text) : StagedContent.OpenReader(_file.OutputContentPath!);
    public bool EndsWith(char value) => _text is not null ? _text.EndsWith(value) : _file.LastCharacter == value;
    public void WriteTo(TextWriter writer)
    {
        using var reader = Open();
        StagedContent.CopyCharacters(reader, writer, long.MaxValue);
    }
    public void WriteXml(XmlWriter writer)
    {
        if (_text is not null)
        {
            using var cleaned = new StringWriter();
            new XmlSafeWriter(cleaned).Write(_text);
            writer.WriteString(cleaned.ToString());
            return;
        }
        using var reader = Open();
        var buffer = new char[32768];
        int read;
        char high = '\0';
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            int start = 0;
            if (high != '\0')
            {
                writer.WriteString(new string([high, buffer[0]]));
                high = '\0'; start = 1;
            }
            if (read > start && char.IsHighSurrogate(buffer[read - 1])) high = buffer[--read];
            if (read > start) writer.WriteChars(buffer, start, read - start);
        }
    }
    public string Fence()
    {
        if (_text is null) return new string('`', Math.Max(3, _file.BacktickRun + 1));
        int max = 0, run = 0;
        foreach (char c in _text) { run = c == '`' ? run + 1 : 0; max = Math.Max(max, run); }
        return new string('`', Math.Max(3, max + 1));
    }
}

/// <summary>Fluent layout writing without retaining the rendered artifact.</summary>
internal sealed class TextOutput(TextWriter writer)
{
    public TextOutput Append(object? value)
    {
        if (value is OutputBody body) body.WriteTo(writer);
        else writer.Write(value);
        return this;
    }
}

internal sealed class CancellationTextWriter(TextWriter target, CancellationToken cancellationToken) : TextWriter
{
    public override Encoding Encoding => target.Encoding;
    public override void Write(char value) { cancellationToken.ThrowIfCancellationRequested(); target.Write(value); }
    public override void Write(string? value) { cancellationToken.ThrowIfCancellationRequested(); target.Write(value); }
    public override void Write(char[] buffer, int index, int count) { cancellationToken.ThrowIfCancellationRequested(); target.Write(buffer, index, count); }
    public override void Flush() => target.Flush();
}

internal sealed class BoundedTextWriter(int limit) : StringWriter
{
    private void Check(int additional)
    {
        if (GetStringBuilder().Length > limit - additional)
            throw new InvalidOperationException($"This destination supports at most {limit:N0} text characters. Choose file output for a larger dump. Nothing was copied.");
    }
    public override void Write(char value) { Check(1); base.Write(value); }
    public override void Write(string? value) { Check(value?.Length ?? 0); base.Write(value); }
    public override void Write(char[] buffer, int index, int count) { Check(count); base.Write(buffer, index, count); }
}
