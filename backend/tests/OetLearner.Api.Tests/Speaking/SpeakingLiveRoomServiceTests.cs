using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

public sealed class SpeakingLiveRoomServiceTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;
    private RecordingLiveKitGateway _gateway = default!;
    private SpeakingLiveRoomService _svc = default!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-live-room-{Guid.NewGuid():N}")
            .Options;
        _db = new LearnerDbContext(options);
        _gateway = new RecordingLiveKitGateway();
        _svc = new SpeakingLiveRoomService(
            _db,
            _gateway,
            Options.Create(new LiveKitOptions { WssUrl = "wss://livekit.test", EgressBucket = "s3://oet-test" }),
            Options.Create(new SpeakingComplianceOptions()),
            NullLogger<SpeakingLiveRoomService>.Instance);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreateRoom_RejectsNonLiveTutorSession_BeforeProviderCall()
    {
        _db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = "speaking-session-ai",
            UserId = "learner-ai",
            RolePlayCardId = "card-ai",
            Mode = SpeakingSessionMode.AiSelfPractice,
            State = SpeakingSessionState.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<SpeakingLiveRoomInvalidStateException>(() => _svc.CreateRoomForSessionAsync(
            "learner-ai",
            "speaking-session-ai",
            CancellationToken.None));

        Assert.Equal(0, _gateway.CreateRoomCalls);
    }

    [Fact]
    public async Task CreateRoom_RejectsTerminalLiveTutorSession_BeforeProviderCall()
    {
        _db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = "speaking-session-finished",
            UserId = "learner-finished",
            RolePlayCardId = "card-finished",
            Mode = SpeakingSessionMode.LiveTutor,
            State = SpeakingSessionState.Finished,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow,
            EndedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<SpeakingLiveRoomInvalidStateException>(() => _svc.CreateRoomForSessionAsync(
            "learner-finished",
            "speaking-session-finished",
            CancellationToken.None));

        Assert.Equal(0, _gateway.CreateRoomCalls);
    }

    [Fact]
    public async Task StartRecording_RejectsEndedRoom_BeforeProviderCall()
    {
        _db.SpeakingLiveRooms.Add(new SpeakingLiveRoom
        {
            Id = "lvrm-ended",
            SpeakingSessionId = "speaking-session-ended",
            Provider = "livekit",
            RoomName = "oet-speaking-ended",
            LearnerIdentity = "learner:learner-1",
            TutorIdentity = "tutor:tutor-1",
            ScheduledStartUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            ActualStartUtc = DateTimeOffset.UtcNow.AddMinutes(-9),
            ActualEndUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            State = SpeakingLiveRoomState.Ended,
            RecordingConsentVersion = "test-v1",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<SpeakingLiveRoomInvalidStateException>(() => _svc.StartRecordingAsync(
            "lvrm-ended",
            CancellationToken.None));

        Assert.Equal(0, _gateway.StartEgressCalls);
    }

    [Fact]
    public async Task StartRecording_RequiresCurrentLearnerConsent_BeforeProviderCall()
    {
        _db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = "speaking-session-active",
            UserId = "learner-consent",
            RolePlayCardId = "card-consent",
            Mode = SpeakingSessionMode.LiveTutor,
            State = SpeakingSessionState.Active,
            InterlocutorActorId = "tutor-1",
            ConsentVersion = "recording.v1",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        _db.SpeakingLiveRooms.Add(new SpeakingLiveRoom
        {
            Id = "lvrm-consent",
            SpeakingSessionId = "speaking-session-active",
            Provider = "livekit",
            RoomName = "oet-speaking-consent",
            LearnerIdentity = "learner:learner-consent",
            TutorIdentity = "tutor:tutor-1",
            ScheduledStartUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            ActualStartUtc = DateTimeOffset.UtcNow.AddMinutes(-9),
            State = SpeakingLiveRoomState.Active,
            RecordingEnabled = true,
            RecordingConsentVersion = "recording.v1",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<SpeakingLiveRoomInvalidStateException>(() => _svc.StartRecordingAsync(
            "lvrm-consent",
            CancellationToken.None));

        Assert.Equal(0, _gateway.StartEgressCalls);
    }

    [Fact]
    public async Task StartRecording_WithCurrentLearnerConsent_CallsProviderOnce()
    {
        _db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = "speaking-session-consented",
            UserId = "learner-consented",
            RolePlayCardId = "card-consented",
            Mode = SpeakingSessionMode.LiveTutor,
            State = SpeakingSessionState.Active,
            InterlocutorActorId = "tutor-1",
            ConsentVersion = "recording.v1",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        _db.SpeakingLiveRooms.Add(new SpeakingLiveRoom
        {
            Id = "lvrm-consented",
            SpeakingSessionId = "speaking-session-consented",
            Provider = "livekit",
            RoomName = "oet-speaking-consented",
            LearnerIdentity = "learner:learner-consented",
            TutorIdentity = "tutor:tutor-1",
            ScheduledStartUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            ActualStartUtc = DateTimeOffset.UtcNow.AddMinutes(-9),
            State = SpeakingLiveRoomState.Active,
            RecordingEnabled = true,
            RecordingConsentVersion = "recording.v1",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        _db.SpeakingComplianceConsents.AddRange(
            new SpeakingComplianceConsent
            {
                Id = "consent-recording",
                UserId = "learner-consented",
                ConsentType = SpeakingComplianceConsentTypes.Recording,
                ConsentVersion = "recording.v1",
                AcceptedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            },
            new SpeakingComplianceConsent
            {
                Id = "consent-live-video",
                UserId = "learner-consented",
                ConsentType = SpeakingComplianceConsentTypes.LiveVideoWithTutor,
                ConsentVersion = "live_video_with_tutor.v1",
                AcceptedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            });
        await _db.SaveChangesAsync();

        var result = await _svc.StartRecordingAsync("lvrm-consented", CancellationToken.None);

        Assert.Equal("egress-test", result.EgressId);
        Assert.Equal(1, _gateway.StartEgressCalls);
    }

    // ── B9: audio-only rooms ─────────────────────────────────────────

    [Fact]
    public async Task StartRecording_AudioOnly_NeedsRecordingConsentOnly_AndWritesOgg()
    {
        SeedActiveRoom("audio", bookingId: null);
        AddConsents("learner-audio", SpeakingComplianceConsentTypes.Recording);
        await _db.SaveChangesAsync();

        var result = await _svc.StartRecordingAsync("lvrm-audio", CancellationToken.None);

        Assert.Equal(1, _gateway.StartEgressCalls);
        Assert.Equal("s3://oet-test/oet-speaking/oet-speaking-audio.ogg", result.OutputUrl);
        Assert.Equal(result.OutputUrl, _gateway.LastEgressOutputUrl);
    }

    [Fact]
    public async Task IssueToken_JoinsWithoutLiveVideoConsent_AndGrantsNoVideo()
    {
        SeedActiveRoom("join", bookingId: null);
        AddConsents(
            "learner-join",
            SpeakingComplianceConsentTypes.Recording,
            SpeakingComplianceConsentTypes.TutorReview,
            SpeakingComplianceConsentTypes.Retention);
        await _db.SaveChangesAsync();

        var result = await _svc.IssueTokenAsync("learner-join", "lvrm-join", "learner", CancellationToken.None);

        Assert.Equal("token-test", result.Token);
        Assert.True(result.Capabilities.CanPublishAudio);
        Assert.False(result.Capabilities.CanPublishVideo);
    }

    [Fact]
    public async Task IssueToken_StillRequiresTutorReviewAndRetentionConsent()
    {
        SeedActiveRoom("partial", bookingId: null);
        AddConsents("learner-partial", SpeakingComplianceConsentTypes.Recording);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<SpeakingLiveRoomInvalidStateException>(() =>
            _svc.IssueTokenAsync("learner-partial", "lvrm-partial", "learner", CancellationToken.None));
    }

    // ── B9: recording → review hand-off ──────────────────────────────

    [Fact]
    public async Task EgressEnded_RoutesToReview_NotifiesBoth_Audits_AndIsIdempotent()
    {
        var notifications = new NotificationService(
            _db,
            emailSender: null!,
            webPushDispatcher: null!,
            mobilePushDispatcher: null!,
            hubContext: null!,
            platformLinks: null!,
            timeProvider: TimeProvider.System,
            webPushOptions: Options.Create(new WebPushOptions()),
            runtimeSettingsProvider: TestRuntimeSettingsProvider.FromZoomOptions(new ZoomOptions()),
            notificationProofOptions: Options.Create(new NotificationProofHarnessOptions()),
            environment: null!,
            logger: NullLogger<NotificationService>.Instance);
        var svc = new SpeakingLiveRoomService(
            _db,
            _gateway,
            Options.Create(new LiveKitOptions { WssUrl = "wss://livekit.test", EgressBucket = "s3://oet-test" }),
            Options.Create(new SpeakingComplianceOptions()),
            NullLogger<SpeakingLiveRoomService>.Instance,
            notifications);

        var now = DateTimeOffset.UtcNow;
        SeedActiveRoom("review", bookingId: "psb-review");
        var room = _db.SpeakingLiveRooms.Local.Single(r => r.Id == "lvrm-review");
        room.State = SpeakingLiveRoomState.Ended;
        room.EgressId = "egress-review";
        var session = _db.SpeakingSessions.Local.Single(s => s.Id == "speaking-session-review");
        session.State = SpeakingSessionState.Finished;
        session.EndedAt = now.AddMinutes(-1);
        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = "card-review",
            ContentItemId = "ci-review",
            ProfessionId = "medicine",
            ScenarioTitle = "Review card",
            Setting = "Ward",
            CandidateRole = "Doctor",
            InterlocutorRole = "Patient",
            Background = "Background.",
            PatientEmotion = "calm",
            CommunicationGoal = "Explain",
            ClinicalTopic = "topic",
            Difficulty = "core",
            CriteriaFocusJson = "[]",
            Disclaimer = "Practice.",
            Status = ContentStatus.Published,
            CreatedAt = now,
            UpdatedAt = now,
        });
        _db.Users.Add(new LearnerUser
        {
            Id = "learner-review",
            AuthAccountId = "auth-learner-review",
            DisplayName = "Learner Review",
            Email = "learner-review@example.test",
            CreatedAt = now,
            LastActiveAt = now,
        });
        _db.ExpertUsers.Add(new ExpertUser
        {
            Id = "tutor-1",
            AuthAccountId = "auth-tutor-1",
            DisplayName = "Tutor One",
            Email = "tutor-1@example.test",
            CreatedAt = now,
        });
        await _db.SaveChangesAsync();

        const string payload = """
            {"event":"egress_ended","egressInfo":{"egressId":"egress-review","roomName":"oet-speaking-review",
             "fileResults":[{"location":"s3://oet-test/oet-speaking/oet-speaking-review.ogg","duration":60000000000,"size":2048}]}}
            """;
        await svc.HandleWebhookAsync("egress_ended", payload, CancellationToken.None);
        await svc.HandleWebhookAsync("egress_ended", payload, CancellationToken.None);

        var recording = await _db.SpeakingRecordings.Include(r => r.MediaAsset).SingleAsync();
        Assert.Equal("audio/ogg", recording.MimeType);
        Assert.Equal("audio", recording.MediaAsset!.MediaKind);
        Assert.Equal(60, recording.DurationSeconds);

        Assert.Equal(1, await _db.PrivateSpeakingAuditLogs.CountAsync(a =>
            a.BookingId == "psb-review" && a.Action == "livekit_recording_received"));
        Assert.Equal(1, await _db.NotificationEvents.CountAsync(e =>
            e.EventKey == nameof(NotificationEventKey.LearnerPrivateSpeakingRecordingReceived)
            && e.RecipientAuthAccountId == "auth-learner-review"));
        Assert.Equal(1, await _db.NotificationEvents.CountAsync(e =>
            e.EventKey == nameof(NotificationEventKey.ExpertPrivateSpeakingRecordingReady)
            && e.RecipientAuthAccountId == "auth-tutor-1"));

        var queue = await new TutorReviewQueueService(
                _db, NullLogger<TutorReviewQueueService>.Instance, TimeProvider.System)
            .ListQueueAsync("tutor-1", null, CancellationToken.None);
        Assert.Contains(queue, item => item.SessionId == "speaking-session-review");
    }

    private void SeedActiveRoom(string key, string? bookingId)
    {
        _db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = $"speaking-session-{key}",
            UserId = $"learner-{key}",
            RolePlayCardId = $"card-{key}",
            Mode = SpeakingSessionMode.LiveTutor,
            State = SpeakingSessionState.Active,
            InterlocutorActorId = "tutor-1",
            ConsentVersion = "recording.v1",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        _db.SpeakingLiveRooms.Add(new SpeakingLiveRoom
        {
            Id = $"lvrm-{key}",
            SpeakingSessionId = $"speaking-session-{key}",
            BookingId = bookingId,
            Provider = "livekit",
            RoomName = $"oet-speaking-{key}",
            LearnerIdentity = $"learner:learner-{key}",
            TutorIdentity = "tutor:tutor-1",
            ScheduledStartUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            ActualStartUtc = DateTimeOffset.UtcNow.AddMinutes(-9),
            State = SpeakingLiveRoomState.Active,
            RecordingEnabled = true,
            RecordingConsentVersion = "recording.v1",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
    }

    private void AddConsents(string userId, params string[] consentTypes)
    {
        foreach (var consentType in consentTypes)
        {
            _db.SpeakingComplianceConsents.Add(new SpeakingComplianceConsent
            {
                Id = $"consent-{userId}-{consentType}",
                UserId = userId,
                ConsentType = consentType,
                ConsentVersion = "recording.v1",
                AcceptedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            });
        }
    }

    private sealed class RecordingLiveKitGateway : ILiveKitGateway
    {
        public int CreateRoomCalls { get; private set; }
        public int StartEgressCalls { get; private set; }
        public string? LastEgressOutputUrl { get; private set; }

        public Task<LiveKitRoomCreationResult> CreateRoomAsync(string roomName, int maxDurationSeconds, CancellationToken ct)
        {
            CreateRoomCalls++;
            return Task.FromResult(new LiveKitRoomCreationResult("room-sid-test", "wss://livekit.test"));
        }

        public Task<string> MintAccessTokenAsync(
            string roomName,
            string identity,
            LiveKitTokenCapabilities caps,
            TimeSpan ttl,
            CancellationToken ct)
            => Task.FromResult("token-test");

        public Task<string> StartEgressAsync(string roomName, string outputUrl, CancellationToken ct)
        {
            StartEgressCalls++;
            LastEgressOutputUrl = outputUrl;
            return Task.FromResult("egress-test");
        }

        public Task<bool> StopEgressAsync(string egressId, CancellationToken ct)
            => Task.FromResult(true);

        public Task DeleteRoomAsync(string roomName, CancellationToken ct)
            => Task.CompletedTask;

        public bool VerifyWebhookSignature(string payload, string signature) => true;
    }
}
