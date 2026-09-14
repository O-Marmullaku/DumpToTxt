using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DumpToTxt.Core;

namespace DumpToTxt.Tests;

public sealed class OutputPublicationTests
{
    [Fact]
    public void ClassicTokenBudgetReturnsExactStatusWithoutChangingOutputBytes()
    {
        const string source = "A bounded source body with enough distinct words to exceed one token.\n";
        using var fixture = new Fixture(source);
        var cfg = fixture.Config(OutputStyle.Classic, OutputTarget.Stdout);
        var uncounted = new DumpEngine().Run(fixture.Source, cfg);
        Assert.False(uncounted.TokensCounted);
        Assert.False(uncounted.TokenBudgetExceeded);
        Assert.Equal(0L, uncounted.TotalTokens);

        cfg.MaxTokens = 1;
        var exceeded = new DumpEngine().Run(fixture.Source, cfg);

        Assert.True(exceeded.TokensCounted);
        Assert.True(exceeded.TokenBudgetExceeded);
        Assert.Equal((long)TokenCounter.Count(source, cfg.TokenEncoding), exceeded.TotalTokens);
        Assert.Equal(Encoding.UTF8.GetBytes(uncounted.Text), Encoding.UTF8.GetBytes(exceeded.Text));

        cfg.MaxTokens = exceeded.TotalTokens;
        var exactBudget = new DumpEngine().Run(fixture.Source, cfg);
        Assert.True(exactBudget.TokensCounted);
        Assert.False(exactBudget.TokenBudgetExceeded);
        Assert.Equal(exceeded.TotalTokens, exactBudget.TotalTokens);
        Assert.Equal(Encoding.UTF8.GetBytes(uncounted.Text), Encoding.UTF8.GetBytes(exactBudget.Text));
    }

    [Fact]
    public void AggregateFindingLimitFailsBeforeAnyOutputIsPublished()
    {
        string source = string.Concat(Enumerable.Repeat("password123\n", 51_000));
        using var fixture = new Fixture(source);
        File.WriteAllText(Path.Combine(fixture.Source, "second.txt"), source, new UTF8Encoding(false));
        var cfg = fixture.Config(OutputStyle.Classic, OutputTarget.File);
        cfg.SecretScan = SecretScanMode.Warn;

        var error = Assert.Throws<InvalidOperationException>(() => new DumpEngine().Run(fixture.Source, cfg));

        Assert.Contains("100,000 sensitive findings across all files", error.Message);
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Theory]
    [InlineData(OutputStyle.Xml, SecretScanMode.Redact, "[REDACTED:common-password]\n")]
    [InlineData(OutputStyle.XmlCompact, SecretScanMode.Redact, "[REDACTED:common-password]\n")]
    [InlineData(OutputStyle.Xml, SecretScanMode.Skip, "")]
    [InlineData(OutputStyle.XmlCompact, SecretScanMode.Skip, "")]
    public void XmlFindingMetadataCannotInsertBodyWhitespace(OutputStyle style, SecretScanMode mode, string expected)
    {
        string source = mode == SecretScanMode.Skip
            ? "-----BEGIN PRIVATE KEY-----\nMIIBsecretKEYmaterial1234567890\n-----END PRIVATE KEY-----\n"
            : "password123\n";
        using var fixture = new Fixture(source);
        var cfg = fixture.Config(style, OutputTarget.Stdout);
        cfg.SecretScan = mode;

        var result = new DumpEngine().Run(fixture.Source, cfg);

        var file = XDocument.Parse(result.Text, LoadOptions.PreserveWhitespace).Root!.Element("files")!.Element("file")!;
        Assert.Single(file.Elements("secret"));
        Assert.Equal(expected, file.Value);
        Assert.Equal(TokenCounter.Count(expected, cfg.TokenEncoding), (int)file.Attribute("tokens")!);
    }

    [Fact]
    public void InvalidAlwaysHidePatternDiagnosticCannotDiscloseItsValue()
    {
        var error = Assert.ThrowsAny<ArgumentException>(() => SecretScanner.CompilePatterns(["valid", "PRIVATE-VALUE["]));
        Assert.Contains("row 2", error.Message);
        Assert.DoesNotContain("PRIVATE-VALUE", error.ToString(), StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void StdoutCancellationAfterFirstBodyWriteDoesNotReturnSuccessOrPublishAFile()
    {
        const string start = "BODY-START-7319\n";
        string source = start + string.Concat(Enumerable.Repeat("bounded source line\n", 6000)) + "BODY-END-7319\n";
        using var fixture = new Fixture(source);
        using var cancellation = new CancellationTokenSource();
        using var writer = new CancelOnBodyWriter(start, cancellation);
        var cfg = fixture.Config(OutputStyle.Classic, OutputTarget.Stdout);
        DumpResult? result = null;

        Assert.ThrowsAny<OperationCanceledException>(() => result = new DumpEngine().Run(
            fixture.Source, cfg, cancellationToken: cancellation.Token, textOutput: writer));

        Assert.Null(result);
        Assert.True(writer.CancelledOnBody);
        Assert.InRange(writer.FirstBodyWriteCharacters, start.Length, source.Length - 1);
        Assert.Contains(start, writer.Captured, StringComparison.Ordinal);
        Assert.DoesNotContain("BODY-END-7319", writer.Captured, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public void CancellingWordBeforeRenderingPreservesExistingOutputAndLeavesNoTemporaryArtifact()
    {
        using var fixture = new Fixture("document source\n");
        Directory.CreateDirectory(fixture.Output);
        string existing = Path.Combine(fixture.Output, "existing.docx");
        byte[] sentinel = Encoding.UTF8.GetBytes("already present; never truncate");
        File.WriteAllBytes(existing, sentinel);
        using var cancellation = new CancellationTokenSource();
        var cfg = fixture.Config(OutputStyle.Docx, OutputTarget.File);
        bool reachedRendering = false;

        Assert.ThrowsAny<OperationCanceledException>(() => new DumpEngine().Run(fixture.Source, cfg,
            cancellationToken: cancellation.Token,
            progress: new InlineProgress(progress =>
            {
                if (progress.Phase != "Rendering") return;
                reachedRendering = true;
                cancellation.Cancel();
            })));

        Assert.True(reachedRendering);
        Assert.Equal(sentinel, File.ReadAllBytes(existing));
        Assert.Equal(new[] { existing }, Directory.GetFiles(fixture.Output));
    }

    [Fact]
    public void ReplacingSourceIdentityDuringReviewCannotChangeTheCheckedPublishedBody()
    {
        const string original = "original-version\npassword123\nstable-footer\n";
        const string expected = "original-version\n[REDACTED:common-password]\nstable-footer\n";
        const string replacement = "replacement-version\nunchecked replacement\n";
        using var fixture = new Fixture(original);
        var cfg = fixture.Config(OutputStyle.Json, OutputTarget.File);
        cfg.SecretScan = SecretScanMode.Warn;
        string displaced = Path.Combine(fixture.Source, "displaced-original.bin");
        bool reviewedOriginal = false;

        var result = new DumpEngine().Run(fixture.Source, cfg, reviewSensitiveData: review =>
        {
            var finding = Assert.Single(review.Items);
            Assert.Equal("input.txt", finding.RelativePath);
            Assert.Equal("common-password", finding.RuleId);
            Assert.Equal(2, finding.Line);
            Assert.DoesNotContain("password123", finding.Preview, StringComparison.Ordinal);
            reviewedOriginal = true;
            File.Move(fixture.Input, displaced);
            File.WriteAllText(fixture.Input, replacement, new UTF8Encoding(false));
            return SensitiveDataDecision.Redact;
        });

        Assert.True(reviewedOriginal);
        Assert.Equal(replacement, File.ReadAllText(fixture.Input));
        Assert.Equal(original, File.ReadAllText(displaced));
        Assert.Equal(SecretScanMode.Redact, result.EffectiveSecretScan);
        Assert.False(result.Cancelled);
        Assert.Empty(result.Text);
        using var json = JsonDocument.Parse(File.ReadAllText(result.OutputPath!));
        var file = Assert.Single(json.RootElement.GetProperty("fileList").EnumerateArray());
        string body = file.GetProperty("content").GetString()!;
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        Assert.Equal((long)Encoding.UTF8.GetByteCount(original), file.GetProperty("size").GetInt64());
        Assert.DoesNotContain("displaced-original", json.RootElement.GetProperty("directoryStructure").GetString()!, StringComparison.Ordinal);
        Assert.DoesNotContain("unchecked replacement", body, StringComparison.Ordinal);
    }

    [Fact]
    public void OutputDirectoryThatIsAFileFailsWithoutChangingThatFile()
    {
        using var fixture = new Fixture("export source\n");
        byte[] sentinel = Encoding.UTF8.GetBytes("destination is an existing regular file");
        File.WriteAllBytes(fixture.Output, sentinel);
        var cfg = fixture.Config(OutputStyle.Classic, OutputTarget.File);
        DumpResult? result = null;

        Assert.ThrowsAny<IOException>(() => result = new DumpEngine().Run(fixture.Source, cfg));

        Assert.Null(result);
        Assert.Equal(sentinel, File.ReadAllBytes(fixture.Output));
        Assert.Equal(new[] { fixture.Output }, Directory.GetFiles(fixture.Work));
        Assert.Equal("export source\n", File.ReadAllText(fixture.Input));
    }

    [Fact]
    public void AnExistingDumpRemainsByteIdenticalWhenTheSameSourceIsExportedAgain()
    {
        using var fixture = new Fixture("first version\n");
        var cfg = fixture.Config(OutputStyle.Classic, OutputTarget.File);
        var first = new DumpEngine().Run(fixture.Source, cfg);
        byte[] saved = File.ReadAllBytes(first.OutputPath!);
        File.WriteAllText(fixture.Input, "second version\n", new UTF8Encoding(false));

        var second = new DumpEngine().Run(fixture.Source, cfg);

        Assert.NotEqual(first.OutputPath, second.OutputPath);
        Assert.Equal(saved, File.ReadAllBytes(first.OutputPath!));
        Assert.Contains("second version", File.ReadAllText(second.OutputPath!), StringComparison.Ordinal);
        Assert.Equal(2, Directory.GetFiles(fixture.Output).Length);
        Assert.DoesNotContain(Directory.GetFiles(fixture.Output), path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class InlineProgress(Action<DumpProgress> report) : IProgress<DumpProgress>
    {
        public void Report(DumpProgress value) => report(value);
    }

    private sealed class CancelOnBodyWriter(string marker, CancellationTokenSource cancellation) : TextWriter
    {
        private readonly StringBuilder _captured = new();
        public override Encoding Encoding => Encoding.UTF8;
        public bool CancelledOnBody { get; private set; }
        public int FirstBodyWriteCharacters { get; private set; }
        public string Captured => _captured.ToString();
        public override void Write(char value) => Append(new string(value, 1));
        public override void Write(string? value) { if (value is not null) Append(value.AsSpan()); }
        public override void Write(char[] buffer, int index, int count) => Append(buffer.AsSpan(index, count));
        public override void Write(ReadOnlySpan<char> value) => Append(value);
        private void Append(ReadOnlySpan<char> value)
        {
            _captured.Append(value);
            if (CancelledOnBody || !value.Contains(marker.AsSpan(), StringComparison.Ordinal)) return;
            CancelledOnBody = true;
            FirstBodyWriteCharacters = value.Length;
            cancellation.Cancel();
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Work { get; } = Path.Combine(Path.GetTempPath(), "dtt-publication-audit-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Work, "source");
        public string Input => Path.Combine(Source, "input.txt");
        public string Output => Path.Combine(Work, "output");
        public Fixture(string content)
        {
            Directory.CreateDirectory(Source);
            File.WriteAllText(Input, content, new UTF8Encoding(false));
        }
        public DumpConfig Config(OutputStyle style, OutputTarget target)
        {
            var cfg = DumpConfig.CreateDefault();
            cfg.Style = style;
            cfg.OutputTarget = target;
            cfg.OutputDir = Output;
            cfg.SecretScan = SecretScanMode.Off;
            return cfg;
        }
        public void Dispose() => Directory.Delete(Work, true);
    }
}
