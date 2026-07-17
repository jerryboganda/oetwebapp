using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Speaking;

namespace OetWithDrHesham.Api.Tests.Speaking;

/// <summary>
/// WS4 — submit-for-marking gate (Developer Implementation Notes §4) and
/// WS6 — result-visibility resolution (§10).
///
/// WS4 invariants proven:
///   * submitting before the role-play is Finished is rejected (Conflict);
///   * submitting a Finished session with no recording/transcript is rejected
///     (the assessor evidence gate);
///   * a Finished session with a non-empty transcript stamps SubmittedAt;
///   * a second submit is idempotent (no error, timestamp unchanged).
///
/// WS6 invariants proven:
///   * the global default config is created on first read and shows everything;
///   * a per-card override row resolves over the global default.
///
/// Uses the in-memory provider so the tests run in-process without Program.cs
/// DI wiring, matching <see cref="SpeakingSessionClockTests"/>.
/// </summary>
public sealed class SpeakingSubmitAndVisibilityTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;
    private SpeakingSessionService _sessions = default!;
    private SpeakingResultVisibilityService _visibility = default!;

    public Task InitializeAsync()
    {
        var opts = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-submit-{Guid.NewGuid():N}")
            .Options;
        _db = new LearnerDbContext(opts);
        _sessions = new SpeakingSessionService(_db);
        _visibility = new SpeakingResultVisibilityService(
            _db,
            TimeProvider.System,
            NullLogger<SpeakingResultVisibilityService>.Instance);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    // ── WS4 — submit-for-marking gate ──────────────────────────────────

    [Fact]
    public async Task Submit_BeforeFinished_ThrowsConflict()
    {
        const string userId = "sub-active";
        var sessionId = await SeedSessionAsync(userId, SpeakingSessionState.Active);

        var thrown = await Assert.ThrowsAsync<ApiException>(() =>
            _sessions.SubmitForMarkingAsync(userId, sessionId, CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, thrown.StatusCode);
        Assert.Equal("speaking_session_not_finished", thrown.ErrorCode);
    }

    [Fact]
    public async Task Submit_FinishedWithoutEvidence_ThrowsConflict()
    {
        const string userId = "sub-noevidence";
        var sessionId = await SeedSessionAsync(userId, SpeakingSessionState.Finished);

        var thrown = await Assert.ThrowsAsync<ApiException>(() =>
            _sessions.SubmitForMarkingAsync(userId, sessionId, CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, thrown.StatusCode);
        Assert.Equal("speaking_session_no_recording", thrown.ErrorCode);
    }

    [Fact]
    public async Task Submit_FinishedWithTranscript_StampsSubmittedAt_AndIsIdempotent()
    {
        const string userId = "sub-ok";
        var sessionId = await SeedSessionAsync(userId, SpeakingSessionState.Finished);
        await SeedTranscriptAsync(sessionId, wordCount: 42);

        var first = await _sessions.SubmitForMarkingAsync(userId, sessionId, CancellationToken.None);
        Assert.NotNull(first.SubmittedAt);

        var persisted = await _db.SpeakingSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId);
        var stampedAt = persisted.SubmittedAt;
        Assert.NotNull(stampedAt);

        // Second submit is idempotent — no throw, timestamp preserved.
        var second = await _sessions.SubmitForMarkingAsync(userId, sessionId, CancellationToken.None);
        Assert.Equal(first.SubmittedAt, second.SubmittedAt);

        var persistedAgain = await _db.SpeakingSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId);
        Assert.Equal(stampedAt, persistedAgain.SubmittedAt);
    }

    // ── WS6 — result-visibility resolution ─────────────────────────────

    [Fact]
    public async Task Visibility_GlobalDefault_CreatedOnFirstRead_ShowsEverything()
    {
        var dto = await _visibility.ResolveDtoAsync(null, CancellationToken.None);

        Assert.Null(dto.RolePlayCardId);
        Assert.True(dto.ShowSubmissionReceived);
        Assert.True(dto.ShowAiEstimate);
        Assert.True(dto.ShowTutorScore);
        Assert.True(dto.ShowTranscript);
        Assert.True(dto.AllowReattempt);

        // The global row is now persisted.
        Assert.True(await _db.SpeakingResultVisibilityConfigs.AnyAsync(c => c.Id == "global"));
    }

    [Fact]
    public async Task Visibility_CardOverride_ResolvesOverGlobalDefault()
    {
        const string cardId = "rpc-override";

        // Establish a global default first, then override the card to hide the
        // transcript + AI estimate.
        await _visibility.ResolveDtoAsync(null, CancellationToken.None);
        var overrideDto = new SpeakingResultVisibilityDto(
            RolePlayCardId: cardId,
            ShowSubmissionReceived: true,
            ShowAiEstimate: false,
            ShowReadinessBand: true,
            ShowTutorScore: true,
            ShowFullCriteria: true,
            ShowTranscript: false,
            ShowTutorComments: true,
            ShowRecommendedDrills: true,
            AllowReattempt: true,
            UpdatedAt: DateTimeOffset.UtcNow);

        await _visibility.UpsertAsync(overrideDto, cardId, CancellationToken.None);

        var resolved = await _visibility.ResolveDtoAsync(cardId, CancellationToken.None);
        Assert.Equal(cardId, resolved.RolePlayCardId);
        Assert.False(resolved.ShowAiEstimate);
        Assert.False(resolved.ShowTranscript);
        Assert.True(resolved.ShowTutorScore);

        // A different card with no override still resolves to the global default.
        var fallback = await _visibility.ResolveDtoAsync("rpc-unconfigured", CancellationToken.None);
        Assert.Null(fallback.RolePlayCardId);
        Assert.True(fallback.ShowAiEstimate);
        Assert.True(fallback.ShowTranscript);
    }

    // ── Fixture ────────────────────────────────────────────────────────

    private async Task<string> SeedSessionAsync(string userId, SpeakingSessionState state)
    {
        var contentItemId = $"ci-{Guid.NewGuid():N}";
        _db.ContentItems.Add(new ContentItem
        {
            Id = contentItemId,
            ContentType = "speaking_role_play",
            ProfessionId = "nursing",
            SubtestCode = "speaking",
            Title = "Submit test card",
            Difficulty = "core",
            Status = ContentStatus.Published,
            PublishedRevisionId = $"rev-{Guid.NewGuid():N}",
        });

        var cardId = $"card-{Guid.NewGuid():N}";
        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = contentItemId,
            ScenarioTitle = "Submit test card",
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
            WarmupStartedAt = now.AddMinutes(-7),
            PrepStartedAt = now.AddMinutes(-5),
            RolePlayStartedAt = state is SpeakingSessionState.Active or SpeakingSessionState.Finished
                ? now.AddMinutes(-2)
                : null,
            EndedAt = state == SpeakingSessionState.Finished ? now : null,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await _db.SaveChangesAsync();
        return sessionId;
    }

    private async Task SeedTranscriptAsync(string sessionId, int wordCount)
    {
        _db.SpeakingTranscripts.Add(new SpeakingTranscript
        {
            Id = $"spt_{Guid.NewGuid():N}",
            SpeakingSessionId = sessionId,
            Provider = "mock",
            Language = "en",
            SegmentsJson = "[]",
            IsLatest = true,
            WordCount = wordCount,
            MeanConfidence = 0.9,
            GeneratedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }
}
