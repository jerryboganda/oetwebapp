using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Audit P2-2 closure (May 2026). Locks the rule-violation analytics
/// aggregator that powers the new admin endpoints
/// <c>GET /v1/admin/writing/analytics/rule-violations</c> and
/// <c>GET /v1/admin/writing/analytics/rule-violations/{attemptId}</c>.
///
/// Coverage:
///   • Rolling-window filter clamps to [7,365] days.
///   • Profession filter is case-insensitive and lower-cases the response field.
///   • Group-by-rule includes severity counts + per-profession breakdown.
///   • Distinct attempts / learners are de-duplicated correctly.
///   • Drill-down returns rows for the attempt sorted by severity then ruleId.
/// </summary>
public class WritingRuleViolationAnalyticsTests
{
    private static LearnerDbContext NewDb() =>
        new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task Dashboard_AggregatesByRule_AndExposesSeverityAndProfessionBreakdown()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;

        // 6 violations: 4 on rule_a (3 medicine, 1 nursing), 2 on rule_b (medicine).
        db.WritingRuleViolations.AddRange(
            Violation("v1", "attempt-1", "user-1", "medicine", "discharge", "rule_a", "critical", now.AddDays(-1)),
            Violation("v2", "attempt-1", "user-1", "medicine", "discharge", "rule_b", "minor", now.AddDays(-1)),
            Violation("v3", "attempt-2", "user-2", "medicine", "routine_referral", "rule_a", "major", now.AddDays(-2)),
            Violation("v4", "attempt-3", "user-3", "medicine", "routine_referral", "rule_a", "minor", now.AddDays(-3)),
            Violation("v5", "attempt-4", "user-4", "nursing", "discharge", "rule_a", "minor", now.AddDays(-4)),
            Violation("v6", "attempt-2", "user-2", "medicine", "routine_referral", "rule_b", "minor", now.AddDays(-2)));
        await db.SaveChangesAsync();

        var dto = await WritingAnalyticsAdminEndpoints.BuildRuleViolationDashboardAsync(
            db, days: 30, profession: null, ct: CancellationToken.None);

        Assert.Equal(30, dto.WindowDays);
        Assert.Null(dto.ProfessionFilter);
        Assert.Equal(6, dto.Summary.TotalViolations);
        Assert.Equal(2, dto.Summary.DistinctRules);
        Assert.Equal(4, dto.Summary.DistinctAttempts);
        Assert.Equal(4, dto.Summary.DistinctLearners);

        var ruleA = dto.TopRules.First(r => r.RuleId == "rule_a");
        Assert.Equal(4, ruleA.TotalCount);
        Assert.Equal(4, ruleA.DistinctAttempts);
        Assert.Equal(4, ruleA.DistinctLearners);
        Assert.Equal(1, ruleA.CriticalCount);
        Assert.Equal(1, ruleA.MajorCount);
        Assert.Equal(2, ruleA.MinorCount);
        Assert.Equal(0, ruleA.InfoCount);
        var medRow = ruleA.Professions.First(p => p.Profession == "medicine");
        Assert.Equal(3, medRow.Count);
        var nurseRow = ruleA.Professions.First(p => p.Profession == "nursing");
        Assert.Equal(1, nurseRow.Count);
    }

    [Fact]
    public async Task Dashboard_FiltersByProfession_CaseInsensitive()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;

        db.WritingRuleViolations.AddRange(
            Violation("v1", "attempt-1", "user-1", "medicine", "discharge", "rule_a", "minor", now.AddDays(-1)),
            Violation("v2", "attempt-2", "user-2", "nursing", "discharge", "rule_a", "minor", now.AddDays(-1)));
        await db.SaveChangesAsync();

        var dto = await WritingAnalyticsAdminEndpoints.BuildRuleViolationDashboardAsync(
            db, days: 30, profession: "Nursing", ct: CancellationToken.None);

        Assert.Equal("nursing", dto.ProfessionFilter);
        Assert.Equal(1, dto.Summary.TotalViolations);
        Assert.Equal("nursing", dto.ProfessionBreakdown.Single().Profession);
    }

    [Fact]
    public async Task Dashboard_ExcludesViolationsOutsideWindow()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;

        db.WritingRuleViolations.AddRange(
            Violation("v1", "attempt-1", "user-1", "medicine", "discharge", "rule_a", "minor", now.AddDays(-1)),
            // 200 days back — outside the default 30-day window, inside a 365-day window.
            Violation("v2", "attempt-2", "user-2", "medicine", "discharge", "rule_a", "minor", now.AddDays(-200)));
        await db.SaveChangesAsync();

        var defaultWindow = await WritingAnalyticsAdminEndpoints.BuildRuleViolationDashboardAsync(
            db, days: null, profession: null, ct: CancellationToken.None);
        Assert.Equal(30, defaultWindow.WindowDays);
        Assert.Equal(1, defaultWindow.Summary.TotalViolations);

        var maxWindow = await WritingAnalyticsAdminEndpoints.BuildRuleViolationDashboardAsync(
            db, days: 365, profession: null, ct: CancellationToken.None);
        Assert.Equal(365, maxWindow.WindowDays);
        Assert.Equal(2, maxWindow.Summary.TotalViolations);
    }

    [Fact]
    public async Task Dashboard_ClampsExtremeWindowDays_ToMinAndMax()
    {
        await using var db = NewDb();
        var asksTooSmall = await WritingAnalyticsAdminEndpoints.BuildRuleViolationDashboardAsync(
            db, days: 1, profession: null, ct: CancellationToken.None);
        Assert.Equal(7, asksTooSmall.WindowDays);

        var asksTooLarge = await WritingAnalyticsAdminEndpoints.BuildRuleViolationDashboardAsync(
            db, days: 5_000, profession: null, ct: CancellationToken.None);
        Assert.Equal(365, asksTooLarge.WindowDays);
    }

    [Fact]
    public async Task DrillDown_ReturnsRowsOrderedBySeverityThenRuleId()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;

        db.WritingRuleViolations.AddRange(
            Violation("v1", "attempt-x", "user-1", "medicine", "discharge", "rule_zzz", "minor", now),
            Violation("v2", "attempt-x", "user-1", "medicine", "discharge", "rule_aaa", "critical", now),
            Violation("v3", "attempt-x", "user-1", "medicine", "discharge", "rule_bbb", "major", now),
            Violation("v4", "attempt-x", "user-1", "medicine", "discharge", "rule_ccc", "info", now));
        await db.SaveChangesAsync();

        var dto = await WritingAnalyticsAdminEndpoints.BuildAttemptDrillDownAsync(
            db, attemptId: "attempt-x", ct: CancellationToken.None);

        Assert.Equal("attempt-x", dto.AttemptId);
        Assert.Equal("medicine", dto.Profession);
        Assert.Equal("discharge", dto.LetterType);
        Assert.Equal(4, dto.Items.Count);
        Assert.Equal(new[] { "rule_aaa", "rule_bbb", "rule_zzz", "rule_ccc" },
            dto.Items.Select(i => i.RuleId).ToArray());
        Assert.Equal(new[] { "critical", "major", "minor", "info" },
            dto.Items.Select(i => i.Severity).ToArray());
    }

    [Fact]
    public async Task DrillDown_ReturnsEmptyShape_WhenAttemptHasNoViolations()
    {
        await using var db = NewDb();

        var dto = await WritingAnalyticsAdminEndpoints.BuildAttemptDrillDownAsync(
            db, attemptId: "attempt-missing", ct: CancellationToken.None);

        Assert.Equal("attempt-missing", dto.AttemptId);
        Assert.Null(dto.EvaluationId);
        Assert.Null(dto.UserId);
        Assert.Empty(dto.Items);
    }

    private static WritingRuleViolation Violation(
        string id,
        string attemptId,
        string userId,
        string profession,
        string letterType,
        string ruleId,
        string severity,
        DateTimeOffset generatedAt) => new()
    {
        Id = id,
        AttemptId = attemptId,
        EvaluationId = $"eval-{attemptId}",
        UserId = userId,
        Profession = profession,
        LetterType = letterType,
        RuleId = ruleId,
        Severity = severity,
        Source = "rulebook",
        Message = $"Violation {ruleId}",
        Quote = null,
        GeneratedAt = generatedAt,
    };
}
