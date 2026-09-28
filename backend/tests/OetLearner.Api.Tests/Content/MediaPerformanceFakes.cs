using System.Security.Claims;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Reading;
using OetLearner.Api.Services.VideoLibrary;

namespace OetLearner.Api.Tests.Content;

internal sealed class MediaPerformanceContentEntitlementService : IContentEntitlementService
{
    public bool Allowed { get; set; }
    public int AllowCalls { get; private set; }

    public Task<ContentEntitlementResult> AllowAccessAsync(
        string? userId,
        ContentPaper paper,
        CancellationToken ct)
    {
        AllowCalls++;
        return Task.FromResult(new ContentEntitlementResult(
            Allowed,
            Allowed ? "test_allowed" : "test_denied",
            null,
            null));
    }

    public Task RequireAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
        => Task.CompletedTask;

    public bool IsAdmin(ClaimsPrincipal? principal) => false;
}

internal sealed class MediaPerformanceReadingPolicyService : IReadingPolicyService
{
    public bool AllowPaperReadingMode { get; set; } = true;

    public Task<ReadingPolicy> GetGlobalAsync(CancellationToken ct)
        => Task.FromResult(new ReadingPolicy());

    public Task<ReadingUserPolicyOverride?> GetUserOverrideAsync(string userId, CancellationToken ct)
        => Task.FromResult<ReadingUserPolicyOverride?>(null);

    public Task<ReadingResolvedPolicy> ResolveForUserAsync(string? userId, CancellationToken ct)
        => Task.FromResult(new ReadingResolvedPolicy(
            AttemptsPerPaperPerUser: 3,
            AttemptCooldownMinutes: 0,
            PartATimerStrictness: "strict",
            PartATimerMinutes: 15,
            PartBCTimerMinutes: 45,
            GracePeriodSeconds: 0,
            OnExpirySubmitPolicy: "auto",
            CountdownWarnings: [],
            EnabledQuestionTypes: [],
            ShortAnswerNormalisation: "none",
            ShortAnswerAcceptSynonyms: false,
            MatchingAllowPartialCredit: false,
            UnknownTypeFallbackPolicy: "skip",
            ShowExplanationsAfterSubmit: false,
            ShowExplanationsOnlyIfWrong: false,
            ShowCorrectAnswerOnReview: false,
            SubmitRateLimitPerMinute: 10,
            AutosaveRateLimitPerMinute: 30,
            ExtraTimeEntitlementPct: 0,
            AllowMultipleConcurrentAttempts: false,
            AllowPausingAttempt: false,
            AllowResumeAfterExpiry: false,
            AllowPaperReadingMode: AllowPaperReadingMode));

    public Task<ReadingPolicy> UpsertGlobalAsync(
        ReadingPolicy next,
        string adminId,
        CancellationToken ct)
        => Task.FromResult(next);

    public Task<ReadingUserPolicyOverride> UpsertUserOverrideAsync(
        string userId,
        ReadingUserPolicyOverride next,
        string adminId,
        CancellationToken ct)
        => Task.FromResult(next);
}

internal sealed class MediaPerformanceVideoEntitlementService : IVideoEntitlementService
{
    public bool Allowed { get; set; }

    public Task<VideoEntitlementResult> AllowAccessAsync(
        string? userId,
        LibraryVideo video,
        CancellationToken ct)
        => Task.FromResult(new VideoEntitlementResult(
            Allowed,
            Allowed ? "test_allowed" : "test_denied",
            null));

    public Task RequireAccessAsync(string? userId, LibraryVideo video, CancellationToken ct)
        => Task.CompletedTask;

    public Task<VideoAccessContext> ResolveContextAsync(
        string? userId,
        bool isAdmin,
        CancellationToken ct)
        => Task.FromResult(new VideoAccessContext(
            IsAdmin: isAdmin,
            Authenticated: true,
            HasEligibleSubscription: Allowed,
            Frozen: false,
            Expired: false,
            PlanGrantsPremium: Allowed,
            AddOnGrantsPremium: false,
            CurrentTier: Allowed ? "premium" : "free"));

    public VideoEntitlementResult Evaluate(VideoAccessContext context, LibraryVideo video)
        => new(context.PlanGrantsPremium, "test", context.CurrentTier);

    public VideoEntitlementResult Evaluate(
        VideoAccessContext context,
        LibraryVideo video,
        IReadOnlyList<string>? extraLabels)
        => Evaluate(context, video);
}

internal sealed class MediaPerformanceEffectiveEntitlementResolver : IEffectiveEntitlementResolver
{
    /// <summary>Scenarios modelling a subscribed learner set this true.</summary>
    public bool HasEligibleSubscription { get; set; }

    public Task<EffectiveEntitlementSnapshot> ResolveAsync(string? userId, CancellationToken ct)
        => Task.FromResult(new EffectiveEntitlementSnapshot(
            UserId: userId,
            HasEligibleSubscription: HasEligibleSubscription,
            IsTrial: false,
            Tier: "free",
            SubscriptionId: null,
            SubscriptionStatus: null,
            PlanId: null,
            PlanVersionId: null,
            PlanCode: null,
            AiQuotaPlanCode: null,
            AiQuotaPlanCodeSource: null,
            ActiveAddOnCodes: [],
            IsFrozen: false,
            Trace: []));
}
