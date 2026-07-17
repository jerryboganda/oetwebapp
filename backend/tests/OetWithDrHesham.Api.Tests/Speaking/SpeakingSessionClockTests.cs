using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Speaking;

namespace OetWithDrHesham.Api.Tests.Speaking;

/// <summary>
/// WS1 — server-authoritative Speaking session clock tests
/// (Developer Implementation Notes §1.2, §13.3, §22.5).
///
/// The clock is computed entirely server-side from persisted timestamps
/// plus the card's prep/role-play windows. These tests prove:
///   * each lifecycle stage maps to the right clock stage + advance options;
///   * a timed stage whose deadline has passed reports Expired/0 without
///     mutating persisted state (read-only);
///   * cross-user access is IDOR-safe (NotFound);
///   * the §22.5 technical-issue flag is recorded without touching scoring
///     or the state machine.
///
/// Uses <see cref="DbContextOptionsBuilder.UseInMemoryDatabase(string)"/>
/// so they run in-process and do not depend on Program.cs DI wiring.
/// </summary>
public sealed class SpeakingSessionClockTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;
    private SpeakingSessionService _svc = default!;

    public Task InitializeAsync()
    {
        var opts = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-clock-{Guid.NewGuid():N}")
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

    [Fact]
    public async Task Clock_InPrep_ReportsPrepStageWithRemainingTime()
    {
        const string userId = "clk-prep";
        var sessionId = await SeedAsync(userId, SpeakingSessionState.Prep,
            prepStartedAt: DateTimeOffset.UtcNow.AddSeconds(-10));

        var clock = await _svc.GetClockAsync(userId, sessionId, CancellationToken.None);

        Assert.Equal("prep", clock.Stage);
        Assert.False(clock.Expired);
        // 180s window started 10s ago → ~170s remaining (allow slack).
        Assert.NotNull(clock.SecondsRemaining);
        Assert.InRange(clock.SecondsRemaining!.Value, 150, 180);
        Assert.Equal(new[] { "active" }, clock.CanAdvanceTo);
        Assert.NotNull(clock.StageEndsAt);
    }

    [Fact]
    public async Task Clock_InActive_ReportsActiveStageWithRemainingTime()
    {
        const string userId = "clk-active";
        var sessionId = await SeedAsync(userId, SpeakingSessionState.Active,
            rolePlayStartedAt: DateTimeOffset.UtcNow.AddSeconds(-30));

        var clock = await _svc.GetClockAsync(userId, sessionId, CancellationToken.None);

        Assert.Equal("active", clock.Stage);
        Assert.False(clock.Expired);
        // 300s window started 30s ago → ~270s remaining.
        Assert.InRange(clock.SecondsRemaining!.Value, 250, 300);
        Assert.Equal(new[] { "finished" }, clock.CanAdvanceTo);
    }

    [Fact]
    public async Task Clock_ExpiredActiveWindow_ReportsExpiredZeroWithoutMutatingState()
    {
        const string userId = "clk-expired";
        var sessionId = await SeedAsync(userId, SpeakingSessionState.Active,
            rolePlayStartedAt: DateTimeOffset.UtcNow.AddSeconds(-600));

        var clock = await _svc.GetClockAsync(userId, sessionId, CancellationToken.None);

        Assert.Equal("active", clock.Stage);
        Assert.True(clock.Expired);
        Assert.Equal(0, clock.SecondsRemaining);

        // The clock is read-only: the persisted state must NOT have flipped
        // to Expired/Finished just because the deadline passed.
        var refreshed = await _db.SpeakingSessions.AsNoTracking()
            .FirstAsync(s => s.Id == sessionId);
        Assert.Equal(SpeakingSessionState.Active, refreshed.State);
    }

    [Fact]
    public async Task Clock_InFinished_ReportsNoDeadlineAndNoAdvanceOptions()
    {
        const string userId = "clk-finished";
        var sessionId = await SeedAsync(userId, SpeakingSessionState.Finished,
            rolePlayStartedAt: DateTimeOffset.UtcNow.AddMinutes(-6),
            endedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var clock = await _svc.GetClockAsync(userId, sessionId, CancellationToken.None);

        Assert.Equal("finished", clock.Stage);
        Assert.Null(clock.SecondsRemaining);
        Assert.False(clock.Expired);
        Assert.Empty(clock.CanAdvanceTo);
    }

    [Fact]
    public async Task Clock_InWarmUp_ReportsWarmupStageWithNoCountdown()
    {
        const string userId = "clk-warmup";
        var sessionId = await SeedAsync(userId, SpeakingSessionState.WarmUp);

        var clock = await _svc.GetClockAsync(userId, sessionId, CancellationToken.None);

        Assert.Equal("warmup", clock.Stage);
        Assert.Null(clock.SecondsRemaining);
        Assert.Equal(new[] { "prep" }, clock.CanAdvanceTo);
    }

    [Fact]
    public async Task Clock_CrossUser_ThrowsNotFound()
    {
        var sessionId = await SeedAsync("clk-owner", SpeakingSessionState.Prep);

        var thrown = await Assert.ThrowsAsync<ApiException>(() =>
            _svc.GetClockAsync("clk-stranger", sessionId, CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, thrown.StatusCode);
    }

    [Fact]
    public async Task ReportTechnicalIssue_SetsFlagAndNote_WithoutChangingState()
    {
        const string userId = "clk-tech";
        var sessionId = await SeedAsync(userId, SpeakingSessionState.Active,
            rolePlayStartedAt: DateTimeOffset.UtcNow.AddSeconds(-30));

        await _svc.ReportTechnicalIssueAsync(userId, sessionId, "Mic dropped out", CancellationToken.None);

        var refreshed = await _db.SpeakingSessions.AsNoTracking()
            .FirstAsync(s => s.Id == sessionId);
        Assert.True(refreshed.TechnicalIssueFlag);
        Assert.Equal("Mic dropped out", refreshed.TechnicalIssueNote);
        // State machine untouched.
        Assert.Equal(SpeakingSessionState.Active, refreshed.State);
    }

    // ─────────────────────────────────────────────────────────────────
    // Fixture
    // ─────────────────────────────────────────────────────────────────

    private async Task<string> SeedAsync(
        string userId,
        SpeakingSessionState state,
        DateTimeOffset? prepStartedAt = null,
        DateTimeOffset? rolePlayStartedAt = null,
        DateTimeOffset? endedAt = null)
    {
        var contentItemId = $"ci-{Guid.NewGuid():N}";
        _db.ContentItems.Add(new ContentItem
        {
            Id = contentItemId,
            ContentType = "speaking_role_play",
            ProfessionId = "nursing",
            SubtestCode = "speaking",
            Title = "Clock test card",
            Difficulty = "core",
            Status = ContentStatus.Published,
            PublishedRevisionId = $"rev-{Guid.NewGuid():N}",
        });

        var cardId = $"card-{Guid.NewGuid():N}";
        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = contentItemId,
            ScenarioTitle = "Clock test card",
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
            WarmupStartedAt = state >= SpeakingSessionState.Prep ? now.AddMinutes(-7) : null,
            PrepStartedAt = prepStartedAt
                ?? (state is SpeakingSessionState.Prep or SpeakingSessionState.Active or SpeakingSessionState.Finished
                    ? now.AddMinutes(-5)
                    : null),
            RolePlayStartedAt = rolePlayStartedAt
                ?? (state is SpeakingSessionState.Active or SpeakingSessionState.Finished
                    ? now.AddMinutes(-2)
                    : null),
            EndedAt = endedAt ?? (state == SpeakingSessionState.Finished ? now : null),
            CreatedAt = now,
            UpdatedAt = now,
        });

        await _db.SaveChangesAsync();
        return sessionId;
    }
}
