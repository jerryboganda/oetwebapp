using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Classes;

namespace OetLearner.Api.Tests.Classes;

public sealed class ClassNotificationServiceTests
{
    [Fact]
    public void BuildIcsAttachment_ProducesVeventWithCorrectFields()
    {
        var session = NewSession(
            sessionId: "session-1",
            classId: "class-1",
            title: "Acute Care for Nurses",
            scheduledStartUtc: new DateTimeOffset(2026, 6, 1, 14, 30, 0, TimeSpan.Zero),
            scheduledEndUtc: new DateTimeOffset(2026, 6, 1, 15, 30, 0, TimeSpan.Zero),
            joinUrl: "https://zoom.test/j/9999");

        var (db, service) = NewService(new DateTimeOffset(2026, 5, 20, 0, 0, 0, TimeSpan.Zero));
        try
        {
            var attachment = service.BuildIcsAttachment(session, userTimezone: "Australia/Sydney", tutorEmail: "tutor@example.test");

            Assert.Equal($"oet-class-{session.Id}.ics", attachment.FileName);
            Assert.StartsWith("text/calendar", attachment.ContentType);

            var ics = Encoding.UTF8.GetString(attachment.Content);
            Assert.Contains("BEGIN:VEVENT", ics);
            Assert.Contains("END:VEVENT", ics);
            Assert.Contains($"UID:oet-live-class-{session.Id}@oetlearner", ics);
            Assert.Contains("SUMMARY:Acute Care for Nurses", ics);
            // Either TZID (Australia/Sydney) or UTC suffix should appear on DTSTART.
            Assert.True(ics.Contains("DTSTART;TZID=Australia/Sydney") || ics.Contains("DTSTART:20260601T143000Z"),
                $"DTSTART line missing or in unexpected format. Ics:\n{ics}");
            Assert.Contains("ORGANIZER", ics);
            Assert.Contains("mailto:tutor@example.test", ics);
            Assert.Contains("https://zoom.test/j/9999", ics);
        }
        finally
        {
            db.Dispose();
        }
    }

    [Fact]
    public void BuildIcsAttachment_FallsBackToUtcWhenTimezoneIsInvalid()
    {
        var session = NewSession(
            sessionId: "session-2",
            classId: "class-1",
            title: "Class Title",
            scheduledStartUtc: new DateTimeOffset(2026, 6, 1, 14, 0, 0, TimeSpan.Zero),
            scheduledEndUtc: new DateTimeOffset(2026, 6, 1, 15, 0, 0, TimeSpan.Zero),
            joinUrl: "https://zoom.test/j/100");

        var (db, service) = NewService(new DateTimeOffset(2026, 5, 20, 0, 0, 0, TimeSpan.Zero));
        try
        {
            var attachment = service.BuildIcsAttachment(session, userTimezone: "Not/A_Real_Timezone", tutorEmail: null);
            var ics = Encoding.UTF8.GetString(attachment.Content);
            Assert.Contains("DTSTART", ics);
        }
        finally
        {
            db.Dispose();
        }
    }

    [Fact]
    public async Task SendEnrollmentConfirmedAsync_QueuesNotificationEventWithIcsPayloadHint()
    {
        var now = new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.Zero);
        var (db, service) = NewService(now);
        try
        {
            var account = SeedLearnerAccount(db, "auth-acc-1", "learner@example.test", "en-AU");
            var learner = SeedLearnerUser(db, "learner-1", account.Id, "Learner One", "learner@example.test");
            var (session, enrollment) = SeedClassWithEnrollment(db, learner.Id, now);
            await db.SaveChangesAsync();

            await service.SendEnrollmentConfirmedAsync(enrollment, session, CancellationToken.None);

            var notificationEvent = await db.NotificationEvents.SingleAsync();
            Assert.Equal("LearnerClassEnrollmentConfirmed", notificationEvent.EventKey);
            Assert.Equal(account.Id, notificationEvent.RecipientAuthAccountId);
            Assert.Equal(ApplicationUserRoles.Learner, notificationEvent.RecipientRole);
            Assert.Contains("oet-class-", notificationEvent.PayloadJson);
        }
        finally
        {
            db.Dispose();
        }
    }

    [Fact]
    public async Task SendReminderAsync_ProducesDistinctDedupeKeysPerLeadWindow()
    {
        var now = new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.Zero);
        var (db, service) = NewService(now);
        try
        {
            var account = SeedLearnerAccount(db, "auth-acc-1", "learner@example.test", "en");
            var learner = SeedLearnerUser(db, "learner-1", account.Id, "Learner One", "learner@example.test");
            var (session, enrollment) = SeedClassWithEnrollment(db, learner.Id, now);
            await db.SaveChangesAsync();

            await service.SendReminderAsync(enrollment, session, leadMinutes: 1440, CancellationToken.None);
            await service.SendReminderAsync(enrollment, session, leadMinutes: 60, CancellationToken.None);
            await service.SendReminderAsync(enrollment, session, leadMinutes: 10, CancellationToken.None);

            var events = await db.NotificationEvents
                .OrderBy(e => e.CreatedAt)
                .ThenBy(e => e.Id)
                .ToListAsync();
            Assert.Equal(3, events.Count);
            Assert.All(events, e => Assert.Equal("LearnerLiveClassReminder", e.EventKey));
            var distinctDedupe = events.Select(e => e.DedupeKey).Distinct().Count();
            Assert.Equal(3, distinctDedupe);

            // The 24h-leg payload should hint at an .ics filename; the 1h/10min legs should not.
            var t24h = events.Single(e => e.PayloadJson.Contains("\"leadMinutes\":1440"));
            Assert.Contains("\"icsFileName\":", t24h.PayloadJson);
            foreach (var pushOnlyEvent in events.Where(e => e.PayloadJson.Contains("\"leadMinutes\":60") || e.PayloadJson.Contains("\"leadMinutes\":10")))
            {
                Assert.DoesNotContain("\"icsFileName\":", pushOnlyEvent.PayloadJson);
            }
        }
        finally
        {
            db.Dispose();
        }
    }

    [Fact]
    public async Task SendRecordingReadyAsync_FansOutToActiveAndAttendedOnly()
    {
        var now = new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.Zero);
        var (db, service) = NewService(now);
        try
        {
            var attended = SeedLearnerAccount(db, "auth-attended", "att@example.test", "en");
            var attendedUser = SeedLearnerUser(db, "learner-attended", attended.Id, "Attended", "att@example.test");

            var active = SeedLearnerAccount(db, "auth-active", "act@example.test", "en");
            var activeUser = SeedLearnerUser(db, "learner-active", active.Id, "Active", "act@example.test");

            var refunded = SeedLearnerAccount(db, "auth-refunded", "ref@example.test", "en");
            var refundedUser = SeedLearnerUser(db, "learner-refunded", refunded.Id, "Refunded", "ref@example.test");

            var noshow = SeedLearnerAccount(db, "auth-noshow", "noshow@example.test", "en");
            var noshowUser = SeedLearnerUser(db, "learner-noshow", noshow.Id, "NoShow", "noshow@example.test");

            var (session, _) = SeedClassWithEnrollment(db, attendedUser.Id, now, enrollmentStatus: LiveClassEnrollmentStatus.Attended, enrollmentId: "enr-attended");
            db.LiveClassEnrollments.Add(new LiveClassEnrollment
            {
                Id = "enr-active",
                ClassSessionId = session.Id,
                UserId = activeUser.Id,
                EnrolledAt = now,
                IdempotencyKey = "enr-active",
                Status = LiveClassEnrollmentStatus.Active,
            });
            db.LiveClassEnrollments.Add(new LiveClassEnrollment
            {
                Id = "enr-refunded",
                ClassSessionId = session.Id,
                UserId = refundedUser.Id,
                EnrolledAt = now,
                IdempotencyKey = "enr-refunded",
                Status = LiveClassEnrollmentStatus.Refunded,
            });
            db.LiveClassEnrollments.Add(new LiveClassEnrollment
            {
                Id = "enr-noshow",
                ClassSessionId = session.Id,
                UserId = noshowUser.Id,
                EnrolledAt = now,
                IdempotencyKey = "enr-noshow",
                Status = LiveClassEnrollmentStatus.NoShow,
            });
            var recording = new LiveClassRecording
            {
                Id = "rec-1",
                ClassSessionId = session.Id,
                Status = LiveClassRecordingStatus.Ready,
                RecordedAt = now,
                ProcessedAt = now,
            };
            db.LiveClassRecordings.Add(recording);
            await db.SaveChangesAsync();

            await service.SendRecordingReadyAsync(recording, session, CancellationToken.None);

            var learnerNotifications = await db.NotificationEvents
                .Where(e => e.EventKey == "LearnerLiveClassRecordingReady")
                .Select(e => e.RecipientAuthAccountId)
                .ToListAsync();
            Assert.Equal(2, learnerNotifications.Count);
            Assert.Contains(attended.Id, learnerNotifications);
            Assert.Contains(active.Id, learnerNotifications);
            Assert.DoesNotContain(refunded.Id, learnerNotifications);
            Assert.DoesNotContain(noshow.Id, learnerNotifications);
        }
        finally
        {
            db.Dispose();
        }
    }

    private static (LearnerDbContext Db, ClassNotificationService Service) NewService(DateTimeOffset now)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var notificationService = NewNotificationService(db, now);
        var service = new ClassNotificationService(
            db,
            notificationService,
            new FixedTimeProvider(now),
            NullLogger<ClassNotificationService>.Instance);
        return (db, service);
    }

    private static NotificationService NewNotificationService(LearnerDbContext db, DateTimeOffset now)
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

    private static (LiveClassSession Session, LiveClassEnrollment Enrollment) SeedClassWithEnrollment(
        LearnerDbContext db,
        string learnerUserId,
        DateTimeOffset now,
        LiveClassEnrollmentStatus enrollmentStatus = LiveClassEnrollmentStatus.Active,
        string enrollmentId = "enr-1")
    {
        var liveClass = new LiveClass
        {
            Id = "class-1",
            Slug = "class-one",
            Title = "Live Class",
            Description = "Description",
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
        var session = new LiveClassSession
        {
            Id = "session-1",
            LiveClassId = liveClass.Id,
            ScheduledStartAt = now.AddDays(2),
            ScheduledEndAt = now.AddDays(2).AddHours(1),
            Capacity = 20,
            Status = LiveClassSessionStatus.Scheduled,
            ZoomJoinUrl = "https://zoom.test/j/1234",
            CreatedAt = now,
            UpdatedAt = now,
            LiveClass = liveClass,
        };
        liveClass.Sessions.Add(session);
        var enrollment = new LiveClassEnrollment
        {
            Id = enrollmentId,
            ClassSessionId = session.Id,
            UserId = learnerUserId,
            EnrolledAt = now,
            IdempotencyKey = enrollmentId,
            Status = enrollmentStatus,
            ClassSession = session,
        };
        db.LiveClasses.Add(liveClass);
        db.LiveClassEnrollments.Add(enrollment);
        return (session, enrollment);
    }

    private static ApplicationUserAccount SeedLearnerAccount(LearnerDbContext db, string id, string email, string locale)
    {
        var account = new ApplicationUserAccount
        {
            Id = id,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "x",
            Role = ApplicationUserRoles.Learner,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.ApplicationUserAccounts.Add(account);
        return account;
    }

    private static LearnerUser SeedLearnerUser(LearnerDbContext db, string id, string authAccountId, string displayName, string email)
    {
        var learner = new LearnerUser
        {
            Id = id,
            AuthAccountId = authAccountId,
            DisplayName = displayName,
            Email = email,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(learner);
        return learner;
    }

    private static LiveClassSession NewSession(
        string sessionId,
        string classId,
        string title,
        DateTimeOffset scheduledStartUtc,
        DateTimeOffset scheduledEndUtc,
        string? joinUrl)
    {
        var liveClass = new LiveClass
        {
            Id = classId,
            Slug = classId,
            Title = title,
            Description = "Course description.",
            TutorDisplayName = "Tutor Test",
            Type = LiveClassType.GroupClass,
        };
        return new LiveClassSession
        {
            Id = sessionId,
            LiveClassId = classId,
            ScheduledStartAt = scheduledStartUtc,
            ScheduledEndAt = scheduledEndUtc,
            Capacity = 20,
            EnrolledCount = 1,
            Status = LiveClassSessionStatus.Scheduled,
            ZoomJoinUrl = joinUrl,
            LiveClass = liveClass,
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
