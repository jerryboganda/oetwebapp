using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

public class AiQuotaServiceTests
{
    private static (LearnerDbContext db, AiQuotaService quota) Build(
        bool killSwitch = false,
        AiKillSwitchScope scope = AiKillSwitchScope.PlatformKeysOnly,
        int monthlyCap = 10_000,
        int dailyCap = 2_500,
        string planCode = "pro",
        string allowedFeatures = "",
        AiOveragePolicy overage = AiOveragePolicy.Deny,
        bool disableUser = false,
        string disabledFeaturesCsv = "")
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new LearnerDbContext(options);

        db.AiGlobalPolicies.Add(new AiGlobalPolicy
        {
            Id = "global",
            KillSwitchEnabled = killSwitch,
            KillSwitchScope = scope,
            DisabledFeaturesCsv = disabledFeaturesCsv,
            MonthlyBudgetUsd = 0,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        db.AiQuotaPlans.Add(new AiQuotaPlan
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = planCode,
            Name = planCode,
            MonthlyTokenCap = monthlyCap,
            DailyTokenCap = dailyCap,
            OveragePolicy = overage,
            AllowedFeaturesCsv = allowedFeatures,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        if (disableUser)
        {
            db.AiUserQuotaOverrides.Add(new AiUserQuotaOverride
            {
                UserId = "user-001",
                AiDisabled = true,
                Reason = "Under investigation.",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan-pro", Code = planCode, Name = planCode,
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-001", UserId = "user-001", PlanId = "plan-pro",
            Status = SubscriptionStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            ChangedAt = DateTimeOffset.UtcNow,
        });

        db.SaveChanges();

        var quota = new AiQuotaService(
            db,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AiQuotaService>.Instance);
        return (db, quota);
    }

    [Fact]
    public async Task Allows_WhenWithinQuota()
    {
        var (db, quota) = Build();
        var decision = await quota.TryReserveAsync("user-001", AiFeatureCodes.WritingGrade, AiKeySource.Platform, default);
        Assert.True(decision.Allowed);
        Assert.Equal(10_000, decision.TokensCapThisPeriod);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task DeniesWithKillSwitchCode_WhenKillSwitchPlatformOnly()
    {
        var (db, quota) = Build(killSwitch: true, scope: AiKillSwitchScope.PlatformKeysOnly);
        var decision = await quota.TryReserveAsync("user-001", AiFeatureCodes.WritingGrade, AiKeySource.Platform, default);
        Assert.False(decision.Allowed);
        Assert.Equal("kill_switch", decision.ErrorCode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task KillSwitchPlatformOnly_DoesNotBlockByok()
    {
        var (db, quota) = Build(killSwitch: true, scope: AiKillSwitchScope.PlatformKeysOnly);
        var decision = await quota.TryReserveAsync("user-001", AiFeatureCodes.WritingGrade, AiKeySource.Byok, default);
        Assert.True(decision.Allowed);
        Assert.Equal("byok.unmetered", decision.PolicyTrace);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task KillSwitchAllCalls_BlocksEvenByok()
    {
        var (db, quota) = Build(killSwitch: true, scope: AiKillSwitchScope.AllCalls);
        var decision = await quota.TryReserveAsync("user-001", AiFeatureCodes.WritingGrade, AiKeySource.Byok, default);
        Assert.False(decision.Allowed);
        Assert.Equal("kill_switch", decision.ErrorCode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task DisabledFeatures_RefusesPlatformCall_BeforeQuota()
    {
        // Per-feature kill list is evaluated before the global kill-switch
        // or quota so admins can isolate a single broken feature.
        var (db, quota) = Build(disabledFeaturesCsv: "conversation.evaluation,writing.grade");
        var decision = await quota.TryReserveAsync("user-001", AiFeatureCodes.WritingGrade, AiKeySource.Platform, default);
        Assert.False(decision.Allowed);
        Assert.Equal("feature_disabled", decision.ErrorCode);
        Assert.Contains("writing.grade", decision.PolicyTrace);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task DisabledFeatures_RefusesEvenByok()
    {
        // The per-feature disable intentionally blocks BYOK too because the
        // reason to disable a feature (e.g. rulebook regression) is not
        // something a user's own key can fix.
        var (db, quota) = Build(disabledFeaturesCsv: "writing.grade");
        var decision = await quota.TryReserveAsync("user-001", AiFeatureCodes.WritingGrade, AiKeySource.Byok, default);
        Assert.False(decision.Allowed);
        Assert.Equal("feature_disabled", decision.ErrorCode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task DisabledFeatures_IsCaseInsensitiveAndTrimmed()
    {
        var (db, quota) = Build(disabledFeaturesCsv: " Writing.Grade , Other.Feature ");
        var decision = await quota.TryReserveAsync("user-001", AiFeatureCodes.WritingGrade, AiKeySource.Platform, default);
        Assert.False(decision.Allowed);
        Assert.Equal("feature_disabled", decision.ErrorCode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task DisabledFeatures_AllowsUnrelatedFeatures()
    {
        var (db, quota) = Build(disabledFeaturesCsv: "conversation.evaluation");
        var decision = await quota.TryReserveAsync("user-001", AiFeatureCodes.WritingGrade, AiKeySource.Platform, default);
        Assert.True(decision.Allowed);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task DeniesUser_WhenAdminDisabled()
    {
        var (db, quota) = Build(disableUser: true);
        var decision = await quota.TryReserveAsync("user-001", AiFeatureCodes.WritingGrade, AiKeySource.Platform, default);
        Assert.False(decision.Allowed);
        Assert.Equal("user_disabled", decision.ErrorCode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task DeniesFeatureNotInPlan_WhenAllowListExcludesIt()
    {
        var (db, quota) = Build(allowedFeatures: "conversation.reply,summarise.passage");
        var decision = await quota.TryReserveAsync("user-001", AiFeatureCodes.WritingGrade, AiKeySource.Platform, default);
        Assert.False(decision.Allowed);
        Assert.Equal("feature_not_in_plan", decision.ErrorCode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task DeniesQuotaExhausted_WhenMonthlyCapReached_DenyPolicy()
    {
        var (db, quota) = Build(monthlyCap: 100, dailyCap: 0, overage: AiOveragePolicy.Deny);
        await quota.CommitAsync("user-001", AiFeatureCodes.ConversationReply, 60, 60, 0.001m, default);
        var decision = await quota.TryReserveAsync("user-001", AiFeatureCodes.ConversationReply, AiKeySource.Platform, default);
        Assert.False(decision.Allowed);
        Assert.Equal("quota_exhausted", decision.ErrorCode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AllowsDegrade_WhenPolicyIsDegrade()
    {
        var (db, quota) = Build(monthlyCap: 100, overage: AiOveragePolicy.DegradeToSmallerModel);
        await quota.CommitAsync("user-001", AiFeatureCodes.ConversationReply, 60, 60, 0m, default);
        var decision = await quota.TryReserveAsync("user-001", AiFeatureCodes.ConversationReply, AiKeySource.Platform, default);
        Assert.True(decision.Allowed);
        Assert.Contains("degrade", decision.PolicyTrace);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Commit_IncrementsMonthAndDayCounters()
    {
        var (db, quota) = Build();
        await quota.CommitAsync("user-001", AiFeatureCodes.ConversationReply, 100, 50, 0.002m, default);
        var counters = await db.AiQuotaCounters.Where(c => c.UserId == "user-001").ToListAsync();
        Assert.Equal(2, counters.Count); // month + day
        Assert.All(counters, c => Assert.Equal(150, c.TokensUsed));
        Assert.All(counters, c => Assert.Equal(1, c.RequestsCount));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GetUserPolicyAsync_ReflectsCommittedUsage()
    {
        var (db, quota) = Build(monthlyCap: 1_000);
        await quota.CommitAsync("user-001", AiFeatureCodes.ConversationReply, 200, 100, 0.003m, default);
        var snapshot = await quota.GetUserPolicyAsync("user-001", default);
        Assert.Equal(300, snapshot.TokensUsedThisMonth);
        Assert.Equal(300, snapshot.TokensUsedToday);
        Assert.Equal(1_000, snapshot.MonthlyTokenCap);
        Assert.Equal("pro", snapshot.PlanCode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Anonymous_Bypasses_Quota()
    {
        var (db, quota) = Build();
        var decision = await quota.TryReserveAsync(null, AiFeatureCodes.AdminContentGeneration, AiKeySource.Platform, default);
        Assert.True(decision.Allowed);
        Assert.Equal("anonymous.unmetered", decision.PolicyTrace);
        await db.DisposeAsync();
    }
}

public class AiGatewayQuotaIntegrationTests
{
    private readonly RulebookLoader _loader = new();

    [Fact]
    public async Task Gateway_DoesNotDebit_WhenCallIsByok()
    {
        // Regression guard: BYOK calls must NEVER debit the platform quota.
        // Issue found during Slice 2-7 audit.
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);

        db.AiGlobalPolicies.Add(new AiGlobalPolicy
        {
            Id = "global",
            AllowByokOnNonScoringFeatures = true,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.AiQuotaPlans.Add(new AiQuotaPlan
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = "pro", Name = "pro", MonthlyTokenCap = 1_000_000, IsActive = true,
        });
        db.BillingPlans.Add(new BillingPlan { Id = "p", Code = "pro", Name = "Pro" });
        db.Subscriptions.Add(new Subscription
        {
            Id = "s", UserId = "u", PlanId = "p", Status = SubscriptionStatus.Active,
            StartedAt = DateTimeOffset.UtcNow, ChangedAt = DateTimeOffset.UtcNow,
        });
        db.UserAiPreferences.Add(new UserAiPreferences
        {
            UserId = "u", Mode = AiCredentialMode.Auto, AllowPlatformFallback = true,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        // Stash a BYOK key so resolver picks it.
        var dp = Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("test-byok");
        var httpFactory = new SimpleHttpClientFactory();
        var vault = new AiCredentialVault(db, dp, httpFactory, NullLogger<AiCredentialVault>.Instance);
        await vault.UpsertAsync("u", null, "openai-platform", "sk-thisisasupersecrettestkey-abcd", null, true, default);

        var provider = new TokenReportingProvider(prompt: 120, completion: 80);
        var recorder = new AiUsageRecorder(db, NullLogger<AiUsageRecorder>.Instance);
        var quota = new AiQuotaService(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<AiQuotaService>.Instance);
        var resolver = new AiCredentialResolver(db, quota, vault);
        var gateway = new AiGatewayService(_loader, new[] { (IAiModelProvider)provider }, recorder, quota, resolver);

        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing, Profession = ExamProfession.Medicine, Task = AiTaskMode.Score, LetterType = "routine_referral",
        });

        var result = await gateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = prompt,
            Provider = "token-reporter",
            UserId = "u",
            FeatureCode = AiFeatureCodes.ConversationReply, // non-scoring → BYOK allowed
        });

        Assert.False(string.IsNullOrWhiteSpace(result.Completion));

        // Quota counters MUST be empty — this was the bug.
        var counters = await db.AiQuotaCounters.Where(c => c.UserId == "u").ToListAsync();
        Assert.Empty(counters);

        // But usage record should still exist with KeySource=Byok.
        var record = await db.AiUsageRecords.SingleAsync();
        Assert.Equal(AiKeySource.Byok, record.KeySource);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Gateway_EnforcesQuota_AndRecordsRefusal()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);

        // Plan with 0 monthly cap → immediately over
        db.AiQuotaPlans.Add(new AiQuotaPlan
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = "trial", Name = "trial",
            MonthlyTokenCap = 1, DailyTokenCap = 0,
            OveragePolicy = AiOveragePolicy.Deny,
            IsActive = true,
        });
        db.BillingPlans.Add(new BillingPlan { Id = "p", Code = "trial", Name = "Trial" });
        db.Subscriptions.Add(new Subscription
        {
            Id = "s", UserId = "u", PlanId = "p",
            Status = SubscriptionStatus.Active,
            StartedAt = DateTimeOffset.UtcNow, ChangedAt = DateTimeOffset.UtcNow,
        });
        db.AiQuotaCounters.Add(new AiQuotaCounter
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = "u",
            PeriodKey = $"month:{DateTimeOffset.UtcNow:yyyy-MM}",
            TokensUsed = 5, RequestsCount = 1, CostAccumulatedUsd = 0,
            LastUpdatedAt = DateTimeOffset.UtcNow, RowVersion = 1,
        });
        await db.SaveChangesAsync();

        var recorder = new AiUsageRecorder(db, NullLogger<AiUsageRecorder>.Instance);
        var quota = new AiQuotaService(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<AiQuotaService>.Instance);
        var gateway = new AiGatewayService(_loader, new[] { (IAiModelProvider)new MockAiProvider() }, recorder, quota);

        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing, Profession = ExamProfession.Medicine, Task = AiTaskMode.Score, LetterType = "routine_referral",
        });

        var ex = await Assert.ThrowsAsync<AiQuotaDeniedException>(() =>
            gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserId = "u",
                FeatureCode = AiFeatureCodes.WritingGrade,
            }));

        Assert.Equal("quota_exhausted", ex.ErrorCode);

        var row = await db.AiUsageRecords.SingleAsync();
        Assert.Equal(AiCallOutcome.GatewayRefused, row.Outcome);
        Assert.Equal("quota_exhausted", row.ErrorCode);
        Assert.Contains("trial", row.PolicyTrace);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Gateway_CommitsUsage_OnSuccess()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);

        db.AiQuotaPlans.Add(new AiQuotaPlan
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = "pro", Name = "pro",
            MonthlyTokenCap = 1_000_000,
            IsActive = true,
        });
        db.BillingPlans.Add(new BillingPlan { Id = "p", Code = "pro", Name = "Pro" });
        db.Subscriptions.Add(new Subscription
        {
            Id = "s", UserId = "u", PlanId = "p",
            Status = SubscriptionStatus.Active,
            StartedAt = DateTimeOffset.UtcNow, ChangedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        // Provider that reports actual usage so Commit has something to add.
        var provider = new FakeUsageProvider(promptTokens: 120, completionTokens: 80);
        var recorder = new AiUsageRecorder(db, NullLogger<AiUsageRecorder>.Instance);
        var quota = new AiQuotaService(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<AiQuotaService>.Instance);
        var gateway = new AiGatewayService(_loader, new[] { (IAiModelProvider)provider }, recorder, quota);

        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing, Profession = ExamProfession.Medicine, Task = AiTaskMode.Score, LetterType = "routine_referral",
        });

        var result = await gateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = prompt,
            Provider = "fake-usage",
            UserId = "u",
            FeatureCode = AiFeatureCodes.WritingGrade,
        });

        Assert.False(string.IsNullOrWhiteSpace(result.Completion));

        var counters = await db.AiQuotaCounters.Where(c => c.UserId == "u").ToListAsync();
        Assert.Equal(2, counters.Count);
        Assert.All(counters, c => Assert.Equal(200, c.TokensUsed));
        await db.DisposeAsync();
    }

    private sealed class FakeUsageProvider(int promptTokens, int completionTokens) : IAiModelProvider
    {
        public string Name => "fake-usage";
        public Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
            => Task.FromResult(new AiProviderCompletion
            {
                Text = "{}",
                Usage = new AiUsage { PromptTokens = promptTokens, CompletionTokens = completionTokens },
            });
    }

    private sealed class TokenReportingProvider(int prompt, int completion) : IAiModelProvider
    {
        public string Name => "token-reporter";
        public Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
            => Task.FromResult(new AiProviderCompletion
            {
                Text = "{}",
                Usage = new AiUsage { PromptTokens = prompt, CompletionTokens = completion },
            });
    }

    private sealed class SimpleHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler());
        private sealed class StubHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
                => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
