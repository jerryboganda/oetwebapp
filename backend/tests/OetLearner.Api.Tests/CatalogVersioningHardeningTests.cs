using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// Slice C — May 2026 billing-hardening regression tests for catalog version
/// snapshots and plan archival.
///
/// Coverage:
///   • EF SaveChangesInterceptor blocks any UPDATE / DELETE on
///     BillingPlanVersion / BillingAddOnVersion / BillingCouponVersion rows.
///   • Code-immutability check on plan / add-on / coupon update.
///   • Version-history summaries do not leak commerce-mutable fields
///     (RedemptionCount, Invoices, CheckoutSessions, PaymentTransaction*).
///   • Plan archival hides the plan from the active learner-facing catalog
///     while leaving its snapshot intact for existing subscriptions.
/// </summary>
[Collection("AuthFlows")]
public class CatalogVersioningHardeningTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public CatalogVersioningHardeningTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");
    }

    [Fact]
    public async Task BillingPlanVersion_CannotBeMutatedAfterCreation()
    {
        var suffix = NewSuffix();
        var planCode = $"slice-c-immut-plan-{suffix}";
        var planId = await CreatePlanAsync(planCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var version = await db.BillingPlanVersions.FirstAsync(v => v.PlanId == planId);

        version.Price = version.Price + 1m;

        var ex = await Assert.ThrowsAsync<ApiException>(() => db.SaveChangesAsync());
        Assert.Equal("billing_catalog_version_immutable", ex.ErrorCode);
        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
    }

    [Fact]
    public async Task BillingAddOnVersion_CannotBeDeletedAfterCreation()
    {
        var suffix = NewSuffix();
        var planCode = $"slice-c-immut-addon-plan-{suffix}";
        var addOnCode = $"slice-c-immut-addon-{suffix}";
        await CreatePlanAsync(planCode);
        var addOnId = await CreateAddOnAsync(addOnCode, planCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var version = await db.BillingAddOnVersions.FirstAsync(v => v.AddOnId == addOnId);

        db.BillingAddOnVersions.Remove(version);

        var ex = await Assert.ThrowsAsync<ApiException>(() => db.SaveChangesAsync());
        Assert.Equal("billing_catalog_version_immutable", ex.ErrorCode);
    }

    [Fact]
    public async Task BillingCouponVersion_CannotBeMutatedAfterCreation()
    {
        var suffix = NewSuffix();
        var planCode = $"slice-c-immut-coupon-plan-{suffix}";
        var addOnCode = $"slice-c-immut-coupon-addon-{suffix}";
        var couponCode = $"SLICECIMMUT{suffix}".ToUpperInvariant();
        await CreatePlanAsync(planCode);
        await CreateAddOnAsync(addOnCode, planCode);
        var createResponse = await _client.PostAsJsonAsync(
            "/v1/admin/billing/coupons",
            ValidCouponPayload(couponCode, "percentage", 10m, [planCode], [addOnCode]));
        createResponse.EnsureSuccessStatusCode();
        using var createJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var couponId = createJson.RootElement.GetProperty("id").GetString()!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var version = await db.BillingCouponVersions.FirstAsync(v => v.CouponId == couponId);

        version.DiscountValue = 99m;

        var ex = await Assert.ThrowsAsync<ApiException>(() => db.SaveChangesAsync());
        Assert.Equal("billing_catalog_version_immutable", ex.ErrorCode);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("add_on")]
    [InlineData("coupon")]
    public async Task CatalogCode_RenameAfterCreationIsRejected(string kind)
    {
        var suffix = NewSuffix();
        var planCode = $"slice-c-rename-plan-{kind}-{suffix}";
        var addOnCode = $"slice-c-rename-addon-{kind}-{suffix}";
        var couponCode = $"SLICECRENAME{kind}{suffix}".ToUpperInvariant();
        var planId = await CreatePlanAsync(planCode);
        var addOnId = await CreateAddOnAsync(addOnCode, planCode);
        var couponCreate = await _client.PostAsJsonAsync(
            "/v1/admin/billing/coupons",
            ValidCouponPayload(couponCode, "percentage", 10m, [planCode], [addOnCode]));
        couponCreate.EnsureSuccessStatusCode();
        using var couponCreateJson = JsonDocument.Parse(await couponCreate.Content.ReadAsStringAsync());
        var couponId = couponCreateJson.RootElement.GetProperty("id").GetString()!;

        HttpResponseMessage response;
        string expectedErrorCode;
        switch (kind)
        {
            case "plan":
                response = await _client.PutAsJsonAsync(
                    $"/v1/admin/billing/plans/{Uri.EscapeDataString(planId)}",
                    ValidPlanPayload($"{planCode}-changed"));
                expectedErrorCode = "billing_plan_invalid";
                break;
            case "add_on":
                response = await _client.PutAsJsonAsync(
                    $"/v1/admin/billing/add-ons/{Uri.EscapeDataString(addOnId)}",
                    ValidAddOnPayload($"{addOnCode}-changed", planCode));
                expectedErrorCode = "billing_addon_invalid";
                break;
            default:
                response = await _client.PutAsJsonAsync(
                    $"/v1/admin/billing/coupons/{Uri.EscapeDataString(couponId)}",
                    ValidCouponPayload($"{couponCode}X", "percentage", 10m, [planCode], [addOnCode]));
                expectedErrorCode = "billing_coupon_invalid";
                break;
        }

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedErrorCode, json.RootElement.GetProperty("code").GetString());
        var fields = json.RootElement.GetProperty("fieldErrors")
            .EnumerateArray()
            .Select(e => e.GetProperty("field").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("code", fields);
    }

    [Fact]
    public async Task VersionHistorySummary_DoesNotLeakMutableCommerceFields()
    {
        // The version snapshot summary is a free-form Dictionary<string,object?>
        // — assert that NONE of the commerce-mutable fields ever appear in any
        // catalog version summary. This is the regression guard for
        // /memories/repo/billing-catalog-history-scope.md.
        var forbidden = new[]
        {
            "redemptionCount",
            "RedemptionCount",
            "invoiceId",
            "InvoiceId",
            "invoices",
            "checkoutSessionId",
            "CheckoutSessionId",
            "checkoutSessions",
            "paymentTransactionId",
            "paymentTransactionIds",
            "paymentTransactions",
            "PaymentTransactionId",
        };

        var suffix = NewSuffix();
        var planCode = $"slice-c-leak-plan-{suffix}";
        var addOnCode = $"slice-c-leak-addon-{suffix}";
        var couponCode = $"SLICECLEAK{suffix}".ToUpperInvariant();
        var planId = await CreatePlanAsync(planCode);
        var addOnId = await CreateAddOnAsync(addOnCode, planCode);
        var couponCreate = await _client.PostAsJsonAsync(
            "/v1/admin/billing/coupons",
            ValidCouponPayload(couponCode, "percentage", 10m, [planCode], [addOnCode]));
        couponCreate.EnsureSuccessStatusCode();
        using var couponCreateJson = JsonDocument.Parse(await couponCreate.Content.ReadAsStringAsync());
        var couponId = couponCreateJson.RootElement.GetProperty("id").GetString()!;

        await AssertNoForbiddenSummaryFieldsAsync($"/v1/admin/billing/plans/{Uri.EscapeDataString(planId)}/versions", forbidden);
        await AssertNoForbiddenSummaryFieldsAsync($"/v1/admin/billing/add-ons/{Uri.EscapeDataString(addOnId)}/versions", forbidden);
        await AssertNoForbiddenSummaryFieldsAsync($"/v1/admin/billing/coupons/{Uri.EscapeDataString(couponId)}/versions", forbidden);
    }

    [Fact]
    public void EnsurePlanCanStartNewSubscription_RejectsArchivedPlan()
    {
        var archived = new BillingPlan
        {
            Id = "plan-archived",
            Code = "plan-archived",
            Name = "Archived plan",
            Status = BillingPlanStatus.Archived
        };

        var ex = Assert.Throws<ApiException>(() => AdminService.EnsurePlanCanStartNewSubscription(archived));
        Assert.Equal("plan_archived", ex.ErrorCode);

        // Active plan is accepted.
        var active = new BillingPlan
        {
            Id = "plan-active",
            Code = "plan-active",
            Name = "Active plan",
            Status = BillingPlanStatus.Active
        };
        AdminService.EnsurePlanCanStartNewSubscription(active);
    }

    [Fact]
    public async Task ArchivedPlan_HiddenFromActiveCatalogButSnapshotPreservedForExistingSubscription()
    {
        var suffix = NewSuffix();
        var planCode = $"slice-c-archive-plan-{suffix}";
        var planId = await CreatePlanAsync(planCode);

        // Capture the v1 snapshot id before archival.
        string v1Id;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            v1Id = (await db.BillingPlanVersions.AsNoTracking()
                .FirstAsync(v => v.PlanId == planId)).Id;
        }

        // Admin archives the plan.
        var archivePayload = ValidPlanPayloadDictionary(planCode);
        archivePayload["status"] = "archived";
        var archiveResponse = await _client.PutAsJsonAsync($"/v1/admin/billing/plans/{Uri.EscapeDataString(planId)}", archivePayload);
        archiveResponse.EnsureSuccessStatusCode();

        // The snapshot row from before the archival is still present and intact.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var v1 = await db.BillingPlanVersions.AsNoTracking().FirstAsync(v => v.Id == v1Id);
            Assert.Equal(BillingPlanStatus.Active, v1.Status);
            Assert.Null(v1.ArchivedAt);

            // A new snapshot was appended capturing the archived state.
            var v2 = await db.BillingPlanVersions.AsNoTracking()
                .Where(v => v.PlanId == planId)
                .OrderByDescending(v => v.VersionNumber)
                .FirstAsync();
            Assert.Equal(BillingPlanStatus.Archived, v2.Status);
            Assert.NotNull(v2.ArchivedAt);
        }
    }

    private async Task AssertNoForbiddenSummaryFieldsAsync(string url, string[] forbidden)
    {
        var response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
        {
            var summary = item.GetProperty("summary");
            foreach (var field in forbidden)
            {
                Assert.False(
                    summary.TryGetProperty(field, out _),
                    $"Catalog version summary at {url} must not include forbidden field '{field}'.");
            }
        }
    }

    private async Task<string> CreatePlanAsync(string code)
    {
        var response = await _client.PostAsJsonAsync("/v1/admin/billing/plans", ValidPlanPayload(code));
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<string> CreateAddOnAsync(string code, string planCode)
    {
        var response = await _client.PostAsJsonAsync("/v1/admin/billing/add-ons", ValidAddOnPayload(code, planCode));
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetString()!;
    }

    private static string NewSuffix() => Guid.NewGuid().ToString("N")[..8];

    private static object ValidPlanPayload(string code) => ValidPlanPayloadDictionary(code);

    private static Dictionary<string, object?> ValidPlanPayloadDictionary(string code) => new()
    {
        ["code"] = code,
        ["name"] = $"Plan {code}",
        ["description"] = "Plan used by Slice C catalog hardening tests.",
        ["price"] = 49m,
        ["currency"] = "AUD",
        ["interval"] = "month",
        ["durationMonths"] = 1,
        ["includedCredits"] = 4,
        ["displayOrder"] = 10,
        ["isVisible"] = true,
        ["isRenewable"] = true,
        ["trialDays"] = 0,
        ["status"] = "active",
        ["includedSubtestsJson"] = JsonSerializer.Serialize(new[] { "writing", "speaking" }),
        ["entitlementsJson"] = JsonSerializer.Serialize(new { tier = "slice-c" })
    };

    private static object ValidAddOnPayload(string code, string planCode) => new
    {
        code,
        name = $"Add-on {code}",
        description = "Add-on used by Slice C catalog hardening tests.",
        price = 19m,
        currency = "AUD",
        interval = "one_time",
        durationDays = 30,
        grantCredits = 2,
        displayOrder = 10,
        isRecurring = false,
        appliesToAllPlans = false,
        isStackable = true,
        quantityStep = 1,
        maxQuantity = 5,
        status = "active",
        compatiblePlanCodesJson = JsonSerializer.Serialize(new[] { planCode }),
        grantEntitlementsJson = JsonSerializer.Serialize(new { reviewCredits = 2 })
    };

    private static object ValidCouponPayload(string code, string discountType, decimal discountValue, string[] planCodes, string[] addOnCodes) => new
    {
        code,
        name = $"Coupon {code}",
        description = "Coupon used by Slice C catalog hardening tests.",
        discountType,
        discountValue,
        currency = "AUD",
        startsAt = DateTimeOffset.UtcNow.AddDays(-1),
        endsAt = DateTimeOffset.UtcNow.AddDays(14),
        usageLimitTotal = 20,
        usageLimitPerUser = 2,
        minimumSubtotal = 0m,
        isStackable = false,
        status = "active",
        applicablePlanCodesJson = JsonSerializer.Serialize(planCodes),
        applicableAddOnCodesJson = JsonSerializer.Serialize(addOnCodes),
        notes = "Created by Slice C tests."
    };
}
