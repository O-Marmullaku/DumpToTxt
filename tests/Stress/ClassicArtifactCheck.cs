using System.Security.Cryptography;
using System.Text;
using DumpToTxt.Core;

internal static class ClassicArtifactCheck
{
    // Independent fixture oracle: expected bytes are hashed as written, never accumulated.
    // Applies to generated UTF-8, uncapped, secret-free fixtures with all text selected.
    public const string PlantedKey = "-----BEGIN PRIVATE KEY-----\nMIIBsecretKEYmaterial1234567890\n-----END PRIVATE KEY-----";

    public static string Verify(string root, string outputPath, SecretScanMode? plantedMode = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var sink = new HashSink(hash);
        using (var writer = new StreamWriter(sink, new UTF8Encoding(true), 65536, leaveOpen: true))
        {
            writer.Write("===== DIRECTORY LIST (filtered) =====\r\nROOT: ");
            writer.Write(root);
            writer.Write("\r\n\r\n");
            var entries = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var path in entries) { writer.Write(path); writer.Write("\r\n"); }
            writer.Write("\r\n\r\n===== FILE CONTENTS (LEGIBLE ONLY) =====\r\n");
            var buffer = new char[32768];
            foreach (var path in entries.Where(File.Exists).Where(p => !p.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)))
            {
                writer.Write("\r\n==============================\r\n");
                writer.Write(path);
                writer.Write("\r\n==============================\r\n");
                using var reader = new StreamReader(path, Encoding.UTF8, true, 65536);
                if (plantedMode is not null)
                {
                    var prefix = new char[PlantedKey.Length];
                    if (reader.ReadBlock(prefix, 0, prefix.Length) != prefix.Length)
                        throw new InvalidDataException("Incomplete planted fixture prefix.");
                    if (new string(prefix) != PlantedKey) throw new InvalidDataException("Unexpected planted fixture prefix.");
                    if (plantedMode == SecretScanMode.Skip)
                    {
                        writer.Write("[content skipped: 1 sensitive value(s) detected]\r\n");
                        continue;
                    }
                    writer.Write(plantedMode == SecretScanMode.Redact ? "[REDACTED:private-key]" : PlantedKey);
                }
                int read; char last = '\0';
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                { writer.Write(buffer, 0, read); last = buffer[read - 1]; }
                if (last != '\n') writer.Write("\r\n");
            }
        }
        string expected = Convert.ToHexString(hash.GetHashAndReset());
        using var actual = File.OpenRead(outputPath);
        string emitted = Convert.ToHexString(SHA256.HashData(actual));
        if (expected != emitted) throw new InvalidDataException($"Independent Classic fixture hash mismatch. Expected {expected}, got {emitted}.");
        return emitted;
    }

    private sealed class HashSink(IncrementalHash hash) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => hash.AppendData(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => hash.AppendData(buffer);
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
