namespace OetWithDrHesham.Api.Services.Common;

/// <summary>
/// Canonical normalization for <c>ExamTypeCode</c> values stored on entities such as
/// <c>VocabularyTerm</c>, <c>StrategyGuide</c>, <c>VideoLessonProgram</c>, content papers,
/// and <c>User.ActiveExamTypeCode</c>.
/// </summary>
/// <remarks>
/// The canonical on-disk form is <c>"OET"</c> (upper case, trimmed). Historical writes have
/// produced empty strings, <c>"oet"</c>, and mixed-case variants which silently dropped rows
/// from EF Core <c>== examTypeCode</c> equality filters in PostgreSQL (case-sensitive collation).
/// Every read-path parameter and every entity write should funnel through these helpers so the
/// equality predicate always matches.
/// </remarks>
public static class ExamCodes
{
    public const string DefaultCode = "OET";

    /// <summary>Normalize a code to its canonical upper-case form, defaulting to <see cref="DefaultCode"/>.</summary>
    public static string Normalize(string? code)
        => string.IsNullOrWhiteSpace(code) ? DefaultCode : code.Trim().ToUpperInvariant();

    /// <summary>Normalize a code, returning <c>null</c> when input is null/empty/whitespace.</summary>
    public static string? NormalizeOrNull(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
}
