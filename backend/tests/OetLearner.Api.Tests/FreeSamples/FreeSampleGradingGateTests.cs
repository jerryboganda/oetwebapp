using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;
// Both namespaces declare `AiCreditReservationService`; the Ai one holds the writing hold.
using AiCreditReservationService = OetLearner.Api.Services.Ai.AiCreditReservationService;

namespace OetLearner.Api.Tests.FreeSamples;

/// <summary>
/// Free Mocks — the three server gates a zero-credit learner's ONE free AI-graded
/// sample has to clear: Writing start authorisation, the grade-time credit hold,
/// and the AI quota plan gate. Each has a paid/other-feature control proving the
/// bypass is narrow (only the sample's grading features, never emergency controls).
/// </summary>
public sealed class FreeSampleGradingGateTests
{
    private static LearnerDbContext NewDb()
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    // ── AI quota plan gate ───────────────────────────────────────────────────

    private static (LearnerDbContext Db, AiQuotaService Quota) BuildQuota(bool killSwitch = false, bool disableUser = false)
    {
        var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        db.AiGlobalPolicies.Add(new AiGlobalPolicy
        {
            Id = "global",
            KillSwitchEnabled = killSwitch,
            KillSwitchScope = AiKillSwitchScope.AllCalls,
            DisabledFeaturesCsv = "",
            MonthlyBudgetUsd = 0,
            UpdatedAt = now,
        });
        // The seeded 'free' plan: conversation-type features only, NO writing.grade / speaking.grade.
        db.AiQuotaPlans.Add(new AiQuotaPlan
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = "free",
            Name = "free",
            MonthlyTokenCap = 20_000,
            DailyTokenCap = 5_000,
            OveragePolicy = AiOveragePolicy.Deny,
            AllowedFeaturesCsv = "conversation.reply",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        if (disableUser)
        {
            db.AiUserQuotaOverrides.Add(new AiUserQuotaOverride
            {
                UserId = "user-zero", AiDisabled = true, Reason = "Under investigation.", CreatedAt = now, UpdatedAt = now,
            });
        }
        db.SaveChanges();
        var quota = new AiQuotaService(
            db, new MemoryCache(new MemoryCacheOptions()), NullLogger<AiQuotaService>.Instance, new EffectiveEntitlementResolver(db));
        return (db, quota);
    }

    [Fact]
    public async Task Quota_ZeroCreditLearnerOnTheFreePlan_IsRefusedGradingWithoutTheGrant()
    {
        var (db, quota) = BuildQuota();
        await using var _ = db;

        var decision = await quota.TryReserveAsync("user-zero", AiFeatureCodes.WritingGrade, AiKeySource.Platform, default);

        Assert.False(decision.Allowed);
        Assert.Equal("feature_not_in_plan", decision.ErrorCode);
    }

    [Theory]
    [InlineData("writing.grade")]
    [InlineData("speaking.grade")]
    public async Task Quota_TheFreeSampleGrant_UnlocksOnlyTheSamplesGradingFeatures(string featureCode)
    {
        var (db, quota) = BuildQuota();
        await using var _ = db;

        var decision = await quota.TryReserveAsync("user-zero", featureCode, AiKeySource.Platform, true, default);

        Assert.True(decision.Allowed);
        Assert.Equal("free_sample.unmetered", decision.PolicyTrace);
    }

    [Fact]
    public async Task Quota_TheGrantDoesNotWidenToOtherFeatures()
    {
        var (db, quota) = BuildQuota();
        await using var _ = db;

        var decision = await quota.TryReserveAsync("user-zero", "writing.coach", AiKeySource.Platform, true, default);

        Assert.False(decision.Allowed);
        Assert.Equal("feature_not_in_plan", decision.ErrorCode);
    }

    [Fact]
    public async Task Quota_AdminEmergencyControlsStillWinOverTheGrant()
    {
        var (killedDb, killed) = BuildQuota(killSwitch: true);
        await using var _1 = killedDb;
        var killDecision = await killed.TryReserveAsync("user-zero", AiFeatureCodes.WritingGrade, AiKeySource.Platform, true, default);
        Assert.False(killDecision.Allowed);
        Assert.Equal("kill_switch", killDecision.ErrorCode);

        var (disabledDb, disabled) = BuildQuota(disableUser: true);
        await using var _2 = disabledDb;
        var disabledDecision = await disabled.TryReserveAsync("user-zero", AiFeatureCodes.SpeakingGrade, AiKeySource.Platform, true, default);
        Assert.False(disabledDecision.Allowed);
        Assert.Equal("user_disabled", disabledDecision.ErrorCode);
    }

    // ── grade-time credit hold ───────────────────────────────────────────────

    [Fact]
    public async Task Reservation_TheFreeSampleHoldIsZeroUnitsAndIdempotent_WhilePaidGradingStillNeedsCredits()
    {
        await using var db = NewDb();
        var ledger = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var reservations = new AiCreditReservationService(db, ledger, TimeProvider.System);

        var ticket = await reservations.ReserveFreeSampleAsync("learner-zero", "op-free-1", "writing-grade:free1", default);
        Assert.Equal("free_sample", ticket.BucketKind);
        Assert.Equal(0, ticket.Units);
        Assert.False(ticket.AlreadyExisted);

        var again = await reservations.ReserveFreeSampleAsync("learner-zero", "op-free-1", "writing-grade:free1", default);
        Assert.True(again.AlreadyExisted);
        Assert.Equal(ticket.ReservationId, again.ReservationId);

        // Control: the same zero-credit learner cannot hold a PAID grade.
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            reservations.ReserveWritingAsync("learner-zero", "op-paid-1", "writing-grade:paid1", default));
        Assert.Equal("ai_credits_insufficient", ex.ErrorCode);
    }

    // ── Writing start authorisation ──────────────────────────────────────────

    private static WritingEntitlementService BuildWritingEntitlement(LearnerDbContext db)
        => new(
            db,
            new EffectiveEntitlementResolver(db),
            new WritingOptionsProvider(db, new MemoryCache(new MemoryCacheOptions())),
            new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance));

    [Fact]
    public async Task WritingStart_ZeroCreditLearnerMayOpenTheOfferedTask_WithoutBurningTheSample()
    {
        await using var db = NewDb();
        await FreeSampleServiceTests.EnableAsync(db);
        var offered = await FreeSampleServiceTests.SeedScenarioAsync(db, "medicine", "Alpha", difficulty: 1);
        await FreeSampleServiceTests.SeedLearnerAsync(db, "learner-zero", "medicine");
        var svc = BuildWritingEntitlement(db);

        var first = await svc.AuthorizeStartAsync("learner-zero", "ref-1", offered.ToString("D"), default);
        var refresh = await svc.AuthorizeStartAsync("learner-zero", "ref-1", offered.ToString("D"), default);

        Assert.True(first.Allowed);
        Assert.Equal("free_sample", first.EntitlementSource);
        Assert.False(first.Charged);
        Assert.Equal(ContentEntitlementService.FreeSampleFeedback, first.FeedbackMessage);
        Assert.True(refresh.Allowed);                 // the eligibility GET is read-only …
        Assert.Empty(db.FreeSampleClaims);            // … the claim is only taken at grade time
    }

    [Fact]
    public async Task WritingStart_AnyOtherTaskStaysPaid_AndTheFlagOffMeansNothingIsFree()
    {
        await using var db = NewDb();
        await FreeSampleServiceTests.EnableAsync(db);
        var offered = await FreeSampleServiceTests.SeedScenarioAsync(db, "medicine", "Alpha", difficulty: 1);
        var other = await FreeSampleServiceTests.SeedScenarioAsync(db, "medicine", "Bravo", difficulty: 2);
        await FreeSampleServiceTests.SeedLearnerAsync(db, "learner-zero", "medicine");
        var svc = BuildWritingEntitlement(db);

        var blocked = await svc.AuthorizeStartAsync("learner-zero", "ref-2", other.ToString("D"), default);
        Assert.False(blocked.Allowed);
        Assert.Equal("premium_required", blocked.ErrorCode);

        // Kill switch: the flag is switched off -> even the offered task is paid again.
        (await db.FeatureFlags.SingleAsync()).Enabled = false;
        await db.SaveChangesAsync();
        var off = await svc.AuthorizeStartAsync("learner-zero", "ref-3", offered.ToString("D"), default);
        Assert.False(off.Allowed);
    }
}
