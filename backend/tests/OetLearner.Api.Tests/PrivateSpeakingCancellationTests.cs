using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests;

public sealed class PrivateSpeakingCancellationTests
{
    private static readonly DateTimeOffset Now = new(2026, 06, 06, 12, 0, 0, TimeSpan.Zero);

    // ── Learner cancellation refund tiers ───────────────────────────────

    [Fact]
    public async Task LearnerCancel_MoreThan48hBefore_EntitlementBased_RefundsAndRestores()
    {
        await using var db = CreateDb();
        var stripe = new FakeStripeService();
        var service = CreateService(db, stripe);

        var subscription = SeedSubscription(db, "sub-1", "learner-1", speakingRemaining: 0);
        var booking = SeedBooking(db, sessionStartUtc: Now.AddHours(72), b =>
        {
            b.EntitlementConsumed = true;
            b.EntitlementSubscriptionId = subscription.Id;
            b.EntitlementConsumedAt = Now.AddDays(-1);
        });
        await db.SaveChangesAsync();

        var (success, error) = await service.CancelBookingAsync(
            booking.Id, "learner-1", "learner", "changed my mind", CancellationToken.None);

        Assert.True(success);
        Assert.Null(error);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.Cancelled, saved!.Status);
        Assert.True(saved.RefundIssued);
        Assert.NotNull(saved.EntitlementRestoredAt);

        var savedSub = await db.Subscriptions.FindAsync(subscription.Id);
        Assert.Equal(1, savedSub!.SpeakingSessionsRemaining);

        Assert.Equal(0, stripe.RefundCallCount);
    }

    [Fact]
    public async Task LearnerCancel_LessThan48hBefore_NoRefund_NoRestore()
    {
        await using var db = CreateDb();
        var stripe = new FakeStripeService();
        var service = CreateService(db, stripe);

        var subscription = SeedSubscription(db, "sub-1", "learner-1", speakingRemaining: 0);
        var booking = SeedBooking(db, sessionStartUtc: Now.AddHours(24), b =>
        {
            b.EntitlementConsumed = true;
            b.EntitlementSubscriptionId = subscription.Id;
            b.EntitlementConsumedAt = Now.AddDays(-1);
        });
        await db.SaveChangesAsync();

        var (success, error) = await service.CancelBookingAsync(
            booking.Id, "learner-1", "learner", null, CancellationToken.None);

        Assert.True(success);
        Assert.Null(error);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.Cancelled, saved!.Status);
        Assert.False(saved.RefundIssued);
        Assert.Null(saved.EntitlementRestoredAt);

        var savedSub = await db.Subscriptions.FindAsync(subscription.Id);
        Assert.Equal(0, savedSub!.SpeakingSessionsRemaining);

        Assert.Equal(0, stripe.RefundCallCount);
    }

    [Fact]
    public async Task LearnerCancel_AfterStart_Fails()
    {
        await using var db = CreateDb();
        var stripe = new FakeStripeService();
        var service = CreateService(db, stripe);

        var booking = SeedBooking(db, sessionStartUtc: Now.AddMinutes(-5));
        await db.SaveChangesAsync();

        var (success, error) = await service.CancelBookingAsync(
            booking.Id, "learner-1", "learner", null, CancellationToken.None);

        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("already started", error!);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        Assert.NotEqual(PrivateSpeakingBookingStatus.Cancelled, saved!.Status);
    }

    // ── Tutor / admin cancellation (edge case #7: always full refund) ───

    [Fact]
    public async Task ExpertCancel_LessThan48hBefore_StillFullRefundAndRestore()
    {
        await using var db = CreateDb();
        var stripe = new FakeStripeService();
        var service = CreateService(db, stripe);

        var subscription = SeedSubscription(db, "sub-1", "learner-1", speakingRemaining: 0);
        var booking = SeedBooking(db, sessionStartUtc: Now.AddHours(2), b =>
        {
            b.EntitlementConsumed = true;
            b.EntitlementSubscriptionId = subscription.Id;
            b.EntitlementConsumedAt = Now.AddDays(-1);
        });
        await db.SaveChangesAsync();

        var (success, error) = await service.CancelBookingAsync(
            booking.Id, "expert-9", "expert", "tutor unavailable", CancellationToken.None);

        Assert.True(success);
        Assert.Null(error);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.Cancelled, saved!.Status);
        Assert.True(saved.RefundIssued);
        Assert.NotNull(saved.EntitlementRestoredAt);

        var savedSub = await db.Subscriptions.FindAsync(subscription.Id);
        Assert.Equal(1, savedSub!.SpeakingSessionsRemaining);
    }

    // ── Direct-paid (Stripe) refund path ────────────────────────────────

    [Fact]
    public async Task LearnerCancel_DirectPaid_MoreThan48h_InvokesStripeRefund()
    {
        await using var db = CreateDb();
        var stripe = new FakeStripeService { RefundIdToReturn = "re_test_123" };
        var service = CreateService(db, stripe);

        var booking = SeedBooking(db, sessionStartUtc: Now.AddHours(72), b =>
        {
            b.StripePaymentIntentId = "pi_test_abc";
            b.PaymentStatus = PrivateSpeakingPaymentStatus.Succeeded;
            b.PriceMinorUnits = 5000;
        });
        await db.SaveChangesAsync();

        var (success, error) = await service.CancelBookingAsync(
            booking.Id, "learner-1", "learner", null, CancellationToken.None);

        Assert.True(success);
        Assert.Null(error);

        Assert.Equal(1, stripe.RefundCallCount);
        Assert.Equal("pi_test_abc", stripe.LastPaymentIntentId);
        Assert.Equal(5000, stripe.LastAmountCents);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        Assert.True(saved!.RefundIssued);
        Assert.Equal("re_test_123", saved.StripeRefundId);
        Assert.Equal(5000, saved.RefundAmountMinorUnits);
        Assert.Equal(PrivateSpeakingPaymentStatus.Refunded, saved.PaymentStatus);
    }

    // ── PDF status mapper ───────────────────────────────────────────────

    [Fact]
    public void StatusMapper_ProducesPdfStrings()
    {
        Assert.Equal("Confirmed", SpeakingSessionStatusMapper.Map(
            new PrivateSpeakingBooking { Status = PrivateSpeakingBookingStatus.Confirmed }));

        Assert.Equal("CancelledWithRefund", SpeakingSessionStatusMapper.Map(
            new PrivateSpeakingBooking { Status = PrivateSpeakingBookingStatus.Cancelled, RefundIssued = true }));

        Assert.Equal("CancelledWithoutRefund", SpeakingSessionStatusMapper.Map(
            new PrivateSpeakingBooking { Status = PrivateSpeakingBookingStatus.Cancelled, RefundIssued = false }));

        Assert.Equal("Rescheduled", SpeakingSessionStatusMapper.Map(
            new PrivateSpeakingBooking
            {
                Status = PrivateSpeakingBookingStatus.Cancelled,
                RescheduledToBookingId = "booking-next",
                RefundIssued = false
            }));

        Assert.Equal("Rescheduled", SpeakingSessionStatusMapper.Map(
            new PrivateSpeakingBooking
            {
                Status = PrivateSpeakingBookingStatus.Confirmed,
                RescheduledToBookingId = "booking-next"
            }));

        Assert.Equal("NoShow", SpeakingSessionStatusMapper.Map(
            new PrivateSpeakingBooking { Status = PrivateSpeakingBookingStatus.NoShow }));
    }

    // ── Helpers ─────────────────────────────────────────────────────────

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

    private static PrivateSpeakingBooking SeedBooking(
        LearnerDbContext db,
        DateTimeOffset sessionStartUtc,
        Action<PrivateSpeakingBooking>? configure = null)
    {
        // Seed the required principal so the `.Include(b => b.TutorProfile)` in
        // CancelBookingAsync resolves under the EF InMemory provider (which drops a
        // dependent from an Include when its required principal is absent).
        // ExpertUserId is set but no ExpertUser row is seeded, so the expert
        // notification safely early-returns.
        if (!db.PrivateSpeakingTutorProfiles.Local.Any(p => p.Id == "tutor-profile-1"))
        {
            db.PrivateSpeakingTutorProfiles.Add(new PrivateSpeakingTutorProfile
            {
                Id = "tutor-profile-1",
                ExpertUserId = "expert-1",
                DisplayName = "Tutor",
                Timezone = "UTC",
                CreatedAt = Now.AddMonths(-2),
                UpdatedAt = Now.AddMonths(-2)
            });
        }

        var booking = new PrivateSpeakingBooking
        {
            Id = "booking-1",
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

    private static PrivateSpeakingService CreateService(LearnerDbContext db, IStripeService stripe)
    {
        // We deliberately do NOT seed LearnerUser/ExpertUser rows: the cancel
        // notifications early-return when the user row is absent, so the
        // notification collaborators are never exercised. zoomService /
        // calendarService / entitlementResolver are not invoked on the cancel
        // path (no ZoomMeetingId is set) and can be passed as null.
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

        var platformLinks = new PlatformLinkService(
            TestRuntimeSettingsProvider.FromPlatformOptions(new PlatformOptions()),
            Options.Create(new BillingOptions()));

        return new PrivateSpeakingService(
            db,
            notificationService,
            zoomService: null!,
            calendarService: null!,
            entitlementResolver: null!,
            stripeService: stripe,
            paymentGateways: null!,
            platformLinks: platformLinks,
            timeProvider: new FixedTimeProvider(Now),
            logger: NullLogger<PrivateSpeakingService>.Instance);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeStripeService : IStripeService
    {
        public int RefundCallCount { get; private set; }
        public string? LastPaymentIntentId { get; private set; }
        public long? LastAmountCents { get; private set; }
        public string? LastReason { get; private set; }
        public string RefundIdToReturn { get; set; } = "re_fake";

        public Task<string> CreateRefundAsync(string paymentIntentId, long? amountCents, string? reason, CancellationToken ct = default)
        {
            RefundCallCount++;
            LastPaymentIntentId = paymentIntentId;
            LastAmountCents = amountCents;
            LastReason = reason;
            return Task.FromResult(RefundIdToReturn);
        }

        // ── Unused members (cancellation path only invokes CreateRefundAsync) ──
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
