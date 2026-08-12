using OetLearner.Api.Contracts;

namespace OetLearner.Api.Services.Assessment;

/// <summary>
/// Resolves candidate-safe mock remediation links to routes already implemented
/// by the learner application. It never invents an external URL or changes a mark.
/// </summary>
internal static class MockNextStepRouteResolver
{
    public static string Reading(string? partCode)
    {
        var part = partCode?.Trim().ToUpperInvariant();
        return part is "A" or "B" or "C"
            ? $"/reading/practice?focus={part}&tab=errors"
            : "/reading/practice?tab=errors";
    }

    public static string Listening(string? errorCategory)
    {
        var drillId = (errorCategory ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "distractor" or "distractor_confusion" => "distractor_confusion",
            "numbers_and_frequencies" or "number_form" or "unit" => "numbers_and_frequencies",
            "spelling" or "meaning_correct_spelling_wrong" => "spelling",
            "grammar_number" => "grammar_number",
            "paraphrase" => "paraphrase",
            "wrong_section" => "wrong_section",
            "extra_info" => "extra_info",
            "empty" => "empty",
            _ => "detail_capture",
        };

        return $"/listening/drills/{drillId}";
    }

    public static MockNextStepResponse? BuildReading(
        IReadOnlyList<MockReviewItemResponse> items)
    {
        var firstMiss = items.FirstOrDefault(item => !item.IsCorrect);
        if (firstMiss is null) return null;

        var category = firstMiss.ErrorCategory?.Replace('_', ' ') ?? "missed answer";
        return new MockNextStepResponse(
            Title: $"Review Reading Part {firstMiss.PartCode} practice",
            Description: $"Focus on {category} items in the Part {firstMiss.PartCode} Error Bank before starting another timed Reading paper.",
            Route: Reading(firstMiss.PartCode));
    }

    public static MockNextStepResponse? BuildListening(
        IReadOnlyList<MockReviewItemResponse> items)
    {
        var firstMiss = items.FirstOrDefault(item => !item.IsCorrect);
        if (firstMiss is null) return null;

        var category = firstMiss.ErrorCategory?.Replace('_', ' ') ?? "missed answer";
        return new MockNextStepResponse(
            Title: $"Open targeted Listening {category} drill",
            Description: $"Practise {category} errors before starting another timed Listening paper.",
            Route: Listening(firstMiss.ErrorCategory));
    }
}
