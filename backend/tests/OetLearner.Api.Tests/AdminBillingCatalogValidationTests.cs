using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

[Collection("AuthFlows")]
public class AdminBillingCatalogValidationTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AdminBillingCatalogValidationTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _client = factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");
    }

    [Fact]
    public async Task AdminBillingPlan_CreateRejectsInvalidCatalogPayload()
    {
        var response = await _client.PostAsJsonAsync("/v1/admin/billing/plans", new
        {
            code = "invalid plan",
            name = "",
            description = "Invalid plan payload.",
            price = -1m,
            currency = "au1",
            interval = "fortnight",
            durationMonths = -1,
            includedCredits = -2,
            displayOrder = 0,
            isVisible = true,
            isRenewable = true,
            trialDays = -3,
            status = "paused",
            includedSubtestsJson = "{bad json",
            entitlementsJson = "[]"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("billing_plan_invalid", json.RootElement.GetProperty("code").GetString());
        var fields = ReadFieldErrorFields(json);
        Assert.Contains("code", fields);
        Assert.Contains("name", fields);
        Assert.Contains("price", fields);
        Assert.Contains("interval", fields);
        Assert.Contains("status", fields);
        Assert.Contains("includedSubtestsJson", fields);
        Assert.Contains("entitlementsJson", fields);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("1")]
    [InlineData("active, inactive")]
    public async Task AdminBillingPlan_CreateRejectsNumericAndCompositeStatusValues(string status)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var response = await _client.PostAsJsonAsync("/v1/admin/billing/plans", new
        {
            code = $"phase2a-status-{suffix}",
            name = "Invalid status plan",
            description = "Invalid status payload.",
            price = 49m,
            currency = "AUD",
            interval = "month",
            durationMonths = 1,
            includedCredits = 4,
            displayOrder = 10,
            isVisible = true,
            isRenewable = true,
            trialDays = 0,
            status,
            includedSubtestsJson = JsonSerializer.Serialize(new[] { "writing" }),
            entitlementsJson = JsonSerializer.Serialize(new { tier = "phase-2a" })
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("billing_plan_invalid", json.RootElement.GetProperty("code").GetString());
        Assert.Contains("status", ReadFieldErrorFields(json));
    }

    [Fact]
    public async Task AdminBillingPlan_UpdateRejectsDuplicateCodeExcludingSelf()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var firstCode = $"phase2a-basic-{suffix}";
        var secondCode = $"phase2a-pro-{suffix}";
        var firstPlanId = await CreatePlanAsync(firstCode);
        await CreatePlanAsync(secondCode);

        var response = await _client.PutAsJsonAsync($"/v1/admin/billing/plans/{Uri.EscapeDataString(firstPlanId)}", ValidPlanPayload(secondCode));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("billing_plan_invalid", json.RootElement.GetProperty("code").GetString());
        Assert.Contains("code", ReadFieldErrorFields(json));
    }

    [Fact]
    public async Task AdminBillingAddOn_CreateRejectsInvalidReferencesAndQuantityRules()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await _client.PostAsJsonAsync("/v1/admin/billing/add-ons", new
        {
            code = $"phase2a-addon-{suffix}",
            name = "Invalid restricted add-on",
            description = "Invalid add-on payload.",
            price = 12m,
            currency = "AUD",
            interval = "one_time",
            durationDays = 30,
            grantCredits = 3,
            displayOrder = 0,
            isRecurring = false,
            appliesToAllPlans = false,
            isStackable = true,
            quantityStep = 0,
            maxQuantity = 1,
            status = "active",
            compatiblePlanCodesJson = JsonSerializer.Serialize(new[] { "missing-plan-code" }),
            grantEntitlementsJson = "[]"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("billing_addon_invalid", json.RootElement.GetProperty("code").GetString());
        var fields = ReadFieldErrorFields(json);
        Assert.Contains("quantityStep", fields);
        Assert.Contains("compatiblePlanCodesJson", fields);
        Assert.Contains("grantEntitlementsJson", fields);
    }

    [Fact]
    public async Task AdminBillingCoupon_CreatePersistsFixedAliasAndRejectsInvalidCouponRules()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var planCode = $"phase2a-coupon-plan-{suffix}";
        var addOnCode = $"phase2a-coupon-addon-{suffix}";
        await CreatePlanAsync(planCode);
        await CreateAddOnAsync(addOnCode, planCode);

        var fixedAliases = new[] { "fixed", "fixedAmount", "FixedAmount" };
        for (var index = 0; index < fixedAliases.Length; index++)
        {
            var discountAlias = fixedAliases[index];
            var fixedResponse = await _client.PostAsJsonAsync("/v1/admin/billing/coupons", ValidCouponPayload(
                $"PHASE2A{index}{discountAlias.ToUpperInvariant()}{suffix}",
                discountAlias,
                15m,
                [planCode],
                [addOnCode]));
            fixedResponse.EnsureSuccessStatusCode();
            using var fixedJson = JsonDocument.Parse(await fixedResponse.Content.ReadAsStringAsync());
            Assert.Equal("fixed", fixedJson.RootElement.GetProperty("discountType").GetString());
        }

        var invalidResponse = await _client.PostAsJsonAsync("/v1/admin/billing/coupons", new
        {
            code = $"PHASE2AINVALID{suffix}",
            name = "Invalid coupon",
            description = "Invalid coupon payload.",
            discountType = "percentage",
            discountValue = 150m,
            currency = "AUD",
            startsAt = DateTimeOffset.UtcNow.AddDays(2),
            endsAt = DateTimeOffset.UtcNow.AddDays(1),
            usageLimitTotal = 1,
            usageLimitPerUser = 2,
            minimumSubtotal = -1m,
            isStackable = false,
            status = "active",
            applicablePlanCodesJson = JsonSerializer.Serialize(new[] { "missing-plan-code" }),
            applicableAddOnCodesJson = JsonSerializer.Serialize(new[] { "missing-addon-code" }),
            notes = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        using var invalidJson = JsonDocument.Parse(await invalidResponse.Content.ReadAsStringAsync());
        Assert.Equal("billing_coupon_invalid", invalidJson.RootElement.GetProperty("code").GetString());
        var fields = ReadFieldErrorFields(invalidJson);
        Assert.Contains("discountValue", fields);
        Assert.Contains("endsAt", fields);
        Assert.Contains("usageLimitPerUser", fields);
        Assert.Contains("minimumSubtotal", fields);
        Assert.Contains("applicablePlanCodesJson", fields);
        Assert.Contains("applicableAddOnCodesJson", fields);
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

    private static object ValidPlanPayload(string code) => new
    {
        code,
        name = $"Plan {code}",
        description = "Plan used by admin billing validation tests.",
        price = 49m,
        currency = "AUD",
        interval = "month",
        durationMonths = 1,
        includedCredits = 4,
        displayOrder = 10,
        isVisible = true,
        isRenewable = true,
        trialDays = 0,
        status = "active",
        includedSubtestsJson = JsonSerializer.Serialize(new[] { "writing", "speaking" }),
        entitlementsJson = JsonSerializer.Serialize(new { tier = "phase-2a", invoiceDownloadsAvailable = true })
    };

    private static object ValidAddOnPayload(string code, string planCode) => new
    {
        code,
        name = $"Add-on {code}",
        description = "Add-on used by admin billing validation tests.",
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
        description = "Coupon used by admin billing validation tests.",
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
        notes = "Created by validation tests."
    };

    private static HashSet<string> ReadFieldErrorFields(JsonDocument json)
        => json.RootElement.GetProperty("fieldErrors")
            .EnumerateArray()
            .Select(error => error.GetProperty("field").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
