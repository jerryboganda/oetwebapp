using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Tests;

/// <summary>
/// T3a — admin/tutor actions required by the PDF (§7.1/§7.3/§11): PUT availability,
/// override-refund, manual-reschedule, edit, mark-no-show, CSV export, plus the
/// ProfessionTrack threaded through booking creation.
/// </summary>
public sealed class PrivateSpeakingAdminActionsTests
{
    // Now = Saturday 2026-06-06 12:00 UTC.
    private static readonly DateTimeOffset Now = new(2026, 06, 06, 12, 0, 0, TimeSpan.Zero);

    // First generated slot of the seeded Monday availability window (>24h lead time).
    private static readonly DateTimeOffset MondaySlotUtc = new(2026, 06, 08, 09, 0, 0, TimeSpan.Zero);

    private const int TutorPriceMinorUnits = 5000;

    // ── UpdateAvailabilityRuleAsync ─────────────────────────────────────

    [Fact]
    public async Task UpdateAvailabilityRule_ChangesTheRule()
    {
        await using var db = CreateDb();
        var stripe = new FakeStripeService();
        var service = CreateService(db, stripe);

        SeedTutorWithMondayAvailability(db);
        await db.SaveChangesAsync();

        var updated = await service.UpdateAvailabilityRuleAsync(
            "psar-1", dayOfWeek: 2, startTime: "10:00", endTime: "14:00",
            effectiveFrom: new DateOnly(2026, 06, 01), effectiveTo: new DateOnly(2026, 12, 31),
            isActive: false, actorId: "admin-1", ct: CancellationToken.None);

        Assert.Equal(2, updated.DayOfWeek);
        Assert.Equal("10:00", updated.StartTime);
        Assert.Equal("14:00", updated.EndTime);
        Assert.False(updated.IsActive);

        var saved = await db.PrivateSpeakingAvailabilityRules.FindAsync("psar-1");
        Assert.Equal(2, saved!.DayOfWeek);
        Assert.Equal("14:00", saved.EndTime);
        Assert.Equal(new DateOnly(2026, 06, 01), saved.EffectiveFrom);
        Assert.False(saved.IsActive);
    }

    [Fact]
    public async Task UpdateAvailabilityRule_Missing_Throws()
    {
        await using var db = CreateDb();
        var service = CreateService(db, new FakeStripeService());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAvailabilityRuleAsync(
                "psar-missing", 1, "09:00", "17:00", null, null, true, "admin-1", CancellationToken.None));
    }

    // ── OverrideRefundAsync ─────────────────────────────────────────────

    [Fact]
    public async Task OverrideRefund_EntitlementBooking_RestoresCreditAndRefundsAndSetsRefunded()
    {
        await using var db = CreateDb();
        var stripe = new FakeStripeService();
        var service = CreateService(db, stripe);

        var subscription = SeedSubscription(db, "sub-1", "learner-1", speakingRemaining: 0);
        // Inside the cancellation window (only 2h out) — override bypasses the window.
        var booking = SeedConfirmedBooking(db, Now.AddHours(2), b =>
        {
            b.EntitlementConsumed = true;
            b.EntitlementSubscriptionId = subscription.Id;
            b.EntitlementConsumedAt = Now.AddDays(-1);
        });
        await db.SaveChangesAsync();

        var (success, error) = await service.OverrideRefundAsync(
            booking.Id, "admin-1", amountMinorUnits: null, reason: "goodwill", CancellationToken.None);

        Assert.True(success, error);
        Assert.Null(error);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.Refunded, saved!.Status);
        Assert.True(saved.RefundIssued);
        Assert.NotNull(saved.EntitlementRestoredAt);

        var savedSub = await db.Subscriptions.FindAsync(subscription.Id);
        Assert.Equal(1, savedSub!.SpeakingSessionsRemaining);

        // Entitlement-only booking → no Stripe money refund.
        Assert.Equal(0, stripe.RefundCallCount);
    }

    [Fact]
    public async Task OverrideRefund_DirectPaidBooking_CallsStripeRefund()
    {
        await using var db = CreateDb();
        var stripe = new FakeStripeService { RefundIdToReturn = "re_override_1" };
        var service = CreateService(db, stripe);

        var booking = SeedConfirmedBooking(db, Now.AddHours(2), b =>
        {
            b.StripePaymentIntentId = "pi_paid_1";
            b.PaymentStatus = PrivateSpeakingPaymentStatus.Succeeded;
            b.PriceMinorUnits = 5000;
        });
        await db.SaveChangesAsync();

        var (success, error) = await service.OverrideRefundAsync(
            booking.Id, "admin-1", amountMinorUnits: 2500, reason: "partial", CancellationToken.None);

        Assert.True(success, error);

        Assert.Equal(1, stripe.RefundCallCount);
        Assert.Equal("pi_paid_1", stripe.LastPaymentIntentId);
        Assert.Equal(2500, stripe.LastAmountCents);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.Refunded, saved!.Status);
        Assert.True(saved.RefundIssued);
        Assert.Equal("re_override_1", saved.StripeRefundId);
        Assert.Equal(2500, saved.RefundAmountMinorUnits);
        Assert.Equal(PrivateSpeakingPaymentStatus.Refunded, saved.PaymentStatus);
    }

    [Fact]
    public async Task OverrideRefund_AlreadyRefunded_IsRejected()
    {
        await using var db = CreateDb();
        var service = CreateService(db, new FakeStripeService());

        var booking = SeedConfirmedBooking(db, Now.AddHours(2), b =>
        {
            b.Status = PrivateSpeakingBookingStatus.Refunded;
        });
        await db.SaveChangesAsync();

        var (success, error) = await service.OverrideRefundAsync(
            booking.Id, "admin-1", null, null, CancellationToken.None);

        Assert.False(success);
        Assert.NotNull(error);
    }

    // ── AdminManualRescheduleAsync ──────────────────────────────────────

    [Fact]
    public async Task AdminManualReschedule_MovesSessionAndResetsZoomForRecreation()
    {
        await using var db = CreateDb();
        var stripe = new FakeStripeService();
        // No ZoomMeetingId is set on the booking, so zoomService is never touched
        // (passed null! by CreateService) — the reset + recreate path runs without
        // an old meeting to delete.
        var service = CreateService(db, stripe);

        var booking = SeedConfirmedBooking(db, Now.AddHours(48), b =>
        {
            b.Status = PrivateSpeakingBookingStatus.ZoomCreated;
            b.ZoomStatus = PrivateSpeakingZoomStatus.Created;
        });
        await db.SaveChangesAsync();

        var newStart = Now.AddHours(72);
        var (success, error) = await service.AdminManualRescheduleAsync(
            booking.Id, "admin-1", newStart, "tutor sick", CancellationToken.None);

        Assert.True(success, error);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        Assert.Equal(newStart, saved!.SessionStartUtc);
        Assert.Equal(PrivateSpeakingBookingStatus.Confirmed, saved.Status);
        Assert.Equal(PrivateSpeakingZoomStatus.Pending, saved.ZoomStatus);
        Assert.Null(saved.ZoomMeetingId);

        // A Zoom-create background job was queued for recreation at the new time.
        var zoomJobQueued = await db.BackgroundJobs.AnyAsync(j =>
            j.Type == JobType.PrivateSpeakingZoomCreate && j.ResourceId == booking.Id);
        Assert.True(zoomJobQueued);
    }

    [Fact]
    public async Task AdminManualReschedule_TerminalState_IsRejected()
    {
        await using var db = CreateDb();
        var service = CreateService(db, new FakeStripeService());

        var booking = SeedConfirmedBooking(db, Now.AddHours(48), b =>
        {
            b.Status = PrivateSpeakingBookingStatus.Completed;
        });
        await db.SaveChangesAsync();

        var (success, error) = await service.AdminManualRescheduleAsync(
            booking.Id, "admin-1", Now.AddHours(72), null, CancellationToken.None);

        Assert.False(success);
        Assert.NotNull(error);
    }

    // ── MarkNoShowAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task MarkNoShow_SetsNoShow_AndDoesNotRestoreEntitlement()
    {
        await using var db = CreateDb();
        var service = CreateService(db, new FakeStripeService());

        var subscription = SeedSubscription(db, "sub-1", "learner-1", speakingRemaining: 0);
        var booking = SeedConfirmedBooking(db, Now.AddHours(-1), b =>
        {
            b.EntitlementConsumed = true;
            b.EntitlementSubscriptionId = subscription.Id;
            b.EntitlementConsumedAt = Now.AddDays(-1);
        });
        await db.SaveChangesAsync();

        var (success, error) = await service.MarkNoShowAsync(
            booking.Id, "expert-1", "expert", CancellationToken.None);

        Assert.True(success, error);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        Assert.Equal(PrivateSpeakingBookingStatus.NoShow, saved!.Status);
        Assert.Null(saved.CompletedAt);
        Assert.Null(saved.EntitlementRestoredAt);

        // No-show forfeits — the credit is NOT returned.
        var savedSub = await db.Subscriptions.FindAsync(subscription.Id);
        Assert.Equal(0, savedSub!.SpeakingSessionsRemaining);
    }

    [Fact]
    public async Task MarkNoShow_FromInvalidState_IsRejected()
    {
        await using var db = CreateDb();
        var service = CreateService(db, new FakeStripeService());

        var booking = SeedConfirmedBooking(db, Now.AddHours(-1), b =>
        {
            b.Status = PrivateSpeakingBookingStatus.Cancelled;
        });
        await db.SaveChangesAsync();

        var (success, error) = await service.MarkNoShowAsync(
            booking.Id, "admin-1", "admin", CancellationToken.None);

        Assert.False(success);
        Assert.NotNull(error);
    }

    // ── ExportBookingsCsvAsync ──────────────────────────────────────────

    [Fact]
    public async Task ExportBookingsCsv_ReturnsHeaderAndRow_AndEscapesCommas()
    {
        await using var db = CreateDb();
        var service = CreateService(db, new FakeStripeService());

        // Tutor display name contains a comma → must be RFC4180-escaped.
        db.PrivateSpeakingTutorProfiles.Add(new PrivateSpeakingTutorProfile
        {
            Id = "tutor-profile-1",
            ExpertUserId = "expert-1",
            DisplayName = "Smith, Jane",
            Timezone = "UTC",
            CreatedAt = Now.AddMonths(-2),
            UpdatedAt = Now.AddMonths(-2)
        });
        SeedConfirmedBooking(db, Now.AddHours(48), b =>
        {
            b.ProfessionTrack = "Nursing";
            b.PriceMinorUnits = 5000;
            b.Currency = "GBP";
        });
        await db.SaveChangesAsync();

        var csv = await service.ExportBookingsCsvAsync(
            tutorProfileId: null, status: null, learnerId: null, from: null, to: null, CancellationToken.None);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length); // header + one row

        Assert.StartsWith("Id,LearnerUserId,TutorProfileId,TutorName,Status,", lines[0]);

        // Comma-containing tutor name is wrapped in quotes.
        Assert.Contains("\"Smith, Jane\"", lines[1]);
        Assert.Contains("Nursing", lines[1]);
    }

    // ── CreateBookingAndCheckoutAsync persists ProfessionTrack ───────────

    [Fact]
    public async Task CreateBookingAndCheckout_PersistsProfessionTrack()
    {
        await using var db = CreateDb();
        var stripe = new FakeStripeService();
        var subscription = SeedSubscription(db, "sub-1", "learner-1", speakingRemaining: 1);
        var resolver = new FakeEntitlementResolver(subscription.Id, speakingRemaining: 1);
        var service = CreateService(db, stripe, resolver);

        SeedTutorWithMondayAvailability(db);
        SeedLearnerUser(db);
        await db.SaveChangesAsync();

        var result = await service.CreateBookingAndCheckoutAsync(
            "learner-1", "tutor-profile-1", MondaySlotUtc, 30, "UTC",
            learnerNotes: null, professionTrack: "Medicine",
            idempotencyKey: NewKey(), sessionFormat: null, ct: CancellationToken.None);

        Assert.True(result.Success, result.Error);

        var saved = await db.PrivateSpeakingBookings.FindAsync(result.BookingId);
        Assert.NotNull(saved);
        Assert.Equal("Medicine", saved!.ProfessionTrack);
    }

    // ── AdminEditBookingAsync (partial update) ──────────────────────────

    [Fact]
    public async Task AdminEditBooking_UpdatesOnlyProvidedFields_DoesNotChangeStatusOrPayment()
    {
        await using var db = CreateDb();
        var service = CreateService(db, new FakeStripeService());

        var originalStart = Now.AddHours(48);
        var booking = SeedConfirmedBooking(db, originalStart, b =>
        {
            b.DurationMinutes = 30;
            b.ProfessionTrack = "Nursing";
            b.PaymentStatus = PrivateSpeakingPaymentStatus.Succeeded;
        });
        await db.SaveChangesAsync();

        var updated = await service.AdminEditBookingAsync(
            booking.Id, "admin-1",
            sessionStartUtc: null, durationMinutes: null,
            professionTrack: "Medicine", tutorNotes: "bring case notes",
            CancellationToken.None);

        Assert.NotNull(updated);

        var saved = await db.PrivateSpeakingBookings.FindAsync(booking.Id);
        // Provided fields changed:
        Assert.Equal("Medicine", saved!.ProfessionTrack);
        Assert.Equal("bring case notes", saved.TutorNotes);
        // Omitted fields untouched (incl. Status + payment):
        Assert.Equal(originalStart, saved.SessionStartUtc);
        Assert.Equal(30, saved.DurationMinutes);
        Assert.Equal(PrivateSpeakingBookingStatus.Confirmed, saved.Status);
        Assert.Equal(PrivateSpeakingPaymentStatus.Succeeded, saved.PaymentStatus);

        // Audit row written.
        var audited = await db.PrivateSpeakingAuditLogs.AnyAsync(a =>
            a.BookingId == booking.Id && a.Action == "admin_booking_edited");
        Assert.True(audited);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static string NewKey() => Guid.NewGuid().ToString();

    private static void SeedTutorWithMondayAvailability(LearnerDbContext db)
    {
        if (!db.PrivateSpeakingTutorProfiles.Local.Any(p => p.Id == "tutor-profile-1"))
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
        }

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
                PriceOverrideMinorUnits = TutorPriceMinorUnits,
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

    private static PrivateSpeakingService CreateService(
        LearnerDbContext db, IStripeService stripe, IEffectiveEntitlementResolver? resolver = null)
    {
        var platformLinks = new PlatformLinkService(
            TestRuntimeSettingsProvider.FromPlatformOptions(new PlatformOptions()),
            Options.Create(new BillingOptions()));

        // A real calendar service is constructed, but with no calendar connection
        // seeded CheckBusyAsync short-circuits to "not connected" and never touches
        // the HTTP client.
        var calendarService = new PrivateSpeakingCalendarService(
            db,
            httpClientFactory: new ThrowingHttpClientFactory(),
            runtimeSettings: TestRuntimeSettingsProvider.FromZoomOptions(new ZoomOptions()),
            dataProtectionProvider: DataProtectionProvider.Create("PrivateSpeakingAdminActionsTests"),
            platformLinks: platformLinks,
            timeProvider: new FixedTimeProvider(Now),
            logger: NullLogger<PrivateSpeakingCalendarService>.Instance);

        // MarkNoShowAsync now dispatches no-show notifications, so a real
        // NotificationService is required. No LearnerUser/ExpertUser rows are seeded,
        // so CreateForLearnerAsync/CreateForExpertAsync early-return and the
        // collaborators passed as null! are never exercised.
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
            entitlementResolver: resolver!,
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

    // No calendar connection is ever seeded, so this factory must never be called.
    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException("HTTP client should not be used without a calendar connection.");
    }

    private sealed class FakeEntitlementResolver(string subscriptionId, int speakingRemaining)
        : IEffectiveEntitlementResolver
    {
        public Task<EffectiveEntitlementSnapshot> ResolveAsync(string? userId, CancellationToken ct)
        {
            var snapshot = new EffectiveEntitlementSnapshot(
                UserId: userId,
                HasEligibleSubscription: true,
                IsTrial: false,
                Tier: "premium",
                SubscriptionId: subscriptionId,
                SubscriptionStatus: SubscriptionStatus.Active,
                PlanId: "plan-1",
                PlanVersionId: null,
                PlanCode: null,
                AiQuotaPlanCode: null,
                AiQuotaPlanCodeSource: null,
                ActiveAddOnCodes: Array.Empty<string>(),
                IsFrozen: false,
                Trace: Array.Empty<string>())
            {
                SpeakingAddonsEnabled = true,
                SpeakingSessionsRemaining = speakingRemaining
            };
            return Task.FromResult(snapshot);
        }
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

        // ── Unused members ──────────────────────────────────────────────
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
