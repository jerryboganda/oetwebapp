using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// Phase 12 (P12) of the OET Speaking module plan — strict state-machine
/// guard tests for <see cref="SpeakingSessionService"/>.
///
/// The Speaking lifecycle MUST follow exactly:
///
///   WarmUp → Prep → Active → Finished
///
/// Skip-attacks (e.g. WarmUp → Active without finishing warm-up, or
/// re-using the same endpoint twice) MUST surface an <see cref="ApiException"/>
/// with HTTP 409 so the frontend can present a clean error rather than
/// silently allowing the learner to bypass the unscored warm-up window or
/// re-end an already-finished session.
///
/// All tests use <see cref="DbContextOptionsBuilder.UseInMemoryDatabase(string)"/>
/// so they run fast in-process and don't depend on Program.cs DI wiring
/// (Program.cs is forbidden for this agent).
/// </summary>
public sealed class SpeakingStateMachineGuardsTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;
    private SpeakingSessionService _svc = default!;

    public Task InitializeAsync()
    {
        var dbName = $"speaking-state-{Guid.NewGuid():N}";
        var opts = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        _db = new LearnerDbContext(opts);
        _svc = new SpeakingSessionService(_db);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────
    // Guard 1: WarmUp → Prep is the only authorised exit from WarmUp.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartRolePlay_FromWarmUp_Throws409()
    {
        const string userId = "learner-state-1";
        var (_, sessionId) = await SeedSessionAsync(userId, SpeakingSessionState.WarmUp);

        var thrown = await Assert.ThrowsAsync<ApiException>(() =>
            _svc.StartRolePlayAsync(userId, sessionId, CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, thrown.StatusCode);
        // Surface a specific error code so the frontend can deep-link the
        // learner back to /warmup rather than show a generic "try again".
        Assert.Equal("speaking_session_warmup_not_finished", thrown.ErrorCode);
    }

    [Fact]
    public async Task FinishWarmup_FromWarmUp_TransitionsToPrep()
    {
        const string userId = "learner-state-2";
        var (_, sessionId) = await SeedSessionAsync(userId, SpeakingSessionState.WarmUp);

        var detail = await _svc.FinishWarmupAsync(userId, sessionId, CancellationToken.None);

        Assert.Equal(SpeakingSessionStates.Prep, detail.State);
        Assert.NotNull(detail.PrepStartedAt);
        Assert.NotNull(detail.WarmupEndedAt);

        // The session row reflects the new state.
        var refreshed = await _db.SpeakingSessions.AsNoTracking()
            .FirstAsync(s => s.Id == sessionId);
        Assert.Equal(SpeakingSessionState.Prep, refreshed.State);
    }

    [Fact]
    public async Task FinishWarmup_FromPrep_Throws409()
    {
        // Calling /finish-warmup twice (or from a session that started in
        // Prep — e.g. live-tutor mode) must fail loud so the frontend can
        // re-fetch the latest state rather than corrupt timestamps.
        const string userId = "learner-state-3";
        var (_, sessionId) = await SeedSessionAsync(userId, SpeakingSessionState.Prep);

        var thrown = await Assert.ThrowsAsync<ApiException>(() =>
            _svc.FinishWarmupAsync(userId, sessionId, CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, thrown.StatusCode);
        Assert.Equal("speaking_session_invalid_state", thrown.ErrorCode);
    }

    // ─────────────────────────────────────────────────────────────────
    // Guard 2: Prep → Active is the only authorised entry into Active.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartRolePlay_FromPrep_TransitionsToActive()
    {
        const string userId = "learner-state-4";
        var (_, sessionId) = await SeedSessionAsync(userId, SpeakingSessionState.Prep);

        var detail = await _svc.StartRolePlayAsync(userId, sessionId, CancellationToken.None);

        Assert.Equal(SpeakingSessionStates.Active, detail.State);
        Assert.NotNull(detail.RolePlayStartedAt);
    }

    [Fact]
    public async Task StartRolePlay_FromActive_Throws409()
    {
        // Starting role-play twice MUST be rejected — otherwise the
        // RolePlayStartedAt timestamp gets clobbered and the time-up
        // event fires at the wrong moment.
        const string userId = "learner-state-5";
        var (_, sessionId) = await SeedSessionAsync(userId, SpeakingSessionState.Active);

        var thrown = await Assert.ThrowsAsync<ApiException>(() =>
            _svc.StartRolePlayAsync(userId, sessionId, CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, thrown.StatusCode);
        Assert.Equal("speaking_session_invalid_state", thrown.ErrorCode);
    }

    [Fact]
    public async Task StartRolePlay_FromFinished_Throws409()
    {
        const string userId = "learner-state-6";
        var (_, sessionId) = await SeedSessionAsync(userId, SpeakingSessionState.Finished);

        var thrown = await Assert.ThrowsAsync<ApiException>(() =>
            _svc.StartRolePlayAsync(userId, sessionId, CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, thrown.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────
    // Guard 3: Active → Finished is the only authorised exit from Active.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EndSession_FromActive_TransitionsToFinished()
    {
        const string userId = "learner-state-7";
        var (_, sessionId) = await SeedSessionAsync(userId, SpeakingSessionState.Active);

        var detail = await _svc.EndSessionAsync(userId, sessionId, CancellationToken.None);

        Assert.Equal(SpeakingSessionStates.Finished, detail.State);
        Assert.NotNull(detail.EndedAt);
    }

    [Fact]
    public async Task EndSession_FromPrep_Throws409()
    {
        // Ending while still in Prep is a sequencing bug; rejecting it
        // forces the client to call /start first so the role-play window
        // is properly timed.
        const string userId = "learner-state-8";
        var (_, sessionId) = await SeedSessionAsync(userId, SpeakingSessionState.Prep);

        var thrown = await Assert.ThrowsAsync<ApiException>(() =>
            _svc.EndSessionAsync(userId, sessionId, CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, thrown.StatusCode);
        Assert.Equal("speaking_session_invalid_state", thrown.ErrorCode);
    }

    [Fact]
    public async Task EndSession_FromWarmUp_Throws409()
    {
        const string userId = "learner-state-9";
        var (_, sessionId) = await SeedSessionAsync(userId, SpeakingSessionState.WarmUp);

        var thrown = await Assert.ThrowsAsync<ApiException>(() =>
            _svc.EndSessionAsync(userId, sessionId, CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, thrown.StatusCode);
    }

    [Fact]
    public async Task EndSession_FromFinished_Throws409()
    {
        // Idempotency belongs in the endpoint layer (Idempotency-Key
        // header), not in the state machine. A second EndSession call MUST
        // fail loud so duplicate-finish bugs are visible.
        const string userId = "learner-state-10";
        var (_, sessionId) = await SeedSessionAsync(userId, SpeakingSessionState.Finished);

        var thrown = await Assert.ThrowsAsync<ApiException>(() =>
            _svc.EndSessionAsync(userId, sessionId, CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, thrown.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────
    // Guard 4: Cross-user access ALWAYS surfaces as not-found (IDOR safe).
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StateTransitions_RefuseCrossUserSessions()
    {
        // Even if a learner brute-forces a session id belonging to another
        // user, every transition MUST throw NotFound (404) — not Forbidden
        // — so the existence of the session id is not leaked. Mirrors the
        // existing LoadOwnedSessionAsync IDOR guard.
        const string ownerId = "learner-state-owner";
        const string strangerId = "learner-state-stranger";
        var (_, sessionId) = await SeedSessionAsync(ownerId, SpeakingSessionState.Prep);

        var startThrown = await Assert.ThrowsAsync<ApiException>(() =>
            _svc.StartRolePlayAsync(strangerId, sessionId, CancellationToken.None));
        Assert.Equal(StatusCodes.Status404NotFound, startThrown.StatusCode);

        var endThrown = await Assert.ThrowsAsync<ApiException>(() =>
            _svc.EndSessionAsync(strangerId, sessionId, CancellationToken.None));
        Assert.Equal(StatusCodes.Status404NotFound, endThrown.StatusCode);

        var warmupThrown = await Assert.ThrowsAsync<ApiException>(() =>
            _svc.FinishWarmupAsync(strangerId, sessionId, CancellationToken.None));
        Assert.Equal(StatusCodes.Status404NotFound, warmupThrown.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────
    // Fixture helpers
    // ─────────────────────────────────────────────────────────────────

    private async Task<(string userId, string sessionId)> SeedSessionAsync(
        string userId,
        SpeakingSessionState state)
    {
        var contentItemId = $"ci-{Guid.NewGuid():N}";
        _db.ContentItems.Add(new ContentItem
        {
            Id = contentItemId,
            ContentType = "speaking_role_play",
            ProfessionId = "nursing",
            SubtestCode = "speaking",
            Title = "State machine test card",
            Difficulty = "core",
            Status = ContentStatus.Published,
        });

        var cardId = $"card-{Guid.NewGuid():N}";
        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = contentItemId,
            ScenarioTitle = "State machine test card",
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
            State = state,
            // Backfill timestamps so the projection has plausible values
            // when the state under test is later in the lifecycle.
            WarmupStartedAt = state >= SpeakingSessionState.Prep || state == SpeakingSessionState.WarmUp
                ? now.AddMinutes(-5)
                : null,
            WarmupEndedAt = state == SpeakingSessionState.Prep
                || state == SpeakingSessionState.Active
                || state == SpeakingSessionState.Finished
                ? now.AddMinutes(-4)
                : null,
            PrepStartedAt = state == SpeakingSessionState.Prep
                || state == SpeakingSessionState.Active
                || state == SpeakingSessionState.Finished
                ? now.AddMinutes(-4)
                : null,
            RolePlayStartedAt = state == SpeakingSessionState.Active
                || state == SpeakingSessionState.Finished
                ? now.AddMinutes(-1)
                : null,
            EndedAt = state == SpeakingSessionState.Finished
                ? now
                : null,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await _db.SaveChangesAsync();
        return (userId, sessionId);
    }
}
