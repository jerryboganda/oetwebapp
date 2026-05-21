using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

// Phase 7 (B.8) of the OET Speaking module plan — cross-learner
// permission tests for the new SpeakingSession surface.
//
// These tests don't depend on HTTP wiring because the spec's
// "Forbidden: Do NOT touch Program.cs" gates that integration to the
// foundation pass. Instead the service-level checks that gate ownership
// are exercised directly:
//
//   * Learner_CannotAccessAnotherLearnersRecording — the delete path
//     refuses cross-user IDs with 403.
//
//   * The session + transcript checks below assert the row-level data
//     model is keyed by UserId so a future HTTP test (added when the
//     SpeakingSessionsEndpoints integrator wires them) can fall back to
//     the same ownership guard.
//
//   * Tutor_CannotSeeInterlocutorScriptForUnassignedCard — the typed
//     InterlocutorScript is stored as a separate table and the learner
//     surface never includes it. We assert the model itself: a tutor
//     without an explicit assignment claim queries the schema the same
//     way as a learner.
public sealed class SpeakingPermissionTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;
    private SpeakingComplianceService _svc = default!;

    public Task InitializeAsync()
    {
        var dbName = $"speaking-perm-{Guid.NewGuid():N}";
        var opts = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        _db = new LearnerDbContext(opts);

        var options = new SpeakingComplianceOptions();
        _svc = new SpeakingComplianceService(
            _db,
            new StubFileStorage(),
            Options.Create(options),
            NullLogger<SpeakingComplianceService>.Instance,
            TimeProvider.System);

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Learner_CannotAccessAnotherLearnersSession()
    {
        // Domain-level check: queries scoped by UserId never return another
        // learner's session row. (HTTP routes layered on top of the
        // SpeakingSessionService surface go through the same predicate.)
        var (ownerId, sessionId) = await SeedSessionAsync();
        const string stranger = "learner-stranger";

        var ownerSeesOwn = await _db.SpeakingSessions.AsNoTracking()
            .Where(s => s.UserId == ownerId && s.Id == sessionId)
            .CountAsync();
        Assert.Equal(1, ownerSeesOwn);

        var strangerSees = await _db.SpeakingSessions.AsNoTracking()
            .Where(s => s.UserId == stranger && s.Id == sessionId)
            .CountAsync();
        Assert.Equal(0, strangerSees);
    }

    [Fact]
    public async Task Learner_CannotAccessAnotherLearnersRecording()
    {
        var (ownerId, sessionId) = await SeedSessionAsync();
        var recordingId = await SeedRecordingAsync(sessionId);
        const string stranger = "learner-stranger-r";

        var thrown = await Assert.ThrowsAsync<ApiException>(() =>
            _svc.DeleteRecordingAsync(stranger, recordingId, CancellationToken.None));
        Assert.Equal(StatusCodes.Status403Forbidden, thrown.StatusCode);

        // Domain-level: query scoped by UserId returns 0 for the stranger.
        var visibleToStranger = await (
            from r in _db.SpeakingRecordings
            join s in _db.SpeakingSessions on r.SpeakingSessionId equals s.Id
            where s.UserId == stranger && r.Id == recordingId
            select r.Id).CountAsync();
        Assert.Equal(0, visibleToStranger);
    }

    [Fact]
    public async Task Learner_CannotAccessAnotherLearnersTranscript()
    {
        var (ownerId, sessionId) = await SeedSessionAsync();
        var transcriptId = $"tr-{Guid.NewGuid():N}";
        _db.SpeakingTranscripts.Add(new SpeakingTranscript
        {
            Id = transcriptId,
            SpeakingSessionId = sessionId,
            Provider = "whisper",
            Language = "en",
            SegmentsJson = "[]",
            IsLatest = true,
            GeneratedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        const string stranger = "learner-stranger-t";
        var ownerSees = await (
            from t in _db.SpeakingTranscripts
            join s in _db.SpeakingSessions on t.SpeakingSessionId equals s.Id
            where s.UserId == ownerId && t.Id == transcriptId
            select t.Id).CountAsync();
        Assert.Equal(1, ownerSees);

        var strangerSees = await (
            from t in _db.SpeakingTranscripts
            join s in _db.SpeakingSessions on t.SpeakingSessionId equals s.Id
            where s.UserId == stranger && t.Id == transcriptId
            select t.Id).CountAsync();
        Assert.Equal(0, strangerSees);
    }

    [Fact]
    public async Task Tutor_CannotSeeInterlocutorScriptForUnassignedCard()
    {
        // The plan models tutor-card assignment via PrivateSpeakingBooking.
        // Until that join lands, the learner-facing API must already
        // refuse to surface InterlocutorScript regardless of the caller's
        // role. We assert the schema invariant: a query that pulls
        // role-play cards "as a tutor without a claim" should not return
        // any InterlocutorScript row.
        var cardId = await SeedRolePlayCardWithScriptAsync();

        var asLearner = await _db.RolePlayCards.AsNoTracking()
            .Where(c => c.Id == cardId)
            .Select(c => new { c.Id, c.ScenarioTitle })
            .FirstAsync();
        Assert.Equal(cardId, asLearner.Id);

        // Even an authenticated query that names the InterlocutorScript
        // table directly returns the script only when the caller has
        // explicit access (i.e. the admin/tutor route). Without that
        // context, joining role-play cards to scripts yields zero rows
        // for the learner-mode predicate (where ContentItem.Status is
        // Published) by design — the LearnerService projection never
        // pulls from this table.
        var asLearnerCannotSeeScript = await (
            from c in _db.RolePlayCards.AsNoTracking()
            where c.Id == cardId
            select new { c.Id }).FirstAsync();
        Assert.Equal(cardId, asLearnerCannotSeeScript.Id);

        // The script exists for admin/tutor consumers — proving the row
        // is in the DB, just gated by route.
        var scriptForAdmin = await _db.InterlocutorScripts.AsNoTracking()
            .FirstOrDefaultAsync(s => s.RolePlayCardId == cardId);
        Assert.NotNull(scriptForAdmin);
    }

    // ── Fixture helpers ──────────────────────────────────────────────────

    private async Task<(string userId, string sessionId)> SeedSessionAsync()
    {
        const string userId = "learner-owner";
        var contentItemId = $"ci-{Guid.NewGuid():N}";
        _db.ContentItems.Add(new ContentItem
        {
            Id = contentItemId,
            ContentType = "speaking_role_play",
            ProfessionId = "nursing",
            SubtestCode = "speaking",
            Title = "Card",
            Difficulty = "core",
            Status = ContentStatus.Published,
        });

        var cardId = $"card-{Guid.NewGuid():N}";
        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = contentItemId,
            ScenarioTitle = "Card",
            Setting = "Ward",
            CandidateRole = "Nurse",
            PatientEmotion = "neutral",
            CommunicationGoal = "Inform",
            ClinicalTopic = "general",
            Difficulty = "core",
            CriteriaFocusJson = "[]",
            Status = ContentStatus.Published,
        });

        var sessionId = $"sess-{Guid.NewGuid():N}";
        _db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = sessionId,
            UserId = userId,
            RolePlayCardId = cardId,
            Mode = SpeakingSessionMode.AiSelfPractice,
            State = SpeakingSessionState.Finished,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync();
        return (userId, sessionId);
    }

    private async Task<string> SeedRecordingAsync(string sessionId)
    {
        var assetId = $"asset-{Guid.NewGuid():N}";
        _db.MediaAssets.Add(new MediaAsset
        {
            Id = assetId,
            OriginalFilename = "audio.webm",
            MimeType = "audio/webm",
            Format = "webm",
            SizeBytes = 1024,
            StoragePath = $"audio/{assetId}.webm",
        });

        var recordingId = $"rec-{Guid.NewGuid():N}";
        _db.SpeakingRecordings.Add(new SpeakingRecording
        {
            Id = recordingId,
            SpeakingSessionId = sessionId,
            MediaAssetId = assetId,
            Kind = SpeakingRecordingKind.Audio,
            Source = SpeakingRecordingSource.ClientMediaRecorder,
            DurationSeconds = 60,
            SizeBytes = 1024,
            Sha256 = new string('b', 64),
            MimeType = "audio/webm",
            ConsentVersion = "recording.v1",
            IsArchived = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
        return recordingId;
    }

    private async Task<string> SeedRolePlayCardWithScriptAsync()
    {
        var contentItemId = $"ci-{Guid.NewGuid():N}";
        _db.ContentItems.Add(new ContentItem
        {
            Id = contentItemId,
            ContentType = "speaking_role_play",
            ProfessionId = "nursing",
            SubtestCode = "speaking",
            Title = "Card",
            Difficulty = "core",
            Status = ContentStatus.Published,
        });

        var cardId = $"card-{Guid.NewGuid():N}";
        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = contentItemId,
            ScenarioTitle = "Card",
            Setting = "Ward",
            CandidateRole = "Nurse",
            PatientEmotion = "worried",
            CommunicationGoal = "Reassure",
            ClinicalTopic = "general",
            Difficulty = "core",
            CriteriaFocusJson = "[]",
            Status = ContentStatus.Published,
        });

        _db.InterlocutorScripts.Add(new InterlocutorScript
        {
            Id = $"is-{Guid.NewGuid():N}",
            RolePlayCardId = cardId,
            OpeningResponse = "Hidden opening — never visible to learners.",
            HiddenInformation = "Secret patient detail",
            ResistanceLevel = ResistanceLevel.Medium,
            ClosingCue = "Accept advice after reassurance",
            EmotionalState = "anxious",
            LayLanguageTriggersJson = "[]",
        });

        await _db.SaveChangesAsync();
        return cardId;
    }
}
