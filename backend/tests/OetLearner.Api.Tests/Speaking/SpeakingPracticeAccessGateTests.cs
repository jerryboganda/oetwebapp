using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// Requirements gap audit (2026-07-01), gap #2 — "Practice Card Access" was
/// not plan-tierable: every plan behaved identically regardless of
/// <c>BillingPlan.SpeakingPracticeAccessEnabled</c>. These tests pin the new
/// gate in <see cref="SpeakingSessionService.FinishWarmupAsync"/>:
///
///   * An eligible subscription whose plan disables practice access is
///     blocked with a 403, before any credit is touched.
///   * An eligible subscription whose plan allows it (the default) is
///     unaffected.
///   * No-subscription / free accounts — today's a-la-carte AI credit
///     buyers — are NEVER blocked by this flag, matching pre-existing
///     behaviour exactly (regression guard).
/// </summary>
public sealed class SpeakingPracticeAccessGateTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;

    public Task InitializeAsync()
    {
        var opts = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-practice-gate-{Guid.NewGuid():N}")
            .Options;
        _db = new LearnerDbContext(opts);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task FinishWarmup_EligiblePlanWithPracticeDisabled_ThrowsForbidden()
    {
        const string userId = "learner-practice-gate-1";
        var (_, sessionId) = await SeedWarmUpSessionAsync(userId);
        var svc = new SpeakingSessionService(_db, aiPackageCreditService: null,
            entitlementResolver: new FakeEntitlementResolver(hasEligibleSubscription: true, practiceAccessEnabled: false));

        var thrown = await Assert.ThrowsAsync<ApiException>(() =>
            svc.FinishWarmupAsync(userId, sessionId, CancellationToken.None));

        Assert.Equal("speaking_practice_not_included", thrown.ErrorCode);

        // Must not have transitioned — the gate fires before any state change.
        var refreshed = await _db.SpeakingSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId);
        Assert.Equal(SpeakingSessionState.WarmUp, refreshed.State);
    }

    [Fact]
    public async Task FinishWarmup_EligiblePlanWithPracticeEnabled_Succeeds()
    {
        const string userId = "learner-practice-gate-2";
        var (_, sessionId) = await SeedWarmUpSessionAsync(userId);
        var svc = new SpeakingSessionService(_db, aiPackageCreditService: null,
            entitlementResolver: new FakeEntitlementResolver(hasEligibleSubscription: true, practiceAccessEnabled: true));

        var detail = await svc.FinishWarmupAsync(userId, sessionId, CancellationToken.None);

        Assert.Equal(SpeakingSessionStates.Prep, detail.State);
    }

    [Fact]
    public async Task FinishWarmup_NoEligibleSubscription_NeverBlocked_EvenIfPlanFlagWouldDisable()
    {
        // Free / no-subscription accounts buy AI credits a la carte today and
        // must keep working exactly as before this change — the practice
        // flag only ever applies to a *resolved, eligible* plan.
        const string userId = "learner-practice-gate-3";
        var (_, sessionId) = await SeedWarmUpSessionAsync(userId);
        var svc = new SpeakingSessionService(_db, aiPackageCreditService: null,
            entitlementResolver: new FakeEntitlementResolver(hasEligibleSubscription: false, practiceAccessEnabled: false));

        var detail = await svc.FinishWarmupAsync(userId, sessionId, CancellationToken.None);

        Assert.Equal(SpeakingSessionStates.Prep, detail.State);
    }

    [Fact]
    public async Task FinishWarmup_NoEntitlementResolverWired_SkipsGate_RegressionSafe()
    {
        // Mirrors every pre-existing test/call site that constructs
        // SpeakingSessionService without an entitlement resolver — must
        // behave exactly as it did before this change (no gate at all).
        const string userId = "learner-practice-gate-4";
        var (_, sessionId) = await SeedWarmUpSessionAsync(userId);
        var svc = new SpeakingSessionService(_db);

        var detail = await svc.FinishWarmupAsync(userId, sessionId, CancellationToken.None);

        Assert.Equal(SpeakingSessionStates.Prep, detail.State);
    }

    private sealed class FakeEntitlementResolver(bool hasEligibleSubscription, bool practiceAccessEnabled)
        : IEffectiveEntitlementResolver
    {
        public Task<EffectiveEntitlementSnapshot> ResolveAsync(string? userId, CancellationToken ct) =>
            Task.FromResult(new EffectiveEntitlementSnapshot(
                UserId: userId,
                HasEligibleSubscription: hasEligibleSubscription,
                IsTrial: false,
                Tier: hasEligibleSubscription ? "paid" : "free",
                SubscriptionId: hasEligibleSubscription ? "sub-fake" : null,
                SubscriptionStatus: null,
                PlanId: hasEligibleSubscription ? "plan-fake" : null,
                PlanVersionId: null,
                PlanCode: hasEligibleSubscription ? "fake" : null,
                AiQuotaPlanCode: null,
                AiQuotaPlanCodeSource: null,
                ActiveAddOnCodes: Array.Empty<string>(),
                IsFrozen: false,
                Trace: Array.Empty<string>())
            {
                SpeakingPracticeAccessEnabled = practiceAccessEnabled,
            });
    }

    private async Task<(string userId, string sessionId)> SeedWarmUpSessionAsync(string userId)
    {
        var contentItemId = $"ci-{Guid.NewGuid():N}";
        _db.ContentItems.Add(new ContentItem
        {
            Id = contentItemId,
            ContentType = "speaking_role_play",
            ProfessionId = "nursing",
            SubtestCode = "speaking",
            Title = "Practice access gate test card",
            Difficulty = "core",
            Status = ContentStatus.Published,
            PublishedRevisionId = $"rev-{Guid.NewGuid():N}",
        });

        var cardId = $"card-{Guid.NewGuid():N}";
        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = contentItemId,
            ScenarioTitle = "Practice access gate test card",
            Setting = "Ward",
            CandidateRole = "Nurse",
            PatientEmotion = "neutral",
            CommunicationGoal = "Inform",
            ClinicalTopic = "general",
            Difficulty = "core",
            CriteriaFocusJson = "[]",
            Status = ContentStatus.Published,
            PrepTimeSeconds = 180,
            RolePlayTimeSeconds = 300,
        });

        var sessionId = $"sps_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        _db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = sessionId,
            UserId = userId,
            RolePlayCardId = cardId,
            Mode = SpeakingSessionMode.AiSelfPractice,
            State = SpeakingSessionState.WarmUp,
            WarmupStartedAt = now.AddMinutes(-1),
            CreatedAt = now,
            UpdatedAt = now,
        });

        await _db.SaveChangesAsync();
        return (userId, sessionId);
    }
}
