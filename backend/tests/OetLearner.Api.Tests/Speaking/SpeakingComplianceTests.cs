using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

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
            CurrentConsentVersion = "recording.v4",
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
    public async Task RetentionWorker_KeepsACalibrationRecordingForItsYear_ThenPurgesIt()
    {
        // Promotion into the calibration set extends RetentionExpiresAt to now + 365 days (owner decision 2026-10-05).
        const string ownerId = "learner-retention-calibration";
        var (_, recordingId) = await SeedSessionWithRecordingAsync(
            ownerId,
            retentionExpiresAt: DateTimeOffset.UtcNow + SpeakingGraderCalibrationService.CalibrationAudioRetention);
        var key = (await _db.MediaAssets.AsNoTracking().FirstAsync(m => m.Id != null)).StoragePath;
        _storage.AddBlob(key, [0x01, 0x02, 0x03]);
        var worker = new SpeakingAudioRetentionWorker(
            new SingleInstanceScopeFactory(_db, _storage, Options.Create(_options)),
            NullLogger<SpeakingAudioRetentionWorker>.Instance);

        // Well past the normal 90-day window the audio is still there...
        await worker.SweepSpeakingRecordingsOnceAsync(CancellationToken.None);
        var kept = await _db.SpeakingRecordings.AsNoTracking().FirstAsync(r => r.Id == recordingId);
        Assert.False(kept.IsArchived);
        Assert.True(await _storage.ExistsAsync(key, CancellationToken.None));

        // ...and once its 365 days have elapsed the sweep purges it.
        var tracked = await _db.SpeakingRecordings.FirstAsync(r => r.Id == recordingId);
        tracked.RetentionExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();
        await worker.SweepSpeakingRecordingsOnceAsync(CancellationToken.None);
        Assert.True((await _db.SpeakingRecordings.AsNoTracking().FirstAsync(r => r.Id == recordingId)).IsArchived);
        Assert.False(await _storage.ExistsAsync(key, CancellationToken.None));
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
    public async Task RetentionWorker_ForgetsTheRemoteJoinDerivativeOfEveryClipItArchives()
    {
        // A helper-prepared join of the candidate's clips (media.speaking-join) is learner audio: it must go no later than the clips.
        var (sessionId, recordingId) = await SeedSessionWithRecordingAsync(
            "learner-retention-join",
            retentionExpiresAt: DateTimeOffset.UtcNow.AddDays(-2));
        var key = (await _db.MediaAssets.AsNoTracking().FirstAsync(m => m.Id != null)).StoragePath;
        _storage.AddBlob(key, [0x01, 0x02, 0x03]);
        var remote = new RecordingRemoteSpeakingJoin();
        var worker = new SpeakingAudioRetentionWorker(
            new SingleInstanceScopeFactory(_db, _storage, Options.Create(_options), remote),
            NullLogger<SpeakingAudioRetentionWorker>.Instance);

        var archived = await worker.SweepSpeakingRecordingsOnceAsync(CancellationToken.None);

        Assert.True(archived >= 1);
        Assert.True((await _db.SpeakingRecordings.AsNoTracking().FirstAsync(r => r.Id == recordingId)).IsArchived);
        var deleted = Assert.Single(remote.Deleted);
        Assert.Contains(sessionId, deleted);
    }

    [Fact]
    public async Task RetentionWorker_WithNothingToArchive_NeverTouchesTheRemoteJoin()
    {
        await SeedSessionWithRecordingAsync("learner-retention-fresh", retentionExpiresAt: DateTimeOffset.UtcNow.AddDays(30));
        var remote = new RecordingRemoteSpeakingJoin();
        var worker = new SpeakingAudioRetentionWorker(
            new SingleInstanceScopeFactory(_db, _storage, Options.Create(_options), remote),
            NullLogger<SpeakingAudioRetentionWorker>.Instance);

        Assert.Equal(0, await worker.SweepSpeakingRecordingsOnceAsync(CancellationToken.None));

        Assert.Empty(remote.Deleted);
    }

    [Fact]
    public async Task LearnerErasureOfARecording_ForgetsTheRemoteJoinDerivativeOfItsSession()
    {
        const string ownerId = "learner-owner-join";
        var (sessionId, recordingId) = await SeedSessionWithRecordingAsync(ownerId);
        var remote = new RecordingRemoteSpeakingJoin();
        var service = new SpeakingComplianceService(
            _db,
            _storage,
            Options.Create(_options),
            NullLogger<SpeakingComplianceService>.Instance,
            TimeProvider.System,
            remote);

        // a refused erasure (not the owner) touches nothing
        await Assert.ThrowsAsync<ApiException>(() => service.DeleteRecordingAsync("learner-stranger-join", recordingId, CancellationToken.None));
        Assert.Empty(remote.Deleted);

        await service.DeleteRecordingAsync(ownerId, recordingId, CancellationToken.None);

        Assert.Equal(new[] { sessionId }, Assert.Single(remote.Deleted));
    }

    [Fact]
    public async Task MyRecordings_IdentifiesConsentedLiveCandidateClips()
    {
        const string ownerId = "learner-live-clip-list";
        var (_, recordingId) = await SeedSessionWithRecordingAsync(
            ownerId, source: SpeakingRecordingSource.ConversationHub);

        var response = await _svc.GetMyRecordingsAsync(ownerId, CancellationToken.None);

        var recording = Assert.Single(response.Recordings);
        Assert.Equal(recordingId, recording.RecordingId);
        Assert.Equal(SpeakingRecordingSource.ConversationHub.ToString(), recording.Source);
    }

    [Theory]
    [InlineData("recording.v1")]
    [InlineData("recording.v2")]
    [InlineData("  ")]
    [InlineData(null)]
    public void AConfiguredConsentVersionOlderThanTheCalibrationWording_IsReadAsV3(string? configured)
    {
        // A stale runtime setting must not keep serving wording that does not cover calibration retention.
        _options.CurrentConsentVersion = configured!;

        Assert.Equal("recording.v3", _svc.ResolveCurrentConsentVersion(SpeakingComplianceConsentTypes.Recording));
        Assert.Equal("recording.v3", SpeakingConsentVersions.Effective(configured));
        // A later version is left alone.
        Assert.Equal("recording.v4", SpeakingConsentVersions.Effective("recording.v4"));
    }

    [Fact]
    public void TheDefaultConsentWording_TellsTheLearnerAboutCalibrationRetention_AndTheDefaultVersionIsV3()
    {
        var defaults = new SpeakingComplianceOptions();

        Assert.Equal("recording.v3", defaults.CurrentConsentVersion);
        Assert.Contains("quality assurance and calibration", defaults.ConsentText);
        Assert.Contains("365 days", defaults.ConsentText);
        Assert.Contains("delete a recording at any time", defaults.ConsentText);
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
        Assert.Equal("recording.v4", consent.ConsentVersion);
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
        DateTimeOffset? retentionExpiresAt = null,
        SpeakingRecordingSource source = SpeakingRecordingSource.ClientMediaRecorder)
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
            Source = source,
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
