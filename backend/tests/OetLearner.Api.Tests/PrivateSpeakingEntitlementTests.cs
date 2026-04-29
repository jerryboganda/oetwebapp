using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Tests;

/// <summary>
/// Verifies that <see cref="PrivateSpeakingService"/> consults
/// <see cref="ILearnerEntitlementResolver"/> before always creating a Stripe
/// checkout session. Sponsor-covered learners book without payment; paid
/// learners without coverage still go through Stripe; the price is always
/// server-authoritative — never client-supplied.
/// </summary>
public sealed class PrivateSpeakingEntitlementTests
{
    private const string LearnerId = "learner-1";
    private const string TutorProfileId = "pstp-1";
    private const string ExpertId = "expert-1";
    private const int ServerPriceMinorUnits = 7777; // distinct from defaults so we can detect overrides

    [Fact]
    public async Task SponsorCoveredLearner_BypassesStripeAndConfirmsBooking()
    {
        var (db, service) = await BuildAsync();
        db.Sponsorships.Add(new Sponsorship
        {
            Id = Guid.NewGuid(),
            SponsorUserId = "sponsor-user",
            LearnerUserId = LearnerId,
            LearnerEmail = "learner@example.test",
            Status = "Active",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var sessionStart = DateTimeOffset.UtcNow.AddDays(3);
        var result = await service.CreateBookingAndCheckoutAsync(
            LearnerId, TutorProfileId, sessionStart, 30, "UTC", null,
            idempotencyKey: $"idem-{Guid.NewGuid():N}", default);

        Assert.True(result.Success);
        Assert.NotNull(result.BookingId);
        Assert.Null(result.CheckoutSessionId);
        Assert.Null(result.CheckoutUrl);

        var booking = await db.PrivateSpeakingBookings.SingleAsync(b => b.Id == result.BookingId);
        Assert.Equal(PrivateSpeakingBookingStatus.Confirmed, booking.Status);
        Assert.Equal(PrivateSpeakingPaymentStatus.Succeeded, booking.PaymentStatus);
        Assert.Null(booking.StripeCheckoutSessionId);
        Assert.Equal(ServerPriceMinorUnits, booking.PriceMinorUnits);

        // Audit must record coverage reason for traceability
        var coverageAudit = db.PrivateSpeakingAuditLogs
            .Single(a => a.BookingId == booking.Id && a.Action == "booking_covered_by_entitlement");
        Assert.Contains("sponsor_seat", coverageAudit.Details ?? "");

        await db.DisposeAsync();
    }

    [Fact]
    public async Task PaidLearnerWithoutCoverage_StillGoesThroughStripe()
    {
        var (db, service) = await BuildAsync();
        // Active subscription is NOT a coverage source for private speaking
        // (1:1 tutor sessions are an upsell, not part of content access).
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-paid",
            UserId = LearnerId,
            PlanId = "premium-monthly",
            Status = SubscriptionStatus.Active,
            StartedAt = DateTimeOffset.UtcNow.AddDays(-10),
            ChangedAt = DateTimeOffset.UtcNow.AddDays(-10),
            NextRenewalAt = DateTimeOffset.UtcNow.AddDays(20)
        });
        await db.SaveChangesAsync();

        var sessionStart = DateTimeOffset.UtcNow.AddDays(3);
        var result = await service.CreateBookingAndCheckoutAsync(
            LearnerId, TutorProfileId, sessionStart, 30, "UTC", null,
            idempotencyKey: $"idem-{Guid.NewGuid():N}", default);

        Assert.True(result.Success);
        Assert.NotNull(result.CheckoutSessionId);
        Assert.NotNull(result.CheckoutUrl);

        var booking = await db.PrivateSpeakingBookings.SingleAsync(b => b.Id == result.BookingId);
        Assert.Equal(PrivateSpeakingBookingStatus.PendingPayment, booking.Status);
        Assert.Equal(PrivateSpeakingPaymentStatus.Pending, booking.PaymentStatus);
        Assert.NotNull(booking.StripeCheckoutSessionId);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task BookingPrice_IsServerAuthoritative_NotClientSupplied()
    {
        var (db, service) = await BuildAsync();
        await db.SaveChangesAsync();

        var sessionStart = DateTimeOffset.UtcNow.AddDays(3);
        // The booking API does NOT accept a price parameter — this test is a
        // structural regression guard: the booking is persisted with the
        // server-derived price even though no price was sent.
        var result = await service.CreateBookingAndCheckoutAsync(
            LearnerId, TutorProfileId, sessionStart, 30, "UTC", null,
            idempotencyKey: $"idem-{Guid.NewGuid():N}", default);

        Assert.True(result.Success);
        var booking = await db.PrivateSpeakingBookings.SingleAsync(b => b.Id == result.BookingId);
        Assert.Equal(ServerPriceMinorUnits, booking.PriceMinorUnits);
        Assert.Equal("AUD", booking.Currency);

        await db.DisposeAsync();
    }

    private static async Task<(LearnerDbContext, PrivateSpeakingService)> BuildAsync()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);

        // Seed config + tutor profile + expert
        db.PrivateSpeakingConfigs.Add(new PrivateSpeakingConfig
        {
            Id = "ps-config-singleton",
            IsEnabled = true,
            DefaultSlotDurationMinutes = 30,
            MinBookingLeadTimeHours = 24,
            MaxBookingAdvanceDays = 30,
            ReservationTimeoutMinutes = 15,
            DefaultPriceMinorUnits = 5000,
            Currency = "AUD",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.ExpertUsers.Add(new ExpertUser
        {
            Id = ExpertId,
            DisplayName = "Test Tutor",
            Email = "tutor@example.test",
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.PrivateSpeakingTutorProfiles.Add(new PrivateSpeakingTutorProfile
        {
            Id = TutorProfileId,
            ExpertUserId = ExpertId,
            DisplayName = "Test Tutor",
            Timezone = "UTC",
            IsActive = true,
            PriceOverrideMinorUnits = ServerPriceMinorUnits,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var billing = Options.Create(new BillingOptions { AllowSandboxFallbacks = true });
        var stripe = new StripeGateway(new HttpClient(), billing);
        var paypal = new PayPalGateway(new HttpClient(), billing);
        var paymentGateways = new PaymentGatewayService(stripe, paypal);

        var service = new PrivateSpeakingService(
            db,
            paymentGateways,
            notificationService: null!, // unused by booking path
            zoomService: null!,         // unused by booking path
            timeProvider: TimeProvider.System,
            billingOptions: billing,
            logger: NullLogger<PrivateSpeakingService>.Instance,
            entitlementResolver: new LearnerEntitlementResolver(db));
        return (db, service);
    }
}
