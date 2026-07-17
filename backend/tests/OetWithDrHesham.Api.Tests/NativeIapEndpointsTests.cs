using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Tests.Infrastructure;

namespace OetWithDrHesham.Api.Tests;

public class NativeIapEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public NativeIapEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AdminMappings_ValidatePlatformAndRejectDuplicateActiveProduct()
    {
        using var admin = CreateAdminClient();
        var productId = $"com.oetwithdrhesham.test.plan.{Guid.NewGuid():N}";
        await EnsureActivePlanAsync("starter");

        var invalid = await admin.PostAsJsonAsync("/v1/admin/billing/iap-products", new
        {
            platform = "windows",
            storeProductId = productId,
            targetType = "plan",
            targetId = "starter",
            isActive = true
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var missingTarget = await admin.PostAsJsonAsync("/v1/admin/billing/iap-products", new
        {
            platform = "ios",
            storeProductId = productId,
            targetType = "plan",
            targetId = "missing-plan",
            isActive = true
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingTarget.StatusCode);

        var created = await admin.PostAsJsonAsync("/v1/admin/billing/iap-products", new
        {
            platform = "ios",
            storeProductId = productId,
            targetType = "plan",
            targetId = "starter",
            isActive = true,
            displayName = "Starter iOS"
        });
        created.EnsureSuccessStatusCode();

        var duplicate = await admin.PostAsJsonAsync("/v1/admin/billing/iap-products", new
        {
            platform = "ios",
            storeProductId = productId,
            targetType = "plan",
            targetId = "starter",
            isActive = true
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var inactiveDuplicate = await admin.PostAsJsonAsync("/v1/admin/billing/iap-products", new
        {
            platform = "ios",
            storeProductId = productId,
            targetType = "plan",
            targetId = "starter",
            isActive = false
        });
        inactiveDuplicate.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task LearnerProducts_ReturnOnlyActiveMappingsForRequestedPlatform()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        db.NativeIapProductMappings.AddRange(
            NewMapping($"ios-active-{suffix}", "ios", $"com.oetwithdrhesham.ios.active.{suffix}", true, now),
            NewMapping($"ios-inactive-{suffix}", "ios", $"com.oetwithdrhesham.ios.inactive.{suffix}", false, now),
            NewMapping($"android-active-{suffix}", "android", $"com.oetwithdrhesham.android.active.{suffix}", true, now));
        await db.SaveChangesAsync();

        using var learner = _factory.CreateClient();
        var response = await learner.GetAsync("/v1/billing/native-iap/products?platform=ios");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var items = document.RootElement.EnumerateArray().ToArray();

        Assert.Contains(items, item => item.GetProperty("storeProductId").GetString() == $"com.oetwithdrhesham.ios.active.{suffix}");
        Assert.DoesNotContain(items, item => item.GetProperty("storeProductId").GetString() == $"com.oetwithdrhesham.ios.inactive.{suffix}");
        Assert.DoesNotContain(items, item => item.GetProperty("platform").GetString() == "android");
    }

    [Fact]
    public async Task AdminMappings_WriteAuditEventsForCreateUpdateAndDelete()
    {
        using var admin = CreateAdminClient();
        await EnsureActivePlanAsync("starter");
        var productId = $"com.oetwithdrhesham.audit.{Guid.NewGuid():N}";

        var created = await admin.PostAsJsonAsync("/v1/admin/billing/iap-products", new
        {
            platform = "ios",
            storeProductId = productId,
            targetType = "plan",
            targetId = "starter",
            isActive = true,
            displayName = "Audit iOS"
        });
        created.EnsureSuccessStatusCode();
        using var createDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var mappingId = createDoc.RootElement.GetProperty("id").GetString()!;

        var updated = await admin.PutAsJsonAsync($"/v1/admin/billing/iap-products/{mappingId}", new
        {
            platform = "ios",
            storeProductId = productId,
            targetType = "plan",
            targetId = "starter",
            isActive = false,
            displayName = "Audit iOS Updated"
        });
        updated.EnsureSuccessStatusCode();

        var deleted = await admin.DeleteAsync($"/v1/admin/billing/iap-products/{mappingId}");
        deleted.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var auditActions = await db.AuditEvents.AsNoTracking()
            .Where(a => a.ResourceType == "NativeIapProductMapping" && a.ResourceId == mappingId)
            .Select(a => new { a.Action, a.Details })
            .ToListAsync();

        Assert.Contains(auditActions, a => a.Action == "NativeIapProductMappingCreated");
        Assert.Contains(auditActions, a => a.Action == "NativeIapProductMappingUpdated");
        Assert.Contains(auditActions, a => a.Action == "NativeIapProductMappingDeleted");
        Assert.All(auditActions, a =>
        {
            Assert.Contains(productId, a.Details ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("receipt", a.Details ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task ReceiptValidation_FailsClosedWithoutGrantingEntitlementOrEchoingRawToken()
    {
        const string rawToken = "raw-native-receipt-token-should-not-echo";
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var subscriptionCountBefore = await db.Subscriptions.CountAsync();
        var walletTransactionCountBefore = await db.WalletTransactions.CountAsync();
        var paymentTransactionCountBefore = await db.PaymentTransactions.CountAsync();

        using var learner = _factory.CreateClient();
        var response = await learner.PostAsJsonAsync("/v1/billing/native-iap/receipts/validate", new
        {
            platform = "ios",
            storeProductId = "com.oetwithdrhesham.test.plan",
            receiptToken = rawToken
        });

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(rawToken, json, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("isValid").GetBoolean());
        Assert.False(document.RootElement.GetProperty("entitlementGranted").GetBoolean());
        Assert.Equal("native_iap_validation_unconfigured", document.RootElement.GetProperty("code").GetString());

        Assert.Equal(subscriptionCountBefore, await db.Subscriptions.CountAsync());
        Assert.Equal(walletTransactionCountBefore, await db.WalletTransactions.CountAsync());
        Assert.Equal(paymentTransactionCountBefore, await db.PaymentTransactions.CountAsync());
    }

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
        client.DefaultRequestHeaders.Add(
            "X-Debug-AdminPermissions",
            $"{AdminPermissions.BillingRead},{AdminPermissions.BillingCatalogWrite}");
        client.DefaultRequestHeaders.Add("X-Debug-UserId", $"admin-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add("X-Debug-Email", "billing-iap-admin@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", "Billing IAP Admin");
        return client;
    }

    private static NativeIapProductMapping NewMapping(string id, string platform, string productId, bool active, DateTimeOffset now)
        => new()
        {
            Id = id,
            Platform = platform,
            StoreProductId = productId,
            TargetType = "plan",
            TargetId = "starter",
            IsActive = active,
            CreatedAt = now,
            UpdatedAt = now,
        };

    private async Task EnsureActivePlanAsync(string code)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var existing = await db.BillingPlans.FirstOrDefaultAsync(plan => plan.Code == code);
        if (existing is not null)
        {
            existing.Status = BillingPlanStatus.Active;
            await db.SaveChangesAsync();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan
        {
            Id = $"plan-{Guid.NewGuid():N}",
            Code = code,
            Name = "Starter",
            Description = "Starter plan",
            Price = 19,
            Currency = "AUD",
            Interval = "month",
            DurationMonths = 1,
            IncludedCredits = 10,
            DisplayOrder = 1,
            IsVisible = true,
            IsRenewable = true,
            Status = BillingPlanStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }
}
