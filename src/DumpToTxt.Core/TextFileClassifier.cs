using System.Text;

namespace DumpToTxt.Core;

/// <summary>Bounded file-head classifier shared by preview discovery and dump binary handling.</summary>
public static class TextFileClassifier
{
    private const int SniffBytes = 8000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>True for text-like files, including BOM-encoded Unicode and ordinary ANSI text.</summary>
    public static bool IsTextLike(string path, CancellationToken cancellationToken = default)
    {
        if (!TryReadHead(path, cancellationToken, out var bytes, out int read, out bool completeHead)) return false;
        if (read == 0 || HasUnicodeBom(bytes, read)) return true;
        if (ContainsNul(bytes, read)) return false;

        int controls = 0, highBytes = 0;
        for (int i = 0; i < read; i++)
        {
            byte b = bytes[i];
            if (b >= 0x80) highBytes++;
            else if (b < 0x20 && b is not 9 and not 10 and not 12 and not 13) controls++;
        }
        if (controls * 100 > read * 5) return false;

        try { StrictUtf8.GetDecoder().GetCharCount(bytes, 0, read, flush: completeHead); }
        catch (DecoderFallbackException)
        {
            // Preserve normal Windows-1252/ANSI text, but reject high-entropy binary heads.
            if (highBytes * 100 > read * 30) return false;
        }
        return true;
    }

    /// <summary>The legacy dump check: NUL means binary, except UTF-16 BOMs.</summary>
    internal static bool IsBinaryForDump(string path)
    {
        if (!TryReadHead(path, default, out var bytes, out int read, out _) || read == 0) return false;
        if (read >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE)
            || (bytes[0] == 0xFE && bytes[1] == 0xFF))) return false;
        return ContainsNul(bytes, read);
    }

    private static bool TryReadHead(string path, CancellationToken cancellationToken,
        out byte[] bytes, out int read, out bool completeHead)
    {
        bytes = Array.Empty<byte>();
        read = 0;
        completeHead = false;
        try
        {
            using Stream stream = cancellationToken.CanBeCanceled
                ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan)
                : File.OpenRead(path);
            int length = (int)Math.Min(stream.Length, SniffBytes);
            completeHead = stream.Length <= SniffBytes;
            if (length == 0) return true;
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
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
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
