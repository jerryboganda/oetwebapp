namespace OetLearner.Api.Domain;

/// <summary>
/// Canonical registry of "recall set" tags — the year/source dimension of the
/// matrix tag system, orthogonal to <see cref="VocabularyTerm.Category"/> (which
/// is the functional dimension) and <see cref="VocabularyTerm.OetSubtestTagsJson"/>
/// (which is the OET-subtest dimension).
///
/// A vocabulary term may carry zero or more recall-set codes (multi-tag): the
/// same term (e.g. "headaches") legitimately appears in multiple historical
/// recall PDFs, and learners filtering by year should see it under each.
///
/// Add new sets here only; never rename existing codes (they are persisted in
/// <see cref="VocabularyTerm.RecallSetCodesJson"/>).
/// </summary>
public static class RecallSetCodes
{
    /// <summary>"Old Recalls + Most Common Words in OET" — Dr Hesham's legacy compendium.</summary>
    public const string Old = "old";

    /// <summary>Recent listening recalls from January 2023 through end of 2025.</summary>
    public const string Y2023To2025 = "2023-2025";

    /// <summary>Recent listening recalls from January 2026 onwards.</summary>
    public const string Y2026 = "2026";

    /// <summary>All known canonical recall-set codes (lowercase).</summary>
    public static readonly IReadOnlyList<string> All = new[] { Old, Y2023To2025, Y2026 };

    /// <summary>Display metadata for the admin/learner UI. Order is rendering order.</summary>
    public static readonly IReadOnlyList<RecallSetMeta> Metadata = new[]
    {
        new RecallSetMeta(
            Code: Y2026,
            DisplayName: "January 2026 onwards",
            ShortLabel: "2026",
            Description: "Most recent listening recalls captured from January 2026 onwards.",
            SortOrder: 10),
        new RecallSetMeta(
            Code: Y2023To2025,
            DisplayName: "January 2023 → End of 2025",
            ShortLabel: "2023–2025",
            Description: "Updated listening recalls collected across 2023, 2024 and 2025.",
            SortOrder: 20),
        new RecallSetMeta(
            Code: Old,
            DisplayName: "Old recalls + most common words",
            ShortLabel: "Classic",
            Description: "Dr Hesham's legacy compendium of the most frequent OET vocabulary across all years.",
            SortOrder: 30),
    };

    /// <summary>True if <paramref name="code"/> matches a canonical recall-set code (case-insensitive).</summary>
    public static bool IsKnown(string? code) =>
        !string.IsNullOrWhiteSpace(code)
        && All.Any(c => string.Equals(c, code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Normalise to canonical lowercase form, or null if unknown.</summary>
    public static string? Normalise(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var trimmed = code.Trim();
        return All.FirstOrDefault(c => string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record RecallSetMeta(
    string Code,
    string DisplayName,
    string ShortLabel,
    string Description,
    int SortOrder);
