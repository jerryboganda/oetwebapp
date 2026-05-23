using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using Xunit;

namespace OetLearner.Api.Tests.Billing;

/// <summary>
/// Unit tests for <see cref="AddonEligibilityService"/>.
/// Covers the 8 PDF-defined eligibility scenarios.
/// </summary>
public class AddonEligibilityServiceTests : IDisposable
{
    private readonly LearnerDbContext _db;

    public AddonEligibilityServiceTests()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"addon-eligibility-{Guid.NewGuid():N}")
            .EnableSensitiveDataLogging()
            .Options;
        _db = new LearnerDbContext(options);
        _db.Database.EnsureCreated();
        SeedCatalog();
    }

    private void SeedCatalog()
    {
        // Plans
        _db.BillingPlans.AddRange(
            Plan("writing-crash", writing: true, speaking: false, tutorBook: true, price: 35, draft: false, visible: true),
            Plan("speaking-crash", writing: false, speaking: true, tutorBook: true, price: 30, draft: false, visible: true),
            Plan("mega-special", writing: true, speaking: true, tutorBook: true, price: 80, draft: false, visible: true),
            Plan("full-condensed-medicine-tbook", writing: true, speaking: false, tutorBook: false, price: 135, draft: false, visible: true),
            Plan("tutor-book", writing: false, speaking: false, tutorBook: false, price: 45, draft: false, visible: true)
        );

        // Add-ons
        _db.BillingAddOns.AddRange(
            AddOn("addon-3-letters", flag: "writing_addons", kind: "writing_assessments", price: 30),
            AddOn("speaking-1session", flag: "speaking_addons", kind: "speaking_sessions", price: 18),
            AddOn("tutor-book-addon", flag: "tutor_book_discount", kind: "tutor_book", price: 32)
        );
        _db.SaveChanges();
    }

    private static BillingPlan Plan(string code, bool writing, bool speaking, bool tutorBook, decimal price, bool draft, bool visible) => new()
    {
        Id = $"plan_{code}",
        Code = code,
        Name = code,
        Price = price,
        Currency = "GBP",
        Status = BillingPlanStatus.Active,
        WritingAddonsEnabled = writing,
        SpeakingAddonsEnabled = speaking,
        TutorBookDiscountEnabled = tutorBook,
        IsDraft = draft,
        IsVisible = visible,
    };

    private static BillingAddOn AddOn(string code, string flag, string kind, decimal price) => new()
    {
        Id = $"addon_{code}",
        Code = code,
        Name = code,
        Price = price,
        Currency = "GBP",
        Status = BillingAddOnStatus.Active,
        EligibilityFlag = flag,
        AddonKind = kind,
        RequiresEligibleParent = true,
    };

    private void GiveUserEnrolment(string userId, string planCode, bool tutorBookUnlocked = false)
    {
        _db.Subscriptions.Add(new Subscription
        {
            Id = $"sub_{userId}_{planCode}",
            UserId = userId,
            PlanId = planCode,
            Status = SubscriptionStatus.Active,
            StartedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ChangedAt = DateTimeOffset.UtcNow.AddDays(-1),
            NextRenewalAt = DateTimeOffset.UtcNow.AddDays(180),
            PriceAmount = 60,
            Currency = "GBP",
            Interval = "one_time",
            TutorBookUnlocked = tutorBookUnlocked,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(180),
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task NoEnrolment_ReturnsIneligibleWithRedirectSku()
    {
        var service = new AddonEligibilityService(_db);
        var result = await service.ResolveAsync("user-no-enrolment", "addon-3-letters", default);
        Assert.False(result.Eligible);
        Assert.Equal("no_eligible_parent", result.Reason);
        Assert.NotNull(result.RedirectSku); // cheapest writing-flag plan: writing-crash
        Assert.Equal("writing-crash", result.RedirectSku);
    }

    [Fact]
    public async Task WritingCrash_AllowsWritingAddon()
    {
        GiveUserEnrolment("user-writing", "writing-crash");
        var service = new AddonEligibilityService(_db);
        var result = await service.ResolveAsync("user-writing", "addon-3-letters", default);
        Assert.True(result.Eligible);
        Assert.Single(result.EligibleParents);
        Assert.Equal("writing-crash", result.EligibleParents[0].PlanCode);
    }

    [Fact]
    public async Task MegaSpecial_AllowsBothWritingAndSpeakingAddons()
    {
        GiveUserEnrolment("user-mega", "mega-special");
        var service = new AddonEligibilityService(_db);

        var writeResult = await service.ResolveAsync("user-mega", "addon-3-letters", default);
        Assert.True(writeResult.Eligible);

        var speakResult = await service.ResolveAsync("user-mega", "speaking-1session", default);
        Assert.True(speakResult.Eligible);
    }

    [Fact]
    public async Task SpeakingCrash_BlocksWritingAddon()
    {
        GiveUserEnrolment("user-speaking", "speaking-crash");
        var service = new AddonEligibilityService(_db);
        var result = await service.ResolveAsync("user-speaking", "addon-3-letters", default);
        Assert.False(result.Eligible);
    }

    [Fact]
    public async Task WritingCrash_BlocksSpeakingAddon()
    {
        GiveUserEnrolment("user-writing-only", "writing-crash");
        var service = new AddonEligibilityService(_db);
        var result = await service.ResolveAsync("user-writing-only", "speaking-1session", default);
        Assert.False(result.Eligible);
    }

    [Fact]
    public async Task ExistingTutorBookEntitlement_BlocksTutorBookAddon()
    {
        GiveUserEnrolment("user-tb", "full-condensed-medicine-tbook", tutorBookUnlocked: true);
        var service = new AddonEligibilityService(_db);
        var result = await service.ResolveAsync("user-tb", "tutor-book-addon", default);
        Assert.False(result.Eligible);
        Assert.Equal("addon_already_owned", result.Reason);
    }

    [Fact]
    public async Task MultipleEligibleParents_ReturnsAllForSelector()
    {
        GiveUserEnrolment("user-multi", "writing-crash");
        GiveUserEnrolment("user-multi", "mega-special");
        var service = new AddonEligibilityService(_db);
        var result = await service.ResolveAsync("user-multi", "addon-3-letters", default);
        Assert.True(result.Eligible);
        Assert.Equal(2, result.EligibleParents.Count);
    }

    [Fact]
    public async Task UnknownAddon_ReturnsAddonNotFound()
    {
        var service = new AddonEligibilityService(_db);
        var result = await service.ResolveAsync("user-x", "addon-99999", default);
        Assert.False(result.Eligible);
        Assert.Equal("addon_not_found", result.Reason);
    }

    public void Dispose() => _db.Dispose();
}
