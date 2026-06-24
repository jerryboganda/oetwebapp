using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests;

/// <summary>
/// T4 — PDF §10 minute-based reminders (24h / 1h / 15m). ProcessRemindersAsync reads
/// config.ReminderOffsetsMinutesJson (default [1440, 60, 15]) and dedups via
/// booking.RemindersSentJson (now a list of MINUTE offsets). No LearnerUser/ExpertUser
/// rows are seeded, so the real NotificationService early-returns from
/// CreateForLearnerAsync/CreateForExpertAsync — leaving RemindersSentJson updated.
/// </summary>
public sealed class PrivateSpeakingReminderTests
{
    private static readonly DateTimeOffset Now = new(2026, 06, 06, 12, 0, 0, TimeSpan.Zero);

    // ── Firing: a booking 15 minutes out fires all three elapsed offsets ──

    [Fact]
    public async Task ProcessReminders_BookingFifteenMinutesOut_FiresAllThreeOffsets()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        SeedTutor(db);
        var booking = SeedConfirmedBooking(db, Now.AddMinutes(15));
        await db.SaveChangesAsync();

        await service.ProcessRemindersAsync(CancellationToken.None);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        var sent = JsonSerializer.Deserialize<List<int>>(saved!.RemindersSentJson) ?? [];

        // minutesUntil(15) <= 15, 60 and 1440 → all three offsets are marked sent.
        Assert.Contains(15, sent);
        Assert.Contains(60, sent);
        Assert.Contains(1440, sent);
    }

    // ── Firing: a booking 24h out only fires the 1440 (24h) offset ──

    [Fact]
    public async Task ProcessReminders_BookingTwentyFourHoursOut_FiresOnlyDayOffset()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        SeedTutor(db);
        // Exactly 24h (1440 min) out → only the 1440 offset is due (minutesUntil 1440
        // is <= 1440 but > 60 and > 15).
        var booking = SeedConfirmedBooking(db, Now.AddMinutes(1440));
        await db.SaveChangesAsync();

        await service.ProcessRemindersAsync(CancellationToken.None);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        var sent = JsonSerializer.Deserialize<List<int>>(saved!.RemindersSentJson) ?? [];

        Assert.Contains(1440, sent);
        Assert.DoesNotContain(60, sent);
        Assert.DoesNotContain(15, sent);
    }

    // ── Dedup: running twice does not re-add already-sent offsets ──

    [Fact]
    public async Task ProcessReminders_RunTwice_IsIdempotent()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        SeedTutor(db);
        var booking = SeedConfirmedBooking(db, Now.AddMinutes(15));
        await db.SaveChangesAsync();

        await service.ProcessRemindersAsync(CancellationToken.None);
        var afterFirst = JsonSerializer.Deserialize<List<int>>(
            (await db.PrivateSpeakingBookings.FindAsync(booking.Id))!.RemindersSentJson) ?? [];

        await service.ProcessRemindersAsync(CancellationToken.None);
        var afterSecond = JsonSerializer.Deserialize<List<int>>(
            (await db.PrivateSpeakingBookings.FindAsync(booking.Id))!.RemindersSentJson) ?? [];

        // Same set, no duplicates introduced by the second pass.
        Assert.Equal(afterFirst.OrderBy(x => x), afterSecond.OrderBy(x => x));
        Assert.Equal(afterSecond.Count, afterSecond.Distinct().Count());
        Assert.Equal(3, afterSecond.Count);
    }

    // ── Outside the window: a booking well beyond the max offset fires nothing ──

    [Fact]
    public async Task ProcessReminders_BookingBeyondMaxOffset_FiresNothing()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        SeedTutor(db);
        // 3 days out → outside the loaded window (max offset 1440 + 5 min).
        var booking = SeedConfirmedBooking(db, Now.AddDays(3));
        await db.SaveChangesAsync();

        await service.ProcessRemindersAsync(CancellationToken.None);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        var sent = JsonSerializer.Deserialize<List<int>>(saved!.RemindersSentJson) ?? [];
        Assert.Empty(sent);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static void SeedTutor(LearnerDbContext db)
    {
        db.PrivateSpeakingTutorProfiles.Add(new PrivateSpeakingTutorProfile
        {
            Id = "tutor-profile-1",
            ExpertUserId = "expert-1",
            DisplayName = "Tutor",
            Timezone = "UTC",
            IsActive = true,
            CreatedAt = Now.AddMonths(-2),
            UpdatedAt = Now.AddMonths(-2)
        });
    }

    private static PrivateSpeakingBooking SeedConfirmedBooking(
        LearnerDbContext db, DateTimeOffset sessionStartUtc)
    {
        var booking = new PrivateSpeakingBooking
        {
            Id = $"booking-{Guid.NewGuid():N}",
            LearnerUserId = "learner-1",
            TutorProfileId = "tutor-profile-1",
            Status = PrivateSpeakingBookingStatus.Confirmed,
            SessionStartUtc = sessionStartUtc,
            DurationMinutes = 30,
            TutorTimezone = "UTC",
            LearnerTimezone = "UTC",
            PriceMinorUnits = 0,
            Currency = "GBP",
            PaymentStatus = PrivateSpeakingPaymentStatus.Succeeded,
            PaymentConfirmedAt = Now.AddDays(-1),
            RemindersSentJson = "[]",
            CreatedAt = Now.AddDays(-2),
            UpdatedAt = Now.AddDays(-2)
        };
        db.PrivateSpeakingBookings.Add(booking);
        return booking;
    }

    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LearnerDbContext(options);
    }

    private static PrivateSpeakingService CreateService(LearnerDbContext db)
    {
        var platformLinks = new PlatformLinkService(
            TestRuntimeSettingsProvider.FromPlatformOptions(new PlatformOptions()),
            Options.Create(new BillingOptions()));

        // A real calendar service is constructed, but with no calendar connection
        // seeded CheckBusyAsync short-circuits and never touches the HTTP client.
        var calendarService = new PrivateSpeakingCalendarService(
            db,
            httpClientFactory: new ThrowingHttpClientFactory(),
            runtimeSettings: TestRuntimeSettingsProvider.FromZoomOptions(new ZoomOptions()),
            dataProtectionProvider: Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("PrivateSpeakingReminderTests"),
            platformLinks: platformLinks,
            timeProvider: new FixedTimeProvider(Now),
            logger: NullLogger<PrivateSpeakingCalendarService>.Instance);

        // ProcessRemindersAsync dispatches reminder notifications. With no
        // LearnerUser/ExpertUser rows seeded these early-return, but a real
        // NotificationService is still required (null! would NPE on the dispatch).
        var notificationService = new NotificationService(
            db,
            emailSender: null!,
            webPushDispatcher: null!,
            mobilePushDispatcher: null!,
            hubContext: null!,
            platformLinks: null!,
            timeProvider: new FixedTimeProvider(Now),
            webPushOptions: Options.Create(new WebPushOptions()),
            runtimeSettingsProvider: TestRuntimeSettingsProvider.FromZoomOptions(new ZoomOptions()),
            notificationProofOptions: Options.Create(new NotificationProofHarnessOptions()),
            environment: null!,
            logger: NullLogger<NotificationService>.Instance);

        return new PrivateSpeakingService(
            db,
            notificationService,
            zoomService: null!,
            calendarService: calendarService,
            entitlementResolver: null!,
            stripeService: null!,
            paymentGateways: null!,
            platformLinks: platformLinks,
            timeProvider: new FixedTimeProvider(Now),
            logger: NullLogger<PrivateSpeakingService>.Instance);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException("HTTP client should not be used without a calendar connection.");
    }
}
