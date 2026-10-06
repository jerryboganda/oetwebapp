using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;

namespace OetLearner.Api.Services.Planner;

/// <summary>The single chosen task (F-039/F-040): never a menu, always one action plus its evidence.</summary>
public sealed record NextBestDecision(
    DateOnly Today,
    string Title,
    string Subtest,
    int Minutes,
    string Kind,
    string? TargetUrl,
    string Why);

/// <summary>
/// Next-best-action engine v1 (SAMI §5.2): deterministic, evidence-first, exam-proximity aware.
/// Order of value:
/// 1. exam within 3 days — rehearsal and error review only, no new content;
/// 2. due Error DNA reviews (spaced reinforcement — cheapest high-yield block);
/// 3. the study-plan item due today that fits the available minutes;
/// 4. a targeted drill on the top evidenced weakness;
/// 5. honest fallback — ask for a diagnostic instead of inventing a weakness.
/// Wave 1-PLANNER extends the plan-item branch with the replanner; the contract stays fixed.
/// </summary>
public interface INextBestActionService
{
    Task<NextBestDecision> DecideAsync(string userId, CompanionTurnContext context, int minutesAvailable, CancellationToken ct);
}

public sealed class NextBestActionService(
    LearnerDbContext db,
    IErrorDnaService errorDna,
    ICompanionAvailabilityService availability,
    ICompanionDestinationRegistry destinations,
    TimeProvider clock) : INextBestActionService
{
    public async Task<NextBestDecision> DecideAsync(string userId, CompanionTurnContext context, int minutesAvailable, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var daysToExam = context.DaysUntilExam;

        var dueReviews = await errorDna.DueReviewsAsync(userId, 5, ct);
        var weaknesses = await errorDna.TopWeaknessesAsync(userId, 5, ct);

        // 1. Near-exam: rehearsal and error review, never new content (SAMI §5.2).
        if (daysToExam is { } days && days >= 0 && days <= 3)
        {
            if (dueReviews.Count > 0)
            {
                var review = dueReviews[0];
                return new NextBestDecision(today,
                    $"Rapid review: {review.Pattern}", review.Subtest,
                    Math.Min(minutesAvailable, 20), "error_review", null,
                    $"Your exam is in {days} day(s); re-testing your own recorded mistakes now protects marks better than new content (seen {review.EvidenceCount}x, mastery {review.MasteryScore}/100).");
            }
            return new NextBestDecision(today,
                "Timed mini-mock rehearsal of your weakest sub-test",
                weaknesses.FirstOrDefault()?.Subtest ?? "reading",
                Math.Min(minutesAvailable, 45), "rehearsal", null,
                days == 0
                    ? "Exam day: a short warm-up rehearsal beats learning anything new."
                    : $"Exam is {days} day(s) away — rehearsal mode: run a timed mini-set under exam conditions.");
        }

        // 2. Due spaced reviews.
        if (dueReviews.Count > 0)
        {
            var review = dueReviews[0];
            return new NextBestDecision(today,
                $"Spaced review: {review.Pattern}", review.Subtest,
                Math.Min(minutesAvailable, 15), "error_review", null,
                $"This recorded error is due for its re-test today (seen {review.EvidenceCount}x, mastery {review.MasteryScore}/100) — re-testing at the right moment is the cheapest way to lock it in.");
        }

        // 3. Today's plan item that fits.
        var plan = await db.StudyPlans.AsNoTracking()
            .Where(p => p.UserId == userId && p.IsActive)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct);
        if (plan is not null)
        {
            var item = await db.StudyPlanItems.AsNoTracking()
                .Where(i => i.StudyPlanId == plan.Id && i.DueDate == today && i.Status == StudyPlanItemStatus.NotStarted)
                .OrderBy(i => i.PriorityScore)
                .FirstOrDefaultAsync(ct)
                ?? await db.StudyPlanItems.AsNoTracking()
                    .Where(i => i.StudyPlanId == plan.Id && i.DueDate < today && i.Status == StudyPlanItemStatus.NotStarted)
                    .OrderBy(i => i.DueDate)
                    .ThenBy(i => i.PriorityScore)
                    .FirstOrDefaultAsync(ct);
            if (item is not null)
            {
                var url = await SafeResolveAsync(destinations, context, item.SubtestCode, ct);
                return new NextBestDecision(today,
                    item.Title, item.SubtestCode,
                    Math.Min(item.DurationMinutes, Math.Max(minutesAvailable, 10)),
                    "plan_item", url,
                    item.DueDate < today
                        ? $"This plan item is overdue from {item.DueDate:yyyy-MM-dd}; it targets your plan's weakest area and still fits your {minutesAvailable} minutes."
                        : $"It is today's highest-priority plan item ({item.DurationMinutes} min) and matches your available {minutesAvailable} minutes.");
            }
        }

        // 4. Targeted drill on the top evidenced weakness.
        if (weaknesses.Count > 0)
        {
            var top = weaknesses[0];
            var url = await SafeResolveAsync(destinations, context, top.Subtest, ct);
            return new NextBestDecision(today,
                $"Targeted drill: {top.Pattern}", top.Subtest,
                Math.Min(minutesAvailable, 20), "weakness_drill", url,
                $"Your most frequent evidenced error ({top.EvidenceCount}x, mastery {top.MasteryScore}/100) is {top.Pattern}; a short focused drill attacks it directly.");
        }

        // 5. Honest fallback.
        return new NextBestDecision(today,
            "Short diagnostic so I can target what actually needs work", "general",
            Math.Min(minutesAvailable, 20), "diagnostic",
            await SafeResolveAsync(destinations, context, "reading", ct),
            "There is not enough recorded evidence yet to name a weakness honestly — a short diagnostic gives the data without guessing.");
    }

    private static async Task<string?> SafeResolveAsync(
        ICompanionDestinationRegistry destinations, CompanionTurnContext context, string subtest, CancellationToken ct)
    {
        var id = subtest switch
        {
            "reading" => "reading.practice",
            "listening" => "listening.practice",
            _ => "study.plan",
        };
        try
        {
            var destination = await destinations.ResolveAsync(id, context, ct);
            return destination.Url;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
