using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests;

public class WritingWeaknessAnalyticsServiceTests
{
    private const string DefaultUser = "user-1";

    private static (LearnerDbContext db, WritingWeaknessAnalyticsService svc, FixedClock clock) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var clock = new FixedClock(new DateTimeOffset(2026, 5, 22, 0, 0, 0, TimeSpan.Zero));
        return (db, new WritingWeaknessAnalyticsService(db, clock), clock);
    }

    private sealed class FixedClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utcNow = start;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private static WritingRuleViolation Violation(string ruleId, DateTimeOffset at, string userId = DefaultUser) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        AttemptId = "att-1",
        EvaluationId = "eval-1",
        UserId = userId,
        Profession = "medicine",
        LetterType = "routine_referral",
        RuleId = ruleId,
        Severity = "major",
        Source = "rulebook",
        Message = $"{ruleId} fired",
        GeneratedAt = at,
    };

    [Fact]
    public async Task Aggregates_rule_violations_into_top_tags()
    {
        var (db, svc, clock) = Build();
        var t0 = clock.GetUtcNow().AddDays(-3);
        db.WritingRuleViolations.AddRange(
            Violation("missing_key_content", t0),
            Violation("missing_key_content", t0.AddHours(1)),
            Violation("irrelevant_content", t0.AddHours(2)),
            Violation("unclear_purpose", t0.AddHours(3)));
        await db.SaveChangesAsync();

        var summary = await svc.ComputeForLearnerAsync(DefaultUser, 14, default);

        Assert.Equal(4, summary.TotalObservations);
        Assert.Equal("missing_key_content", summary.TopTags[0].Tag);
        Assert.Equal(2, summary.TopTags[0].Count);
        Assert.Equal(0.5, summary.TopTags[0].Share, 3);
        Assert.Contains(summary.TopTags, t => t.Tag == "irrelevant_content");
        Assert.Contains(summary.TopTags, t => t.Tag == "unclear_purpose");
        Assert.NotEmpty(summary.ByCriterion);
        Assert.Contains(summary.ByCriterion, c => c.Criterion == "content");
        Assert.Contains(summary.ByCriterion, c => c.Criterion == "purpose");
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Drops_unknown_rule_ids_from_tag_counts_but_keeps_them_in_trend()
    {
        var (db, svc, clock) = Build();
        var t0 = clock.GetUtcNow().AddDays(-1);
        db.WritingRuleViolations.AddRange(
            Violation("missing_key_content", t0),
            Violation("not_a_real_rule", t0.AddHours(1)));
        await db.SaveChangesAsync();

        var summary = await svc.ComputeForLearnerAsync(DefaultUser, 14, default);

        Assert.Equal(2, summary.TotalObservations);
        Assert.Single(summary.TopTags);
        Assert.Equal("missing_key_content", summary.TopTags[0].Tag);
        // Trend should reflect both rows.
        Assert.Equal(2, summary.Trend.Sum(b => b.Count));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Trend_buckets_cover_exact_requested_window()
    {
        var (db, svc, _) = Build();
        var summary = await svc.ComputeForLearnerAsync(DefaultUser, 21, default);
        Assert.Equal(21, summary.Trend.Count);
        // Sequential, no gaps.
        for (var i = 1; i < summary.Trend.Count; i++)
        {
            var prev = DateOnly.Parse(summary.Trend[i - 1].Date);
            var cur = DateOnly.Parse(summary.Trend[i].Date);
            Assert.Equal(prev.AddDays(1), cur);
        }
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Returns_empty_summary_when_no_observations()
    {
        var (db, svc, _) = Build();
        var summary = await svc.ComputeForLearnerAsync(DefaultUser, 14, default);
        Assert.Equal(0, summary.TotalObservations);
        Assert.Empty(summary.TopTags);
        Assert.Empty(summary.ByCriterion);
        Assert.Empty(summary.GradeTrend);
        Assert.Empty(summary.PurposeTrend);
        Assert.Equal(14, summary.Trend.Count);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Clamps_trend_days_to_7_min_90_max()
    {
        var (db, svc, _) = Build();
        var small = await svc.ComputeForLearnerAsync(DefaultUser, 3, default);
        Assert.Equal(7, small.Trend.Count);

        var big = await svc.ComputeForLearnerAsync(DefaultUser, 365, default);
        Assert.Equal(90, big.Trend.Count);

        var zero = await svc.ComputeForLearnerAsync(DefaultUser, 0, default);
        Assert.Equal(14, zero.Trend.Count);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Includes_grade_and_purpose_trend_from_evaluations()
    {
        var (db, svc, clock) = Build();
        db.Attempts.Add(new Attempt
        {
            Id = "att-9",
            UserId = DefaultUser,
            ContentId = "content-9",
            SubtestCode = "writing",
            State = AttemptState.Submitted,
            StartedAt = clock.GetUtcNow().AddDays(-2),
            SubmittedAt = clock.GetUtcNow().AddDays(-2),
            Context = "{}",
            Mode = "exam",
        });
        db.Evaluations.Add(new Evaluation
        {
            Id = "eval-9",
            AttemptId = "att-9",
            SubtestCode = "writing",
            State = AsyncState.Completed,
            ScoreRange = "350",
            GradeRange = "B",
            CriterionScoresJson = "[{\"criterionCode\":\"purpose\",\"score\":2,\"maxScore\":3},{\"criterionCode\":\"content\",\"score\":5,\"maxScore\":7}]",
            LearnerDisclaimer = "Practice estimate only.",
            ModelExplanationSafe = "Model output redacted for test.",
            CreatedAt = clock.GetUtcNow().AddDays(-2),
        });
        await db.SaveChangesAsync();

        var summary = await svc.ComputeForLearnerAsync(DefaultUser, 14, default);

        Assert.Single(summary.GradeTrend);
        Assert.Equal("B", summary.GradeTrend[0].GradeRange);
        Assert.Equal("350", summary.GradeTrend[0].ScoreRange);

        Assert.Single(summary.PurposeTrend);
        Assert.Equal(2, summary.PurposeTrend[0].Score);
        Assert.Equal(3, summary.PurposeTrend[0].MaxScore);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Excludes_other_users_data()
    {
        var (db, svc, clock) = Build();
        var t0 = clock.GetUtcNow().AddDays(-1);
        db.WritingRuleViolations.AddRange(
            Violation("missing_key_content", t0, userId: DefaultUser),
            Violation("missing_key_content", t0, userId: "user-other"));
        await db.SaveChangesAsync();

        var summary = await svc.ComputeForLearnerAsync(DefaultUser, 14, default);
        Assert.Equal(1, summary.TotalObservations);
        await db.DisposeAsync();
    }
}
