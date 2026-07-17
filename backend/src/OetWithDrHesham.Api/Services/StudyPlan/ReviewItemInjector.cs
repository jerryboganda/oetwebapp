using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Planner;

public sealed record InjectedReviewItem(
    string ReviewItemId,
    string Title,
    string SubtestCode,
    int DurationMinutes);

/// <summary>
/// Pulls due ReviewItems (vocab, evaluation issues, grammar errors, pronunciation)
/// for a learner and wraps them as plan items linked back via LinkedReviewItemId.
/// Completing the plan item progresses SM-2 state via SpacedRepetitionService.
/// </summary>
public class ReviewItemInjector(LearnerDbContext db)
{
    public async Task<IReadOnlyList<InjectedReviewItem>> SelectDueAsync(
        string userId,
        int maxItems,
        int defaultMinutesPerItem,
        CancellationToken cancellationToken)
    {
        if (maxItems <= 0) return Array.Empty<InjectedReviewItem>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var items = await db.ReviewItems
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.Status == "active" && r.DueDate <= today)
            .OrderBy(r => r.DueDate)
            .ThenBy(r => r.CreatedAt)
            .Take(maxItems)
            .Select(r => new { r.Id, r.SubtestCode, r.SourceType, r.QuestionJson })
            .ToListAsync(cancellationToken);

        return items
            .Select(r => new InjectedReviewItem(
                r.Id,
                BuildTitle(r.SourceType, r.SubtestCode),
                string.IsNullOrWhiteSpace(r.SubtestCode) ? StudyPlanSubtestCodes.Vocabulary : r.SubtestCode,
                Math.Max(2, defaultMinutesPerItem)))
            .ToList();
    }

    private static string BuildTitle(string sourceType, string subtest)
    {
        var label = (sourceType ?? string.Empty).ToLowerInvariant() switch
        {
            "vocabulary" => "Vocabulary review",
            "grammar_error" => "Grammar fix review",
            "pronunciation" => "Pronunciation review",
            "evaluation_issue" => "Targeted issue review",
            _ => "Spaced-repetition review"
        };

        if (string.IsNullOrWhiteSpace(subtest)) return label;
        return $"{label} ({subtest})";
    }
}
