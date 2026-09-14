using System.Text;
using System.Text.RegularExpressions;

namespace DumpToTxt.Core;

/// <summary>What the dump does when a secret is detected in a packed file.</summary>
public enum SecretScanMode
{
    /// <summary>Don't scan at all (no findings, content untouched).</summary>
    Off,
    /// <summary>Scan + flag/count, but leave content exactly as-is (warn-only). The default.</summary>
    Warn,
    /// <summary>Replace each detected secret span with a <c>[REDACTED:rule]</c> marker (non-Classic only).</summary>
    Redact,
    /// <summary>Drop the whole file's content (replaced by a marker) when it carries any secret (non-Classic only).</summary>
    Skip,
}

/// <summary>One detected secret in a file's content. <see cref="Preview"/> is masked so a finding never
/// re-leaks the secret into the dump's own output; <see cref="Start"/>/<see cref="Length"/> locate the raw
/// span for redaction.</summary>
public sealed class SecretFinding
{
    /// <summary>Stable rule id, e.g. "aws-access-key".</summary>
    public required string RuleId { get; init; }
    /// <summary>Human-readable rule name, e.g. "AWS Access Key ID".</summary>
    public required string RuleName { get; init; }
    /// <summary>1-based line of the match within the file content.</summary>
    public required int Line { get; init; }
    /// <summary>Char offset of the secret span within the content.</summary>
    public required int Start { get; init; }
    /// <summary>Length of the secret span.</summary>
    public required int Length { get; init; }
    /// <summary>Masked, non-reversible preview (e.g. "AKIA…(20 chars)").</summary>
    public required string Preview { get; init; }
    /// <summary>True for a user-supplied "always hide" pattern. These spans are redacted even when
    /// automatic scanning is off or its action is warn/keep.</summary>
    public bool AlwaysRedact { get; init; }
}

/// <summary>
/// Detects credentials/secrets in packed file content using an embedded, curated, high-precision
/// ruleset (gitleaks-style) plus an optional Shannon-entropy detector (off by default). Pure compute —
/// no IO, no network. Engine scans each file's (post-truncation) content into <see cref="DumpFile.Secrets"/>;
/// formatters then warn / redact / skip per <see cref="DumpConfig.SecretScan"/>. User-declared custom values
/// are always redacted, including in Classic output and when automatic scanning is off.
/// </summary>
public static class SecretScanner
{
    /// <summary>A single embedded detection rule.</summary>
    private sealed class Rule
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required Regex Rx { get; init; }
        public Func<string, bool>? Validator { get; init; }
    }

    // Per-match timeout: a defensive ReDoS guard. The curated patterns are linear, but a pathological
    // input still can't hang a dump — a timed-out rule is simply skipped for that file.
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    private static Regex R(string pattern, RegexOptions extra = RegexOptions.None) =>
        new(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant | extra, MatchTimeout);

    // Curated, high-precision rules. A capture group (1), when present, narrows the reported/redacted span
    // to just the secret value (e.g. the quoted value of a "key = '...'" assignment), not the whole match.
    private static readonly Rule[] Rules =
    {
        new() { Id = "aws-access-key", Name = "AWS Access Key ID",
            Rx = R(@"\b(?:AKIA|ASIA|AGPA|AIDA|AROA|AIPA|ANPA|ANVA|A3T[0-9A-Z])[0-9A-Z]{16}\b") },
        new() { Id = "gcp-api-key", Name = "Google API key",
            Rx = R(@"\bAIza[0-9A-Za-z\-_]{35}\b") },
        new() { Id = "google-oauth", Name = "Google OAuth access token",
            Rx = R(@"\bya29\.[0-9A-Za-z\-_]{20,}") },
        new() { Id = "github-token", Name = "GitHub token",
            Rx = R(@"\b(?:ghp|gho|ghu|ghs|ghr)_[0-9A-Za-z]{36}\b") },
        new() { Id = "github-fine-pat", Name = "GitHub fine-grained PAT",
            Rx = R(@"\bgithub_pat_[0-9A-Za-z_]{82}\b") },
        new() { Id = "slack-token", Name = "Slack token",
            Rx = R(@"\bxox[baprs]-[0-9A-Za-z-]{10,48}\b") },
        new() { Id = "slack-webhook", Name = "Slack webhook URL",
            Rx = R(@"https://hooks\.slack\.com/services/[A-Za-z0-9+/]{43,}") },
        new() { Id = "stripe-secret", Name = "Stripe secret key",
            Rx = R(@"\b[sr]k_(?:live|test)_[0-9A-Za-z]{16,}\b") },
        new() { Id = "anthropic-key", Name = "Anthropic API key",
            Rx = R(@"\bsk-ant-[0-9A-Za-z\-_]{20,}") },
        new() { Id = "openai-key", Name = "OpenAI API key",
            Rx = R(@"\bsk-(?!ant-)(?:proj-)?[0-9A-Za-z]{20,}\b") },
        new() { Id = "npm-token", Name = "npm access token",
            Rx = R(@"\bnpm_[0-9A-Za-z]{36}\b") },
        new() { Id = "sendgrid-key", Name = "SendGrid API key",
            Rx = R(@"\bSG\.[0-9A-Za-z\-_]{22}\.[0-9A-Za-z\-_]{43}\b") },
        new() { Id = "private-key", Name = "Private key (PEM)",
            // Match the WHOLE block (header..END) so redaction removes the key MATERIAL, not just the header.
            // When the END marker is missing (a key truncated by a size cap, or a headerless example), DON'T
            // consume to EOF — that destroys benign docs that merely mention the header (e.g. a README with a
            // "-----BEGIN PRIVATE KEY-----" / "<your key here>" placeholder). Instead bound the END-less
            // fallback to the PEM body that actually follows: optional trailing whitespace ON the header line
            // ([ \t]* — else a stray space/tab after the dashes aborts the fallback and the body leaks), then
            // whole-line base64 runs (ANY length + optional trailing whitespace, so a short remnant or a stray
            // trailing space can't leak), ONLY the encrypted-key headers Proc-Type:/DEK-Info: (NOT any "Word:" —
            // that ate real colon/YAML prose), and blank lines, stopping at the first prose line. Line-stepping
            // is CR/CRLF/LF-aware ((?:\r\n?|\n)) so a lone-CR (classic-Mac) body can't leak. Residuals (Redact
            // only; Skip drops the whole file): a lone base64-charset WORD line right after the header is
            // over-redacted (bounded — stops at the first multi-word/non-base64 line); a body line with an
            // INTERNAL space leaks (real PEM bodies have none). Redacts key bytes, leaves prose intact.
            Rx = R(@"-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY-----[ \t]*" +
                   @"(?:[\s\S]*?-----END (?:RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY-----" +
                   @"|(?:(?:\r\n?|\n)[ \t]*(?:[A-Za-z0-9+/=]+[ \t]*(?=\r\n?|\n|$)|(?:Proc-Type|DEK-Info):[ \t]*\S.*)?)*)") },
        new() { Id = "email-address", Name = "Email address",
            Rx = R(@"\b[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?(?:\.[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?)+\b", RegexOptions.IgnoreCase) },
        new() { Id = "iban", Name = "Bank account (IBAN)",
            Rx = R(@"\b[A-Z]{2}[0-9]{2}(?:[ ]?[A-Z0-9]){11,30}\b", RegexOptions.IgnoreCase),
            Validator = IsValidIban },
        new() { Id = "payment-card", Name = "Payment card number",
            Rx = R(@"(?<!\d)(?:\d[ -]?){12,18}\d(?!\d)"),
            Validator = IsValidPaymentCard },
        new() { Id = "phone-number", Name = "Phone number",
            Rx = R(@"(?:phone|mobile|tel(?:ephone)?)\s*[:=]\s*(\+?[0-9][0-9 ()-]{6,}[0-9])", RegexOptions.IgnoreCase),
            Validator = IsValidPhone },
        new() { Id = "common-password", Name = "Common password",
            Rx = R(@"(?<![A-Z0-9])(password(?:123|[0-9!@#$]{1,8})|passw0rd(?:[0-9!@#$]{0,4})|qwerty[0-9]{0,4}|letmein[0-9]{0,4}|123456(?:78|789)?)(?![A-Z0-9])(?!\s*[:=])", RegexOptions.IgnoreCase) },
        new() { Id = "password-assignment", Name = "Password",
            Rx = R(@"(?:password|passwd|pwd)[""']?\s*[:=]\s*[""']?([0-9A-Za-z\-_!@#$%^&*./+=]{8,128})[""']?", RegexOptions.IgnoreCase) },
        // Generic "key/secret/token/password = <value>" assignment. Higher false-positive risk than the
        // vendor rules, so it's deliberately tight: a known keyword, an assignment, then a quoted 16+ char
        // value. Group 1 (the value) is what gets reported/redacted. Allowlist any false positives.
        new() { Id = "generic-secret-assign", Name = "Generic secret assignment",
            Rx = R(@"(?:api[_-]?key|secret|token|password|passwd|pwd|access[_-]?key|client[_-]?secret)" +
                   @"[""']?\s*[:=]\s*[""']([0-9A-Za-z\-_!@#$%^&*./+=]{16,})[""']", RegexOptions.IgnoreCase) },
    };

    private const string EntropyRuleId = "high-entropy-string";
    private const string EntropyRuleName = "High-entropy string";
    private static readonly Regex EntropyToken =
        R(@"[A-Za-z0-9+/=_\-]{20,}");
    // 4.5 bits/char: random base64/hex secrets score ~5–6, while short all-distinct identifiers/slugs
    // (log2(20) ≈ 4.32) fall under it. Shannon ≠ unpredictability, so some false positives remain — which
    // is why this detector is opt-in (off by default) and has the allowlist escape hatch.
    private const double EntropyThresholdBitsPerChar = 4.5;

    /// <summary>Scans <paramref name="content"/> and returns the detected secrets (sorted by position,
    /// non-overlapping, longest-match-wins). <paramref name="entropy"/> enables the generic high-entropy
    /// detector; <paramref name="allowlist"/> regexes suppress a finding whose detected secret VALUE matches
    /// one (for key=value rules that is the narrowed value span, NOT the key name or the surrounding line).</summary>
    public static IReadOnlyList<SecretFinding> Scan(string content, bool entropy,
        IReadOnlyList<Regex>? allowlist = null, IReadOnlyList<Regex>? sensitiveValues = null,
        bool includeAutomatic = true)
    {
        if (string.IsNullOrEmpty(content)) return Array.Empty<SecretFinding>();

        // Collect raw hits across all rules. User-supplied patterns are separate from the allowlist and
        // deliberately win overlaps: "always hide" must never be neutralized by a false-positive exception.
        var hits = new List<(int Start, int Length, string Id, string Name, bool AlwaysRedact)>();
        if (includeAutomatic)
            foreach (var rule in Rules)
                CollectHits(content, rule, hits);
        if (includeAutomatic && entropy)
            CollectEntropyHits(content, hits);
        CollectCustomHits(content, sensitiveValues, hits);

        if (hits.Count == 0) return Array.Empty<SecretFinding>();

        // Longest first at a given start, then resolve overlaps greedily so each secret is reported once
        // and redaction never produces nested/overlapping splices. The rule-id tiebreaker keeps attribution
        // deterministic when two rules hit the exact same span (List.Sort is not a stable sort).
        hits.Sort((a, b) =>
        {
            if (a.Start != b.Start) return a.Start.CompareTo(b.Start);
            if (a.Length != b.Length) return b.Length.CompareTo(a.Length);
            // Same span, different rules: keep the higher-confidence (vendor/PEM) rule over a high-FP generic/
            // entropy one. A vendor token in an assignment (e.g. token = "ghp_…") matches BOTH the vendor rule
            // and generic-secret-assign on the identical value span; the loser is dropped as an overlap, so the
            // winner determines both attribution (the [REDACTED:rule] marker) and the Skip whole-file decision
            // (OmitsWholeFile keys on the rule's confidence). Vendor must win or a real key in `key = "…"` form
            // would be misclassified generic-only and kept under Skip instead of dropped.
            if (a.AlwaysRedact != b.AlwaysRedact) return a.AlwaysRedact ? -1 : 1;
            bool aHigh = !HighFalsePositiveRules.Contains(a.Id);
            bool bHigh = !HighFalsePositiveRules.Contains(b.Id);
            if (aHigh != bHigh) return aHigh ? -1 : 1;
            return string.CompareOrdinal(a.Id, b.Id);
        });

        var findings = new List<SecretFinding>();
        int coveredTo = -1;
        int line = 1, lineOffset = 0;
        foreach (var h in hits)
        {
            // Custom spans are independent requirements. Never discard an automatic finding merely
            // because a custom value overlaps it: that would hide its confidence and expose its remainder.
            if (!h.AlwaysRedact && h.Start <= coveredTo) continue;
            string matched = content.Substring(h.Start, h.Length);
            if (!h.AlwaysRedact && IsAllowlisted(matched, allowlist)) continue;
            while (lineOffset < h.Start)
            {
                char c = content[lineOffset++];
                if (c == '\r' || c == '\n' && (lineOffset < 2 || content[lineOffset - 2] != '\r')) line++;
            }
            findings.Add(new SecretFinding
            {
                RuleId = h.Id,
                RuleName = h.Name,
                Line = line,
                Start = h.Start,
                Length = h.Length,
                // Required patterns can match a retained prefix/suffix of an automatic preview,
                // including matches introduced by a cap. Keep metadata entirely source-free
                // whenever required patterns are configured; rule and line remain available.
                Preview = sensitiveValues is { Count: > 0 } ? "[hidden value]" : Mask(matched, h.Id),
                AlwaysRedact = h.AlwaysRedact,
            });
            if (!h.AlwaysRedact) coveredTo = h.Start + h.Length - 1;
            if (findings.Count > 100_000)
                throw new InvalidOperationException("Sensitive-data review exceeds 100,000 findings in one file. No output was published.");
        }
        return findings;
    }

    private static void CollectHits(string content, Rule rule,
        List<(int, int, string, string, bool)> hits)
    {
        try
        {
            foreach (Match m in rule.Rx.Matches(content))
            {
                // Narrow to the capture group (the secret value) when the rule defines one.
                Group g = m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1] : m;
                if (g.Length > 0 && (rule.Validator is null || rule.Validator(g.Value)))
                    AddHit(hits, (g.Index, g.Length, rule.Id, rule.Name, false));
            }
        }
        catch (RegexMatchTimeoutException ex) { throw new InvalidOperationException($"Sensitive-data scan timed out ({rule.Id}); no output was published.", ex); }
    }

    private static void CollectEntropyHits(string content, List<(int, int, string, string, bool)> hits)
    {
        try
        {
            foreach (Match m in EntropyToken.Matches(content))
            {
                string tok = m.Value;
                // Require a letter+digit mix so long dictionary words / hex-ish runs don't trip it, then
                // gate on Shannon entropy. Off by default precisely because this is the noisy detector.
                if (!(tok.Any(char.IsLetter) && tok.Any(char.IsDigit))) continue;
                if (ShannonBitsPerChar(tok) >= EntropyThresholdBitsPerChar)
                    AddHit(hits, (m.Index, m.Length, EntropyRuleId, EntropyRuleName, false));
            }
        }
        catch (RegexMatchTimeoutException ex) { throw new InvalidOperationException("Sensitive-data entropy scan timed out; no output was published.", ex); }
    }

    private static void CollectCustomHits(string content, IReadOnlyList<Regex>? patterns,
        List<(int, int, string, string, bool)> hits)
    {
        if (patterns is null) return;
        foreach (var pattern in patterns)
        {
            try
            {
                foreach (Match match in pattern.Matches(content))
                    if (match.Length > 0)
                        AddHit(hits, (match.Index, match.Length, "custom-sensitive-value", "Custom sensitive value", true));
            }
            catch (RegexMatchTimeoutException ex) { throw new InvalidOperationException("An always-hide pattern timed out; no output was published.", ex); }
        }
    }

    private static void AddHit(List<(int, int, string, string, bool)> hits,
        (int, int, string, string, bool) hit)
    {
        if (hits.Count >= 100_000)
            throw new InvalidOperationException("Sensitive-data scanning exceeds 100,000 candidate matches in one processing region. No output was published.");
        hits.Add(hit);
    }

    private static double ShannonBitsPerChar(string s)
    {
        var counts = new Dictionary<char, int>();
        foreach (char c in s) counts[c] = counts.TryGetValue(c, out var n) ? n + 1 : 1;
        double h = 0, len = s.Length;
        foreach (var n in counts.Values)
        {
            double p = n / len;
            h -= p * Math.Log2(p);
        }
        return h;
    }

    private static bool IsValidPhone(string candidate)
    {
        int digits = candidate.Count(char.IsDigit);
        return digits is >= 7 and <= 15;
    }

    private static bool IsValidPaymentCard(string candidate)
    {
        string digits = new(candidate.Where(char.IsDigit).ToArray());
        if (digits.Length is < 13 or > 19) return false;
        if (digits.Distinct().Count() < 2) return false;
        int sum = 0;
        bool doubleDigit = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int n = digits[i] - '0';
            if (doubleDigit)
            {
                n *= 2;
                if (n > 9) n -= 9;
            }
            sum += n;
            doubleDigit = !doubleDigit;
        }
        return sum % 10 == 0;
    }

    private static bool IsValidIban(string candidate)
    {
        string compact = new(candidate.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (compact.Length is < 15 or > 34
            || !char.IsLetter(compact[0]) || !char.IsLetter(compact[1])
            || !char.IsDigit(compact[2]) || !char.IsDigit(compact[3])) return false;

        string rearranged = compact.Substring(4) + compact.Substring(0, 4);
        int remainder = 0;
        foreach (char c in rearranged)
        {
            if (char.IsDigit(c))
                remainder = (remainder * 10 + c - '0') % 97;
            else if (c is >= 'A' and <= 'Z')
            {
                int value = c - 'A' + 10;
                remainder = (remainder * 100 + value) % 97;
            }
            else return false;
        }
        return remainder == 1;
    }

    private static bool IsAllowlisted(string matched, IReadOnlyList<Regex>? allowlist)
    {
        if (allowlist is null) return false;
        foreach (var rx in allowlist)
        {
            try { if (rx.IsMatch(matched)) return true; }
            catch (RegexMatchTimeoutException) { }
        }
        return false;
    }

    /// <summary>Compiles required patterns; invalid patterns fail without disclosing their source values.</summary>
    public static IReadOnlyList<Regex> CompilePatterns(IEnumerable<string> patterns)
    {
        var list = new List<Regex>();
        int row = 0;
        foreach (var p in patterns)
        {
            row++;
            if (string.IsNullOrWhiteSpace(p)) continue;
            try { list.Add(new Regex(p, RegexOptions.CultureInvariant, MatchTimeout)); }
            catch (ArgumentException)
            {
                // Regex parse exceptions include the pattern; do not retain them as inner exceptions.
                throw new ArgumentException($"Always-hide pattern in row {row} is invalid. Correct that pattern before exporting.");
            }
        }
        return list;
    }

    /// <summary>Compiles allowlist patterns; invalid patterns are dropped (never throw at dump time).</summary>
    public static IReadOnlyList<Regex> CompileAllowlist(IEnumerable<string> patterns)
    {
        var result = new List<Regex>();
        foreach (string pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            try { result.Add(new Regex(pattern, RegexOptions.CultureInvariant, MatchTimeout)); }
            catch (ArgumentException) { /* Invalid exceptions cannot weaken redaction. */ }
        }
        return result;
    }

    /// <summary>Per-rule finding counts across the dump (rule id → count), ordered by id.</summary>
    public static IReadOnlyList<KeyValuePair<string, int>> RuleTally(IEnumerable<DumpFile> files)
    {
        var d = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in files)
            foreach (var s in f.Secrets)
                d[s.RuleId] = d.TryGetValue(s.RuleId, out var n) ? n + 1 : 1;
        return d.ToList();
    }

    /// <summary>Non-reversible, type-aware preview. It remains recognizable without reproducing the value.</summary>
    private static string Mask(string secret, string ruleId)
    {
        if (ruleId == "custom-sensitive-value") return "[hidden value]";
        string oneLine = secret.Replace("\r", "").Replace("\n", "⏎");
        if (ruleId is "common-password" or "password-assignment")
            return oneLine.Length == 0 ? "***" : oneLine[0] + new string('*', Math.Max(3, oneLine.Length - 1));
        if (ruleId == "email-address")
        {
            int at = oneLine.IndexOf('@');
            int dot = oneLine.LastIndexOf('.');
            if (at > 0 && dot > at + 1)
                return $"{oneLine[0]}***@{oneLine[at + 1]}***{oneLine.Substring(dot)}";
        }
        if (ruleId == "payment-card")
        {
            string digits = new(oneLine.Where(char.IsDigit).ToArray());
            return digits.Length >= 4 ? $"**** **** **** {digits.Substring(digits.Length - 4)}" : "****";
        }
        if (ruleId == "iban")
        {
            string compact = new(oneLine.Where(char.IsLetterOrDigit).ToArray());
            if (compact.Length >= 6)
                return $"{compact.Substring(0, 2)}** **** **** **** {compact.Substring(compact.Length - 4)}";
        }
        if (ruleId == "phone-number")
        {
            string digits = new(oneLine.Where(char.IsDigit).ToArray());
            return digits.Length >= 2 ? $"+** *** *** **{digits.Substring(digits.Length - 2)}" : "***";
        }
        string prefix = oneLine.Length <= 3 ? oneLine : oneLine.Substring(0, 3);
        return $"{prefix}…({secret.Length} chars)";
    }

    // Detectors with a materially higher false-positive rate. Under Skip we redact only THEIR spans rather
    // than dropping the whole file, so a placeholder/example FP (e.g. API_KEY="REPLACE_ME" in a .env.example)
    // can't silently delete a legitimate file's entire content. Vendor/PEM findings still skip the whole file.
    private static readonly HashSet<string> HighFalsePositiveRules =
        new(StringComparer.Ordinal)
        {
            "generic-secret-assign", "password-assignment", "common-password", "email-address",
            "phone-number", "payment-card", "iban", "custom-sensitive-value", EntropyRuleId,
        };

    private static bool HasHighConfidenceFinding(IReadOnlyList<SecretFinding> secrets)
    {
        foreach (var s in secrets)
            if (!s.AlwaysRedact && !HighFalsePositiveRules.Contains(s.RuleId)) return true;
        return false;
    }

    /// <summary>True when <paramref name="f"/>'s content is fully omitted under Skip: Skip mode + at least one
    /// high-confidence (vendor/PEM) finding. A file whose only findings come from the high-FP generic/entropy
    /// detectors is redacted span-wise instead, so a placeholder FP never drops a whole legitimate file.</summary>
    public static bool OmitsWholeFile(DumpFile f, SecretScanMode mode) =>
        mode == SecretScanMode.Skip && HasHighConfidenceFinding(f.Secrets);

    /// <summary>The content to actually emit for a file under <paramref name="mode"/>: the original (Off/Warn),
    /// a redacted copy (Redact), or empty with <paramref name="skipped"/>=true (Skip + a high-confidence secret).
    /// Classic never calls this — its bytes stay pristine.</summary>
    public static string ContentForOutput(DumpFile f, SecretScanMode mode, out bool skipped) =>
        ContentForOutput(f.Content, f.Secrets, mode, out skipped);

    /// <summary>Content-based core of <see cref="ContentForOutput(DumpFile, SecretScanMode, out bool)"/>; the
    /// engine reuses it to count tokens on the EMITTED body. Skip omits the whole file (<paramref name="skipped"/>
    /// =true) only when a high-confidence secret is present — otherwise (generic/entropy FPs only) it redacts the
    /// spans and keeps the file.</summary>
    public static string ContentForOutput(string content, IReadOnlyList<SecretFinding> secrets,
        SecretScanMode mode, out bool skipped)
    {
        skipped = false;
        if (secrets.Count == 0) return content;
        if (mode is SecretScanMode.Off or SecretScanMode.Warn)
        {
            var required = secrets.Where(s => s.AlwaysRedact).ToList();
            return required.Count == 0 ? content : Redact(content, required);
        }
        if (mode == SecretScanMode.Skip)
        {
            if (HasHighConfidenceFinding(secrets)) { skipped = true; return ""; }
            return Redact(content, secrets);   // only high-FP generic/entropy hits -> redact spans, keep the file
        }
        return Redact(content, secrets);
    }

    /// <summary>Returns <paramref name="content"/> with each finding's span replaced by a
    /// <c>[REDACTED:rule]</c> marker. Splices from the end so earlier offsets stay valid.</summary>
    public static string Redact(string content, IReadOnlyList<SecretFinding> findings)
    {
        if (findings.Count == 0) return content;
        var sb = new StringBuilder();
        using var reader = new StringReader(content);
        using var writer = new StringWriter(sb);
        WriteRedacted(reader, writer, findings);
        return sb.ToString();
    }

    internal static void WriteRedacted(TextReader reader, TextWriter writer,
        IReadOnlyList<SecretFinding> findings, CancellationToken cancellationToken = default)
    {
        var ordered = findings.OrderBy(f => f.Start).ThenByDescending(f => f.Length).ToArray();
        long position = 0;
        for (int i = 0; i < ordered.Length; i++)
        {
            var f = ordered[i];
            long start = f.Start, end = start + f.Length;
            string rule = f.RuleId;
            if (start < position || f.Length <= 0) continue;
            while (i + 1 < ordered.Length && ordered[i + 1].Start < end)
            {
                var next = ordered[++i];
                end = Math.Max(end, (long)next.Start + next.Length);
                if (next.AlwaysRedact) rule = "custom-sensitive-value";
            }
            StagedContent.CopyCharacters(reader, writer, start - position, cancellationToken);
            StagedContent.CopyCharacters(reader, TextWriter.Null, end - start, cancellationToken);
            writer.Write($"[REDACTED:{rule}]");
            position = end;
        }
        StagedContent.CopyCharacters(reader, writer, long.MaxValue, cancellationToken);
    }
}
