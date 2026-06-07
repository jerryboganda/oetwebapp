using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Billing;

/// <summary>
/// OET 2026 entitlement conformance — spec rule #9: the provisional
/// <c>full-pharmacy</c> SKU must stay hidden from the public catalogue until it
/// is activated. The plan is shipped with <c>IsDraft = true</c>; the public
/// catalogue endpoint (<c>Oet2026CatalogEndpoints.PublicCatalogPricing</c>) and
/// <see cref="AddonEligibilityService"/> both filter on
/// <c>Status == Active &amp;&amp; IsVisible &amp;&amp; !IsDraft</c>, so a draft
/// plan must never surface to buyers — even though it is otherwise Active,
/// visible and (here) the cheapest writing-eligible parent.
/// </summary>
public sealed class PublicCatalogDraftVisibilityTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private readonly TestWebApplicationFactory _factory;

    public PublicCatalogDraftVisibilityTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PublicCatalogPricing_OmitsDraftPharmacyPlan_ButKeepsVisiblePlan()
    {
        await ResetPlansAsync();
        await ResetAddOnsAsync();
        await SeedPlansAsync();

        // Anonymous endpoint — no auth headers required.
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/v1/catalog/pricing");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"status={response.StatusCode} body={body}");

        using var json = JsonDocument.Parse(body);
        var codes = json.RootElement.GetProperty("plans")
            .EnumerateArray()
            .Select(p => p.GetProperty("code").GetString())
            .ToList();

        Assert.Contains("conformance-visible-plan", codes);
        Assert.DoesNotContain("full-pharmacy", codes);
    }

    [Fact]
    public async Task PublicCatalogPricing_OnlyListsParentEligiblePortfolioAddOns()
    {
        await ResetAddOnsAsync();
        await SeedPublicAddOnsAsync();

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/v1/catalog/pricing");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"status={response.StatusCode} body={body}");

        using var json = JsonDocument.Parse(body);
        var codes = json.RootElement.GetProperty("addOns")
            .EnumerateArray()
            .Select(p => p.GetProperty("code").GetString())
            .ToList();

        Assert.Contains("conformance-writing-addon", codes);
        Assert.Contains("conformance-speaking-addon", codes);
        Assert.Contains("conformance-tutorbook-addon", codes);
        Assert.DoesNotContain("conformance-ai-package", codes);
        Assert.DoesNotContain("conformance-legacy-credits", codes);
    }

    [Fact]
    public async Task AddonEligibility_DraftPharmacyPlan_NotOfferedAsCheapestEligibleParent()
    {
        await ResetPlansAsync();
        await SeedPlansAsync();
        await SeedWritingAddonAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var service = new AddonEligibilityService(db);

        // User has no enrolment, so the writing add-on is ineligible and the
        // service returns the cheapest *visible, non-draft* writing-eligible
        // plan as the upsell RedirectSku. The draft pharmacy plan is cheaper
        // (price 0) and carries the writing flag, but must be excluded.
        var result = await service.ResolveAsync(
            $"conformance-no-enrolment-{Guid.NewGuid():N}",
            "conformance-writing-addon",
            default);

        Assert.False(result.Eligible);
        Assert.Equal("no_eligible_parent", result.Reason);
        Assert.NotEqual("full-pharmacy", result.RedirectSku);
        Assert.Equal("conformance-visible-plan", result.RedirectSku);
    }

    private async Task SeedPlansAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        // Draft pharmacy plan: Active + visible + writing-eligible, but IsDraft.
        // Priced at 0 so it would be the *cheapest* eligible parent if the
        // draft filter were missing.
        db.BillingPlans.Add(NewPlan(
            code: "full-pharmacy",
            name: "Full Pharmacy OET Course",
            price: 0m,
            isDraft: true,
            isVisible: true,
            writingAddons: true));

        // Normal visible writing-eligible plan (the legitimate upsell target).
        db.BillingPlans.Add(NewPlan(
            code: "conformance-visible-plan",
            name: "Conformance Visible Plan",
            price: 35m,
            isDraft: false,
            isVisible: true,
            writingAddons: true));

        await db.SaveChangesAsync();
    }

    private async Task SeedWritingAddonAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        if (await db.BillingAddOns.AnyAsync(a => a.Code == "conformance-writing-addon"))
        {
            return;
        }

        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon_conformance-writing-addon",
            Code = "conformance-writing-addon",
            Name = "Conformance Writing Letters",
            Description = "Writing assessment add-on for conformance tests.",
            Price = 30m,
            Currency = "GBP",
            Interval = "one_time",
            Status = BillingAddOnStatus.Active,
            EligibilityFlag = "writing_addons",
            AddonKind = "writing_assessments",
            RequiresEligibleParent = true,
        });

        await db.SaveChangesAsync();
    }

    private async Task SeedPublicAddOnsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        db.BillingAddOns.AddRange(
            NewAddOn("conformance-writing-addon", "writing_assessments", "writing_addons", requiresParent: true),
            NewAddOn("conformance-speaking-addon", "speaking_sessions", "speaking_addons", requiresParent: true),
            NewAddOn("conformance-tutorbook-addon", "tutor_book", "tutor_book_discount", requiresParent: true),
            NewAddOn("conformance-ai-package", "ai_package", string.Empty, requiresParent: false),
            NewAddOn("conformance-legacy-credits", string.Empty, string.Empty, requiresParent: false));

        await db.SaveChangesAsync();
    }

    private async Task ResetPlansAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var codes = new[] { "full-pharmacy", "conformance-visible-plan" };
        var existing = await db.BillingPlans.Where(p => codes.Contains(p.Code)).ToListAsync();
        if (existing.Count > 0)
        {
            db.BillingPlans.RemoveRange(existing);
            await db.SaveChangesAsync();
        }
    }

    private async Task ResetAddOnsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var codes = new[]
        {
            "conformance-writing-addon",
            "conformance-speaking-addon",
            "conformance-tutorbook-addon",
            "conformance-ai-package",
            "conformance-legacy-credits"
        };
        var existing = await db.BillingAddOns.Where(p => codes.Contains(p.Code)).ToListAsync();
        if (existing.Count > 0)
        {
            db.BillingAddOns.RemoveRange(existing);
            await db.SaveChangesAsync();
        }
    }

    private static BillingPlan NewPlan(
        string code, string name, decimal price, bool isDraft, bool isVisible, bool writingAddons) => new()
    {
        Id = $"plan_{code}",
        Code = code,
        Name = name,
        Description = $"{name} description.",
        Price = price,
        Currency = "GBP",
        Interval = "one_time",
        DurationMonths = 6,
        AccessDurationDays = 180,
        ProductCategory = "full_course",
        Profession = "pharmacy",
        DashboardModulesJson = "[]",
        IncludedSubtestsJson = "[]",
        EntitlementsJson = "{}",
        DisplayOrder = 150,
        Status = BillingPlanStatus.Active,
        IsVisible = isVisible,
        IsDraft = isDraft,
        WritingAddonsEnabled = writingAddons,
    };

    private static BillingAddOn NewAddOn(string code, string kind, string flag, bool requiresParent) => new()
    {
        Id = $"addon_{code}",
        Code = code,
        Name = code,
        Description = $"{code} description.",
        Price = 12m,
        Currency = "GBP",
        Interval = "one_time",
        Status = BillingAddOnStatus.Active,
        AddonKind = kind,
        EligibilityFlag = flag,
        RequiresEligibleParent = requiresParent,
        IsStackable = true,
        DisplayOrder = 10,
    };

    public void Dispose()
    {
        // Best-effort cleanup so the shared class-fixture DB does not leak the
        // fixed `full-pharmacy` code into any sibling test in this class.
        ResetPlansAsync().GetAwaiter().GetResult();
        ResetAddOnsAsync().GetAwaiter().GetResult();
    }
}
