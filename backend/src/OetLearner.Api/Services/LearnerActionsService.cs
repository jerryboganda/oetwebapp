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

        // 1. Overdue spaced repetition items
        var overdueReviewCount = await db.ReviewItems
            .AsNoTracking()
            .CountAsync(r => r.UserId == userId && r.NextReviewAt <= now, ct);
        if (overdueReviewCount > 0)
            actions.Add(new LearnerNextActionResponse("spaced_repetition", "Review Due", $"{overdueReviewCount} items ready for review", "/review", 1, now.ToString("o"), "retention"));

        // 2. Today's study plan tasks
        var todayStart = now.Date;
        var todayEnd = todayStart.AddDays(1);
        var todayTasks = await db.Set<StudyPlanItem>()
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.ScheduledFor >= todayStart && s.ScheduledFor < todayEnd && s.Status == "pending")
            .CountAsync(ct);
        if (todayTasks > 0)
            actions.Add(new LearnerNextActionResponse("study_plan", "Today's Study Plan", $"{todayTasks} tasks scheduled for today", "/study-plan", 2, null, "learning"));

        // 3. Readiness weakest skill
        var goals = await db.Set<Goal>()
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.UserId == userId, ct);
        if (goals is not null)
        {
            var recentAttempts = await db.Attempts
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.State == AttemptState.Completed)
                .OrderByDescending(a => a.CompletedAt)
                .Take(20)
                .ToListAsync(ct);

            if (recentAttempts.Count >= 3)
            {
                var bySubtest = recentAttempts
                    .GroupBy(a => a.SubtestCode)
                    .Select(g => new { Subtest = g.Key, Count = g.Count(), Latest = g.OrderByDescending(a => a.CompletedAt).First() })
                    .ToList();

                if (bySubtest.Count < 4 && bySubtest.Any())
                {
                    var missing = new[] { "reading", "listening", "writing", "speaking" }
                        .Where(s => !bySubtest.Any(b => b.Subtest == s))
                        .FirstOrDefault();
                    if (missing is not null)
                        actions.Add(new LearnerNextActionResponse("missing_skill", $"Practice {missing}", $"You haven't practiced {missing} recently", $"/{missing}", 3, null, "practice"));
                }
            }
            else
            {
                actions.Add(new LearnerNextActionResponse("diagnostic", "Complete Diagnostic", "Take your baseline assessment", "/diagnostic", 2, null, "assessment"));
            }
        }

        // 4. Upcoming mock exam (if exam date close)
        if (goals?.TargetExamDate is not null)
        {
            var weeksUntil = (goals.TargetExamDate.Value.ToDateTime(TimeOnly.MinValue) - now.DateTime).TotalDays / 7;
            if (weeksUntil <= 2 && weeksUntil > 0)
                actions.Add(new LearnerNextActionResponse("mock_readiness", "Mock Exam Recommended", $"Your exam is in {Math.Ceiling(weeksUntil)} weeks — take a mock exam", "/mocks", 1, goals.TargetExamDate.Value.ToString("o"), "assessment"));
        }

        return new LearnerNextActionsResponse(
            actions.OrderBy(a => a.Priority).ToList(),
            now);
    }

    public async Task<LearnerReadinessBlockersResponse> GetReadinessBlockersAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var blockers = new List<LearnerReadinessBlockerResponse>();

        var goals = await db.Set<Goal>()
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.UserId == userId, ct);

        var targetScores = new Dictionary<string, double>
        {
            ["writing"] = goals?.TargetWritingScore ?? 350,
            ["speaking"] = goals?.TargetSpeakingScore ?? 350,
            ["reading"] = goals?.TargetReadingScore ?? 350,
            ["listening"] = goals?.TargetListeningScore ?? 350
        };

        var completedAttempts = await db.Attempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.State == AttemptState.Completed)
            .OrderByDescending(a => a.CompletedAt)
            .ToListAsync(ct);

        var attemptIds = completedAttempts.Select(a => a.Id).ToList();
        var evaluations = attemptIds.Count == 0
            ? new List<Evaluation>()
            : await db.Evaluations
                .AsNoTracking()
                .Where(e => attemptIds.Contains(e.AttemptId))
                .ToListAsync(ct);

        var evalByAttemptId = evaluations.ToDictionary(e => e.AttemptId);

        foreach (var subtest in new[] { "writing", "speaking", "reading", "listening" })
        {
            var subtestAttempts = completedAttempts
                .Where(a => a.SubtestCode == subtest)
                .Take(5)
                .ToList();

            if (subtestAttempts.Count == 0) continue;

            var avgScore = subtestAttempts
                .Select(a => evalByAttemptId.TryGetValue(a.Id, out var e) ? e.ScaledScore ?? 0 : 0)
                .DefaultIfEmpty(0)
                .Average();

            var target = targetScores.GetValueOrDefault(subtest, 350);
            var gap = target - avgScore;

            if (gap > 20)
            {
                blockers.Add(new LearnerReadinessBlockerResponse(
                    subtest,
                    "overall",
                    avgScore,
                    target,
                    gap,
                    $"Focus on {subtest} practice — you're {gap:F0} points below target",
                    $"/{subtest}"));
            }
        }

        var overallReadiness = blockers.Count == 0 ? 85.0 :
            blockers.Count == 1 ? 70.0 :
            blockers.Count == 2 ? 55.0 :
            blockers.Count == 3 ? 40.0 : 25.0;

        return new LearnerReadinessBlockersResponse(
            blockers.OrderByDescending(b => b.Gap).ToList(),
            overallReadiness,
            now);
    }

    public async Task<LearnerProgressTrendResponse> GetProgressTrendAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var points = new List<LearnerProgressTrendPointResponse>();

        var cutoff = now.AddMonths(-3);
        var completedAttempts = await db.Attempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.State == AttemptState.Completed && a.CompletedAt >= cutoff)
            .OrderBy(a => a.CompletedAt)
            .ToListAsync(ct);

        if (completedAttempts.Count == 0)
            return new LearnerProgressTrendResponse(new List<LearnerProgressTrendPointResponse>(), null, string.Empty, now);

        var attemptIds = completedAttempts.Select(a => a.Id).ToList();
        var evaluations = await db.Evaluations
            .AsNoTracking()
            .Where(e => attemptIds.Contains(e.AttemptId))
            .ToListAsync(ct);

        var evalByAttemptId = evaluations.ToDictionary(e => e.AttemptId);

        // Weekly aggregation
        var weekGroups = completedAttempts
            .GroupBy(a => a.CompletedAt?.ToString("yyyy-'W'ww") ?? "unknown")
            .OrderBy(g => g.Key);

        foreach (var week in weekGroups)
        {
            var scores = week
                .Select(a => evalByAttemptId.TryGetValue(a.Id, out var e) ? (double?)e.ScaledScore : null)
                .Where(s => s.HasValue)
                .Select(s => s!.Value)
                .ToList();

            if (scores.Count > 0)
            {
                points.Add(new LearnerProgressTrendPointResponse(
                    week.Key,
                    Math.Round(scores.Average(), 1),
                    scores.Count));
            }
        }

        // Simple linear projection from last 4 points
        double? projectedScore = null;
        var recentPoints = points.TakeLast(4).ToList();
        if (recentPoints.Count >= 2)
        {
            var first = recentPoints.First();
            var last = recentPoints.Last();
            var weeksDiff = Math.Max(1, points.Count - points.IndexOf(first));
            var scoreDiff = last.AverageScore - first.AverageScore;
            projectedScore = Math.Round(last.AverageScore + (scoreDiff / weeksDiff * 4), 1);
        }

        return new LearnerProgressTrendResponse(
            points,
            projectedScore,
            projectedScore.HasValue ? now.AddDays(28).ToString("o") : string.Empty,
            now);
    }
}
