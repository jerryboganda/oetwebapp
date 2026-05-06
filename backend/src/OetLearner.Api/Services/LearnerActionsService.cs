using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public class LearnerActionsService(LearnerDbContext db)
{
    public async Task<LearnerNextActionsResponse> GetNextActionsAsync(string userId, CancellationToken ct)
    {
        var actions = new List<LearnerNextActionResponse>();
        var now = DateTimeOffset.UtcNow;

        // Overdue spaced repetition items
        var reviewDue = await db.ReviewItems
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .CountAsync(ct);
        if (reviewDue > 0)
            actions.Add(new LearnerNextActionResponse("review", "Review Due", $"{reviewDue} items ready for review", "/review", 1, null, "retention"));

        // Recent practice streak check
        var lastAttempt = await db.Attempts
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (lastAttempt is null)
            actions.Add(new LearnerNextActionResponse("diagnostic", "Complete Diagnostic", "Take your baseline assessment to get started", "/diagnostic", 1, null, "assessment"));
        else if (lastAttempt.CreatedAt < now.AddDays(-3))
            actions.Add(new LearnerNextActionResponse("practice", "Keep Practicing", "It's been a few days since your last session", "/writing", 2, null, "practice"));

        // Check for goals
        var goal = await db.Set<LearnerGoal>()
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.UserId == userId, ct);

        if (goal?.TargetExamDate is not null)
        {
            var weeksUntil = (goal.TargetExamDate.Value.ToDateTime(TimeOnly.MinValue) - now.DateTime).TotalDays / 7;
            if (weeksUntil <= 2 && weeksUntil > 0)
                actions.Add(new LearnerNextActionResponse("mock", "Mock Exam Recommended", $"Your exam is in {Math.Ceiling(weeksUntil)} weeks — take a mock", "/mocks", 1, null, "assessment"));
        }

        return new LearnerNextActionsResponse(actions.OrderBy(a => a.Priority).ToList(), now);
    }

    public async Task<LearnerReadinessBlockersResponse> GetReadinessBlockersAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var blockers = new List<LearnerReadinessBlockerResponse>();

        var completedAttempts = await db.Attempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.State == AttemptState.Completed)
            .OrderByDescending(a => a.CompletedAt)
            .ToListAsync(ct);

        var bySubtest = completedAttempts
            .GroupBy(a => a.SubtestCode)
            .ToDictionary(g => g.Key, g => g.Count());

        var allSubtests = new[] { "reading", "listening", "writing", "speaking" };
        foreach (var subtest in allSubtests)
        {
            if (!bySubtest.ContainsKey(subtest))
            {
                blockers.Add(new LearnerReadinessBlockerResponse(
                    subtest, "overall", 0, 350, 350,
                    $"You haven't practiced {subtest} yet — start your first session",
                    $"/{subtest}"));
            }
            else if (bySubtest[subtest] < 3)
            {
                blockers.Add(new LearnerReadinessBlockerResponse(
                    subtest, "overall", 250, 350, 100,
                    $"Only {bySubtest[subtest]} attempts in {subtest} — more practice needed",
                    $"/{subtest}"));
            }
        }

        var overallReadiness = blockers.Count == 0 ? 85.0 :
            blockers.Count == 1 ? 65.0 : blockers.Count == 2 ? 45.0 : 30.0;

        return new LearnerReadinessBlockersResponse(
            blockers.OrderByDescending(b => b.Gap).ToList(),
            overallReadiness, now);
    }

    public async Task<LearnerProgressTrendResponse> GetProgressTrendAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddMonths(-3);

        var completedAttempts = await db.Attempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.State == AttemptState.Completed && a.CompletedAt >= cutoff)
            .OrderBy(a => a.CompletedAt)
            .ToListAsync(ct);

        var points = new List<LearnerProgressTrendPointResponse>();

        if (completedAttempts.Count > 0)
        {
            var weekGroups = completedAttempts
                .GroupBy(a => a.CompletedAt?.ToString("yyyy-'W'ww") ?? "unknown")
                .OrderBy(g => g.Key);

            foreach (var week in weekGroups)
            {
                points.Add(new LearnerProgressTrendPointResponse(
                    week.Key, week.Count(), week.Count()));
            }
        }

        double? projected = null;
        if (points.Count >= 2)
        {
            var trend = points.TakeLast(4).ToList();
            var growth = trend.Count > 1 ? (double)(trend.Last().AttemptCount - trend.First().AttemptCount) / trend.Count : 0;
            projected = Math.Round(trend.Last().AttemptCount + growth * 4, 1);
        }

        return new LearnerProgressTrendResponse(points, projected, projected.HasValue ? now.AddDays(28).ToString("o") : "", now);
    }
}
