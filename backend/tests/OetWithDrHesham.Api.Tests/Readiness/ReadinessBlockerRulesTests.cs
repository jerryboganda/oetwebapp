using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Readiness;

namespace OetWithDrHesham.Api.Tests.Readiness;

public sealed class ReadinessBlockerRulesTests
{
    private static ReadinessBlockerContext BuildContext(
        Dictionary<string, SubtestComputationResult>? subtests = null,
        VocabularyComputationResult? vocab = null,
        LearnerStreak? streak = null,
        int weeksRemaining = 8,
        decimal target = 75m,
        decimal overall = 65m,
        IReadOnlyList<MockReport>? mocks = null,
        IReadOnlyList<ReviewRequest>? reviews = null,
        IReadOnlyList<ReadinessPlanItem>? planItems = null)
    {
        subtests ??= new Dictionary<string, SubtestComputationResult>
        {
            ["writing"] = new SubtestComputationResult(60m, 75m, 3, "Medium"),
            ["speaking"] = new SubtestComputationResult(70m, 75m, 3, "Medium"),
            ["reading"] = new SubtestComputationResult(72m, 75m, 4, "High"),
            ["listening"] = new SubtestComputationResult(68m, 75m, 4, "Medium"),
        };
        return new ReadinessBlockerContext(
            UserId: "u-test",
            Now: DateTimeOffset.UtcNow,
            Subtests: subtests,
            Vocab: vocab ?? new VocabularyComputationResult(0m, 0, 0m, 0),
            MockReports: mocks ?? Array.Empty<MockReport>(),
            Streak: streak,
            PlanItems: planItems ?? Array.Empty<ReadinessPlanItem>(),
            Reviews: reviews ?? Array.Empty<ReviewRequest>(),
            History: Array.Empty<ReadinessHistory>(),
            WeeksRemaining: weeksRemaining,
            Target: target,
            OverallReadiness: overall);
    }

    [Fact]
    public void Build_ReturnsStreakBlocker_WhenStreakIsZero()
    {
        var rules = new ReadinessBlockerRules();
        var blockers = rules.Build(BuildContext(streak: new LearnerStreak { UserId = "u-test", CurrentStreak = 0, LastActiveDate = DateOnly.FromDateTime(DateTime.UtcNow) }));
        Assert.Contains(blockers, b => b.Id == "streak-broken");
    }

    [Fact]
    public void Build_ReturnsNoMockBlocker_WhenNoMocks()
    {
        var rules = new ReadinessBlockerRules();
        var blockers = rules.Build(BuildContext());
        Assert.Contains(blockers, b => b.Id == "no-mock");
    }

    [Fact]
    public void Build_ReturnsWeakSubtestBlocker_WhenGapIsLarge()
    {
        var rules = new ReadinessBlockerRules();
        var subtests = new Dictionary<string, SubtestComputationResult>
        {
            ["writing"] = new SubtestComputationResult(40m, 80m, 5, "Medium"),
            ["speaking"] = new SubtestComputationResult(75m, 80m, 3, "Medium"),
            ["reading"] = new SubtestComputationResult(78m, 80m, 4, "High"),
            ["listening"] = new SubtestComputationResult(72m, 80m, 4, "Medium"),
        };
        var blockers = rules.Build(BuildContext(subtests: subtests, target: 80m, overall: 65m));
        Assert.Contains(blockers, b => b.Id == "weak-writing");
        var writing = blockers.First(b => b.Id == "weak-writing");
        Assert.Equal("high", writing.Severity);
        Assert.Equal("/writing", writing.ActionHref);
    }

    [Fact]
    public void Build_OrdersByImpactDescendingAndCapsAtFive()
    {
        var rules = new ReadinessBlockerRules();
        var blockers = rules.Build(BuildContext(
            streak: new LearnerStreak { UserId = "u-test", CurrentStreak = 0, LastActiveDate = DateOnly.FromDateTime(DateTime.UtcNow) },
            weeksRemaining: 3,
            target: 80m,
            overall: 50m));
        Assert.True(blockers.Count <= 5);
        for (int i = 1; i < blockers.Count; i++)
        {
            Assert.True(blockers[i - 1].ImpactScore >= blockers[i].ImpactScore);
        }
    }
}
