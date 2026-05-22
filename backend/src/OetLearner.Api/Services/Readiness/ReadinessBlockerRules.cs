using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Readiness;

/// <summary>
/// Rule-based generator for actionable readiness blockers. Each blocker has
/// a CTA href that deep-links to a part of the app that addresses the
/// underlying problem (e.g., "Vocabulary mastery 80% behind" -> "/vocabulary?filter=due").
/// </summary>
public sealed class ReadinessBlockerRules
{
    public IReadOnlyList<ReadinessBlockerDto> Build(ReadinessBlockerContext context)
    {
        var blockers = new List<ReadinessBlockerDto>();

        if (context.Streak is { CurrentStreak: 0 })
        {
            blockers.Add(new ReadinessBlockerDto(
                Id: "streak-broken",
                Title: "No active practice streak",
                Description: "Consistency builds readiness. Complete one practice task today to start a streak.",
                ActionLabel: "Open study plan",
                ActionHref: "/study-plan",
                ImpactScore: 25m,
                Severity: "high"));
        }

        var lastMock = context.MockReports.FirstOrDefault(r => r.GeneratedAt.HasValue)?.GeneratedAt;
        if (lastMock is null)
        {
            blockers.Add(new ReadinessBlockerDto(
                Id: "no-mock",
                Title: "No mock exam on record",
                Description: "Take a full mock to establish your baseline readiness.",
                ActionLabel: "Start a mock",
                ActionHref: "/mocks",
                ImpactScore: 40m,
                Severity: "high"));
        }
        else
        {
            var daysSinceMock = (context.Now - lastMock.Value).TotalDays;
            if (daysSinceMock > 21)
            {
                blockers.Add(new ReadinessBlockerDto(
                    Id: "stale-mock",
                    Title: $"Last mock was {(int)daysSinceMock} days ago",
                    Description: "Mocks every 2-3 weeks keep your readiness signal fresh.",
                    ActionLabel: "Schedule a mock",
                    ActionHref: "/mocks",
                    ImpactScore: 20m,
                    Severity: "medium"));
            }
        }

        foreach (var (code, subtest) in context.Subtests)
        {
            if (!subtest.Current.HasValue)
            {
                blockers.Add(new ReadinessBlockerDto(
                    Id: $"no-data-{code}",
                    Title: $"No {Capitalize(code)} evidence yet",
                    Description: $"Practice {Capitalize(code)} to unlock readiness signal for this sub-test.",
                    ActionLabel: $"Practice {Capitalize(code)}",
                    ActionHref: PracticeHref(code),
                    ImpactScore: 35m,
                    Severity: "medium"));
                continue;
            }
            var gap = subtest.Target - subtest.Current.Value;
            if (gap > 15)
            {
                blockers.Add(new ReadinessBlockerDto(
                    Id: $"weak-{code}",
                    Title: $"{Capitalize(code)} {Math.Round(gap, 1)} points below target",
                    Description: $"Focus practice on {Capitalize(code)} — current readiness {Math.Round(subtest.Current.Value, 1)}/100 vs target {subtest.Target}.",
                    ActionLabel: $"Practice {Capitalize(code)}",
                    ActionHref: PracticeHref(code),
                    ImpactScore: Math.Min(50m, gap),
                    Severity: gap > 25 ? "high" : "medium"));
            }
        }

        if (context.Vocab.MasteredCount < 200 && context.Vocab.DataPoints >= 0)
        {
            var progress = ReadinessComputationService.VocabularyMasteryTarget == 0
                ? 0
                : (int)Math.Round((decimal)context.Vocab.MasteredCount / ReadinessComputationService.VocabularyMasteryTarget * 100, 0);
            blockers.Add(new ReadinessBlockerDto(
                Id: "vocab-low",
                Title: $"Vocabulary mastery {context.Vocab.MasteredCount}/{ReadinessComputationService.VocabularyMasteryTarget} ({progress}%)",
                Description: "Build vocabulary mastery to lift comprehension across all sub-tests.",
                ActionLabel: "Open recalls",
                ActionHref: "/vocabulary?filter=due",
                ImpactScore: 25m,
                Severity: context.Vocab.MasteredCount < 50 ? "high" : "medium"));
        }

        var openReviews = context.Reviews.Count(r => r.State != ReviewRequestState.Completed && r.State != ReviewRequestState.Cancelled);
        var lastReview = context.Reviews.Where(r => r.CompletedAt.HasValue).MaxBy(r => r.CompletedAt);
        var daysSinceReview = lastReview is null ? double.MaxValue : (context.Now - lastReview.CompletedAt!.Value).TotalDays;
        if (lastReview is null || daysSinceReview > 14)
        {
            blockers.Add(new ReadinessBlockerDto(
                Id: "stale-review",
                Title: lastReview is null ? "No tutor review yet" : $"Last tutor review was {(int)daysSinceReview} days ago",
                Description: "Expert reviews give you the most precise feedback. Request one for your most recent writing or speaking attempt.",
                ActionLabel: "Request a review",
                ActionHref: "/writing/review/request",
                ImpactScore: 15m,
                Severity: "medium"));
        }

        var totalPlanItems = context.PlanItems.Count;
        if (totalPlanItems > 0)
        {
            var today = DateOnly.FromDateTime(context.Now.UtcDateTime);
            var overdue = context.PlanItems.Count(p => p.Status != StudyPlanItemStatus.Completed && p.DueDate < today);
            var completed = context.PlanItems.Count(p => p.Status == StudyPlanItemStatus.Completed);
            var completionRate = totalPlanItems == 0 ? 0 : (decimal)completed / totalPlanItems;
            if (overdue >= 5 || completionRate < 0.5m)
            {
                var driftLevel = overdue >= 10 || completionRate < 0.3m ? "severe" : "moderate";
                blockers.Add(new ReadinessBlockerDto(
                    Id: "plan-drift",
                    Title: $"Study plan drift: {driftLevel}",
                    Description: $"{overdue} overdue items / {Math.Round(completionRate * 100, 0)}% completion. Regenerate or rebalance your plan.",
                    ActionLabel: "Review drift",
                    ActionHref: "/study-plan/drift",
                    ImpactScore: driftLevel == "severe" ? 40m : 25m,
                    Severity: driftLevel == "severe" ? "high" : "medium"));
            }
        }

        if (context.WeeksRemaining <= 4 && context.OverallReadiness < context.Target - 5)
        {
            blockers.Add(new ReadinessBlockerDto(
                Id: "deadline-pressure",
                Title: $"Only {context.WeeksRemaining} weeks remaining",
                Description: $"You are {Math.Round(context.Target - context.OverallReadiness, 1)} points below target with limited time. Consider intensive practice or postponing.",
                ActionLabel: "Plan intensive study",
                ActionHref: "/study-plan",
                ImpactScore: 45m,
                Severity: "high"));
        }

        return blockers
            .OrderByDescending(b => b.ImpactScore)
            .Take(5)
            .ToList();
    }

    private static string Capitalize(string code) => code switch
    {
        "writing" => "Writing",
        "speaking" => "Speaking",
        "reading" => "Reading",
        "listening" => "Listening",
        _ => code
    };

    private static string PracticeHref(string code) => code switch
    {
        "writing" => "/writing",
        "speaking" => "/speaking",
        "reading" => "/reading",
        "listening" => "/listening",
        _ => "/study-plan"
    };
}

public sealed record ReadinessBlockerContext(
    string UserId,
    DateTimeOffset Now,
    Dictionary<string, SubtestComputationResult> Subtests,
    VocabularyComputationResult Vocab,
    IReadOnlyList<MockReport> MockReports,
    LearnerStreak? Streak,
    IReadOnlyList<ReadinessPlanItem> PlanItems,
    IReadOnlyList<ReviewRequest> Reviews,
    IReadOnlyList<ReadinessHistory> History,
    int WeeksRemaining,
    decimal Target,
    decimal OverallReadiness);

public sealed record ReadinessPlanItem(StudyPlanItemStatus Status, DateOnly DueDate, DateTimeOffset? CompletedAt, string SubtestCode);

public sealed record ReadinessBlockerDto(
    string Id,
    string Title,
    string Description,
    string ActionLabel,
    string ActionHref,
    decimal ImpactScore,
    string Severity);
