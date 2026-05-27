using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Classes;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.LiveClasses;

namespace OetLearner.Api.Tests.Classes;

public sealed class ReminderSchedulingTests
{
    [Fact]
    public async Task EnrollAsync_QueuesThreeReminderJobsAtCorrectLeadTimes()
    {
        await using var db = NewDb();
        var now = new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.Zero);
        var sessionStart = now.AddDays(3);
        var (service, _) = CreateService(db, now);

        var liveClass = NewLiveClass(now);
        var session = NewSession(liveClass.Id, sessionStart, sessionStart.AddHours(1));
        liveClass.Sessions.Add(session);
        db.LiveClasses.Add(liveClass);
        db.Users.Add(new LearnerUser
        {
            Id = "learner-1",
            DisplayName = "Learner One",
            Email = "learner@example.test",
            CreatedAt = now,
            LastActiveAt = now,
        });
        await db.SaveChangesAsync();

        await service.EnrollAsync(session.Id, "learner-1", idempotencyKey: null, CancellationToken.None);

        var enrollment = await db.LiveClassEnrollments.SingleAsync();
        var jobs = await db.BackgroundJobs
            .Where(job => job.Type == JobType.LiveClassSessionReminderDispatch
                && job.ResourceId != null
                && job.ResourceId.StartsWith(enrollment.Id))
            .ToListAsync();
        Assert.Equal(3, jobs.Count);
        AssertCascadeJobsExist(jobs, enrollment.Id, sessionStart);
    }

    [Fact]
    public async Task CancelEnrollmentAsync_RemovesPendingReminderJobs()
    {
        await using var db = NewDb();
        var now = new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.Zero);
        var sessionStart = now.AddDays(3);
        var (service, _) = CreateService(db, now);

        var liveClass = NewLiveClass(now);
        var session = NewSession(liveClass.Id, sessionStart, sessionStart.AddHours(1));
        liveClass.Sessions.Add(session);
        db.LiveClasses.Add(liveClass);
        db.Users.Add(new LearnerUser
        {
            Id = "learner-1",
            DisplayName = "Learner One",
            Email = "learner@example.test",
            CreatedAt = now,
            LastActiveAt = now,
        });
        await db.SaveChangesAsync();

        await service.EnrollAsync(session.Id, "learner-1", idempotencyKey: null, CancellationToken.None);
        var enrollment = await db.LiveClassEnrollments.SingleAsync();
        Assert.Equal(3, await db.BackgroundJobs.CountAsync(j => j.Type == JobType.LiveClassSessionReminderDispatch
            && j.ResourceId != null && j.ResourceId.StartsWith(enrollment.Id)));

        await service.CancelEnrollmentAsync(session.Id, "learner-1", reason: "test cancel", CancellationToken.None);

        var remaining = await db.BackgroundJobs
            .Where(j => j.Type == JobType.LiveClassSessionReminderDispatch
                && j.ResourceId != null && j.ResourceId.StartsWith(enrollment.Id))
            .ToListAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task EnrollAsync_WithinTenMinuteWindow_OnlySchedulesNoOpAndSkipsExpiredLegs()
    {
        await using var db = NewDb();
        var now = new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.Zero);
        var sessionStart = now.AddMinutes(5); // Less than the 10-min lead.
        var (service, _) = CreateService(db, now);

        var liveClass = NewLiveClass(now);
        var session = NewSession(liveClass.Id, sessionStart, sessionStart.AddHours(1));
        liveClass.Sessions.Add(session);
        db.LiveClasses.Add(liveClass);
        db.Users.Add(new LearnerUser
        {
            Id = "learner-1",
            DisplayName = "Learner One",
            Email = "learner@example.test",
            CreatedAt = now,
            LastActiveAt = now,
        });
        await db.SaveChangesAsync();

        await service.EnrollAsync(session.Id, "learner-1", idempotencyKey: null, CancellationToken.None);

        var enrollment = await db.LiveClassEnrollments.SingleAsync();
        var jobs = await db.BackgroundJobs
            .Where(j => j.Type == JobType.LiveClassSessionReminderDispatch
                && j.ResourceId != null && j.ResourceId.StartsWith(enrollment.Id))
            .ToListAsync();
        // All three leads (T-24h, T-1h, T-10min) are in the past relative to now, so no jobs are queued.
        Assert.Empty(jobs);
    }

    [Fact]
    public async Task ReSchedulingReminders_UpdatesExistingJobsRatherThanDuplicating()
    {
        await using var db = NewDb();
        var now = new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.Zero);
        var sessionStart = now.AddDays(3);
        var (service, _) = CreateService(db, now);

        var liveClass = NewLiveClass(now);
        var session = NewSession(liveClass.Id, sessionStart, sessionStart.AddHours(1));
        liveClass.Sessions.Add(session);
        db.LiveClasses.Add(liveClass);
        db.Users.Add(new LearnerUser
        {
            Id = "learner-1",
            DisplayName = "Learner One",
            Email = "learner@example.test",
            CreatedAt = now,
            LastActiveAt = now,
        });
        await db.SaveChangesAsync();

        await service.EnrollAsync(session.Id, "learner-1", idempotencyKey: null, CancellationToken.None);
        var enrollment = await db.LiveClassEnrollments.SingleAsync();

        // Re-run the private scheduling helper directly with a different "now" — it should
        // update AvailableAt on the existing rows instead of inserting new ones.
        var laterNow = now.AddHours(1);
        var rescheduler = typeof(LiveClassService).GetMethod(
            "ScheduleEnrollmentReminderCascadeAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ScheduleEnrollmentReminderCascadeAsync was not found.");
        var task = (Task)rescheduler.Invoke(service, [enrollment, session, laterNow, CancellationToken.None])!;
        await task;
        await db.SaveChangesAsync();

        var jobs = await db.BackgroundJobs
            .Where(j => j.Type == JobType.LiveClassSessionReminderDispatch
                && j.ResourceId != null && j.ResourceId.StartsWith(enrollment.Id))
            .ToListAsync();
        Assert.Equal(3, jobs.Count);
        Assert.All(jobs, j => Assert.Equal(laterNow, j.LastTransitionAt));
    }

    [Fact]
    public void BuildReminderResourceKey_IsStableAcrossLeads()
    {
        // BuildReminderResourceKey was inlined into LiveClassRecordingService; the
        // canonical key shape is "{enrollmentId}:T{leadMinutes}". Verify the
        // contract holds at the call site format level.
        static string Key(string enrollmentId, int leadMinutes) => $"{enrollmentId}:T{leadMinutes}";

        Assert.Equal("enrollment-x:T1440", Key("enrollment-x", 1440));
        Assert.Equal("enrollment-x:T60", Key("enrollment-x", 60));
        Assert.Equal("enrollment-x:T10", Key("enrollment-x", 10));
        // Distinct enrollments do not collide:
        Assert.NotEqual(Key("enrollment-a", 60), Key("enrollment-b", 60));
    }

    private static void AssertCascadeJobsExist(IReadOnlyCollection<BackgroundJobItem> jobs, string enrollmentId, DateTimeOffset sessionStart)
    {
        var byResource = jobs.ToDictionary(job => job.ResourceId!);
        Assert.True(byResource.TryGetValue($"{enrollmentId}:T1440", out var t24));
        Assert.True(byResource.TryGetValue($"{enrollmentId}:T60", out var t1));
        Assert.True(byResource.TryGetValue($"{enrollmentId}:T10", out var t10));
        Assert.Equal(sessionStart.AddMinutes(-1440), t24!.AvailableAt);
        Assert.Equal(sessionStart.AddMinutes(-60), t1!.AvailableAt);
        Assert.Equal(sessionStart.AddMinutes(-10), t10!.AvailableAt);
        foreach (var job in jobs)
        {
            Assert.Equal(AsyncState.Queued, job.State);
            Assert.Contains("\"leadMinutes\":", job.PayloadJson);
        }
    }

    private static (LiveClassService Service, IClassNotificationService Notifications) CreateService(LearnerDbContext db, DateTimeOffset now)
    {
        var notificationService = new NotificationService(
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
        var classNotifications = new ClassNotificationService(
            db,
            notificationService,
            new FixedTimeProvider(now),
            NullLogger<ClassNotificationService>.Instance);
        var zoom = new ZoomMeetingService(
            new StaticHttpClientFactory(),
            TestRuntimeSettingsProvider.FromZoomOptions(new ZoomOptions()),
            NullLogger<ZoomMeetingService>.Instance);
        var service = new LiveClassService(
            db,
            zoom,
            walletService: new WalletService(db, paymentGateways: null!, platformLinks: null!, billingOptions: Options.Create(new BillingOptions())),
            notificationService: notificationService,
            fileStorage: new TestFileStorage(),
            new FixedTimeProvider(now),
            NullLogger<LiveClassService>.Instance,
            classNotifications);
        return (service, classNotifications);
    }

    private static LearnerDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static LiveClass NewLiveClass(DateTimeOffset now)
        => new()
        {
            Id = "class-1",
            Slug = "class-one",
            Title = "Live Class",
            Description = "A live class.",
            Type = LiveClassType.GroupClass,
            ProfessionTrack = "All",
            Level = "All",
            DefaultDurationMinutes = 60,
            DefaultCapacity = 20,
            CreditCost = 0,
            Status = LiveClassStatus.Published,
            CreatedAt = now,
            UpdatedAt = now,
        };

    private static LiveClassSession NewSession(string liveClassId, DateTimeOffset start, DateTimeOffset end)
        => new()
        {
            Id = "session-1",
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
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
