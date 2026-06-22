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
}

/// <summary>
/// Detects credentials/secrets in packed file content using an embedded, curated, high-precision
/// ruleset (gitleaks-style) plus an optional Shannon-entropy detector (off by default). Pure compute —
/// no IO, no network. Engine scans each file's (post-truncation) content into <see cref="DumpFile.Secrets"/>;
/// the non-Classic formatters then warn / redact / skip per <see cref="DumpConfig.SecretScan"/>.
/// Classic output is never altered (findings surface in the non-Classic formatters + the post-dump notice).
/// </summary>
public static class SecretScanner
{
    /// <summary>A single embedded detection rule.</summary>
    private sealed class Rule
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required Regex Rx { get; init; }
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
            // When the END marker is missing (a key truncated by a size cap, or a headerless fragment), fall
            // back to consuming the rest of the content — redacting to EOF errs safe (never leaks key bytes).
            Rx = R(@"-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY-----" +
                   @"(?:[\s\S]*?-----END (?:RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY-----|[\s\S]*)") },
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
    /// detector; <paramref name="allowlist"/> regexes suppress any finding whose matched text matches one.</summary>
    public static IReadOnlyList<SecretFinding> Scan(string content, bool entropy, IReadOnlyList<Regex>? allowlist = null)
    {
        if (string.IsNullOrEmpty(content)) return Array.Empty<SecretFinding>();

        // Collect raw (start, length, rule, name) hits across all rules.
        var hits = new List<(int Start, int Length, string Id, string Name)>();
        foreach (var rule in Rules)
            CollectHits(content, rule.Rx, rule.Id, rule.Name, hits);
        if (entropy)
            CollectEntropyHits(content, hits);

        if (hits.Count == 0) return Array.Empty<SecretFinding>();

        // Longest first at a given start, then resolve overlaps greedily so each secret is reported once
        // and redaction never produces nested/overlapping splices. The rule-id tiebreaker keeps attribution
        // deterministic when two rules hit the exact same span (List.Sort is not a stable sort).
        hits.Sort((a, b) =>
        {
            if (a.Start != b.Start) return a.Start.CompareTo(b.Start);
            if (a.Length != b.Length) return b.Length.CompareTo(a.Length);
            return string.CompareOrdinal(a.Id, b.Id);
        });

        var findings = new List<SecretFinding>();
        int coveredTo = -1;
        foreach (var h in hits)
        {
            if (h.Start <= coveredTo) continue;     // overlaps an already-kept span -> drop the duplicate/inner hit
            string matched = content.Substring(h.Start, h.Length);
            if (IsAllowlisted(matched, allowlist)) continue;
            findings.Add(new SecretFinding
            {
                RuleId = h.Id,
                RuleName = h.Name,
                Line = LineAt(content, h.Start),
                Start = h.Start,
                Length = h.Length,
                Preview = Mask(matched),
            });
            coveredTo = h.Start + h.Length - 1;
        }
        return findings;
    }

    private static void CollectHits(string content, Regex rx, string id, string name,
        List<(int, int, string, string)> hits)
    {
        try
        {
            foreach (Match m in rx.Matches(content))
            {
                // Narrow to the capture group (the secret value) when the rule defines one.
                Group g = m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1] : m;
                if (g.Length > 0) hits.Add((g.Index, g.Length, id, name));
            }
        }
        catch (RegexMatchTimeoutException) { /* pathological input: skip this rule for this file */ }
    }

    private static void CollectEntropyHits(string content, List<(int, int, string, string)> hits)
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
                    hits.Add((m.Index, m.Length, EntropyRuleId, EntropyRuleName));
            }
        }
        catch (RegexMatchTimeoutException) { }
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

    /// <summary>Compiles allowlist patterns; invalid patterns are dropped (never throw at dump time).</summary>
    public static IReadOnlyList<Regex> CompileAllowlist(IEnumerable<string> patterns)
    {
        var list = new List<Regex>();
        foreach (var p in patterns)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            try { list.Add(new Regex(p, RegexOptions.CultureInvariant, MatchTimeout)); }
            catch (ArgumentException) { /* invalid regex: ignore */ }
        }
        return list;
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

    private static int LineAt(string content, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < content.Length; i++)
            if (content[i] == '\n') line++;
        return line;
    }

    /// <summary>Non-reversible mask: keeps a 3-char prefix as a hint, hides the rest behind a length.</summary>
    private static string Mask(string secret)
    {
        string oneLine = secret.Replace("\r", "").Replace("\n", "⏎");
        string prefix = oneLine.Length <= 3 ? oneLine : oneLine.Substring(0, 3);
        return $"{prefix}…({secret.Length} chars)";
    }

    /// <summary>The content to actually emit for a file under <paramref name="mode"/>: the original (Off/Warn),
    /// a redacted copy (Redact), or empty with <paramref name="skipped"/>=true (Skip + file has secrets).
    /// Classic never calls this — its bytes stay pristine.</summary>
    public static string ContentForOutput(DumpFile f, SecretScanMode mode, out bool skipped)
    {
        skipped = false;
        if (f.Secrets.Count == 0 || mode is SecretScanMode.Off or SecretScanMode.Warn) return f.Content;
        if (mode == SecretScanMode.Skip) { skipped = true; return ""; }
        return Redact(f.Content, f.Secrets);
    }

    /// <summary>Returns <paramref name="content"/> with each finding's span replaced by a
    /// <c>[REDACTED:rule]</c> marker. Splices from the end so earlier offsets stay valid.</summary>
    public static string Redact(string content, IReadOnlyList<SecretFinding> findings)
    {
        if (findings.Count == 0) return content;
        var ordered = findings.OrderByDescending(f => f.Start).ToList();
        var sb = new StringBuilder(content);
        int prevStart = int.MaxValue;
        foreach (var f in ordered)
        {
            int end = f.Start + f.Length;
            if (f.Start < 0 || end > sb.Length || end > prevStart) continue; // out-of-range / overlap guard
            sb.Remove(f.Start, f.Length);
            sb.Insert(f.Start, $"[REDACTED:{f.RuleId}]");
            prevStart = f.Start;
        }
        return sb.ToString();
    }
}
