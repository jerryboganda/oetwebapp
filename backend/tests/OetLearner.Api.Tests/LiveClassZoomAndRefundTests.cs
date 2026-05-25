using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.LiveClasses;

namespace OetLearner.Api.Tests;

public sealed class LiveClassZoomAndRefundTests
{
    [Theory]
    [InlineData(10, 25, 10)]
    [InlineData(10, 12, 5)]
    [InlineData(9, 12, 4)]
    [InlineData(10, 0.9, 0)]
    [InlineData(0, 48, 0)]
    public void RefundPolicyMatchesAcceptedClassCancellationRules(int charged, double hoursUntilStart, int expectedRefund)
    {
        var method = typeof(LiveClassService).GetMethod("CalculateRefundCredits", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Refund helper was not found.");

        var refund = (int)method.Invoke(null, [charged, hoursUntilStart])!;

        Assert.Equal(expectedRefund, refund);
    }

    [Fact]
    public void VerifyWebhookSignatureAcceptsZoomHmacSignature()
    {
        var service = CreateZoomService(new ZoomOptions { WebhookSecretToken = "zoom-secret" });
        var rawBody = "{\"event\":\"meeting.started\"}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = BuildZoomWebhookSignature("zoom-secret", timestamp, rawBody);
        var headers = new HeaderDictionary
        {
            ["x-zm-request-timestamp"] = timestamp,
            ["x-zm-signature"] = signature,
        };

        Assert.True(service.VerifyWebhookSignature(rawBody, headers));
        Assert.False(service.VerifyWebhookSignature(rawBody + " ", headers));
    }

    [Fact]
    public void WebhookUrlValidationResponseUsesZoomEncryptedTokenContract()
    {
        var service = CreateZoomService(new ZoomOptions { WebhookSecretToken = "zoom-secret" });
        var response = service.TryBuildWebhookUrlValidationResponse(
            "{\"event\":\"endpoint.url_validation\",\"payload\":{\"plainToken\":\"plain-token\"}}");

        Assert.NotNull(response);
        var plainToken = response.GetType().GetProperty("plainToken")?.GetValue(response);
        var encryptedToken = response.GetType().GetProperty("encryptedToken")?.GetValue(response);

        Assert.Equal("plain-token", plainToken);
        Assert.Equal(BuildPlainTokenHash("zoom-secret", "plain-token"), encryptedToken);
    }

    [Fact]
    public void VerifyWebhookSignatureRequiresConfiguredWebhookSecret()
    {
        var service = CreateZoomService(new ZoomOptions { AllowSandboxFallback = true });

        Assert.False(service.VerifyWebhookSignature("{\"event\":\"meeting.started\"}", new HeaderDictionary()));
    }

    [Fact]
    public async Task FailedZoomWebhookReceiptIsReprocessedOnRetry()
    {
        await using var db = CreateDb();
        var rawBody = "{\"event\":\"meeting.started\",\"payload\":{\"object\":{\"id\":123456789}}}";
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        db.LiveClassWebhookEvents.Add(new LiveClassWebhookEvent
        {
            Id = "webhook-1",
            EventType = "meeting.started",
            PayloadHash = payloadHash,
            RawPayload = "{}",
            Status = LiveClassWebhookStatus.Failed,
            ErrorMessage = "previous failure",
            ReceivedAt = now.AddMinutes(-5),
        });
        await db.SaveChangesAsync();
        var service = CreateLiveClassService(db, now, new ZoomOptions { WebhookSecretToken = "zoom-secret" });
        var timestamp = now.ToUnixTimeSeconds().ToString();
        var headers = new HeaderDictionary
        {
            ["x-zm-request-timestamp"] = timestamp,
            ["x-zm-signature"] = BuildZoomWebhookSignature("zoom-secret", timestamp, rawBody),
        };

        await service.HandleZoomWebhookAsync(rawBody, headers, CancellationToken.None);

        var receipt = await db.LiveClassWebhookEvents.SingleAsync(item => item.PayloadHash == payloadHash);
        Assert.Equal(LiveClassWebhookStatus.Processed, receipt.Status);
        Assert.Null(receipt.ErrorMessage);
    }

    [Fact]
    public void MeetingSdkSignatureRequiresSdkCredentials()
    {
        var missingCredentials = CreateZoomService(new ZoomOptions());
        var configured = CreateZoomService(new ZoomOptions
        {
            MeetingSdkKey = "sdk-key",
            MeetingSdkSecret = "sdk-secret",
        });

        Assert.Null(missingCredentials.GenerateMeetingSdkSignature("123456789", role: 0, DateTimeOffset.UtcNow.AddMinutes(30)));

        var signature = configured.GenerateMeetingSdkSignature("123456789", role: 0, DateTimeOffset.UtcNow.AddMinutes(30));
        Assert.NotNull(signature);
        Assert.Equal(3, signature!.Split('.').Length);
    }

    [Fact]
    public async Task LearnerClassDetailHidesDraftClassesEvenForAuthenticatedLearners()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.LiveClasses.Add(CreateLiveClass("class-1", "draft-class", LiveClassStatus.Draft, now));
        await db.SaveChangesAsync();
        var service = CreateLiveClassService(db, now);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.GetClassAsync("draft-class", "learner-1", CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.Equal("live_class_not_found", exception.ErrorCode);
    }

    [Fact]
    public async Task LearnerJoinTokenIsRefusedBeforeJoinWindow()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var liveClass = CreateLiveClass("class-1", "published-class", LiveClassStatus.Published, now);
        var session = CreateSession("session-1", liveClass.Id, now.AddHours(2), now.AddHours(3));
        session.LiveClass = liveClass;
        liveClass.Sessions.Add(session);
        db.Users.Add(new LearnerUser
        {
            Id = "learner-1",
            DisplayName = "Learner One",
            Email = "learner@example.test",
            CreatedAt = now,
            LastActiveAt = now,
        });
        db.LiveClasses.Add(liveClass);
        db.LiveClassEnrollments.Add(new LiveClassEnrollment
        {
            Id = "enrollment-1",
            ClassSessionId = session.Id,
            UserId = "learner-1",
            EnrolledAt = now,
            IdempotencyKey = "join-window-test",
            Status = LiveClassEnrollmentStatus.Active,
        });
        await db.SaveChangesAsync();
        var service = CreateLiveClassService(db, now);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.CreateLearnerJoinTokenAsync(session.Id, "learner-1", CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Equal("live_class_join_window_closed", exception.ErrorCode);
    }

    [Fact]
    public async Task LearnerJoinTokenExpiryIsCappedToJoinWindowClose()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var liveClass = CreateLiveClass("class-1", "published-class", LiveClassStatus.Published, now);
        var session = CreateSession("session-1", liveClass.Id, now.AddMinutes(10), now.AddMinutes(20));
        session.LiveClass = liveClass;
        liveClass.Sessions.Add(session);
        db.Users.Add(new LearnerUser
        {
            Id = "learner-1",
            DisplayName = "Learner One",
            Email = "learner@example.test",
            CreatedAt = now,
            LastActiveAt = now,
        });
        db.LiveClasses.Add(liveClass);
        db.LiveClassEnrollments.Add(new LiveClassEnrollment
        {
            Id = "enrollment-1",
            ClassSessionId = session.Id,
            UserId = "learner-1",
            EnrolledAt = now,
            IdempotencyKey = "join-expiry-test",
            Status = LiveClassEnrollmentStatus.Active,
        });
        await db.SaveChangesAsync();
        var service = CreateLiveClassService(db, now);

        var token = await service.CreateLearnerJoinTokenAsync(session.Id, "learner-1", CancellationToken.None);

        Assert.Equal(session.ScheduledEndAt.AddMinutes(15), token.ExpiresAt);
    }

    [Fact]
    public async Task RefundedLearnersCannotAccessRecordings()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var liveClass = CreateLiveClass("class-1", "published-class", LiveClassStatus.Published, now);
        var session = CreateSession("session-1", liveClass.Id, now.AddHours(-2), now.AddHours(-1));
        session.LiveClass = liveClass;
        liveClass.Sessions.Add(session);
        db.LiveClasses.Add(liveClass);
        db.LiveClassEnrollments.Add(new LiveClassEnrollment
        {
            Id = "enrollment-1",
            ClassSessionId = session.Id,
            UserId = "learner-1",
            EnrolledAt = now.AddDays(-1),
            CancelledAt = now.AddHours(-3),
            IdempotencyKey = "recording-refund-test",
            Status = LiveClassEnrollmentStatus.Refunded,
        });
        db.LiveClassRecordings.Add(new LiveClassRecording
        {
            Id = "recording-1",
            ClassSessionId = session.Id,
            Status = LiveClassRecordingStatus.Ready,
            RecordedAt = now.AddHours(-1),
        });
        await db.SaveChangesAsync();
        var service = CreateLiveClassService(db, now);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.GetRecordingForLearnerAsync(session.Id, "learner-1", CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.StatusCode);
        Assert.Equal("live_class_recording_forbidden", exception.ErrorCode);
    }

    [Fact]
    public async Task RecordingResponseUsesStorageReadUrlsInsteadOfRawKeys()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var liveClass = CreateLiveClass("class-1", "published-class", LiveClassStatus.Published, now);
        var session = CreateSession("session-1", liveClass.Id, now.AddHours(-2), now.AddHours(-1));
        session.LiveClass = liveClass;
        liveClass.Sessions.Add(session);
        db.LiveClasses.Add(liveClass);
        db.LiveClassEnrollments.Add(new LiveClassEnrollment
        {
            Id = "enrollment-1",
            ClassSessionId = session.Id,
            UserId = "learner-1",
            EnrolledAt = now.AddDays(-1),
            IdempotencyKey = "recording-active-test",
            Status = LiveClassEnrollmentStatus.Active,
        });
        db.LiveClassRecordings.Add(new LiveClassRecording
        {
            Id = "recording-1",
            ClassSessionId = session.Id,
            Status = LiveClassRecordingStatus.Ready,
            S3VideoKey = "live-class-recordings/2026/06/session-1/video.mp4",
            S3TranscriptKey = "live-class-recordings/2026/06/session-1/transcript.vtt",
            RecordedAt = now.AddHours(-1),
        });
        await db.SaveChangesAsync();
        var service = CreateLiveClassService(db, now);

        var recording = await service.GetRecordingForLearnerAsync(session.Id, "learner-1", CancellationToken.None);

        Assert.StartsWith("/test-media/", recording.VideoUrl);
        Assert.StartsWith("/test-media/", recording.TranscriptUrl);
        Assert.DoesNotContain("live-class-recordings/", recording.VideoUrl);
    }

    [Fact]
    public async Task AttendedLearnersCanAccessRecordings()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var liveClass = CreateLiveClass("class-1", "published-class", LiveClassStatus.Published, now);
        var session = CreateSession("session-1", liveClass.Id, now.AddHours(-2), now.AddHours(-1));
        session.LiveClass = liveClass;
        liveClass.Sessions.Add(session);
        db.LiveClasses.Add(liveClass);
        db.LiveClassEnrollments.Add(new LiveClassEnrollment
        {
            Id = "enrollment-1",
            ClassSessionId = session.Id,
            UserId = "learner-1",
            EnrolledAt = now.AddDays(-1),
            IdempotencyKey = "recording-attended-test",
            Status = LiveClassEnrollmentStatus.Attended,
        });
        db.LiveClassRecordings.Add(new LiveClassRecording
        {
            Id = "recording-1",
            ClassSessionId = session.Id,
            Status = LiveClassRecordingStatus.Ready,
            S3VideoKey = "live-class-recordings/2026/06/session-1/video.mp4",
            RecordedAt = now.AddHours(-1),
        });
        await db.SaveChangesAsync();
        var service = CreateLiveClassService(db, now);

        var recording = await service.GetRecordingForLearnerAsync(session.Id, "learner-1", CancellationToken.None);

        Assert.Equal("Ready", recording.Status);
    }

    private static ZoomMeetingService CreateZoomService(ZoomOptions options)
        => new(new StaticHttpClientFactory(), Options.Create(options), NullLogger<ZoomMeetingService>.Instance);

    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static LiveClassService CreateLiveClassService(LearnerDbContext db, DateTimeOffset now, ZoomOptions? zoomOptions = null)
        => new(
            db,
            CreateZoomService(zoomOptions ?? new ZoomOptions()),
            notificationService: null!,
            fileStorage: new TestFileStorage(),
            new FixedTimeProvider(now),
            NullLogger<LiveClassService>.Instance);

    private static LiveClass CreateLiveClass(string id, string slug, LiveClassStatus status, DateTimeOffset now)
        => new()
        {
            Id = id,
            Slug = slug,
            Title = "Live class",
            Description = "A live class.",
            Type = LiveClassType.GroupClass,
            ProfessionTrack = "All",
            Level = "All",
            DefaultDurationMinutes = 60,
            DefaultCapacity = 20,
            CreditCost = 0,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
        };

    private static LiveClassSession CreateSession(string id, string liveClassId, DateTimeOffset start, DateTimeOffset end)
        => new()
        {
            Id = id,
            LiveClassId = liveClassId,
            ScheduledStartAt = start,
            ScheduledEndAt = end,
            Capacity = 20,
            Status = LiveClassSessionStatus.Scheduled,
            ZoomMeetingNumber = "123456789",
            ZoomJoinUrl = "https://zoom.test/j/123456789",
            CreatedAt = start.AddDays(-1),
            UpdatedAt = start.AddDays(-1),
        };

    private static string BuildZoomWebhookSignature(string secret, string timestamp, string rawBody)
    {
        var message = $"v0:{timestamp}:{rawBody}";
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(message));
        return "v0=" + Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static string BuildPlainTokenHash(string secret, string plainToken)
    {
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(plainToken));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestFileStorage : IFileStorage
    {
        public Task<long> WriteAsync(string key, Stream source, CancellationToken ct) => Task.FromResult(0L);
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream());
        public Task<Stream> OpenWriteAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream());
        public bool Exists(string key) => true;
        public bool Delete(string key) => true;
        public long Length(string key) => 0;
        public void Move(string sourceKey, string destKey, bool overwrite) { }
        public int DeletePrefix(string prefix) => 0;
        public string? TryResolveLocalPath(string key) => null;
        public Uri? ResolveReadUrl(string key, TimeSpan ttl) => new($"/test-media/{Uri.EscapeDataString(key)}", UriKind.Relative);
    }
}