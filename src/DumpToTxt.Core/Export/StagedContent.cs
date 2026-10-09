using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace DumpToTxt.Core;

public sealed record DumpProgress(string Phase, string? Path, int FilesProcessed, long BytesProcessed);

/// <summary>One-run, file-backed snapshots. Nothing in this directory is a published artifact.</summary>
internal sealed class StagedContent : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DumpToTxt", Guid.NewGuid().ToString("N"));
    private int _sequence;
    internal const int ProcessingWindowChars = 2 * 1024 * 1024;
    public StagedContent()
    {
        string parent = Path.GetDirectoryName(_directory)!;
        Directory.CreateDirectory(parent);
        if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The temporary DumpToTxt directory is a link. Choose a regular user temporary directory.");
        Directory.CreateDirectory(_directory);
    }
    public string NewPath() => Path.Combine(_directory, (++_sequence).ToString("D8") + ".tmp");
    public static StreamWriter CreateWriter(string path) => new(path, false, new UTF8Encoding(false, true), 16384);
    public static StreamReader OpenReader(string path) => new(path, new UTF8Encoding(false, true), false, 16384);

    public (string Path, string ScanPath, long Characters, long Size, long Consumed, bool Truncated) Snapshot(
        string source, long allowed, bool inspectBeyondCap, CancellationToken cancellationToken)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
            FileOptions.SequentialScan);
        long size = input.Length;
        long consumed = Math.Min(size, Math.Max(0, allowed));
        bool truncated = size > consumed;
        string path = NewPath();
        long characters = DecodeTo(path, consumed, truncated);
        string scanPath = path;
        if (inspectBeyondCap && truncated && consumed > 0)
        {
            // The same handle continues to deny writers while we snapshot the remainder needed to
            // detect secrets crossing the cap. Only the capped snapshot can contribute output.
            scanPath = NewPath();
            DecodeTo(scanPath, size, false);
        }
        return (path, scanPath, characters, size, consumed, truncated);

        long DecodeTo(string destination, long byteLimit, bool clipped)
        {
            using var output = CreateWriter(destination);
            if (byteLimit == 0) return 0;
            input.Position = 0;
            var bytes = new byte[32768];
            var chars = new char[32768];
            int headLength = 0;
            while (headLength < Math.Min(TextFileClassifier.SniffBytes, size))
            {
                int read = input.Read(bytes, headLength, (int)Math.Min(TextFileClassifier.SniffBytes, size) - headLength);
                if (read == 0) throw new IOException($"File ended unexpectedly while reading its encoding: {source}");
                headLength += read;
            }
            long remaining = byteLimit, characterCount = 0;
            int includedHead = (int)Math.Min(remaining, headLength);
            Encoding encoding = TextFileClassifier.DetectTextEncoding(bytes.AsSpan(0, headLength), out int fullBomLength, source);
            int bomLength = fullBomLength;
            if (clipped && includedHead < fullBomLength)
            {
                // StreamReader historically inferred encoding from the capped bytes themselves.
                // An incomplete BOM therefore follows its lenient fallback, not the full-file BOM.
                encoding = (Encoding)TextFileClassifier.DetectTextEncoding(bytes.AsSpan(0, includedHead), out bomLength).Clone();
                encoding.DecoderFallback = new DecoderReplacementFallback("\uFFFD");
            }
            Decoder decoder = encoding.GetDecoder();
            remaining -= includedHead;
            Decode(bytes.AsSpan(Math.Min(bomLength, includedHead), Math.Max(0, includedHead - bomLength)), false);
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = input.Read(bytes, 0, (int)Math.Min(bytes.Length, remaining));
                if (read == 0) throw new IOException($"File changed or ended unexpectedly while reading: {source}");
                remaining -= read;
                Decode(bytes.AsSpan(0, read), false);
            }
            // Preserve the historical capped-read replacement at a clipped multibyte boundary,
            // while rejecting malformed complete sequences during all ordinary decoding.
            if (clipped) decoder.Fallback = new DecoderReplacementFallback("\uFFFD");
            Decode(ReadOnlySpan<byte>.Empty, true);
            return characterCount;

            void Decode(ReadOnlySpan<byte> data, bool flush)
            {
                while (!data.IsEmpty || flush)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    decoder.Convert(data, chars, flush, out int usedBytes, out int usedChars, out bool complete);
                    output.Write(chars, 0, usedChars);
                    characterCount += usedChars;
                    data = data[usedBytes..];
                    if (complete) break;
                }
            }
        }
    }

    public static void CopyCharacters(TextReader reader, TextWriter writer, long count,
        CancellationToken cancellationToken = default)
    {
        var buffer = new char[32768];
        while (count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = reader.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (read == 0)
            {
                if (count != long.MaxValue && count < int.MaxValue)
                    throw new InvalidDataException("Staged content ended before a recorded sensitive span.");
                break;
            }
            writer.Write(buffer, 0, read);
            // long.MaxValue denotes copy-to-end, not a required source length.
            if (count != long.MaxValue) count -= read;
        }
    }

    public void Dispose()
    {
        // The absolute directory is generated and owned exclusively by this instance.
        try { Directory.Delete(_directory, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>A bounded processing region is cut only at proven boundaries, never at an arbitrary offset.</summary>
internal static class ProcessingRegions
{
    // General user regexes can use anchors/lookarounds across the entire file; they cannot be chunked.
    public static IEnumerable<string> Read(TextReader reader, bool automaticSecrets,
        bool wholeFileRequired, CancellationToken cancellationToken)
    {
        // Ordinary newline-delimited regions stay below the large-object heap threshold.
        // Long indivisible regions still retain the same explicit safety window below.
        var buffer = new char[8192];
        var pending = new StringBuilder();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = reader.Read(buffer, 0, buffer.Length);
            if (read > 0) pending.Append(buffer, 0, read);
            if (read == 0)
            {
                if (pending.Length > 0) yield return pending.ToString();
                yield break;
            }
            if (pending.Length < 32768) continue;
            string text = pending.ToString();
            int cut = wholeFileRequired ? 0 : FindBoundary(text, automaticSecrets);
            if (cut > 0)
            {
                yield return text[..cut];
                pending.Remove(0, cut);
            }
            if (pending.Length > StagedContent.ProcessingWindowChars)
                throw new InvalidOperationException(wholeFileRequired
                    ? "A file requiring whole-file always-hide regex matching exceeds the supported 2,097,152-character safety window. No output was published."
                    : "A file has a token or sensitive-data region longer than the supported 2,097,152-character safety window without a safe boundary. No output was published.");
        }
    }

    private static readonly Regex OpenPem = new(@"-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY-----", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private static readonly Regex ClosePem = new(@"-----END (?:RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY-----", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private static readonly Regex AssignmentTail = new(@"(?:phone|mobile|tel(?:ephone)?|password|passwd|pwd|api[_-]?key|secret|token|access[_-]?key|client[_-]?secret)[""']?\s*(?:[:=]\s*[""']?\s*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static int FindBoundary(string text, bool automaticSecrets)
    {
        // Both installed Tiktoken pretokenizers terminate their newline alternatives before the
        // following non-whitespace character. Keeping the complete whitespace run on the left
        // also preserves their trailing-whitespace lookahead. No BPE merge crosses this boundary.
        var pemRegions = new List<(int Start, int End)>();
        if (automaticSecrets)
        {
            int position = 0;
            while (position < text.Length)
            {
                Match begin = OpenPem.Match(text, position);
                if (!begin.Success) break;
                Match end = ClosePem.Match(text, begin.Index + begin.Length);
                pemRegions.Add((begin.Index, end.Success ? end.Index + end.Length : int.MaxValue));
                if (!end.Success) break;
                // Match the scanner's first-BEGIN through first-END semantics, including nested headers.
                position = end.Index + end.Length;
            }
        }
        for (int i = text.Length - 2; i > 0; i--)
        {
            if (text[i] != '\n' || char.IsWhiteSpace(text[i + 1])) continue;
            // o200k's punctuation alternative may absorb slashes AFTER a newline.
            if (!automaticSecrets)
            {
                if (text[i + 1] == '/') continue;
                return i + 1;
            }
            if (pemRegions.Any(region => region.Start < i + 1 && i + 1 < region.End)) continue;
            // The automatic assignment rules alone can consume a newline between a keyword,
            // operator and value. Common-password lookahead also inspects the next non-space.
            if (text[i + 1] is ':' or '=') continue;
            if (AssignmentTail.IsMatch(text[..(i + 1)])) continue;
            return i + 1;
        }
        return 0;
    }
}

/// <summary>Shared XML 1.0 scalar sanitation, used before both counting and rendering.</summary>
internal sealed class XmlSafeWriter(TextWriter target) : TextWriter
{
    private char _high;
    public override Encoding Encoding => target.Encoding;
    public override void Write(char c)
    {
        if (_high != '\0')
        {
            if (char.IsLowSurrogate(c)) { target.Write(_high); target.Write(c); _high = '\0'; return; }
            _high = '\0';
        }
        if (char.IsHighSurrogate(c)) _high = c;
        else if (XmlConvert.IsXmlChar(c)) target.Write(c);
    }
    public override void Write(char[] buffer, int index, int count)
    {
        for (int i = index; i < index + count; i++) Write(buffer[i]);
    }
    public override void Write(string? value) { if (value is not null) foreach (char c in value) Write(c); }
}
