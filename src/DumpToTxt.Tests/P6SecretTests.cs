using System.Text;
using System.Text.Json;
using DumpToTxt.Core;

namespace DumpToTxt.Tests;

/// <summary>P6 secret scan: per-rule detection on fixtures, the Warn/Redact/Skip actions surfaced in the
/// non-Classic formatters, the guarantee that scanning NEVER alters Classic's golden bytes, the
/// entropy detector being off by default, the allowlist, and config round-trip of the new fields.</summary>
public class P6SecretTests
{
    private static readonly UTF8Encoding NoBom = new(false);

    private static string NewTree()
    {
        string root = Path.Combine(Path.GetTempPath(), "dtt-p6-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void W(string root, string rel, string content)
    {
        string p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content, NoBom);
    }

    private static JsonElement RenderJson(string root, DumpConfig cfg)
    {
        cfg.Style = OutputStyle.Json;
        cfg.OutputTarget = OutputTarget.Stdout;
        return JsonDocument.Parse(new DumpEngine().Run(root, cfg).Text).RootElement;
    }

    private static string RenderText(string root, DumpConfig cfg, OutputStyle style)
    {
        cfg.Style = style;
        cfg.OutputTarget = OutputTarget.Stdout;
        return new DumpEngine().Run(root, cfg).Text;
    }

    // Representative live-shaped samples per curated rule (synthetic — not real credentials).
    private static readonly string AwsKey = "AKIAIOSFODNN7EXAMPLE";
    private static readonly string GcpKey = "AIza" + new string('a', 35);
    private static readonly string GithubTok = "ghp_" + new string('A', 36);
    private static readonly string SlackTok = "xoxb-" + new string('1', 12);
    private static readonly string StripeKey = "sk_live_" + new string('A', 24);
    private static readonly string AnthropicKey = "sk-ant-" + new string('A', 24);
    private static readonly string OpenAiKey = "sk-" + new string('A', 32);
    private static readonly string NpmTok = "npm_" + new string('a', 36);
    private static readonly string SendgridKey = "SG." + new string('a', 22) + "." + new string('b', 43);
    private static readonly string PemKey = "-----BEGIN RSA PRIVATE KEY-----";

    // ---------- detection per rule ----------

    [Theory]
    [InlineData(nameof(AwsKey), "aws-access-key")]
    [InlineData(nameof(GcpKey), "gcp-api-key")]
    [InlineData(nameof(GithubTok), "github-token")]
    [InlineData(nameof(SlackTok), "slack-token")]
    [InlineData(nameof(StripeKey), "stripe-secret")]
    [InlineData(nameof(AnthropicKey), "anthropic-key")]
    [InlineData(nameof(OpenAiKey), "openai-key")]
    [InlineData(nameof(NpmTok), "npm-token")]
    [InlineData(nameof(SendgridKey), "sendgrid-key")]
    [InlineData(nameof(PemKey), "private-key")]
    public void Scan_DetectsEachRule(string sampleName, string expectedRule)
    {
        string sample = (string)typeof(P6SecretTests)
            .GetField(sampleName, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(null)!;
        var findings = SecretScanner.Scan($"const value = {sample}\n", entropy: false);
        Assert.Contains(findings, f => f.RuleId == expectedRule);
        // The preview never re-leaks the secret (no full match in the masked preview).
        Assert.All(findings, f => Assert.DoesNotContain(sample, f.Preview));
    }

    [Fact]
    public void Scan_GenericAssignment_NarrowsToValue()
    {
        var findings = SecretScanner.Scan("password = \"supersecretvalue123\"\n", entropy: false);
        var f = Assert.Single(findings, x => x.RuleId == "generic-secret-assign");
        Assert.Equal("supersecretvalue123".Length, f.Length);   // span is the VALUE, not the whole assignment
    }

    [Fact]
    public void Scan_PemPrivateKey_BlockDetectedAndFullyRedacted()
    {
        string body = "MIIBOgIBAAJBAKj34GkxFhD90vcNLYLInFEX6Ppy1tPf9Cnzj4p4WGeKLs1Pt8Qu";
        string content = $"{PemKey}\n{body}\n-----END RSA PRIVATE KEY-----\n";
        var findings = SecretScanner.Scan(content, entropy: false);
        Assert.Contains(findings, f => f.RuleId == "private-key");
        string redacted = SecretScanner.Redact(content, findings);
        Assert.Contains("[REDACTED:private-key]", redacted);
        Assert.DoesNotContain(body, redacted);              // key material gone, not just the header line
    }

    [Fact]
    public void Scan_HeaderlessPemKey_RedactsToEnd_NoBodyLeak()
    {
        // A PEM whose END marker is missing (size-cap truncation or a fragment) must STILL fully redact —
        // regression guard for the leak where only the 31-char BEGIN header was masked.
        string body = "MIIBOgIBAAJBAKj34GkxFhD90vcNLYLInFEX6Ppy1tPf9Cnzj4p4WGeKLs1Pt8Qu";
        string content = $"{PemKey}\n{body}\n";   // NO -----END----- line
        var findings = SecretScanner.Scan(content, entropy: false);
        Assert.Contains(findings, f => f.RuleId == "private-key");
        string redacted = SecretScanner.Redact(content, findings);
        Assert.DoesNotContain(body, redacted);               // body redacted to EOF, not left in the open
        Assert.Contains("[REDACTED:private-key]", redacted);
    }

    [Fact]
    public void Scan_PemHeaderInProse_NoEnd_DoesNotEatTrailingProse()
    {
        // A README that merely MENTIONS the PEM header (placeholder, no -----END-----) must NOT have the rest
        // of the document wiped under Redact. Regression for the END-less fallback that consumed to EOF: the
        // bounded fallback now redacts only the header + any base64 body lines, stopping at the first prose.
        string content =
            "# Setup\r\n" +
            "Put your key after the header:\r\n" +
            "-----BEGIN PRIVATE KEY-----\r\n" +
            "<your key here>\r\n" +
            "\r\n" +
            "## Usage\r\n" +
            "Run the installer, then enjoy the rest of this long readme body.\r\n";
        var findings = SecretScanner.Scan(content, entropy: false);
        Assert.Contains(findings, f => f.RuleId == "private-key");          // the header is still flagged
        string redacted = SecretScanner.Redact(content, findings);
        Assert.DoesNotContain("-----BEGIN PRIVATE KEY-----", redacted);     // header itself redacted
        Assert.Contains("## Usage", redacted);                             // prose AFTER the header survives
        Assert.Contains("Run the installer", redacted);
    }

    [Fact]
    public void Scan_RealPemBody_NoEnd_RedactsKeyMaterial()
    {
        // A genuinely truncated key (header + base64 body, END clipped by a size cap) must STILL fully redact
        // its base64 material — the bounded fallback consumes whole-line base64 runs to EOF here.
        string body = "MIIBOgIBAAJBAKj34GkxFhD90vcNLYLInFEX6Ppy1tPf9Cnzj4p4WGeKLs1Pt8Qu";
        string content = $"{PemKey}\n{body}\n";   // NO -----END-----
        var findings = SecretScanner.Scan(content, entropy: false);
        Assert.Contains(findings, f => f.RuleId == "private-key");
        string redacted = SecretScanner.Redact(content, findings);
        Assert.DoesNotContain(body, redacted);                             // key material gone
        Assert.Contains("[REDACTED:private-key]", redacted);
    }

    [Fact]
    public void Scan_PemBody_TrailingWhitespace_NoEnd_RedactsKeyMaterial()
    {
        // Regression: a truncated key whose base64 body line carries TRAILING WHITESPACE (END clipped) must
        // still redact fully. The END-less fallback's end-of-line lookahead used to require base64 to abut the
        // newline, so a stray trailing space failed it and the whole body line (and everything below) leaked.
        string body = "MIIBOgIBAAJBAKj34GkxFhD90vcNLYLInFEX6Ppy1tPf9Cnzj4p4WGeKLs1Pt8Qu";
        string content = $"{PemKey}\n{body}   \nmoreKeyBytesAAAABBBBCCCCDDDD\n";   // trailing spaces, NO END
        var findings = SecretScanner.Scan(content, entropy: false);
        Assert.Contains(findings, f => f.RuleId == "private-key");
        string redacted = SecretScanner.Redact(content, findings);
        Assert.DoesNotContain(body, redacted);                             // body line gone despite trailing WS
        Assert.DoesNotContain("moreKeyBytesAAAABBBBCCCCDDDD", redacted);   // and the line after it (no cascade leak)
        Assert.Contains("[REDACTED:private-key]", redacted);
    }

    [Fact]
    public void Scan_PemBody_ShortFinalLine_NoEnd_RedactsKeyMaterial()
    {
        // Regression: a truncated key whose FINAL base64 remnant line is short (< 16 chars — an arbitrary
        // mid-body size-cap clip) must still redact fully. The old {16,} floor let that short tail survive.
        string content = $"{PemKey}\nMIIBOgIBAAJBAKj34GkxFhD90vcNLY\nwEAAQ==\n";   // 7-char final line, NO END
        var findings = SecretScanner.Scan(content, entropy: false);
        Assert.Contains(findings, f => f.RuleId == "private-key");
        string redacted = SecretScanner.Redact(content, findings);
        Assert.DoesNotContain("wEAAQ==", redacted);                        // short remnant line redacted, not leaked
        Assert.Contains("[REDACTED:private-key]", redacted);
    }

    [Fact]
    public void Scan_CleanContent_HasNoFindings()
    {
        var findings = SecretScanner.Scan("public class A { public int X = 5; }\n// just code\n", entropy: false);
        Assert.Empty(findings);
    }

    // ---------- entropy detector (off by default) ----------

    [Fact]
    public void Scan_HighEntropy_OnlyWhenEnabled()
    {
        const string blob = "const k = aB3dE5gH7jK9mN1pQ2sT4vW6xZ8cF0hJ5kL7nR9wY\n";  // high variety, no named rule
        Assert.Empty(SecretScanner.Scan(blob, entropy: false));                        // off by default -> nothing
        var on = SecretScanner.Scan(blob, entropy: true);
        Assert.Contains(on, f => f.RuleId == "high-entropy-string");
    }

    // ---------- allowlist ----------

    [Fact]
    public void Scan_Allowlist_SuppressesMatchingFinding()
    {
        string content = $"const v = {AwsKey}\n";
        Assert.NotEmpty(SecretScanner.Scan(content, entropy: false));
        var allow = SecretScanner.CompileAllowlist(new[] { "EXAMPLE" });   // matched text contains EXAMPLE
        Assert.Empty(SecretScanner.Scan(content, entropy: false, allow));
    }

    [Fact]
    public void Scan_Allowlist_MatchesValueNotKeyName()
    {
        // The allowlist is matched against the detected secret VALUE (group 1 for key=value rules), NOT the key
        // name or surrounding line. "password" (the key name) must NOT suppress; a value substring does.
        string content = "password = \"supersecretvalue123\"\n";
        Assert.NotEmpty(SecretScanner.Scan(content, entropy: false));                          // baseline finding
        var byKeyName = SecretScanner.CompileAllowlist(new[] { "password" });
        Assert.NotEmpty(SecretScanner.Scan(content, entropy: false, byKeyName));               // key name ≠ value
        var byValue = SecretScanner.CompileAllowlist(new[] { "supersecret" });
        Assert.Empty(SecretScanner.Scan(content, entropy: false, byValue));                    // value substring matches
    }

    // ---------- actions surfaced in JSON ----------

    [Fact]
    public void Json_Warn_FlagsButLeavesContentIntact()
    {
        string root = NewTree();
        try
        {
            W(root, "cfg.cs", $"var key = {AwsKey};\n");
            var cfg = DumpConfig.CreateDefault();   // default mode = Warn
            var r = RenderJson(root, cfg);

            Assert.Equal("Warn", r.GetProperty("secretScan").GetString());
            Assert.True(r.GetProperty("secretFindings").GetInt32() >= 1);
            var file = r.GetProperty("fileList")[0];
            Assert.Contains(AwsKey, file.GetProperty("content").GetString());   // Warn = untouched
            Assert.True(file.TryGetProperty("secrets", out var secs) && secs.GetArrayLength() >= 1);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Json_Redact_ReplacesSecretSpan()
    {
        string root = NewTree();
        try
        {
            W(root, "cfg.cs", $"var key = {AwsKey};\n");
            var cfg = DumpConfig.CreateDefault();
            cfg.SecretScan = SecretScanMode.Redact;
            var r = RenderJson(root, cfg);

            string content = r.GetProperty("fileList")[0].GetProperty("content").GetString()!;
            Assert.Contains("[REDACTED:aws-access-key]", content);
            Assert.DoesNotContain(AwsKey, content);                            // raw secret gone
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Json_Skip_DropsFileContent()
    {
        string root = NewTree();
        try
        {
            W(root, "cfg.cs", $"var key = {AwsKey};\n");
            var cfg = DumpConfig.CreateDefault();
            cfg.SecretScan = SecretScanMode.Skip;
            var file = RenderJson(root, cfg).GetProperty("fileList")[0];

            Assert.Equal("", file.GetProperty("content").GetString());
            Assert.True(file.GetProperty("secretsSkipped").GetBoolean());
            Assert.True(file.TryGetProperty("secrets", out var secs) && secs.GetArrayLength() >= 1);  // still reported
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(OutputStyle.Markdown)]
    [InlineData(OutputStyle.Plain)]
    [InlineData(OutputStyle.Xml)]
    public void NonClassic_Redact_ScrubsSecretFromBody(OutputStyle style)
    {
        string root = NewTree();
        try
        {
            W(root, "cfg.cs", $"var key = {AwsKey};\n");
            var cfg = DumpConfig.CreateDefault();
            cfg.SecretScan = SecretScanMode.Redact;
            string text = RenderText(root, cfg, style);
            Assert.DoesNotContain(AwsKey, text);                  // raw secret scrubbed from the rendered body
            Assert.Contains("[REDACTED:aws-access-key]", text);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(OutputStyle.Markdown)]
    [InlineData(OutputStyle.Plain)]
    [InlineData(OutputStyle.Xml)]
    public void NonClassic_Skip_OmitsSecretFromBody(OutputStyle style)
    {
        string root = NewTree();
        try
        {
            W(root, "cfg.cs", $"var key = {AwsKey};\n");
            var cfg = DumpConfig.CreateDefault();
            cfg.SecretScan = SecretScanMode.Skip;
            string text = RenderText(root, cfg, style);
            Assert.DoesNotContain(AwsKey, text);                  // skipped file's body (with the secret) omitted
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Json_Off_OmitsSecretFields()
    {
        string root = NewTree();
        try
        {
            W(root, "cfg.cs", $"var key = {AwsKey};\n");
            var cfg = DumpConfig.CreateDefault();
            cfg.SecretScan = SecretScanMode.Off;
            var r = RenderJson(root, cfg);

            Assert.False(r.TryGetProperty("secretScan", out _));
            Assert.False(r.GetProperty("fileList")[0].TryGetProperty("secrets", out _));
        }
        finally { Directory.Delete(root, true); }
    }

    // ---------- Skip: high-FP generic/entropy hits redact instead of dropping the whole file ----------

    [Fact]
    public void Skip_GenericPlaceholderOnly_RedactsSpan_KeepsFile()
    {
        // A .env.example whose only hit is a generic-rule placeholder must NOT have its whole content dropped
        // under Skip — the high-FP generic/entropy detectors redact their span instead, so legitimate lines
        // survive. Regression: previously one placeholder FP silently nuked the entire file.
        string root = NewTree();
        try
        {
            W(root, ".env.example", "DEBUG=true\nPORT=3000\nAPI_KEY=\"REPLACE_WITH_YOUR_KEY_HERE\"\nNAME=app\n");
            var cfg = DumpConfig.CreateDefault();
            cfg.SecretScan = SecretScanMode.Skip;
            var file = RenderJson(root, cfg).GetProperty("fileList")[0];
            string content = file.GetProperty("content").GetString()!;
            Assert.Contains("DEBUG=true", content);                       // file NOT dropped
            Assert.Contains("PORT=3000", content);
            Assert.DoesNotContain("REPLACE_WITH_YOUR_KEY_HERE", content); // the FP value redacted span-wise
            Assert.False(file.TryGetProperty("secretsSkipped", out var sk) && sk.GetBoolean());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Skip_VendorSecret_OmitsWholeFile_EvenWithOtherContent()
    {
        // A high-confidence vendor secret still drops the WHOLE file under Skip (the safe default for a real key).
        string root = NewTree();
        try
        {
            W(root, "c.cs", $"int keep = 1;\nvar k = {AwsKey};\nint alsoKeep = 2;\n");
            var cfg = DumpConfig.CreateDefault();
            cfg.SecretScan = SecretScanMode.Skip;
            var file = RenderJson(root, cfg).GetProperty("fileList")[0];
            Assert.Equal("", file.GetProperty("content").GetString());    // whole content omitted
            Assert.True(file.GetProperty("secretsSkipped").GetBoolean());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Skip_VendorAndGenericInSameFile_OmitsWholeFile()
    {
        // Vendor precedence: a file carrying BOTH a high-confidence vendor key AND a high-FP generic placeholder
        // is still dropped WHOLE under Skip (the vendor finding wins) — not merely span-redacted-and-kept.
        string root = NewTree();
        try
        {
            W(root, "mix.cs", $"var k = {AwsKey};\nAPI_KEY=\"REPLACE_WITH_YOUR_KEY_HERE\"\n");
            var cfg = DumpConfig.CreateDefault();
            cfg.SecretScan = SecretScanMode.Skip;
            var file = RenderJson(root, cfg).GetProperty("fileList")[0];
            Assert.Equal("", file.GetProperty("content").GetString());    // vendor hit forces whole-file omit
            Assert.True(file.GetProperty("secretsSkipped").GetBoolean());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Skip_VendorTokenInAssignment_OmitsWholeFile()
    {
        // A vendor token written as `keyword = "value"` matches BOTH the vendor rule and generic-secret-assign
        // on the SAME value span. The vendor (high-confidence) rule must win the overlap so the file is dropped
        // WHOLE under Skip — not span-redacted-and-kept as a generic-only false positive would be.
        string root = NewTree();
        try
        {
            W(root, "auth.cs", $"int keep = 1;\ntoken = \"{GithubTok}\";\nint alsoKeep = 2;\n");
            var cfg = DumpConfig.CreateDefault();
            cfg.SecretScan = SecretScanMode.Skip;
            var file = RenderJson(root, cfg).GetProperty("fileList")[0];
            Assert.Equal("", file.GetProperty("content").GetString());    // vendor wins overlap -> whole-file omit
            Assert.True(file.GetProperty("secretsSkipped").GetBoolean());
            bool hasVendor = false, hasGeneric = false;
            foreach (var s in file.GetProperty("secrets").EnumerateArray())
            {
                string rule = s.GetProperty("rule").GetString()!;
                if (rule == "github-token") hasVendor = true;
                if (rule == "generic-secret-assign") hasGeneric = true;
            }
            Assert.True(hasVendor);                                       // attributed to the specific vendor rule
            Assert.False(hasGeneric);                                     // not the overlapping generic assignment
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DumpResult_FilesContentOmitted_CountsWholeFileSkips()
    {
        // The aggregate the GUI notice surfaces: under Skip, count only files dropped WHOLE (a vendor/PEM hit).
        // A generic-placeholder-only file is span-redacted + kept (not counted); a clean file is never counted.
        string root = NewTree();
        try
        {
            W(root, "vendor.cs", $"var k = {AwsKey};\n");
            W(root, ".env.example", "API_KEY=\"REPLACE_WITH_YOUR_KEY_HERE\"\n");
            W(root, "clean.cs", "public class Clean { }\n");
            var cfg = DumpConfig.CreateDefault();
            cfg.OutputTarget = OutputTarget.Stdout;
            cfg.Style = OutputStyle.Plain;
            cfg.SecretScan = SecretScanMode.Skip;
            Assert.Equal(1, new DumpEngine().Run(root, cfg).FilesContentOmitted);   // only vendor.cs dropped whole

            cfg.SecretScan = SecretScanMode.Warn;
            Assert.Equal(0, new DumpEngine().Run(root, cfg).FilesContentOmitted);   // Warn never omits content
        }
        finally { Directory.Delete(root, true); }
    }

    // ---------- token counts describe the EMITTED (sanitized) body ----------

    [Fact]
    public void TokenCount_SkippedFile_IsZero_OffIsPositive()
    {
        // Under Skip the emitted body is empty, so the reported per-file token count must be 0 (not the
        // original content's count) — the token summary describes what the dump actually contains.
        string root = NewTree();
        try
        {
            W(root, "secret.cs", $"var k = {AwsKey};\n" + new string('y', 300) + "\n");
            var skip = DumpConfig.CreateDefault();
            skip.SecretScan = SecretScanMode.Skip;
            int tokSkip = RenderJson(root, skip).GetProperty("fileList")[0].GetProperty("tokens").GetInt32();
            Assert.Equal(0, tokSkip);

            var off = DumpConfig.CreateDefault();
            off.SecretScan = SecretScanMode.Off;
            int tokOff = RenderJson(root, off).GetProperty("fileList")[0].GetProperty("tokens").GetInt32();
            Assert.True(tokOff > 0);
        }
        finally { Directory.Delete(root, true); }
    }

    // ---------- Classic golden safety ----------

    [Theory]
    [InlineData(SecretScanMode.Warn)]
    [InlineData(SecretScanMode.Redact)]
    [InlineData(SecretScanMode.Skip)]
    public void Classic_NeverAltersBytes_RegardlessOfMode(SecretScanMode mode)
    {
        string root = NewTree();
        try
        {
            W(root, "a.cs", $"public class A {{ string k = \"{AwsKey}\"; }}\n");

            var off = DumpConfig.CreateDefault();
            off.Style = OutputStyle.Classic;
            off.OutputTarget = OutputTarget.Stdout;
            off.SecretScan = SecretScanMode.Off;
            string textOff = new DumpEngine().Run(root, off).Text;

            var scanned = DumpConfig.CreateDefault();
            scanned.Style = OutputStyle.Classic;
            scanned.OutputTarget = OutputTarget.Stdout;
            scanned.SecretScan = mode;
            string textScanned = new DumpEngine().Run(root, scanned).Text;

            Assert.Equal(textOff, textScanned);            // scanning/redaction never touches Classic bytes
            Assert.Contains(AwsKey, textScanned);          // Classic keeps the raw content (warn/GUI-only)
        }
        finally { Directory.Delete(root, true); }
    }

    // ---------- config round-trip ----------

    [Fact]
    public void Config_SecretFields_RoundTripThroughSerializeAndResolve()
    {
        var cfg = DumpConfig.CreateDefault();
        cfg.SecretScan = SecretScanMode.Redact;
        cfg.SecretScanEntropy = true;
        cfg.SecretAllowlist = new() { "EXAMPLE", "dummy.*" };
        var back = ConfigStore.Parse(ConfigStore.Serialize(cfg));
        Assert.Equal(SecretScanMode.Redact, back.SecretScan);
        Assert.True(back.SecretScanEntropy);
        Assert.Equal(cfg.SecretAllowlist, back.SecretAllowlist);

        string root = NewTree();
        try
        {
            File.WriteAllText(Path.Combine(root, ".dumptotxt.json"),
                "{ \"SecretScan\": \"Skip\", \"SecretScanEntropy\": true }");
            string none = Path.Combine(root, "none.json");
            var resolved = ConfigStore.Resolve(root, none, none);
            Assert.Equal(SecretScanMode.Skip, resolved.SecretScan);   // threads through the resolver
            Assert.True(resolved.SecretScanEntropy);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void OldConfig_MissingSecretFields_GetsWarnDefault()
    {
        var cfg = ConfigStore.Parse(@"{ ""ExtSet"": ["".cs""] }");   // pre-P6 settings.json
        Assert.Equal(SecretScanMode.Warn, cfg.SecretScan);
        Assert.False(cfg.SecretScanEntropy);
        Assert.Empty(cfg.SecretAllowlist);
    }

    [Fact]
    public void DumpResult_ReportsSecretCounts()
    {
        string root = NewTree();
        try
        {
            W(root, "a.cs", $"var k = {AwsKey};\n");
            W(root, "b.cs", $"var t = {GithubTok};\n");
            W(root, "clean.cs", "public class Clean { }\n");
            var cfg = DumpConfig.CreateDefault();
            cfg.OutputTarget = OutputTarget.Stdout;
            cfg.Style = OutputStyle.Plain;
            var result = new DumpEngine().Run(root, cfg);
            Assert.Equal(2, result.SecretFindingCount);
            Assert.Equal(2, result.FilesWithSecrets);
        }
        finally { Directory.Delete(root, true); }
    }
}
