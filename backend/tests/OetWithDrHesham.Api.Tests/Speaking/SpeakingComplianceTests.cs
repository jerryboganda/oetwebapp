using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Speaking;

namespace OetWithDrHesham.Api.Tests.Speaking;

// Phase 7 (B.8) of the OET Speaking module plan.
//
// These tests pin the compliance + retention contract documented in the
// plan:
//   * NonOwnerRecordingAccess_EmitsAuditEvent — admin/tutor non-owner
//     access creates an AuditEvent row.
//   * RetentionWorker_DeletesExpiredRecordings — the Phase 7 sweep
//     archives rows whose RetentionExpiresAt has elapsed and writes
//     audit events.
//   * LearnerCanDeleteOwnRecording_AndCannotDeleteOthers — GDPR erasure
//     respects ownership.
//   * ConsentVersioning_StoresRevocations — revoke marks RevokedAt.
//   * TutorReviewedRecording_GetsExtendedRetention — the compliance
//     options expose the dual retention window.
//
// All tests target the service surface directly against an isolated
// in-memory LearnerDbContext so they don't depend on HTTP wiring done
// by the foundation/integrator (Program.cs is out of scope here).
public sealed class SpeakingComplianceTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;
    private StubFileStorage _storage = default!;
    private SpeakingComplianceService _svc = default!;
    private SpeakingComplianceOptions _options = default!;

    public Task InitializeAsync()
    {
        var dbName = $"speaking-compliance-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        _db = new LearnerDbContext(options);

        _storage = new StubFileStorage();
        _options = new SpeakingComplianceOptions
        {
            RetentionDaysDefault = 90,
            RetentionDaysWhenTutorReviewed = 365,
            CurrentConsentVersion = "recording.v1",
            CurrentLiveVideoConsentVersion = "live_video_with_tutor.v1",
        };

        _svc = new SpeakingComplianceService(
            _db,
            _storage,
            Options.Create(_options),
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
    public async Task NonOwnerRecordingAccess_EmitsAuditEvent()
    {
        const string ownerId = "learner-owner-1";
        const string adminId = "admin-actor-1";
        var (sessionId, recordingId) = await SeedSessionWithRecordingAsync(ownerId);

        var beforeCount = await _db.AuditEvents.AsNoTracking()
            .Where(a => a.Action == "SpeakingRecordingAccessed" && a.ResourceId == recordingId)
            .CountAsync();

        var recording = await _svc.AdminAccessRecordingAsync(
            adminId, "Reviewer One", recordingId, "Calibration drift review", CancellationToken.None);

        Assert.Equal(recordingId, recording.Id);

        var afterCount = await _db.AuditEvents.AsNoTracking()
            .Where(a => a.Action == "SpeakingRecordingAccessed" && a.ResourceId == recordingId)
            .CountAsync();
        Assert.Equal(beforeCount + 1, afterCount);

        var row = await _db.AuditEvents.AsNoTracking()
            .Where(a => a.Action == "SpeakingRecordingAccessed" && a.ResourceId == recordingId)
            .OrderByDescending(a => a.OccurredAt)
            .FirstAsync();
        Assert.Equal(adminId, row.ActorId);
        Assert.Equal("SpeakingRecording", row.ResourceType);
        Assert.Contains("Calibration drift review", row.Details ?? string.Empty);
    }

    [Fact]
    public async Task RetentionWorker_DeletesExpiredRecordings()
    {
        const string ownerId = "learner-retention-1";
        var (_, recordingId) = await SeedSessionWithRecordingAsync(
            ownerId,
            retentionExpiresAt: DateTimeOffset.UtcNow.AddDays(-2));

        var key = (await _db.MediaAssets.AsNoTracking()
            .FirstAsync(m => m.Id != null)).StoragePath;
        _storage.AddBlob(key, [0x01, 0x02, 0x03]);

        var scopeFactory = new SingleInstanceScopeFactory(
            _db, _storage, Options.Create(_options));
        var worker = new SpeakingAudioRetentionWorker(
            scopeFactory, NullLogger<SpeakingAudioRetentionWorker>.Instance);

        var archived = await worker.SweepSpeakingRecordingsOnceAsync(CancellationToken.None);
        Assert.True(archived >= 1);

        var reloaded = await _db.SpeakingRecordings.AsNoTracking()
            .FirstAsync(r => r.Id == recordingId);
        Assert.True(reloaded.IsArchived);
        Assert.False(await _storage.ExistsAsync(key, CancellationToken.None));

        var audit = await _db.AuditEvents.AsNoTracking()
            .Where(a => a.Action == "SpeakingRecordingExpiredByRetention" && a.ResourceId == recordingId)
            .CountAsync();
        Assert.True(audit >= 1);
    }

    [Fact]
    public async Task LearnerCanDeleteOwnRecording_AndCannotDeleteOthers()
    {
        const string ownerId = "learner-owner-2";
        const string strangerId = "learner-stranger-2";
        var (_, recordingId) = await SeedSessionWithRecordingAsync(ownerId);

        // Cross-user delete → 403.
        var forbidden = await Assert.ThrowsAsync<ApiException>(() =>
            _svc.DeleteRecordingAsync(strangerId, recordingId, CancellationToken.None));
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal("speaking_recording_forbidden", forbidden.ErrorCode);

        // The recording must still be live and un-archived.
        var stillThere = await _db.SpeakingRecordings.AsNoTracking()
            .FirstAsync(r => r.Id == recordingId);
        Assert.False(stillThere.IsArchived);

        // Owner can delete it.
        var ok = await _svc.DeleteRecordingAsync(ownerId, recordingId, CancellationToken.None);
        Assert.Equal(recordingId, ok.RecordingId);

        var archived = await _db.SpeakingRecordings.AsNoTracking()
            .FirstAsync(r => r.Id == recordingId);
        Assert.True(archived.IsArchived);

        var audit = await _db.AuditEvents.AsNoTracking()
            .Where(a => a.Action == "SpeakingRecordingDeleted" && a.ResourceId == recordingId)
            .CountAsync();
        Assert.Equal(1, audit);
    }

    [Fact]
    public async Task ConsentVersioning_StoresRevocations()
    {
        const string userId = "learner-consents-1";

        var consent = await _svc.RecordConsentAsync(
            userId,
            new RecordConsentRequest(SpeakingComplianceConsentTypes.Recording, null),
            "203.0.113.5",
            "Mozilla/5.0 jest",
            CancellationToken.None);

        Assert.Equal("recording", consent.ConsentType);
        Assert.Equal("recording.v1", consent.ConsentVersion);
        Assert.Null(consent.RevokedAt);

        var revoked = await _svc.RevokeConsentAsync(
            userId, SpeakingComplianceConsentTypes.Recording, CancellationToken.None);
        Assert.Equal(1, revoked);

        var history = await _svc.GetConsentHistoryAsync(userId, CancellationToken.None);
        var row = Assert.Single(history.Consents);
        Assert.NotNull(row.RevokedAt);
    }

    [Fact]
    public void TutorReviewedRecording_GetsExtendedRetention()
    {
        // The compliance options expose the dual retention window the
        // worker honours when refreshing RetentionExpiresAt.
        var aiOnly = _svc.DefaultRetentionFor(tutorReviewed: false);
        var tutored = _svc.DefaultRetentionFor(tutorReviewed: true);
        Assert.Equal(TimeSpan.FromDays(_options.RetentionDaysDefault), aiOnly);
        Assert.Equal(TimeSpan.FromDays(_options.RetentionDaysWhenTutorReviewed), tutored);
        Assert.True(tutored > aiOnly);
        // Plan G.7: 90/365 split.
        Assert.Equal(90, _options.RetentionDaysDefault);
        Assert.Equal(365, _options.RetentionDaysWhenTutorReviewed);
    }

    // ── Fixture helpers ──────────────────────────────────────────────────

    private async Task<(string sessionId, string recordingId)> SeedSessionWithRecordingAsync(
        string userId,
        DateTimeOffset? retentionExpiresAt = null)
    {
        var contentItemId = $"ci-{Guid.NewGuid():N}";
        _db.ContentItems.Add(new ContentItem
        {
            Id = contentItemId,
            ContentType = "speaking_role_play",
            ProfessionId = "nursing",
            SubtestCode = "speaking",
            Title = "Test card",
            Difficulty = "core",
            Status = ContentStatus.Published,
            PublishedRevisionId = $"rev-{Guid.NewGuid():N}",
        });

        var cardId = $"card-{Guid.NewGuid():N}";
        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = contentItemId,
            ScenarioTitle = "Test scenario",
            Setting = "Test setting",
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
            Sha256 = new string('a', 64),
            MimeType = "audio/webm",
            ConsentVersion = "recording.v1",
            IsArchived = false,
            RetentionExpiresAt = retentionExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync();
        return (sessionId, recordingId);
    }
}
