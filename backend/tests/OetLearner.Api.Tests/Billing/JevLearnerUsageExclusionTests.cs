using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests.Billing;

/// <summary>
/// Jev ("jev.") judgments are recorded against the learner's own UserId for cost
/// attribution, but they are platform-only plumbing: the learner AI-usage summary
/// must never show them (not in totals, failures, features or daily buckets),
/// while the admin summary keeps counting them.
/// </summary>
public sealed class JevLearnerUsageExclusionTests
{
    private static LearnerDbContext NewContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LearnerDbContext(options);
    }

    private static AiUsageRecord Row(string userId, string featureCode, string providerId, AiCallOutcome outcome, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        UserId = userId,
        FeatureCode = featureCode,
        ProviderId = providerId,
        PromptTokens = 50,
        CompletionTokens = 25,
        CostEstimateUsd = 0.02m,
        Outcome = outcome,
        LatencyMs = 400,
        CreatedAt = now,
        PeriodMonthKey = now.ToString("yyyy-MM"),
        PeriodDayKey = now.ToString("yyyy-MM-dd"),
    };

    private static async Task SeedAsync(LearnerDbContext db, DateTimeOffset now)
    {
        db.AiUsageRecords.AddRange(
            Row("learner-1", "writing.grade", "writing-claude-sub", AiCallOutcome.Success, now),
            Row("learner-1", "writing.grade", "writing-claude-sub", AiCallOutcome.Success, now),
            Row("learner-1", "jev.writing.verify", "typesafe-jev", AiCallOutcome.Success, now),
            Row("learner-1", "jev.writing.guard", "typesafe-jev", AiCallOutcome.ProviderError, now),
            Row("learner-1", "jev.speaking.readiness", "typesafe-jev", AiCallOutcome.Success, now),
            Row("learner-2", "writing.grade", "writing-claude-sub", AiCallOutcome.Success, now));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task LearnerSummary_HidesJevRows_FromTotalsFailuresFeaturesAndDaily()
    {
        await using var db = NewContext(nameof(LearnerSummary_HidesJevRows_FromTotalsFailuresFeaturesAndDaily));
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(db, now);
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var summary = await new AiUsageAnalyticsService(db)
            .GetLearnerSummaryAsync("learner-1", today.AddDays(-1), today, CancellationToken.None);

        Assert.Equal(2, summary.TotalCalls);
        // The failed Jev guard call is not the learner's failure.
        Assert.Equal(0, summary.FailedCalls);
        Assert.Equal(150L, summary.TotalTokens);
        var feature = Assert.Single(summary.ByFeature);
        Assert.Equal("writing.grade", feature.FeatureCode);
        Assert.Equal(2, feature.Calls);
        Assert.DoesNotContain(summary.ByFeature, f => f.FeatureCode.StartsWith("jev.", StringComparison.Ordinal));
        Assert.Equal(2, Assert.Single(summary.Daily).Calls);
    }

    [Fact]
    public async Task Forecast_IgnoresJevRows_SoLearnerNumbersDoNotMove()
    {
        await using var db = NewContext(nameof(Forecast_IgnoresJevRows_SoLearnerNumbersDoNotMove));
        var now = DateTimeOffset.UtcNow;
        // Identical learner-visible history; only learner-1 also carries hidden Jev rows.
        for (var i = 0; i < 10; i++)
        {
            var day = now.AddDays(-i);
            db.AiUsageRecords.Add(Row("learner-1", "writing.grade", "writing-claude-sub", AiCallOutcome.Success, day));
            db.AiUsageRecords.Add(Row("learner-2", "writing.grade", "writing-claude-sub", AiCallOutcome.Success, day));
            for (var j = 0; j < 3; j++)
                db.AiUsageRecords.Add(Row("learner-1", "jev.writing.verify", "typesafe-jev", AiCallOutcome.Success, day));
        }
        await db.SaveChangesAsync();

        var svc = new UsageForecastService(db, NullLogger<UsageForecastService>.Instance);
        var withJev = await svc.ForecastUserAsync("learner-1", 30, CancellationToken.None);
        var without = await svc.ForecastUserAsync("learner-2", 30, CancellationToken.None);

        Assert.True(without.ForecastCalls > 0);
        Assert.Equal(without.ForecastCalls, withJev.ForecastCalls);
        Assert.Equal(without.ForecastCredits, withJev.ForecastCredits);
        Assert.Equal(without.ForecastCostUsd, withJev.ForecastCostUsd);
        Assert.Equal(without.Ema30DailyCalls, withJev.Ema30DailyCalls);
        Assert.Equal(without.SuggestedTopUpCredits, withJev.SuggestedTopUpCredits);
        Assert.DoesNotContain("jev.", withJev.PerFeatureJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChurnScore_IgnoresJevRows_WhenJudgingDecliningAiUsage()
    {
        await using var db = NewContext(nameof(ChurnScore_IgnoresJevRows_WhenJudgingDecliningAiUsage));
        var now = DateTimeOffset.UtcNow;
        db.ApplicationUserAccounts.Add(new ApplicationUserAccount
        {
            Id = "learner-1", Email = "l1@example.test", NormalizedEmail = "L1@EXAMPLE.TEST", PasswordHash = "x",
            CreatedAt = now.AddDays(-100), LastLoginAt = now.AddDays(-1),
        });
        // Five learner calls in the prior week, none this week: genuinely declining usage.
        for (var i = 0; i < 5; i++)
            db.AiUsageRecords.Add(Row("learner-1", "writing.grade", "writing-claude-sub", AiCallOutcome.Success, now.AddDays(-10)));
        // Hidden Jev calls this week must not mask the decline.
        for (var i = 0; i < 6; i++)
            db.AiUsageRecords.Add(Row("learner-1", "jev.writing.verify", "typesafe-jev", AiCallOutcome.Success, now.AddDays(-1)));
        await db.SaveChangesAsync();

        var snapshot = await new ChurnPredictionService(db, NullLogger<ChurnPredictionService>.Instance)
            .ScoreUserAsync("learner-1", CancellationToken.None);

        Assert.Contains("ai_usage_declining", snapshot.FactorsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminSummary_StillCountsJevRows()
    {
        await using var db = NewContext(nameof(AdminSummary_StillCountsJevRows));
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(db, now);
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var summary = await new AiUsageAnalyticsService(db)
            .GetAdminSummaryAsync(today.AddDays(-1), today, null, null, CancellationToken.None);

        Assert.Equal(6, summary.TotalCalls);
        Assert.Contains(summary.ByFeature, f => f.FeatureCode == "jev.writing.verify");
        Assert.Contains(summary.ByProvider, p => p.Provider == "typesafe-jev" && p.Calls == 3);
    }
}
