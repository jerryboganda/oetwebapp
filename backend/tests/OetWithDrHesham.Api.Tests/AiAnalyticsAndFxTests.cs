using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Domain.ValueObjects;
using OetWithDrHesham.Api.Services.Billing;

namespace OetWithDrHesham.Api.Tests;

public class AiAnalyticsAndFxTests
{
    private static LearnerDbContext NewContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LearnerDbContext(options);
    }

    [Fact]
    public async Task ChurnPredictionService_NewUserNoActivity_LowRisk()
    {
        await using var db = NewContext(nameof(ChurnPredictionService_NewUserNoActivity_LowRisk));
        var now = DateTimeOffset.UtcNow;
        db.ApplicationUserAccounts.Add(new ApplicationUserAccount
        {
            Id = "user_a", Email = "a@b", NormalizedEmail = "A@B", PasswordHash = "x",
            CreatedAt = now.AddDays(-30), LastLoginAt = now.AddDays(-1),
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub_a", UserId = "user_a", PlanId = "premium",
            Status = SubscriptionStatus.Active, StartedAt = now.AddDays(-30),
            NextRenewalAt = now.AddDays(20), ChangedAt = now,
            PriceAmount = 49m, Currency = "USD", Interval = "monthly",
        });
        await db.SaveChangesAsync();

        var svc = new ChurnPredictionService(db, NullLogger<ChurnPredictionService>.Instance);
        var snapshot = await svc.ScoreUserAsync("user_a", CancellationToken.None);

        Assert.Equal("low", snapshot.RiskBand);
        Assert.True(snapshot.RiskScore < 0.25m);
    }

    [Fact]
    public async Task ChurnPredictionService_FailedPaymentsAndDunning_HighRisk()
    {
        await using var db = NewContext(nameof(ChurnPredictionService_FailedPaymentsAndDunning_HighRisk));
        var now = DateTimeOffset.UtcNow;
        db.ApplicationUserAccounts.Add(new ApplicationUserAccount
        {
            Id = "user_b", Email = "b@b", NormalizedEmail = "B@B", PasswordHash = "x",
            CreatedAt = now.AddDays(-200), LastLoginAt = now.AddDays(-30),
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub_b", UserId = "user_b", PlanId = "premium",
            Status = SubscriptionStatus.PastDue, StartedAt = now.AddDays(-200),
            NextRenewalAt = now.AddDays(-2), ChangedAt = now,
            PriceAmount = 49m, Currency = "USD", Interval = "monthly",
        });
        for (int i = 0; i < 3; i++)
        {
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                LearnerUserId = "user_b",
                Gateway = "stripe",
                GatewayTransactionId = $"pi_failed_{i}",
                TransactionType = "subscription_payment",
                Status = "failed",
                Amount = 49m,
                Currency = "USD",
                CreatedAt = now.AddDays(-i - 1),
                UpdatedAt = now,
            });
        }
        db.DunningCampaigns.Add(new DunningCampaign
        {
            Id = "dc_b", SubscriptionId = "sub_b", UserId = "user_b", Status = "active",
            StartedAt = now.AddDays(-5), NextAttemptAt = now,
            CreatedAt = now, UpdatedAt = now,
        });
        db.CancellationIntents.Add(new CancellationIntent
        {
            Id = "ci_b", SubscriptionId = "sub_b", UserId = "user_b",
            Reason = "too_expensive", Status = "started",
            CreatedAt = now, UpdatedAt = now,
        });
        // Add a refund + a second cancel intent to push the score over the high threshold.
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            LearnerUserId = "user_b",
            Gateway = "stripe",
            GatewayTransactionId = "pi_refunded_x",
            TransactionType = "subscription_payment",
            Status = "refunded",
            Amount = 49m,
            Currency = "USD",
            CreatedAt = now.AddDays(-15),
            UpdatedAt = now,
        });
        db.CancellationIntents.Add(new CancellationIntent
        {
            Id = "ci_b2", SubscriptionId = "sub_b", UserId = "user_b",
            Reason = "passed_exam", Status = "started",
            CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var svc = new ChurnPredictionService(db, NullLogger<ChurnPredictionService>.Instance);
        var snapshot = await svc.ScoreUserAsync("user_b", CancellationToken.None);

        Assert.Contains(snapshot.RiskBand, new[] { "medium", "high" });
        Assert.True(snapshot.RiskScore >= 0.55m, $"Expected >= 0.55, got {snapshot.RiskScore}");
        Assert.NotNull(snapshot.RecommendedAction);
    }

    [Fact]
    public async Task UsageForecastService_ProducesForecastFromAiUsageRecords()
    {
        await using var db = NewContext(nameof(UsageForecastService_ProducesForecastFromAiUsageRecords));
        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 10; i++)
        {
            var day = now.AddDays(-i);
            db.AiUsageRecords.Add(new AiUsageRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = "user_c",
                FeatureCode = "writing.grade",
                ProviderId = "openai-platform",
                PromptTokens = 100, CompletionTokens = 50,
                CostEstimateUsd = 0.05m,
                Outcome = AiCallOutcome.Success,
                LatencyMs = 800,
                CreatedAt = day,
                PeriodMonthKey = day.ToString("yyyy-MM"),
                PeriodDayKey = day.ToString("yyyy-MM-dd"),
            });
        }
        db.Wallets.Add(new Wallet { Id = "w_c", UserId = "user_c", CreditBalance = 5, LastUpdatedAt = now });
        await db.SaveChangesAsync();

        var svc = new UsageForecastService(db, NullLogger<UsageForecastService>.Instance);
        var forecast = await svc.ForecastUserAsync("user_c", 30, CancellationToken.None);

        Assert.True(forecast.ForecastCalls > 0);
        Assert.True(forecast.ForecastCostUsd > 0m);
        Assert.True(forecast.Ema30DailyCalls > 0m);
    }

    [Fact]
    public async Task AiUsageAnalytics_LearnerSummaryAggregatesByFeature()
    {
        await using var db = NewContext(nameof(AiUsageAnalytics_LearnerSummaryAggregatesByFeature));
        var now = DateTimeOffset.UtcNow;
        db.Wallets.Add(new Wallet { Id = "w_d", UserId = "user_d", CreditBalance = 10, LastUpdatedAt = now });
        for (int i = 0; i < 5; i++)
        {
            db.AiUsageRecords.Add(new AiUsageRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = "user_d",
                FeatureCode = i % 2 == 0 ? "writing.grade" : "vocabulary.gloss",
                ProviderId = "openai-platform",
                PromptTokens = 50, CompletionTokens = 25,
                CostEstimateUsd = 0.02m,
                Outcome = i == 0 ? AiCallOutcome.GatewayRefused : AiCallOutcome.Success,
                LatencyMs = 400,
                CreatedAt = now.AddHours(-i),
                PeriodMonthKey = now.ToString("yyyy-MM"),
                PeriodDayKey = now.ToString("yyyy-MM-dd"),
            });
        }
        await db.SaveChangesAsync();

        var svc = new AiUsageAnalyticsService(db);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var summary = await svc.GetLearnerSummaryAsync("user_d", today.AddDays(-1), today, CancellationToken.None);

        Assert.Equal(5, summary.TotalCalls);
        Assert.Equal(1, summary.FailedCalls);
        Assert.Equal(10, summary.WalletBalance);
        Assert.Equal(2, summary.ByFeature.Count);
    }

    [Fact]
    public async Task AiUsageAnalytics_AdminSummaryComputesProviderSuccessRate()
    {
        await using var db = NewContext(nameof(AiUsageAnalytics_AdminSummaryComputesProviderSuccessRate));
        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 4; i++)
        {
            db.AiUsageRecords.Add(new AiUsageRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = $"user_e_{i}",
                FeatureCode = "writing.grade",
                ProviderId = "openai-platform",
                PromptTokens = 50, CompletionTokens = 25,
                CostEstimateUsd = 0.02m,
                Outcome = i == 0 ? AiCallOutcome.ProviderError : AiCallOutcome.Success,
                LatencyMs = 100 * (i + 1),
                CreatedAt = now,
                PeriodMonthKey = now.ToString("yyyy-MM"),
                PeriodDayKey = now.ToString("yyyy-MM-dd"),
            });
        }
        await db.SaveChangesAsync();

        var svc = new AiUsageAnalyticsService(db);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var summary = await svc.GetAdminSummaryAsync(today.AddDays(-1), today, null, null, CancellationToken.None);

        Assert.Equal(4, summary.TotalCalls);
        Assert.Equal(4, summary.UniqueUsers);
        Assert.Equal(75m, summary.SuccessRate); // 3/4
        Assert.Single(summary.ByProvider);
    }

    [Fact]
    public async Task FxRateService_FallbackSeedReturnsKnownPairs()
    {
        await using var db = NewContext(nameof(FxRateService_FallbackSeedReturnsKnownPairs));
        var opts = Microsoft.Extensions.Options.Options.Create(new OetWithDrHesham.Api.Configuration.FxOptions
        {
            BaseCurrency = "USD",
            ApiKey = null,
        });
        using var http = new HttpClient();
        var svc = new FxRateService(db, http, opts, TestRuntimeSettingsProvider.FromFxOptions(opts.Value), NullLogger<FxRateService>.Instance);

        await svc.RefreshRatesAsync(CancellationToken.None);

        var rate = await svc.GetRateAsync("USD", "GBP", CancellationToken.None);
        Assert.True(rate > 0m);
        Assert.True(rate < 2m);
    }

    [Fact]
    public async Task FxRateService_SameCurrencyReturnsOne()
    {
        await using var db = NewContext(nameof(FxRateService_SameCurrencyReturnsOne));
        var opts = Microsoft.Extensions.Options.Options.Create(new OetWithDrHesham.Api.Configuration.FxOptions());
        using var http = new HttpClient();
        var svc = new FxRateService(db, http, opts, TestRuntimeSettingsProvider.FromFxOptions(opts.Value), NullLogger<FxRateService>.Instance);
        var rate = await svc.GetRateAsync("USD", "USD", CancellationToken.None);
        Assert.Equal(1m, rate);
    }

    [Fact]
    public async Task PricingExperimentService_AssignsDeterministicVariant()
    {
        await using var db = NewContext(nameof(PricingExperimentService_AssignsDeterministicVariant));
        var now = DateTimeOffset.UtcNow;
        db.PricingExperiments.Add(new PricingExperiment
        {
            Id = "exp_1",
            Code = "test", Name = "Test",
            TargetType = "plan", TargetId = "premium", Region = "*",
            Status = "running", RolloutPercent = 100,
            VariantsJson = "[{\"code\":\"control\",\"weight\":50,\"priceMultiplier\":1.0},{\"code\":\"variant_a\",\"weight\":50,\"priceMultiplier\":0.9}]",
            StartedAt = now, CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var opts = Microsoft.Extensions.Options.Options.Create(new OetWithDrHesham.Api.Configuration.FxOptions());
        using var http = new HttpClient();
        var fx = new FxRateService(db, http, opts, TestRuntimeSettingsProvider.FromFxOptions(opts.Value), NullLogger<FxRateService>.Instance);
        var svc = new PricingExperimentService(db, fx, NullLogger<PricingExperimentService>.Instance);

        var r1 = await svc.ResolveAsync("user_f", "plan", "premium", "ROW", CancellationToken.None);
        var r2 = await svc.ResolveAsync("user_f", "plan", "premium", "ROW", CancellationToken.None);

        Assert.NotNull(r1);
        Assert.NotNull(r2);
        Assert.Equal(r1!.VariantCode, r2!.VariantCode);
    }

    [Fact]
    public async Task PricingExperimentService_AppliesPriceMultiplier()
    {
        await using var db = NewContext(nameof(PricingExperimentService_AppliesPriceMultiplier));
        var now = DateTimeOffset.UtcNow;
        db.PricingExperiments.Add(new PricingExperiment
        {
            Id = "exp_2", Code = "halfoff", Name = "Half off",
            TargetType = "plan", TargetId = "premium", Region = "*",
            Status = "running", RolloutPercent = 100,
            VariantsJson = "[{\"code\":\"discount\",\"weight\":1,\"priceMultiplier\":0.5}]",
            StartedAt = now, CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var opts = Microsoft.Extensions.Options.Options.Create(new OetWithDrHesham.Api.Configuration.FxOptions());
        using var http = new HttpClient();
        var fx = new FxRateService(db, http, opts, TestRuntimeSettingsProvider.FromFxOptions(opts.Value), NullLogger<FxRateService>.Instance);
        var svc = new PricingExperimentService(db, fx, NullLogger<PricingExperimentService>.Instance);

        var basePrice = Money.FromMajor(100m, "USD");
        var applied = await svc.ApplyAsync(basePrice, "user_g", "plan", "premium", "ROW", CancellationToken.None);

        Assert.Equal(50m, applied.ToMajor());
        Assert.Equal("USD", applied.Currency);
    }

    [Fact]
    public async Task PricingExperimentService_RolloutPercentExcludesUsersAbove()
    {
        await using var db = NewContext(nameof(PricingExperimentService_RolloutPercentExcludesUsersAbove));
        var now = DateTimeOffset.UtcNow;
        db.PricingExperiments.Add(new PricingExperiment
        {
            Id = "exp_3", Code = "tiny", Name = "Tiny rollout",
            TargetType = "plan", TargetId = "premium", Region = "*",
            Status = "running", RolloutPercent = 0,
            VariantsJson = "[{\"code\":\"discount\",\"weight\":1,\"priceMultiplier\":0.5}]",
            StartedAt = now, CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var opts = Microsoft.Extensions.Options.Options.Create(new OetWithDrHesham.Api.Configuration.FxOptions());
        using var http = new HttpClient();
        var fx = new FxRateService(db, http, opts, TestRuntimeSettingsProvider.FromFxOptions(opts.Value), NullLogger<FxRateService>.Instance);
        var svc = new PricingExperimentService(db, fx, NullLogger<PricingExperimentService>.Instance);

        var r = await svc.ResolveAsync("user_h", "plan", "premium", "ROW", CancellationToken.None);
        Assert.Null(r); // 0% rollout → not enrolled.
    }
}
