using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

public sealed class SpeakingSimulationV11AudioCaptureServiceTests
{
    [Fact]
    public async Task CaptureTurnAsync_rejects_stale_consent_versions()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-v11-audio-{Guid.NewGuid():N}")
            .Options;
        await using var db = new LearnerDbContext(options);
        const string userId = "learner-v11-consent";

        foreach (var consentType in new[]
                 {
                     SpeakingComplianceConsentTypes.Recording,
                     SpeakingComplianceConsentTypes.AiProcessing,
                     SpeakingComplianceConsentTypes.Retention,
                 })
        {
            db.SpeakingComplianceConsents.Add(new SpeakingComplianceConsent
            {
                Id = $"consent-{consentType}",
                UserId = userId,
                ConsentType = consentType,
                ConsentVersion = "recording.v1",
                AcceptedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync();
        var compliance = new SpeakingComplianceOptions
        {
            CurrentConsentVersion = "recording.v2",
            CurrentLiveVideoConsentVersion = "live_video_with_tutor.v2",
        };
        var service = new SpeakingSimulationV11AudioCaptureService(
            db,
            new StubFileStorage(),
            new StaticConversationOptionsProvider(),
            Options.Create(compliance),
            NullLogger<SpeakingSimulationV11AudioCaptureService>.Instance);
        var session = new SpeakingSession
        {
            Id = "session-v11-consent",
            UserId = userId,
            RolePlayCardId = "card-v11-consent",
            Mode = SpeakingSessionMode.AiSelfPractice,
            ConsentVersion = "recording.v1",
        };

        var error = await Assert.ThrowsAsync<ApiException>(() => service.CaptureTurnAsync(
            session,
            [0x01, 0x02],
            "audio/webm",
            isWarmup: false,
            durationMs: 1000,
            CancellationToken.None));

        Assert.Equal("SPEAKING_CONSENT_REQUIRED", error.ErrorCode);
        Assert.Empty(await db.SpeakingRecordings.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CaptureTurnAsync_defaults_private_v11_replay_retention_to_30_days()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-v11-retention-{Guid.NewGuid():N}")
            .Options;
        await using var db = new LearnerDbContext(options);
        const string userId = "learner-v11-retention";
        var compliance = new SpeakingComplianceOptions
        {
            RetentionDaysDefault = 90,
            CurrentConsentVersion = "recording.v1",
        };
        foreach (var consentType in new[]
                 {
                     SpeakingComplianceConsentTypes.Recording,
                     SpeakingComplianceConsentTypes.AiProcessing,
                     SpeakingComplianceConsentTypes.Retention,
                 })
        {
            db.SpeakingComplianceConsents.Add(new SpeakingComplianceConsent
            {
                Id = $"retention-consent-{consentType}",
                UserId = userId,
                ConsentType = consentType,
                ConsentVersion = compliance.CurrentConsentVersion,
                AcceptedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync();
        var service = new SpeakingSimulationV11AudioCaptureService(
            db,
            new StubFileStorage(),
            new StaticConversationOptionsProvider(),
            Options.Create(compliance),
            NullLogger<SpeakingSimulationV11AudioCaptureService>.Instance);
        var session = new SpeakingSession
        {
            Id = "session-v11-retention",
            UserId = userId,
            RolePlayCardId = "card-v11-retention",
            Mode = SpeakingSessionMode.AiSelfPractice,
            ConsentVersion = compliance.CurrentConsentVersion,
        };
        var before = DateTimeOffset.UtcNow;

        var result = await service.CaptureTurnAsync(
            session,
            [0x01, 0x02],
            "audio/webm",
            isWarmup: false,
            durationMs: 1000,
            CancellationToken.None);

        var recording = await db.SpeakingRecordings.AsNoTracking()
            .SingleAsync(x => x.Id == result.RecordingId);
        Assert.NotNull(recording.RetentionExpiresAt);
        Assert.InRange(
            recording.RetentionExpiresAt!.Value,
            before.AddDays(30),
            DateTimeOffset.UtcNow.AddDays(30).AddSeconds(1));
    }

    [Fact]
    public async Task CaptureTurnAsync_preserves_live_tutor_retention_default()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-v11-tutor-retention-{Guid.NewGuid():N}")
            .Options;
        await using var db = new LearnerDbContext(options);
        const string userId = "learner-v11-tutor-retention";
        var compliance = new SpeakingComplianceOptions
        {
            RetentionDaysDefault = 90,
            RetentionDaysWhenTutorReviewed = 365,
            CurrentConsentVersion = "recording.v1",
            CurrentLiveVideoConsentVersion = "live_video_with_tutor.v1",
        };
        foreach (var consentType in new[]
                 {
                     SpeakingComplianceConsentTypes.Recording,
                     SpeakingComplianceConsentTypes.TutorReview,
                     SpeakingComplianceConsentTypes.Retention,
                 })
        {
            db.SpeakingComplianceConsents.Add(new SpeakingComplianceConsent
            {
                Id = $"tutor-consent-{consentType}",
                UserId = userId,
                ConsentType = consentType,
                ConsentVersion = compliance.CurrentConsentVersion,
                AcceptedAt = DateTimeOffset.UtcNow,
            });
        }
        db.SpeakingComplianceConsents.Add(new SpeakingComplianceConsent
        {
            Id = "tutor-consent-live-video",
            UserId = userId,
            ConsentType = SpeakingComplianceConsentTypes.LiveVideoWithTutor,
            ConsentVersion = compliance.CurrentLiveVideoConsentVersion,
            AcceptedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var service = new SpeakingSimulationV11AudioCaptureService(
            db,
            new StubFileStorage(),
            new StaticConversationOptionsProvider(),
            Options.Create(compliance),
            NullLogger<SpeakingSimulationV11AudioCaptureService>.Instance);
        var session = new SpeakingSession
        {
            Id = "session-v11-tutor-retention",
            UserId = userId,
            RolePlayCardId = "card-v11-tutor-retention",
            Mode = SpeakingSessionMode.LiveTutor,
            ConsentVersion = compliance.CurrentConsentVersion,
        };
        var before = DateTimeOffset.UtcNow;

        var result = await service.CaptureTurnAsync(
            session,
            [0x01, 0x02],
            "audio/webm",
            isWarmup: false,
            durationMs: 1000,
            CancellationToken.None);

        var recording = await db.SpeakingRecordings.AsNoTracking()
            .SingleAsync(x => x.Id == result.RecordingId);
        Assert.NotNull(recording.RetentionExpiresAt);
        Assert.InRange(
            recording.RetentionExpiresAt!.Value,
            before.AddDays(365),
            DateTimeOffset.UtcNow.AddDays(365).AddSeconds(1));
    }

    private sealed class StaticConversationOptionsProvider : IConversationOptionsProvider
    {
        private readonly ConversationOptions _options = new();

        public ConversationOptions Current => _options;
        public Task<ConversationOptions> GetAsync(CancellationToken ct = default)
            => Task.FromResult(_options);
        public void Invalidate() { }
    }
}
