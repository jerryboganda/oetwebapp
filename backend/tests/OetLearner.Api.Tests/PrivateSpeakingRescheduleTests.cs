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
/// T2b — PDF §2.3/§9 reschedule tiers:
///   • >24h before, OR &lt;24h but a different calendar day → FREE (entitlement carries).
///   • same calendar day (learner tz) before start → 50% Stripe penalty.
///   • after start → rejected.
/// Plus the Stripe webhook hooks that finalize / abort a same-day reschedule.
/// </summary>
public sealed class PrivateSpeakingRescheduleTests
{
    // Now = Saturday 2026-06-06 12:00 UTC.
    private static readonly DateTimeOffset Now = new(2026, 06, 06, 12, 0, 0, TimeSpan.Zero);

    // New slot the learner reschedules INTO: Monday 2026-06-08 09:00 UTC (first
    // generated slot of the seeded Monday availability window; >24h lead time).
    private static readonly DateTimeOffset NewSlotUtc = new(2026, 06, 08, 09, 0, 0, TimeSpan.Zero);

    private const int TutorPriceMinorUnits = 5000;
    private const int ExpectedPenaltyMinorUnits = 2500; // ceil(5000 * 50 / 100)

    // ── FREE tier ───────────────────────────────────────────────────────

    [Fact]
    public async Task Reschedule_MoreThan24hBefore_DifferentDay_IsFreeAndConfirmed()
    {
        await using var db = CreateDb();
        var stripe = new FakeStripeService();
        var service = CreateService(db, stripe);

        SeedTutorWithMondayAvailability(db);
        SeedSubscription(db, "sub-1", "learner-1", speakingRemaining: 0);
        SeedLearnerUser(db);
        // Original is Tuesday 2026-06-09 12:00 UTC → 72h out, different day.
        var original = SeedConfirmedBooking(db, Now.AddHours(72));
        await db.SaveChangesAsync();

        var result = await service.RescheduleBookingAsync(
            original.Id, "learner-1", NewSlotUtc, "UTC", null, NewKey(), CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Null(result.CheckoutUrl);

        var replacement = await db.PrivateSpeakingBookings.FindAsync(result.BookingId);
        Assert.NotNull(replacement);
        Assert.Equal(PrivateSpeakingBookingStatus.Confirmed, replacement!.Status);
        Assert.Equal(original.Id, replacement.RescheduledFromBookingId);

        var savedOriginal = await db.PrivateSpeakingBookings.FindAsync(original.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.Cancelled, savedOriginal!.Status);
        Assert.Equal(replacement.Id, savedOriginal.RescheduledToBookingId);

        Assert.Equal(0, stripe.AdHocCallCount);
    }

    [Fact]
    public async Task Reschedule_LessThan24hBefore_DifferentDay_IsFreeAndConfirmed()
    {
        await using var db = CreateDb();
        var stripe = new FakeStripeService();
        var service = CreateService(db, stripe);

        SeedTutorWithMondayAvailability(db);
        SeedSubscription(db, "sub-1", "learner-1", speakingRemaining: 0);
        SeedLearnerUser(db);
        // Original is Sunday 2026-06-07 08:00 UTC → 20h out (<24h) but a DIFFERENT
        // calendar day than Now (06-06) in UTC → still FREE.
        var original = SeedConfirmedBooking(db, new DateTimeOffset(2026, 06, 07, 08, 0, 0, TimeSpan.Zero));
        await db.SaveChangesAsync();

        var result = await service.RescheduleBookingAsync(
            original.Id, "learner-1", NewSlotUtc, "UTC", null, NewKey(), CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Null(result.CheckoutUrl);

        var replacement = await db.PrivateSpeakingBookings.FindAsync(result.BookingId);
        Assert.Equal(PrivateSpeakingBookingStatus.Confirmed, replacement!.Status);

        var savedOriginal = await db.PrivateSpeakingBookings.FindAsync(original.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.Cancelled, savedOriginal!.Status);
        Assert.Equal(replacement.Id, savedOriginal.RescheduledToBookingId);

        Assert.Equal(0, stripe.AdHocCallCount);
    }

    // ── SAME-DAY penalty tier ───────────────────────────────────────────

    [Fact]
    public async Task Reschedule_SameCalendarDay_RequiresPenaltyCheckout()
    {
        await using var db = CreateDb();
        var stripe = new FakeStripeService();
        var service = CreateService(db, stripe);

        SeedTutorWithMondayAvailability(db);
        var subscription = SeedSubscription(db, "sub-1", "learner-1", speakingRemaining: 0);
        SeedLearnerUser(db);
        // Original is Saturday 2026-06-06 14:00 UTC → +2h, SAME calendar day as Now.
        var original = SeedConfirmedBooking(db, Now.AddHours(2));
        await db.SaveChangesAsync();

        var result = await service.RescheduleBookingAsync(
            original.Id, "learner-1", NewSlotUtc, "UTC", null, NewKey(), CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal("https://stripe.test/x", result.CheckoutUrl);
        Assert.Equal("cs_test", result.CheckoutSessionId);

        var replacement = await db.PrivateSpeakingBookings.FindAsync(result.BookingId);
        Assert.NotNull(replacement);
        Assert.Equal(PrivateSpeakingBookingStatus.PendingPayment, replacement!.Status);
        Assert.Equal(ExpectedPenaltyMinorUnits, replacement.PenaltyAmountMinorUnits);
        Assert.Equal(original.Id, replacement.RescheduledFromBookingId);
        Assert.Equal("cs_test", replacement.StripeCheckoutSessionId);

        // Original still holds the slot (Confirmed) but is linked to the replacement.
        var savedOriginal = await db.PrivateSpeakingBookings.FindAsync(original.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.Confirmed, savedOriginal!.Status);
        Assert.Equal(replacement.Id, savedOriginal.RescheduledToBookingId);

        Assert.Equal(1, stripe.EnsureCustomerCallCount);
        Assert.Equal(1, stripe.AdHocCallCount);
        Assert.Equal(ExpectedPenaltyMinorUnits, stripe.LastAdHocAmount);

        // Entitlement carried over, NOT double-consumed.
        var savedSub = await db.Subscriptions.FindAsync(subscription.Id);
        Assert.Equal(0, savedSub!.SpeakingSessionsRemaining);
    }

    // ── After start → rejected ──────────────────────────────────────────

    [Fact]
    public async Task Reschedule_AfterStart_Fails()
    {
        await using var db = CreateDb();
        var stripe = new FakeStripeService();
        var service = CreateService(db, stripe);

        SeedTutorWithMondayAvailability(db);
        SeedSubscription(db, "sub-1", "learner-1", speakingRemaining: 0);
        SeedLearnerUser(db);
        var original = SeedConfirmedBooking(db, Now.AddMinutes(-5));
        await db.SaveChangesAsync();

        var result = await service.RescheduleBookingAsync(
            original.Id, "learner-1", NewSlotUtc, "UTC", null, NewKey(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("already started", result.Error!);

        var savedOriginal = await db.PrivateSpeakingBookings.FindAsync(original.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.Confirmed, savedOriginal!.Status);
        Assert.Equal(0, stripe.AdHocCallCount);
    }

    // ── Webhook: penalty paid → finalize reschedule ─────────────────────

    [Fact]
    public async Task ConfirmBookingPayment_PenaltyReplacement_CancelsOriginal()
    {
        await using var db = CreateDb();
        var stripe = new FakeStripeService();
        var service = CreateService(db, stripe);

        SeedTutorWithMondayAvailability(db);
        SeedSubscription(db, "sub-1", "learner-1", speakingRemaining: 0);
        SeedLearnerUser(db);
        var original = SeedConfirmedBooking(db, Now.AddHours(2));
        await db.SaveChangesAsync();

        var result = await service.RescheduleBookingAsync(
            original.Id, "learner-1", NewSlotUtc, "UTC", null, NewKey(), CancellationToken.None);
        Assert.True(result.Success, result.Error);

        var replacement = await db.PrivateSpeakingBookings.FindAsync(result.BookingId);

        var confirmed = await service.ConfirmBookingPaymentAsync(
            replacement!.StripeCheckoutSessionId!, "pi_x", CancellationToken.None);

        Assert.True(confirmed);

        var savedReplacement = await db.PrivateSpeakingBookings.FindAsync(replacement.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.Confirmed, savedReplacement!.Status);
        Assert.Equal(PrivateSpeakingPaymentStatus.Succeeded, savedReplacement.PaymentStatus);

        var savedOriginal = await db.PrivateSpeakingBookings.FindAsync(original.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.Cancelled, savedOriginal!.Status);
        Assert.Equal("rescheduled", savedOriginal.CancellationReason);
    }

    // ── Webhook: checkout expired → abort reschedule ────────────────────

    [Fact]
    public async Task CheckoutExpired_PenaltyReplacement_RevertsReschedule()
    {
        await using var db = CreateDb();
        var stripe = new FakeStripeService();
        var service = CreateService(db, stripe);

        SeedTutorWithMondayAvailability(db);
        SeedSubscription(db, "sub-1", "learner-1", speakingRemaining: 0);
        SeedLearnerUser(db);
        var original = SeedConfirmedBooking(db, Now.AddHours(2));
        await db.SaveChangesAsync();

        var result = await service.RescheduleBookingAsync(
            original.Id, "learner-1", NewSlotUtc, "UTC", null, NewKey(), CancellationToken.None);
        Assert.True(result.Success, result.Error);

        var replacement = await db.PrivateSpeakingBookings.FindAsync(result.BookingId);

        await service.HandleCheckoutExpiredAsync(replacement!.StripeCheckoutSessionId!, CancellationToken.None);

        var savedReplacement = await db.PrivateSpeakingBookings.FindAsync(replacement.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.Expired, savedReplacement!.Status);

        // Original is restored to a standalone Confirmed booking (slot kept).
        var savedOriginal = await db.PrivateSpeakingBookings.FindAsync(original.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.Confirmed, savedOriginal!.Status);
        Assert.Null(savedOriginal.RescheduledToBookingId);
    }

    // ── Background sweep safety net: expired penalty reservation reverts too ──

    [Fact]
    public async Task ExpireStaleReservations_PenaltyReplacement_RevertsAndNeutralizesEntitlement()
    {
        await using var db = CreateDb();
        var stripe = new FakeStripeService();
        var service = CreateService(db, stripe);

        SeedTutorWithMondayAvailability(db);
        SeedSubscription(db, "sub-1", "learner-1", speakingRemaining: 0);
        SeedLearnerUser(db);
        var original = SeedConfirmedBooking(db, Now.AddHours(2)); // same-day → penalty path
        await db.SaveChangesAsync();

        var result = await service.RescheduleBookingAsync(
            original.Id, "learner-1", NewSlotUtc, "UTC", null, NewKey(), CancellationToken.None);
        Assert.True(result.Success, result.Error);

        // Simulate the Stripe webhook never arriving: force the reservation stale so the
        // background sweep (not the webhook) is what expires the penalty replacement.
        var replacement = await db.PrivateSpeakingBookings.FindAsync(result.BookingId);
        replacement!.ReservationExpiresAt = Now.AddMinutes(-1);
        await db.SaveChangesAsync();

        await service.ExpireStaleReservationsAsync(CancellationToken.None);

        // Replacement expired AND its inherited entitlement neutralized (no phantom credit).
        var savedReplacement = await db.PrivateSpeakingBookings.FindAsync(replacement.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.Expired, savedReplacement!.Status);
        Assert.False(savedReplacement.EntitlementConsumed);
        Assert.Null(savedReplacement.EntitlementSubscriptionId);

        // Original restored to a standalone Confirmed booking (link cleared) — not stranded.
        var savedOriginal = await db.PrivateSpeakingBookings.FindAsync(original.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.Confirmed, savedOriginal!.Status);
        Assert.Null(savedOriginal.RescheduledToBookingId);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static string NewKey() => Guid.NewGuid().ToString();

    private static void SeedTutorWithMondayAvailability(LearnerDbContext db)
    {
        db.PrivateSpeakingTutorProfiles.Add(new PrivateSpeakingTutorProfile
        {
            Id = "tutor-profile-1",
            ExpertUserId = "expert-1",
            DisplayName = "Tutor",
            Timezone = "UTC",
            PriceOverrideMinorUnits = TutorPriceMinorUnits,
            IsActive = true,
            CreatedAt = Now.AddMonths(-2),
            UpdatedAt = Now.AddMonths(-2)
        });

        // Monday (DayOfWeek=1) 09:00-17:00 → first generated slot is 09:00 UTC.
        db.PrivateSpeakingAvailabilityRules.Add(new PrivateSpeakingAvailabilityRule
        {
            Id = "psar-1",
            TutorProfileId = "tutor-profile-1",
            DayOfWeek = 1,
            StartTime = "09:00",
            EndTime = "17:00",
            IsActive = true
        });
    }

    private static Subscription SeedSubscription(
        LearnerDbContext db, string id, string userId, int speakingRemaining)
    {
        var subscription = new Subscription
        {
            Id = id,
            UserId = userId,
            PlanId = "plan-1",
            Status = SubscriptionStatus.Active,
            StartedAt = Now.AddMonths(-1),
            ChangedAt = Now.AddMonths(-1),
            NextRenewalAt = Now.AddMonths(1),
            SpeakingSessionsRemaining = speakingRemaining
        };
        db.Subscriptions.Add(subscription);
        return subscription;
    }

    private static void SeedLearnerUser(LearnerDbContext db)
    {
        db.Users.Add(new LearnerUser
        {
            Id = "learner-1",
            DisplayName = "Learner One",
            Email = "learner1@example.test",
            Timezone = "UTC"
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
            EntitlementSubscriptionId = "sub-1",
            EntitlementConsumed = true,
            EntitlementConsumedAt = Now.AddDays(-1),
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

    private static PrivateSpeakingService CreateService(LearnerDbContext db, IStripeService stripe)
    {
        var platformLinks = new PlatformLinkService(
            TestRuntimeSettingsProvider.FromPlatformOptions(new PlatformOptions()),
            Options.Create(new BillingOptions()));

        // A real calendar service is constructed, but with no calendar connection
        // seeded CheckBusyAsync short-circuits to "not connected" and never touches
        // the HTTP client — so the reschedule slot-availability path runs cleanly.
        var calendarService = new PrivateSpeakingCalendarService(
            db,
            httpClientFactory: new ThrowingHttpClientFactory(),
            runtimeSettings: TestRuntimeSettingsProvider.FromZoomOptions(new ZoomOptions()),
            dataProtectionProvider: DataProtectionProvider.Create("PrivateSpeakingRescheduleTests"),
            platformLinks: platformLinks,
            timeProvider: new FixedTimeProvider(Now),
            logger: NullLogger<PrivateSpeakingCalendarService>.Instance);

        // Real NotificationService (collaborators null!) so the inline reschedule-
        // confirmation notification has a non-null service to call. The seeded learner
        // has no AuthAccountId and no ExpertUser row exists, so CreateFor{Learner,Expert}Async
        // early-return before touching the null! collaborators. Mirrors PrivateSpeakingCancellationTests.
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
            notificationService: notificationService,
            zoomService: null!,
            calendarService: calendarService,
            entitlementResolver: null!,
            stripeService: stripe,
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

    private sealed class FakeStripeService : IStripeService
    {
        public int EnsureCustomerCallCount { get; private set; }
        public int AdHocCallCount { get; private set; }
        public long? LastAdHocAmount { get; private set; }
        public string? LastAdHocCurrency { get; private set; }
        public IReadOnlyDictionary<string, string>? LastAdHocMetadata { get; private set; }

        public Task<string> EnsureCustomerAsync(string userId, string email, CancellationToken ct = default)
        {
            EnsureCustomerCallCount++;
            return Task.FromResult("cus_test");
        }

        public Task<(string SessionId, string Url)> CreateAdHocPaymentCheckoutSessionAsync(
            string stripeCustomerId, string userId, string userEmail, string currency, long amountMinorUnits,
            string productName, string successUrl, string cancelUrl, string? idempotencyKey,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
        {
            AdHocCallCount++;
            LastAdHocAmount = amountMinorUnits;
            LastAdHocCurrency = currency;
            LastAdHocMetadata = metadata;
            return Task.FromResult(("cs_test", "https://stripe.test/x"));
        }

        // ── Unused members ──────────────────────────────────────────────
        public Task<(string SessionId, string Url)> CreateCheckoutSessionAsync(CreateCheckoutSessionRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Stripe.Checkout.Session> RetrieveCheckoutSessionAsync(string sessionId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string> CreatePortalSessionAsync(string stripeCustomerId, string returnUrl, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string> CreateRefundAsync(string paymentIntentId, long? amountCents, string? reason, CancellationToken ct = default)
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
