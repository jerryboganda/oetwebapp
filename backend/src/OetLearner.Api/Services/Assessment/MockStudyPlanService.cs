using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Assessment;

/// <summary>
/// Bridges legacy Listening/Reading mock evidence into the learner's editable
/// study plan without changing marks, score conversion, or result claims.
/// </summary>
internal static class MockStudyPlanService
{
    public static async Task SeedRemediationItemsAsync(
        LearnerDbContext db,
        string userId,
        string subtest,
        IReadOnlyList<MockErrorSummaryResponse> errorSummary,
        string fallbackRoute,
        IReadOnlyDictionary<string, string>? routesByCategory,
        string sourceKey,
        CancellationToken ct)
    {
        if (errorSummary.Count == 0) return;

        var plan = await db.StudyPlans
            .Where(item => item.UserId == userId && item.IsActive)
            .OrderByDescending(item => item.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        if (plan is null)
        {
            plan = new StudyPlan
            {
                Id = $"plan-{Guid.NewGuid():N}",
                UserId = userId,
                Version = 1,
                GeneratedAt = DateTimeOffset.UtcNow,
                State = AsyncState.Completed,
                Checkpoint = "Remediation plan created from your latest mock error patterns.",
                WeakSkillFocus = subtest,
                ExamFamilyCode = "oet",
                ExamTypeCode = "oet",
                IsActive = true
            };
            db.StudyPlans.Add(plan);
        }

        var prefix = $"mock-remediation:{sourceKey}:";
        var existingKeys = await db.StudyPlanItems
            .Where(item => item.StudyPlanId == plan.Id && item.ContentId != null && item.ContentId.StartsWith(prefix))
            .Select(item => item.ContentId!)
            .ToListAsync(ct);
        var existing = existingKeys.ToHashSet(StringComparer.Ordinal);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var index = 0;

        foreach (var summary in errorSummary.OrderByDescending(item => item.Count).ThenBy(item => item.ErrorCategory, StringComparer.Ordinal))
        {
            var contentId = $"{prefix}{index}";
            if (existing.Contains(contentId))
            {
                index++;
                continue;
            }

            var categoryLabel = summary.ErrorCategory.Replace('_', ' ');
            var route = fallbackRoute;
            if (routesByCategory is not null && routesByCategory.TryGetValue(summary.ErrorCategory, out var categoryRoute))
            {
                route = categoryRoute;
            }
            db.StudyPlanItems.Add(new StudyPlanItem
            {
                Id = $"study-item-{Guid.NewGuid():N}",
                StudyPlanId = plan.Id,
                Title = $"{Capitalise(subtest)} repair: {categoryLabel}",
                SubtestCode = subtest,
                DurationMinutes = 20,
                Rationale = $"Repair this persisted mock pattern ({summary.Count} item{(summary.Count == 1 ? string.Empty : "s")}; questions {string.Join(", ", summary.QuestionIds)}), then retry targeted practice.",
                DueDate = today.AddDays(index),
                Status = StudyPlanItemStatus.NotStarted,
                Section = index == 0 ? "today" : "thisWeek",
                ContentId = contentId,
                ItemType = "mock_remediation",
                ContentRoute = route,
                PriorityScore = Math.Max(1, 100 - index),
                WeekIndex = 0,
                SlotKind = "weak-skill-focus"
            });
            index++;
        }

        await db.SaveChangesAsync(ct);
    }

    private static string Capitalise(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "Assessment"
            : char.ToUpperInvariant(value[0]) + value[1..];
}
