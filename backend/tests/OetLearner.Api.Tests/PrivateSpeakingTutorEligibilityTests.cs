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
/// FINAL 2026-09-06: "Book a tutor as your patient" is entitlement-gated.
/// Only holders of an eligible main course/package (plan with
/// SpeakingAddonsEnabled) or the Speaking Crash Course may book — enforced
/// server-side on every booking path, so a direct URL/API bypass cannot
/// create a booking. AI-credit ownership alone never grants access.
/// </summary>
public sealed class PrivateSpeakingTutorEligibilityTests
{
    // Now = Saturday 2026-06-06 12:00 UTC; slot = Monday 2026-06-08 09:00 UTC
    // (first generated slot of the seeded Monday window; >24h lead time).
    private static readonly DateTimeOffset Now = new(2026, 06, 06, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SlotUtc = new(2026, 06, 08, 09, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Book_WithoutEligibleCourse_BlockedWithNotEligible()
    {
        await using var db = CreateDb();
        var service = CreateService(db, new FakeEntitlementResolver(false, 0, null));
        SeedTutorWithMondayAvailability(db);
        SeedSessionAddOn(db);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            service.CreateBookingAndCheckoutAsync(
                "learner-1", "tutor-profile-1", SlotUtc, 30, "UTC", null, "Medicine",
                Guid.NewGuid().ToString(), "practice", CancellationToken.None));

        Assert.Equal("live_tutor_not_eligible", ex.ErrorCode);
        Assert.Equal(0, await db.PrivateSpeakingBookings.CountAsync());
    }

    [Fact]
    public async Task Book_WithAiCreditsButNoEligibleCourse_StillBlocked()
    {
        await using var db = CreateDb();
        var service = CreateService(db, new FakeEntitlementResolver(false, 0, null));
        SeedTutorWithMondayAvailability(db);
        SeedSessionAddOn(db);
        // AI-credit ownership alone must not grant Live Tutor eligibility.
        db.AiPackageCreditAccounts.Add(new AiPackageCreditAccount
        {
            Id = "aipkg-learner-1",
            UserId = "learner-1",
            SpeakingOnlyCredits = 6,
            FlexibleCredits = 10,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            service.CreateBookingAndCheckoutAsync(
                "learner-1", "tutor-profile-1", SlotUtc, 30, "UTC", null, "Medicine",
                Guid.NewGuid().ToString(), "practice", CancellationToken.None));

        Assert.Equal("live_tutor_not_eligible", ex.ErrorCode);
        Assert.Equal(0, await db.PrivateSpeakingBookings.CountAsync());
    }

    [Fact]
    public async Task Book_WithEligibleCourse_PassesGateAndConsumesSession()
    {
        await using var db = CreateDb();
        var service = CreateService(db, new FakeEntitlementResolver(true, 1, "sub-1"));
        SeedTutorWithMondayAvailability(db);
        SeedSessionAddOn(db);
        SeedPlan(db, "full-nursing", speakingAddons: true);
        SeedSubscription(db, "sub-1", "learner-1", "full-nursing", speakingRemaining: 1);
        SeedLearnerUser(db);
        await db.SaveChangesAsync();

        var result = await service.CreateBookingAndCheckoutAsync(
            "learner-1", "tutor-profile-1", SlotUtc, 30, "UTC", null, "Medicine",
            Guid.NewGuid().ToString(), "practice", CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.True(result.EntitlementUsed);
        var booking = await db.PrivateSpeakingBookings.SingleAsync();
        Assert.Equal(PrivateSpeakingBookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public async Task Book_WithNonEligibleCourse_BlockedEvenWithSessions()
    {
        await using var db = CreateDb();
        var service = CreateService(db, new FakeEntitlementResolver(true, 1, "sub-1"));
        SeedTutorWithMondayAvailability(db);
        SeedSessionAddOn(db);
        SeedPlan(db, "basic-english", speakingAddons: false);
        SeedSubscription(db, "sub-1", "learner-1", "basic-english", speakingRemaining: 1);
        SeedLearnerUser(db);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            service.CreateBookingAndCheckoutAsync(
                "learner-1", "tutor-profile-1", SlotUtc, 30, "UTC", null, "Medicine",
                Guid.NewGuid().ToString(), "practice", CancellationToken.None));

        Assert.Equal("live_tutor_not_eligible", ex.ErrorCode);
        Assert.Equal(0, await db.PrivateSpeakingBookings.CountAsync());
    }

    private sealed class FakeEntitlementResolver(bool hasEligibleSubscription, int sessions, string? subscriptionId)
        : IEffectiveEntitlementResolver
    {
        public Task<EffectiveEntitlementSnapshot> ResolveAsync(string? userId, CancellationToken ct) =>
            Task.FromResult(new EffectiveEntitlementSnapshot(
                UserId: userId,
                HasEligibleSubscription: hasEligibleSubscription,
                IsTrial: false,
                Tier: hasEligibleSubscription ? "paid" : "free",
                SubscriptionId: hasEligibleSubscription ? subscriptionId : null,
                SubscriptionStatus: null,
                PlanId: null,
                PlanVersionId: null,
                PlanCode: null,
                AiQuotaPlanCode: null,
                AiQuotaPlanCodeSource: null,
                ActiveAddOnCodes: Array.Empty<string>(),
                IsFrozen: false,
                Trace: Array.Empty<string>())
            {
                SpeakingSessionsRemaining = sessions,
            });
    }

    private static void SeedPlan(LearnerDbContext db, string code, bool speakingAddons)
    {
        db.BillingPlans.Add(new BillingPlan
        {
            Id = $"plan-{code}",
            Code = code,
            Name = code,
            Status = BillingPlanStatus.Active,
            Price = 60m,
            Currency = "GBP",
            Interval = "one_time",
            AccessDurationDays = 180,
            SpeakingAddonsEnabled = speakingAddons,
            IsVisible = true,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
    }

    private static void SeedSessionAddOn(LearnerDbContext db)
    {
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon-speaking-1session",
            Code = "addon-speaking-1session",
            Name = "1 Private Speaking Assessment Session",
            Status = BillingAddOnStatus.Active,
            Price = 18m,
            Currency = "GBP",
            Interval = "one_time",
            DurationDays = 0,
            AddonKind = "speaking_sessions",
            EligibilityFlag = "speaking_addons",
            RequiresEligibleParent = true,
            AppliesToAllPlans = true,
            IsStackable = true,
            QuantityStep = 1,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
    }

    private static void SeedSubscription(
        LearnerDbContext db, string id, string userId, string planCode, int speakingRemaining)
    {
        db.Subscriptions.Add(new Subscription
        {
            Id = id,
            UserId = userId,
            PlanId = planCode,
            Status = SubscriptionStatus.Active,
            StartedAt = Now.AddMonths(-1),
            ChangedAt = Now.AddMonths(-1),
            NextRenewalAt = Now.AddMonths(1),
            ExpiresAt = Now.AddMonths(5),
            SpeakingSessionsRemaining = speakingRemaining,
        });
    }

    private static void SeedTutorWithMondayAvailability(LearnerDbContext db)
    {
        db.PrivateSpeakingTutorProfiles.Add(new PrivateSpeakingTutorProfile
        {
            Id = "tutor-profile-1",
            ExpertUserId = "expert-1",
            DisplayName = "Tutor",
            Timezone = "UTC",
            PriceOverrideMinorUnits = 5000,
            IsActive = true,
            CreatedAt = Now.AddMonths(-2),
            UpdatedAt = Now.AddMonths(-2),
        });
        db.PrivateSpeakingAvailabilityRules.Add(new PrivateSpeakingAvailabilityRule
        {
            Id = "psar-1",
            TutorProfileId = "tutor-profile-1",
            DayOfWeek = 1,
            StartTime = "09:00",
            EndTime = "17:00",
            IsActive = true,
        });
    }

    private static void SeedLearnerUser(LearnerDbContext db)
    {
        db.Users.Add(new LearnerUser
        {
            Id = "learner-1",
            DisplayName = "Learner One",
            Email = "learner1@example.test",
            Timezone = "UTC",
        });
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
        LearnerDbContext db, IEffectiveEntitlementResolver resolver)
    {
        var platformLinks = new PlatformLinkService(
            TestRuntimeSettingsProvider.FromPlatformOptions(new PlatformOptions()),
            Options.Create(new BillingOptions()));
        var calendarService = new PrivateSpeakingCalendarService(
            db,
            httpClientFactory: new ThrowingHttpClientFactory(),
            runtimeSettings: TestRuntimeSettingsProvider.FromZoomOptions(new ZoomOptions()),
            dataProtectionProvider: DataProtectionProvider.Create("PrivateSpeakingTutorEligibilityTests"),
            platformLinks: platformLinks,
            timeProvider: new FixedTimeProvider(Now),
            logger: NullLogger<PrivateSpeakingCalendarService>.Instance);
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
            entitlementResolver: resolver,
            addonEligibility: new AddonEligibilityService(db),
            // Entitlement-path bookings never touch Stripe.
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

    // No calendar connection is ever seeded, so this factory must never be called.
    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException("HTTP client should not be used without a calendar connection.");
    }
}
