using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DumpToTxt.Core;

namespace DumpToTxt.Tests;

public sealed class StreamingExportTests
{
    [Theory]
    [InlineData(SecretScanMode.Redact)]
    [InlineData(SecretScanMode.Skip)]
    public void CustomOverlapDoesNotExposePemMaterial(SecretScanMode mode)
    {
        const string body = "-----BEGIN PRIVATE KEY-----\nMIIBsecretKEYmaterial1234567890\n-----END PRIVATE KEY-----\n";
        var findings = SecretScanner.Scan(body, false, sensitiveValues: SecretScanner.CompilePatterns(["PRIVATE KEY"]));
        string actual = SecretScanner.ContentForOutput(body, findings, mode, out bool skipped);
        Assert.DoesNotContain("MIIBsecret", actual);
        Assert.Equal(mode == SecretScanMode.Skip, skipped);
    }

    [Fact]
    public void ShortCustomValueNeverAppearsInFindingMetadata()
    {
        var findings = SecretScanner.Scan("abc", false, sensitiveValues: SecretScanner.CompilePatterns(["abc"]));
        Assert.DoesNotContain("abc", Assert.Single(findings).Preview);
    }

    [Fact]
    public void OverlappingAutomaticPreviewCannotReExposeAnAlwaysHiddenSuffix()
    {
        using var fixture = new Fixture("person@example.com\n");
        var cfg = fixture.Config(OutputStyle.Json);
        cfg.SecretScan = SecretScanMode.Warn;
        cfg.SensitiveValuePatterns = ["com"];
        var output = new DumpEngine().Run(fixture.Root, cfg).Text;
        Assert.DoesNotContain("com", output, StringComparison.Ordinal);
        Assert.Contains("email-address", output);
    }

    [Fact]
    public void InvalidRequiredPatternFailsExplicitly()
    {
        Assert.ThrowsAny<ArgumentException>(() => SecretScanner.CompilePatterns(["["]));
    }

    [Fact]
    public void RequiredPatternTimeoutFailsClosed()
    {
        var pattern = new Regex("(a+)+$", RegexOptions.None, TimeSpan.FromMilliseconds(1));
        var error = Assert.Throws<InvalidOperationException>(() => SecretScanner.Scan(
            new string('a', 65536) + "!", false, sensitiveValues: [pattern], includeAutomatic: false));
        Assert.Contains("timed out", error.Message);
    }

    [Fact]
    public void RawCandidateCollectionHasAnExplicitMemoryLimit()
    {
        var error = Assert.Throws<InvalidOperationException>(() => SecretScanner.Scan(
            new string('x', 100001), false, sensitiveValues: SecretScanner.CompilePatterns(["x"]), includeAutomatic: false));
        Assert.Contains("100,000 candidate", error.Message);
    }

    [Fact]
    public void FindingLineNumbersIncludeLoneCr()
    {
        var findings = SecretScanner.Scan("first\rsecond\rpassword123", false);
        Assert.Equal(3, Assert.Single(findings).Line);
    }

    [Fact]
    public void XmlPreservesSupplementaryUnicodeAndCountsEmittedBody()
    {
        using var fixture = new Fixture("hello 😀\u0001 world\n");
        var cfg = fixture.Config(OutputStyle.Xml);
        var result = new DumpEngine().Run(fixture.Root, cfg);
        var file = XDocument.Parse(result.Text).Root!.Element("files")!.Element("file")!;
        Assert.Contains("😀", file.Value);
        Assert.DoesNotContain((char)1, file.Value);
        Assert.Equal(TokenCounter.Count(file.Value, cfg.TokenEncoding), (int)file.Attribute("tokens")!);
    }

    [Theory]
    [InlineData(OutputStyle.Xml)]
    [InlineData(OutputStyle.XmlCompact)]
    public void XmlRoundTripsSourceLineEndingsAndBodyTokens(OutputStyle style)
    {
        const string source = "first\r\nsecond\rthird\nfourth\r\n";
        using var fixture = new Fixture(source);
        var cfg = fixture.Config(style);
        var file = XDocument.Parse(new DumpEngine().Run(fixture.Root, cfg).Text)
            .Root!.Element("files")!.Element("file")!;
        Assert.Equal(source, file.Value);
        Assert.Equal(TokenCounter.Count(file.Value, cfg.TokenEncoding), (int)file.Attribute("tokens")!);
    }

    [Theory]
    [InlineData(OutputStyle.MarkdownAi)]
    [InlineData(OutputStyle.MarkdownCompact)]
    public void MarkdownLayoutsDiscloseTruncation(OutputStyle style)
    {
        using var fixture = new Fixture("some source longer than the cap\n");
        var cfg = fixture.Config(style);
        cfg.MaxFileSizeBytes = 5;
        Assert.Contains("[truncated:", new DumpEngine().Run(fixture.Root, cfg).Text);
    }

    [Fact]
    public void DocxXmlIsValidAndStylesAreConnected()
    {
        using var fixture = new Fixture("hello 😀\u0001 world\n");
        var cfg = fixture.Config(OutputStyle.Docx);
        cfg.OutputTarget = OutputTarget.File;
        cfg.OutputDir = fixture.Output;
        var result = new DumpEngine().Run(fixture.Root, cfg);
        using var zip = ZipFile.OpenRead(result.OutputPath!);
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels")))
        {
            using var input = entry.Open();
            XDocument.Load(input);
        }
        var relationships = zip.GetEntry("word/_rels/document.xml.rels");
        Assert.NotNull(relationships);
        using var reader = new StreamReader(relationships!.Open());
        Assert.Contains("styles.xml", reader.ReadToEnd());
    }

    [Theory]
    [InlineData(TokenEncoding.O200kBase)]
    [InlineData(TokenEncoding.Cl100kBase)]
    public void StreamedTokenCountsEqualWholeInputAcrossAdversarialBoundaries(TokenEncoding encoding)
    {
        string[] rows = ["word 😀 café 中文\r\n", "};\n/// comment /\n", "line\n\nnext\n",
            "a\r\n  \t\r\nb\n", "punctuation!!!\n/next\n", "123456789012\n", "can't I'll I'M\n"];
        string input = string.Concat(Enumerable.Range(0, 7000).Select(i => rows[i % rows.Length]));
        using var reader = new StringReader(input);
        Assert.Equal(TokenCounter.Count(input, encoding), TokenCounter.Count(reader, encoding));
    }

    [Theory]
    [InlineData(SecretScanMode.Warn)]
    [InlineData(SecretScanMode.Redact)]
    [InlineData(SecretScanMode.Skip)]
    public void StreamedSecretRegionsEqualWholeInput(SecretScanMode mode)
    {
        var source = new StringBuilder();
        while (source.Length < 65510) source.Append("public void Example() { return; }\r\n");
        source.Append("password =\n\"my-long-private-value\";\n")
            .Append("password123\n: label\n")
            .Append("phone\n=\n+41 79 123 45 67\n")
            .Append("4111 1111 1111 1111\nCH93 0076 2011 6238 5295 7\n")
            .Append("osman@example.com\n")
            .Append("-----BEGIN PRIVATE KEY-----\nMIIBprivateKEYmaterial12345\n-----END PRIVATE KEY-----\n");
        while (source.Length < 180000) source.Append("public void Next() { return; }\n");
        source.Append("password\n=\npassword123\n");
        string input = source.ToString();
        var expectedFindings = SecretScanner.Scan(input, false);
        string expectedBody = SecretScanner.ContentForOutput(input, expectedFindings, mode, out _);
        using var fixture = new Fixture(input);
        var cfg = fixture.Config(OutputStyle.Json);
        cfg.SecretScan = mode;
        var result = new DumpEngine().Run(fixture.Root, cfg);
        var file = JsonDocument.Parse(result.Text).RootElement.GetProperty("fileList")[0];
        Assert.Equal(expectedBody, file.GetProperty("content").GetString());
        var actual = file.GetProperty("secrets").EnumerateArray().Select(s =>
            (s.GetProperty("rule").GetString(), s.GetProperty("line").GetInt32(), s.GetProperty("preview").GetString())).ToArray();
        Assert.Equal(expectedFindings.Select(s => ((string?)s.RuleId, s.Line, (string?)s.Preview)), actual);
        Assert.Equal(TokenCounter.Count(expectedBody, cfg.TokenEncoding), file.GetProperty("tokens").GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PemEndAfterTheLastCandidateBoundaryStaysInTheSameRegion(bool nestedHeader)
    {
        const string header = "-----BEGIN PRIVATE KEY-----\n";
        const string end = "-----END PRIVATE KEY-----";
        string prefix = "safe\n";
        string nested = nestedHeader ? header : "";
        string input = prefix + header + nested + new string('A', 65536 - prefix.Length - header.Length - nested.Length - end.Length - 1) + "\n" + end;
        Assert.Equal(65536, input.Length);
        var findings = SecretScanner.Scan(input, false);
        string expected = SecretScanner.ContentForOutput(input, findings, SecretScanMode.Redact, out _);
        using var fixture = new Fixture(input);
        var cfg = fixture.Config(OutputStyle.Json);
        cfg.SecretScan = SecretScanMode.Redact;
        var file = JsonDocument.Parse(new DumpEngine().Run(fixture.Root, cfg).Text).RootElement.GetProperty("fileList")[0];
        Assert.Equal(expected, file.GetProperty("content").GetString());
        Assert.Equal(findings.Count, file.GetProperty("secrets").GetArrayLength());
    }

    [Theory]
    [InlineData(SecretScanMode.Off)]
    [InlineData(SecretScanMode.Warn)]
    [InlineData(SecretScanMode.Redact)]
    [InlineData(SecretScanMode.Skip)]
    public void AlwaysHideCrossingByteCapRedactsTheEmittedPrefix(SecretScanMode mode)
    {
        using var fixture = new Fixture("safe\nMY-PRIVATE-VALUE\n");
        var cfg = fixture.Config(OutputStyle.Json);
        cfg.SecretScan = mode;
        cfg.SensitiveValuePatterns = ["MY-PRIVATE-VALUE"];
        cfg.MaxFileSizeBytes = 10;
        var result = new DumpEngine().Run(fixture.Root, cfg);
        var file = JsonDocument.Parse(result.Text).RootElement.GetProperty("fileList")[0];
        Assert.Equal("safe\n[REDACTED:custom-sensitive-value]", file.GetProperty("content").GetString());
        Assert.True(file.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void AlwaysHideAnchoredToTheCappedBodyRetainsItsExistingSemantics()
    {
        using var fixture = new Fixture("PRIVATE-tail");
        var cfg = fixture.Config(OutputStyle.Json);
        cfg.SensitiveValuePatterns = ["^PRIVATE$"];
        cfg.MaxFileSizeBytes = 7;
        var file = JsonDocument.Parse(new DumpEngine().Run(fixture.Root, cfg).Text)
            .RootElement.GetProperty("fileList")[0];
        Assert.Equal("[REDACTED:custom-sensitive-value]", file.GetProperty("content").GetString());
    }

    [Fact]
    public void CappedUtf8SplitKeepsHistoricalReplacementCharacter()
    {
        using var fixture = new Fixture("a😀z");
        var cfg = fixture.Config(OutputStyle.Json);
        cfg.MaxFileSizeBytes = 3;
        var result = new DumpEngine().Run(fixture.Root, cfg);
        Assert.Equal("a�", JsonDocument.Parse(result.Text).RootElement.GetProperty("fileList")[0].GetProperty("content").GetString());
    }

    [Fact]
    public void PartialBomCapsMatchTheHistoricalBoundedStreamReader()
    {
        Encoding[] encodings = [new UTF8Encoding(true), new UnicodeEncoding(false, true),
            new UnicodeEncoding(true, true), new UTF32Encoding(false, true), new UTF32Encoding(true, true)];
        using var fixture = new Fixture("");
        foreach (var encoding in encodings)
        {
            byte[] source = encoding.GetPreamble().Concat(encoding.GetBytes("abc")).ToArray();
            File.WriteAllBytes(Path.Combine(fixture.Root, "input.txt"), source);
            for (int limit = 1; limit <= encoding.GetPreamble().Length; limit++)
            {
                using var memory = new MemoryStream(source, 0, limit);
                using var expectedReader = new StreamReader(memory, Encoding.UTF8, true);
                string expected = expectedReader.ReadToEnd();
                var cfg = fixture.Config(OutputStyle.Json);
                cfg.MaxFileSizeBytes = limit;
                var actual = JsonDocument.Parse(new DumpEngine().Run(fixture.Root, cfg).Text)
                    .RootElement.GetProperty("fileList")[0].GetProperty("content").GetString();
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    public void ReviewUsesAnImmutableSourceAndConfigurationSnapshot()
    {
        using var fixture = new Fixture("password123\n");
        var cfg = fixture.Config(OutputStyle.Json);
        cfg.SecretScan = SecretScanMode.Warn;
        var result = new DumpEngine().Run(fixture.Root, cfg, reviewSensitiveData: _ =>
        {
            File.WriteAllText(Path.Combine(fixture.Root, "input.txt"), "CHANGED SOURCE");
            cfg.SecretScan = SecretScanMode.Off;
            cfg.Style = OutputStyle.Classic;
            return SensitiveDataDecision.Redact;
        });
        Assert.Equal(SecretScanMode.Redact, result.EffectiveSecretScan);
        Assert.DoesNotContain("password123", result.Text);
        Assert.DoesNotContain("CHANGED SOURCE", result.Text);
        JsonDocument.Parse(result.Text);
    }

    [Fact]
    public void SingleFileDoesNotInventoryItsSiblings()
    {
        using var fixture = new Fixture("selected");
        File.WriteAllText(Path.Combine(fixture.Root, "unrelated.txt"), "unrelated contents");
        var cfg = fixture.Config(OutputStyle.Classic);
        var result = new DumpEngine().Run(Path.Combine(fixture.Root, "input.txt"), cfg);
        Assert.Contains("selected", result.Text);
        Assert.DoesNotContain("unrelated", result.Text);
        Assert.Equal(1, result.FilesIncluded);
    }

    [Fact]
    public void SafetyWindowGuardDoesNotPublishOrWriteToStdout()
    {
        using var fixture = new Fixture(new string('x', 2 * 1024 * 1024 + 100));
        var cfg = fixture.Config(OutputStyle.Classic);
        cfg.SecretScan = SecretScanMode.Warn;
        using var output = new StringWriter();
        var error = Assert.Throws<InvalidOperationException>(() => new DumpEngine().Run(fixture.Root, cfg, textOutput: output));
        Assert.Contains("safety window", error.Message);
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public void FilePublicationDoesNotReturnAnInMemoryArtifact()
    {
        using var fixture = new Fixture("small output");
        var cfg = fixture.Config(OutputStyle.Classic);
        cfg.OutputTarget = OutputTarget.File;
        cfg.OutputDir = fixture.Output;
        var result = new DumpEngine().Run(fixture.Root, cfg);
        Assert.Empty(result.Text);
        Assert.Null(result.BinaryContent);
        Assert.Contains("small output", File.ReadAllText(result.OutputPath!));
        Assert.Single(Directory.GetFiles(fixture.Output));
    }

    [Fact]
    public async Task ConcurrentPublicationNeverOverwritesAnotherRun()
    {
        using var fixture = new Fixture("password123\n");
        var cfg = fixture.Config(OutputStyle.Classic);
        cfg.SecretScan = SecretScanMode.Warn;
        cfg.OutputTarget = OutputTarget.File;
        cfg.OutputDir = fixture.Output;
        using var barrier = new Barrier(2);
        Task<DumpResult> Run() => Task.Run(() => new DumpEngine().Run(fixture.Root, cfg,
            reviewSensitiveData: _ =>
            {
                Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
                return SensitiveDataDecision.Redact;
            }));
        var results = await Task.WhenAll(Run(), Run());
        Assert.NotEqual(results[0].OutputPath, results[1].OutputPath);
        Assert.Equal(File.ReadAllBytes(results[0].OutputPath!), File.ReadAllBytes(results[1].OutputPath!));
        Assert.Equal(2, Directory.GetFiles(fixture.Output).Length);
    }

    [Fact]
    public void CancellationDuringPublicationLeavesNoFinalOrTemporaryFile()
    {
        using var fixture = new Fixture("some output\n");
        var cfg = fixture.Config(OutputStyle.Classic);
        cfg.OutputTarget = OutputTarget.File;
        cfg.OutputDir = fixture.Output;
        using var cancellation = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() => new DumpEngine().Run(fixture.Root, cfg,
            cancellationToken: cancellation.Token,
            progress: new InlineProgress(p => { if (p.Phase == "Rendering") cancellation.Cancel(); })));
        Assert.True(!Directory.Exists(fixture.Output) || Directory.GetFiles(fixture.Output).Length == 0);
    }

    private sealed class InlineProgress(Action<DumpProgress> report) : IProgress<DumpProgress>
    {
        public void Report(DumpProgress value) => report(value);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _work = Path.Combine(Path.GetTempPath(), "dtt-export-audit-" + Guid.NewGuid().ToString("N"));
        public string Root => Path.Combine(_work, "source");
        public string Output => Path.Combine(_work, "output");
        public Fixture(string content)
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "input.txt"), content, new UTF8Encoding(false));
        }
        public DumpConfig Config(OutputStyle style)
        {
            var cfg = DumpConfig.CreateDefault();
            cfg.Style = style;
            cfg.OutputTarget = OutputTarget.Stdout;
            cfg.SecretScan = SecretScanMode.Off;
            return cfg;
        }
        public void Dispose() => Directory.Delete(_work, true);
    }
}
