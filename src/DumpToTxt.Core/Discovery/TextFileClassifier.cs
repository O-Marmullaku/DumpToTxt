using System.Text;
using System.Text.RegularExpressions;

namespace DumpToTxt.Core;

/// <summary>Bounded file-head classifier shared by preview discovery and dump binary handling.</summary>
public static class TextFileClassifier
{
    internal const int SniffBytes = 8000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>True for supported text. Unreadable or unsupported text encodings are reported explicitly.</summary>
    public static bool IsTextLike(string path, CancellationToken cancellationToken = default)
    {
        ReadHead(path, cancellationToken, out var bytes, out int read, out bool completeHead);
        if (read == 0 || HasUnicodeBom(bytes, read)) return true;
        if (ContainsNul(bytes, read)) return false;

        int controls = 0;
        for (int i = 0; i < read; i++)
        {
            byte b = bytes[i];
            if (b < 0x20 && b is not 9 and not 10 and not 12 and not 13) controls++;
        }
        if (controls * 100 > read * 5) return false;

        try { DetectTextEncoding(bytes.AsSpan(0, read), out _, path).GetDecoder().GetCharCount(bytes, 0, read, flush: completeHead); }
        catch (DecoderFallbackException)
        {
            // Distinguish unsupported ANSI text from binary instead of silently corrupting it as UTF-8.
            throw new InvalidDataException($"Cannot read '{path}' as supported text. It may be binary or use an unsupported encoding. Use UTF-8, Unicode with a byte-order mark, or a supported Python encoding declaration.");
        }
        return true;
    }

    /// <summary>The legacy dump check: NUL means binary, except UTF-16 BOMs.</summary>
    internal static bool IsBinaryForDump(string path, CancellationToken cancellationToken = default)
    {
        ReadHead(path, cancellationToken, out var bytes, out int read, out _);
        if (read == 0) return false;
        if (HasUnicodeBom(bytes, read)) return false;
        return ContainsNul(bytes, read);
    }

    private static void ReadHead(string path, CancellationToken cancellationToken,
        out byte[] bytes, out int read, out bool completeHead)
    {
        bytes = Array.Empty<byte>();
        read = 0;
        completeHead = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureRegularFile(path);
            using Stream stream = cancellationToken.CanBeCanceled
                ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan)
                : File.OpenRead(path);
            int length = (int)Math.Min(stream.Length, SniffBytes);
            completeHead = stream.Length <= SniffBytes;
            if (length == 0) return;
            bytes = new byte[length];
            while (read < length)
            {
                int n = cancellationToken.CanBeCanceled
                    ? stream.ReadAsync(bytes.AsMemory(read, length - read), cancellationToken)
                        .AsTask().GetAwaiter().GetResult()
                    : stream.Read(bytes, read, length - read);
                if (n == 0) break;
                read += n;
            }
        }
        catch (OperationCanceledException) { throw; }
    }

    /// <summary>File links remain inventory entries but their targets are never read implicitly.</summary>
    public static void EnsureRegularFile(string path)
    {
        FilesystemSafety.EnsureDirectoryPath(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"File link content was not read: '{path}'. Select the original file instead.");
    }

    /// <summary>Strict decoding: Unicode BOM, Python encoding declaration, or UTF-8 by default.</summary>
    public static Encoding DetectTextEncoding(ReadOnlySpan<byte> head, out int bomLength, string? path = null)
    {
        bomLength = 0;
        if (head.StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF })) { bomLength = 4; return new UTF32Encoding(true, false, true); }
        if (head.StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 })) { bomLength = 4; return new UTF32Encoding(false, false, true); }
        if (head.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) { bomLength = 3; return StrictUtf8; }
        if (head.StartsWith(new byte[] { 0xFE, 0xFF })) { bomLength = 2; return new UnicodeEncoding(true, false, true); }
        if (head.StartsWith(new byte[] { 0xFF, 0xFE })) { bomLength = 2; return new UnicodeEncoding(false, false, true); }
        if (Path.GetExtension(path)?.Equals(".py", StringComparison.OrdinalIgnoreCase) == true
            || Path.GetExtension(path)?.Equals(".pyw", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Python source declares its encoding in a comment on either of the first two lines.
            string prefix = Encoding.ASCII.GetString(head[..Math.Min(head.Length, SniffBytes)]);
            using var lines = new StringReader(prefix);
            for (int i = 0; i < 2 && lines.ReadLine() is { } line; i++)
            {
                var match = Regex.Match(line, @"^[ \t\f]*#.*?coding[:=][ \t]*([-_.a-zA-Z0-9]+)",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (match.Success)
                {
                    string name = match.Groups[1].Value.ToLowerInvariant().Replace('_', '-');
                    if (name is "latin-1" or "latin1" or "iso-latin-1") name = "iso-8859-1";
                    if (name is "utf8" or "utf-8") return StrictUtf8;
                    try
                    {
                        Encoding encoding;
                        if (name.StartsWith("cp", StringComparison.Ordinal) && int.TryParse(name.AsSpan(2), out int codePage))
                            encoding = CodePagesEncodingProvider.Instance.GetEncoding(codePage,
                                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
                                ?? Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                        else
                            encoding = CodePagesEncodingProvider.Instance.GetEncoding(name,
                                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
                                ?? Encoding.GetEncoding(name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                        if (encoding.GetString("# coding: ascii\n"u8) != "# coding: ascii\n")
                            throw new InvalidDataException($"Python encoding must preserve ASCII source: {name}");
                        return encoding;
                    }
                    catch (ArgumentException ex) { throw new InvalidDataException($"Unsupported Python encoding: {name}", ex); }
                }
                if (!Regex.IsMatch(line, @"^[ \t\f]*(#|$)")) break;
            }
        }
        return StrictUtf8;
    }

    private static bool ContainsNul(byte[] bytes, int read)
    {
        for (int i = 0; i < read; i++) if (bytes[i] == 0) return true;
        return false;
    }

    private static bool HasUnicodeBom(byte[] bytes, int read) =>
        read >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
        || read >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF
        || read >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00
        || read >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE
        || read >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF;
}
