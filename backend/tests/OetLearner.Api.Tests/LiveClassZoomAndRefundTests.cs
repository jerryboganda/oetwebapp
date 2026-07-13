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
    public async Task VerifyWebhookSignatureAcceptsZoomHmacSignature()
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

        Assert.True(await service.VerifyWebhookSignatureAsync(rawBody, headers, CancellationToken.None));
        Assert.False(await service.VerifyWebhookSignatureAsync(rawBody + " ", headers, CancellationToken.None));
    }

    [Fact]
    public async Task VerifyWebhookSignatureRejectsFutureTimestampsOutsideTolerance()
    {
        var service = CreateZoomService(new ZoomOptions { WebhookSecretToken = "zoom-secret", WebhookRetryToleranceSeconds = 300 });
        var rawBody = "{\"event\":\"meeting.started\"}";
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds().ToString();
        var headers = new HeaderDictionary
        {
            ["x-zm-request-timestamp"] = timestamp,
            ["x-zm-signature"] = BuildZoomWebhookSignature("zoom-secret", timestamp, rawBody),
        };

        Assert.False(await service.VerifyWebhookSignatureAsync(rawBody, headers, CancellationToken.None));
    }

    [Fact]
    public async Task WebhookUrlValidationResponseUsesZoomEncryptedTokenContract()
    {
        var service = CreateZoomService(new ZoomOptions { WebhookSecretToken = "zoom-secret" });
        var response = await service.TryBuildWebhookUrlValidationResponseAsync(
            "{\"event\":\"endpoint.url_validation\",\"payload\":{\"plainToken\":\"plain-token\"}}",
            CancellationToken.None);

        Assert.NotNull(response);
        var plainToken = response.GetType().GetProperty("plainToken")?.GetValue(response);
        var encryptedToken = response.GetType().GetProperty("encryptedToken")?.GetValue(response);

        Assert.Equal("plain-token", plainToken);
        Assert.Equal(BuildPlainTokenHash("zoom-secret", "plain-token"), encryptedToken);
    }

    [Fact]
    public async Task ZoomWebhookUrlValidationRequiresValidZoomSignatureAtServiceBoundary()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var service = CreateLiveClassService(db, now, new ZoomOptions { WebhookSecretToken = "zoom-secret" });
        var rawBody = "{\"event\":\"endpoint.url_validation\",\"payload\":{\"plainToken\":\"plain-token\"}}";

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.HandleZoomWebhookAsync(rawBody, new HeaderDictionary(), CancellationToken.None));

        Assert.Equal(StatusCodes.Status401Unauthorized, exception.StatusCode);
        Assert.Equal("zoom_webhook_invalid_signature", exception.ErrorCode);

        var timestamp = now.ToUnixTimeSeconds().ToString();
        var headers = new HeaderDictionary
        {
            ["x-zm-request-timestamp"] = timestamp,
            ["x-zm-signature"] = BuildZoomWebhookSignature("zoom-secret", timestamp, rawBody),
        };

        var response = await service.HandleZoomWebhookAsync(rawBody, headers, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("plain-token", response.GetType().GetProperty("plainToken")?.GetValue(response));
    }

    [Fact]
    public async Task VerifyWebhookSignatureRequiresConfiguredWebhookSecret()
    {
        var service = CreateZoomService(new ZoomOptions { AllowSandboxFallback = true });

        Assert.False(await service.VerifyWebhookSignatureAsync("{\"event\":\"meeting.started\"}", new HeaderDictionary(), CancellationToken.None));
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
    public async Task MeetingSdkSignatureRequiresSdkCredentials()
    {
        var missingCredentials = CreateZoomService(new ZoomOptions());
        var configured = CreateZoomService(new ZoomOptions
        {
            MeetingSdkKey = "sdk-key",
            MeetingSdkSecret = "sdk-secret",
        });

        Assert.Null(await missingCredentials.GenerateMeetingSdkSignatureAsync("123456789", role: 0, DateTimeOffset.UtcNow.AddMinutes(30), CancellationToken.None));

        var signature = await configured.GenerateMeetingSdkSignatureAsync("123456789", role: 0, DateTimeOffset.UtcNow.AddMinutes(30), CancellationToken.None);
        Assert.NotNull(signature);
        Assert.Equal(3, signature!.Split('.').Length);
    }

    [Fact]
    public async Task ZoomRuntimeSettingsFailureFailsClosedInsteadOfUsingAppsettingsFallback()
    {
        var service = new ZoomMeetingService(
            new StaticHttpClientFactory(),
            new ThrowingRuntimeSettingsProvider(),
            NullLogger<ZoomMeetingService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IsEnabledAsync(CancellationToken.None));
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
    public async Task ExpertJoinTokenIsRefusedBeforeJoinWindow()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var liveClass = CreateLiveClass("class-1", "published-class", LiveClassStatus.Published, now);
        var session = CreateSession("session-1", liveClass.Id, now.AddHours(2), now.AddHours(3));
        var tutor = CreateTutorProfile("tutor-1", "expert-1", now);
        liveClass.TutorProfileId = tutor.Id;
        liveClass.TutorProfile = tutor;
        session.LiveClass = liveClass;
        liveClass.Sessions.Add(session);
        db.ExpertUsers.Add(CreateExpertUser("expert-1", now));
        db.PrivateSpeakingTutorProfiles.Add(tutor);
        db.LiveClasses.Add(liveClass);
        await db.SaveChangesAsync();
        var service = CreateLiveClassService(db, now);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.CreateExpertJoinTokenAsync(session.Id, "expert-1", CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Equal("live_class_join_window_closed", exception.ErrorCode);
    }

    [Fact]
    public async Task ExpertClassListOnlyIncludesPublishedAssignments()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var tutor = CreateTutorProfile("tutor-1", "expert-1", now);
        var published = CreateLiveClass("class-1", "published-class", LiveClassStatus.Published, now);
        var draft = CreateLiveClass("class-2", "draft-class", LiveClassStatus.Draft, now);
        published.TutorProfileId = tutor.Id;
        published.TutorProfile = tutor;
        draft.TutorProfileId = tutor.Id;
        draft.TutorProfile = tutor;
        published.Sessions.Add(CreateSession("session-1", published.Id, now.AddMinutes(10), now.AddMinutes(70)));
        draft.Sessions.Add(CreateSession("session-2", draft.Id, now.AddMinutes(10), now.AddMinutes(70)));
        db.PrivateSpeakingTutorProfiles.Add(tutor);
        db.LiveClasses.AddRange(published, draft);
        await db.SaveChangesAsync();
        var service = CreateLiveClassService(db, now);

        var classes = await service.ListExpertClassesAsync("expert-1", CancellationToken.None);

        var item = Assert.Single(classes);
        Assert.Equal("published-class", item.Slug);
    }

    [Fact]
    public async Task AttendedLearnersRemainVisibleInPastClasses()
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
            IdempotencyKey = "past-attended-test",
            Status = LiveClassEnrollmentStatus.Attended,
        });
        await db.SaveChangesAsync();
        var service = CreateLiveClassService(db, now);

        var classes = await service.ListLearnerEnrollmentsAsync("learner-1", upcoming: false, CancellationToken.None);

        var item = Assert.Single(classes);
        Assert.Equal("published-class", item.Slug);
        Assert.Single(item.Sessions);
        Assert.Equal("session-1", item.Sessions[0].Id);
    }

    [Fact]
    public async Task ReEnrollAfterRefundDebitsWalletAgain()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var liveClass = CreateLiveClass("class-1", "published-class", LiveClassStatus.Published, now);
        liveClass.CreditCost = 10;
        var session = CreateSession("session-1", liveClass.Id, now.AddDays(2), now.AddDays(2).AddHours(1));
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
        db.Wallets.Add(new Wallet
        {
            Id = "wallet-1",
            UserId = "learner-1",
            CreditBalance = 30,
            LastUpdatedAt = now,
        });
        db.LiveClasses.Add(liveClass);
        await db.SaveChangesAsync();
        var service = CreateLiveClassService(db, now);

        await service.EnrollAsync(session.Id, "learner-1", idempotencyKey: null, CancellationToken.None);
        await service.CancelEnrollmentAsync(session.Id, "learner-1", "learner cancelled", CancellationToken.None);
        await service.EnrollAsync(session.Id, "learner-1", idempotencyKey: null, CancellationToken.None);

        var wallet = await db.Wallets.SingleAsync(item => item.Id == "wallet-1");
        var debits = await db.WalletTransactions.CountAsync(item => item.WalletId == "wallet-1" && item.Amount == -10);
        var credits = await db.WalletTransactions.CountAsync(item => item.WalletId == "wallet-1" && item.Amount == 10);
        Assert.Equal(20, wallet.CreditBalance);
        Assert.Equal(2, debits);
        Assert.Equal(1, credits);
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
    public async Task PendingRecordingsAreNotExposedToLearners()
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
            IdempotencyKey = "recording-pending-test",
            Status = LiveClassEnrollmentStatus.Attended,
        });
        db.LiveClassRecordings.Add(new LiveClassRecording
        {
            Id = "recording-1",
            ClassSessionId = session.Id,
            Status = LiveClassRecordingStatus.Processing,
            S3VideoKey = "live-class-recordings/2026/06/session-1/video.mp4",
            RecordedAt = now.AddHours(-1),
        });
        await db.SaveChangesAsync();
        var service = CreateLiveClassService(db, now);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.GetRecordingForLearnerAsync(session.Id, "learner-1", CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.Equal("live_class_recording_not_ready", exception.ErrorCode);
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
        => new(
            new StaticHttpClientFactory(),
            TestRuntimeSettingsProvider.FromZoomOptions(options),
            NullLogger<ZoomMeetingService>.Instance);

    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            // LiveClassService wraps mutations in a Serializable transaction.
            // The in-memory provider has no transaction support and throws on
            // BeginTransactionAsync by default; treat that as a harmless no-op
            // so re-enrollment/refund flows exercise the real service logic.
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LearnerDbContext(options);
    }

    private static LiveClassService CreateLiveClassService(LearnerDbContext db, DateTimeOffset now, ZoomOptions? zoomOptions = null)
        => new(
            db,
            CreateZoomService(zoomOptions ?? new ZoomOptions()),
            walletService: new WalletService(db, paymentGateways: null!, platformLinks: null!, billingOptions: Options.Create(new BillingOptions())),
            notificationService: CreateNotificationService(db, now),
            fileStorage: new TestFileStorage(),
            new FixedTimeProvider(now),
            NullLogger<LiveClassService>.Instance);

    private static NotificationService CreateNotificationService(LearnerDbContext db, DateTimeOffset now)
        => new(
            db,
            emailSender: null!,
            webPushDispatcher: null!,
            mobilePushDispatcher: null!,
            hubContext: null!,
            platformLinks: null!,
            timeProvider: new FixedTimeProvider(now),
            webPushOptions: Options.Create(new WebPushOptions()),
            runtimeSettingsProvider: TestRuntimeSettingsProvider.FromZoomOptions(new ZoomOptions()),
            notificationProofOptions: Options.Create(new NotificationProofHarnessOptions()),
            environment: null!,
            logger: NullLogger<NotificationService>.Instance);

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

    private static ExpertUser CreateExpertUser(string id, DateTimeOffset now)
        => new()
        {
            Id = id,
            DisplayName = "Expert One",
            Email = "expert@example.test",
            CreatedAt = now,
        };

    private static PrivateSpeakingTutorProfile CreateTutorProfile(string id, string expertUserId, DateTimeOffset now)
        => new()
        {
            Id = id,
            ExpertUserId = expertUserId,
            DisplayName = "Expert One",
            Timezone = "UTC",
            CreatedAt = now,
            UpdatedAt = now,
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

    private sealed class ThrowingRuntimeSettingsProvider : OetLearner.Api.Services.Settings.IRuntimeSettingsProvider
    {
        public Task<OetLearner.Api.Services.Settings.EffectiveSettings> GetAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("runtime settings unavailable");

        public Task<RuntimeSettingsRow> GetRawAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("runtime settings unavailable");

        public void Invalidate() { }
        public string Protect(string plain) => plain;
        public string? Unprotect(string? cipher) => cipher;
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
        public Task<bool> ExistsAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }

        public Task<long> LengthAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(0L);
        }

        public Task MoveAsync(string sourceKey, string destKey, bool overwrite, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(0);
        }
        public string? TryResolveLocalPath(string key) => null;
        public Uri? ResolveReadUrl(string key, TimeSpan ttl) => new($"/test-media/{Uri.EscapeDataString(key)}", UriKind.Relative);
    }
}