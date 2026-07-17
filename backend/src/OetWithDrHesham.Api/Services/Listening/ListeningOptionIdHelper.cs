namespace OetWithDrHesham.Api.Services.Listening;

/// <summary>
/// Generates stable option IDs for listening multiple-choice questions.
/// Option IDs are deterministic based on question ID + option index,
/// enabling grading to work even if option text is later edited.
/// Format: "lo-{questionId}-{index}" where index is 0-based.
/// </summary>
public static class ListeningOptionIdHelper
{
    private const string Prefix = "lo-";

    public static string GenerateOptionId(string questionId, int optionIndex)
    {
        ArgumentNullException.ThrowIfNull(questionId);
        if (optionIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(optionIndex), "Option index must be non-negative.");

        return $"{Prefix}{questionId}-{optionIndex}";
    }

    public static int? ExtractOptionIndex(string optionId)
    {
        if (string.IsNullOrEmpty(optionId) || !optionId.StartsWith(Prefix))
            return null;

        // Find the last '-' which separates the index from the questionId
        var lastDash = optionId.LastIndexOf('-');
        if (lastDash <= Prefix.Length - 1)
            return null;

        var indexPart = optionId[(lastDash + 1)..];
        if (int.TryParse(indexPart, out var index) && index >= 0)
            return index;

        return null;
    }

    public static string? ExtractQuestionId(string optionId)
    {
        if (string.IsNullOrEmpty(optionId) || !optionId.StartsWith(Prefix))
            return null;

        // Find the last '-' which separates the index from the questionId
        var lastDash = optionId.LastIndexOf('-');
        if (lastDash <= Prefix.Length - 1)
            return null;

        // Verify the part after last dash is a valid non-negative integer
        var indexPart = optionId[(lastDash + 1)..];
        if (!int.TryParse(indexPart, out var index) || index < 0)
            return null;

        var questionId = optionId[Prefix.Length..lastDash];
        return string.IsNullOrEmpty(questionId) ? null : questionId;
    }

    public static bool IsValidOptionId(string? optionId)
        => !string.IsNullOrEmpty(optionId)
           && optionId.StartsWith(Prefix)
           && ExtractOptionIndex(optionId) is not null
           && ExtractQuestionId(optionId) is not null;

    /// <summary>
    /// Given a legacy answer (plain text or numeric index), attempts to resolve
    /// it to a stable option ID by looking up the question's options.
    /// Used during the migration/dual-read period.
    /// </summary>
    public static string? ResolveLegacyAnswer(string? legacyAnswer, string questionId, IReadOnlyList<string> currentOptions)
    {
        if (string.IsNullOrEmpty(legacyAnswer))
            return null;

        ArgumentNullException.ThrowIfNull(questionId);
        ArgumentNullException.ThrowIfNull(currentOptions);

        // If already a valid option ID, return as-is
        if (IsValidOptionId(legacyAnswer))
            return legacyAnswer;

        // Try numeric index
        if (int.TryParse(legacyAnswer, out var idx) && idx >= 0 && idx < currentOptions.Count)
            return GenerateOptionId(questionId, idx);

        // Try text match (case-insensitive)
        for (var i = 0; i < currentOptions.Count; i++)
        {
            if (string.Equals(currentOptions[i], legacyAnswer, StringComparison.OrdinalIgnoreCase))
                return GenerateOptionId(questionId, i);
        }

        return null;
    }
}
