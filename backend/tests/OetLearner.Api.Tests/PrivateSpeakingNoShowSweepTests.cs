using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
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
/// T5 (PDF §3.3.6/§13) — automatic no-show sweep: bookings whose session has
/// ended (start + duration + grace) and whose learner attendance was never
/// verified by the Zoom attendance webhook are marked NoShow. Verified,
/// future, or already-terminal bookings are left untouched.
/// </summary>
public sealed class PrivateSpeakingNoShowSweepTests
{
    // Now = Saturday 2026-06-06 12:00 UTC.
    private static readonly DateTimeOffset Now = new(2026, 06, 06, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sweep_MarksPastUnattendedConfirmedBookingAsNoShow()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        // Session started 2h ago, 30 min long → ended ~90 min ago (past grace).
        var booking = SeedBooking(db, Now.AddHours(-2), b =>
        {
            b.Status = PrivateSpeakingBookingStatus.Confirmed;
            b.AttendanceVerified = false;
        });
        await db.SaveChangesAsync();

        await service.ProcessNoShowSweepAsync(CancellationToken.None);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.NoShow, saved!.Status);
    }

    [Fact]
    public async Task Sweep_DoesNotMarkBookingWithVerifiedAttendance()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var booking = SeedBooking(db, Now.AddHours(-2), b =>
        {
            b.Status = PrivateSpeakingBookingStatus.ZoomCreated;
            b.AttendanceVerified = true;
            b.AttendanceJoinedAt = Now.AddHours(-2);
        });
        await db.SaveChangesAsync();

        await service.ProcessNoShowSweepAsync(CancellationToken.None);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.ZoomCreated, saved!.Status);
    }

    [Fact]
    public async Task Sweep_DoesNotMarkFutureBooking()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var booking = SeedBooking(db, Now.AddHours(2), b =>
        {
            b.Status = PrivateSpeakingBookingStatus.Confirmed;
            b.AttendanceVerified = false;
        });
        await db.SaveChangesAsync();

        await service.ProcessNoShowSweepAsync(CancellationToken.None);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.Confirmed, saved!.Status);
    }

    [Fact]
    public async Task Sweep_DoesNotMarkRecentlyEndedBookingInsideGracePeriod()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        // Session started 35 min ago, 30 min long → ended 5 min ago, still
        // inside the 15-min grace window.
        var booking = SeedBooking(db, Now.AddMinutes(-35), b =>
        {
            b.Status = PrivateSpeakingBookingStatus.Confirmed;
            b.AttendanceVerified = false;
        });
        await db.SaveChangesAsync();

        await service.ProcessNoShowSweepAsync(CancellationToken.None);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.Confirmed, saved!.Status);
    }

    [Fact]
    public async Task Sweep_DoesNotMarkAlreadyCancelledBooking()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var booking = SeedBooking(db, Now.AddHours(-2), b =>
        {
            b.Status = PrivateSpeakingBookingStatus.Cancelled;
            b.AttendanceVerified = false;
        });
        await db.SaveChangesAsync();

        await service.ProcessNoShowSweepAsync(CancellationToken.None);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.Cancelled, saved!.Status);
    }

    [Fact]
    public async Task Sweep_DoesNotMarkCompletedBooking()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var booking = SeedBooking(db, Now.AddHours(-2), b =>
        {
            b.Status = PrivateSpeakingBookingStatus.Completed;
            b.AttendanceVerified = false;
            b.CompletedAt = Now.AddHours(-1);
        });
        await db.SaveChangesAsync();

        await service.ProcessNoShowSweepAsync(CancellationToken.None);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.Completed, saved!.Status);
    }

    // ── ApplyZoomAttendanceWebhookAsync (attendance mapping) ────────────

    [Fact]
    public async Task AttendanceWebhook_LearnerJoin_SetsAttendanceVerified()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        SeedLearnerUser(db, "learner1@example.test");
        var booking = SeedBooking(db, Now.AddHours(1), b =>
        {
            b.Status = PrivateSpeakingBookingStatus.ZoomCreated;
            b.ZoomMeetingId = 87654321L;
        });
        await db.SaveChangesAsync();

        var root = ParseEvent("meeting.participant_joined", 87654321L,
            participantEmail: "learner1@example.test", timeField: "join_time", timeValue: Now.ToString("O"));

        await service.ApplyZoomAttendanceWebhookAsync("meeting.participant_joined", root, CancellationToken.None);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        Assert.True(saved!.AttendanceVerified);
        Assert.NotNull(saved.AttendanceJoinedAt);
    }

    [Fact]
    public async Task AttendanceWebhook_NonLearnerJoin_RecordsJoinButDoesNotVerify()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        SeedLearnerUser(db, "learner1@example.test");
        var booking = SeedBooking(db, Now.AddHours(1), b =>
        {
            b.Status = PrivateSpeakingBookingStatus.ZoomCreated;
            b.ZoomMeetingId = 87654321L;
        });
        await db.SaveChangesAsync();

        // The tutor/host joins first — not the learner's email.
        var root = ParseEvent("meeting.participant_joined", 87654321L,
            participantEmail: "tutor@example.test", timeField: "join_time", timeValue: Now.ToString("O"));

        await service.ApplyZoomAttendanceWebhookAsync("meeting.participant_joined", root, CancellationToken.None);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        // Diagnostic join is stamped, but attendance is NOT verified.
        Assert.NotNull(saved!.AttendanceJoinedAt);
        Assert.False(saved.AttendanceVerified);
    }

    [Fact]
    public async Task AttendanceWebhook_VerifiedJoin_PreventsNoShowSweep()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        SeedLearnerUser(db, "learner1@example.test");
        // Past, ended session — would be a no-show without verified attendance.
        var booking = SeedBooking(db, Now.AddHours(-2), b =>
        {
            b.Status = PrivateSpeakingBookingStatus.ZoomCreated;
            b.ZoomMeetingId = 87654321L;
        });
        await db.SaveChangesAsync();

        var joinRoot = ParseEvent("meeting.participant_joined", 87654321L,
            participantEmail: "learner1@example.test", timeField: "join_time", timeValue: Now.AddHours(-2).ToString("O"));
        await service.ApplyZoomAttendanceWebhookAsync("meeting.participant_joined", joinRoot, CancellationToken.None);

        await service.ProcessNoShowSweepAsync(CancellationToken.None);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.ZoomCreated, saved!.Status);
    }

    private static JsonElement ParseEvent(
        string eventType, long meetingId, string participantEmail, string timeField, string timeValue)
    {
        var json = JsonSerializer.Serialize(new
        {
            @event = eventType,
            event_ts = 1_700_000_000_000L,
            payload = new
            {
                @object = new Dictionary<string, object?>
                {
                    ["id"] = meetingId.ToString(),
                    ["uuid"] = "abc==",
                    ["participant"] = new Dictionary<string, object?>
                    {
                        ["user_email"] = participantEmail,
                        ["user_id"] = "p-1",
                        [timeField] = timeValue,
                    }
                }
            }
        });
        // Clone so the JsonElement outlives the JsonDocument.
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static void SeedLearnerUser(LearnerDbContext db, string email)
    {
        db.Users.Add(new LearnerUser
        {
            Id = "learner-1",
            DisplayName = "Learner One",
            Email = email,
            Timezone = "UTC"
        });
    }

    private static PrivateSpeakingBooking SeedBooking(
        LearnerDbContext db, DateTimeOffset sessionStartUtc,
        Action<PrivateSpeakingBooking>? configure = null)
    {
        if (!db.PrivateSpeakingTutorProfiles.Local.Any(p => p.Id == "tutor-profile-1"))
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
            CreatedAt = Now.AddDays(-2),
            UpdatedAt = Now.AddDays(-2)
        };
        configure?.Invoke(booking);
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
            Options.Create(new PlatformOptions()),
            Options.Create(new BillingOptions()));

        // A real calendar service is constructed, but with no calendar connection
        // seeded CheckBusyAsync short-circuits to "not connected" and never touches
        // the HTTP client.
        var calendarService = new PrivateSpeakingCalendarService(
            db,
            httpClientFactory: new ThrowingHttpClientFactory(),
            runtimeSettings: TestRuntimeSettingsProvider.FromZoomOptions(new ZoomOptions()),
            dataProtectionProvider: DataProtectionProvider.Create("PrivateSpeakingNoShowSweepTests"),
            platformLinks: platformLinks,
            timeProvider: new FixedTimeProvider(Now),
            logger: NullLogger<PrivateSpeakingCalendarService>.Instance);

        // MarkNoShowAsync dispatches no-show notifications, so a real
        // NotificationService is required. No LearnerUser/ExpertUser rows are
        // seeded, so CreateForLearnerAsync/CreateForExpertAsync early-return and
        // the collaborators passed as null! are never exercised.
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
            stripeService: new FakeStripeService(),
            platformLinks: platformLinks,
            timeProvider: new FixedTimeProvider(Now),
            logger: NullLogger<PrivateSpeakingService>.Instance);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // No calendar connection is ever seeded, so this factory must never be called.
    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException("HTTP client should not be used without a calendar connection.");
    }

    // The sweep never refunds, so the Stripe service is never called.
    private sealed class FakeStripeService : IStripeService
    {
        public Task<string> CreateRefundAsync(string paymentIntentId, long? amountCents, string? reason, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string> EnsureCustomerAsync(string userId, string email, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<(string SessionId, string Url)> CreateCheckoutSessionAsync(CreateCheckoutSessionRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<(string SessionId, string Url)> CreateAdHocPaymentCheckoutSessionAsync(
            string stripeCustomerId, string userId, string userEmail, string currency, long amountMinorUnits,
            string productName, string successUrl, string cancelUrl, string? idempotencyKey,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Stripe.Checkout.Session> RetrieveCheckoutSessionAsync(string sessionId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string> CreatePortalSessionAsync(string stripeCustomerId, string returnUrl, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Stripe.Event ConstructWebhookEvent(string requestBody, string signatureHeader, string webhookSecret)
            => throw new NotImplementedException();
        public Task<Stripe.Subscription> RetrieveSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task CancelSubscriptionAsync(string subscriptionId, bool cancelAtPeriodEnd = true, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task UpdateSubscriptionAsync(string subscriptionId, string newPriceId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task UpdateSubscriptionAsync(string subscriptionId, string newPriceId, bool prorate, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task PauseSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task PauseSubscriptionAsync(string subscriptionId, DateTimeOffset? resumeAt, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task ResumeSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task ApplyCouponToSubscriptionAsync(string subscriptionId, string? couponId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IEnumerable<Stripe.Invoice>> ListInvoicesAsync(string stripeCustomerId, int limit = 24, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string?> GetInvoiceSubscriptionIdAsync(string invoiceId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<PayInvoiceResult> PayInvoiceAsync(string stripeInvoiceId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string> CreateCouponAsync(CreateStripeCouponRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string> CreatePromotionCodeAsync(string couponId, string code, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
