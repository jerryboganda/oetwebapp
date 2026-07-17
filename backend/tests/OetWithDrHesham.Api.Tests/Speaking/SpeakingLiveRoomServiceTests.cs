using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Speaking;

namespace OetWithDrHesham.Api.Tests.Speaking;

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

    private sealed class RecordingLiveKitGateway : ILiveKitGateway
    {
        public int CreateRoomCalls { get; private set; }
        public int StartEgressCalls { get; private set; }

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
            return Task.FromResult("egress-test");
        }

        public Task<bool> StopEgressAsync(string egressId, CancellationToken ct)
            => Task.FromResult(true);

        public bool VerifyWebhookSignature(string payload, string signature) => true;
    }
}