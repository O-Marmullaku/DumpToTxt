namespace DumpToTxt.Core;

/// <summary>The user's decision for automatically detected sensitive data before output is committed.</summary>
public enum SensitiveDataDecision
{
    Keep,
    Redact,
    Cancel,
}

/// <summary>One display-safe finding. <see cref="Preview"/> is already masked by the scanner.</summary>
public sealed record SensitiveDataReviewItem(
    string RelativePath,
    string RuleId,
    string RuleName,
    int Line,
    string Preview);

/// <summary>Display-safe automatic findings grouped by the UI. User-declared always-hide values are omitted
/// because they are unconditionally redacted and therefore require no keep/hide decision.</summary>
public sealed class SensitiveDataReview
{
    public SensitiveDataReview(IEnumerable<SensitiveDataReviewItem> items)
    {
        Items = items.ToList();
        FileCount = Items.Select(item => item.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }

    public IReadOnlyList<SensitiveDataReviewItem> Items { get; }
    public int FileCount { get; }

    internal static SensitiveDataReview FromModel(DumpModel model) => new(
        model.Files.SelectMany(file => file.Secrets
            .Where(finding => !finding.AlwaysRedact)
            .Select(finding => new SensitiveDataReviewItem(
                file.RelativePath, finding.RuleId, finding.RuleName,
                finding.Line, finding.Preview))));
}
