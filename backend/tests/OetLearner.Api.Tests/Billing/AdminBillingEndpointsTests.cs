using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Domain.Billing;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Tests.Infrastructure;
using Xunit;

namespace OetLearner.Api.Tests.Billing;

/// <summary>
/// Wave B4 — end-to-end coverage for the new admin billing surface. Asserts
/// auth gating, product CRUD round-trip, refund issuance, and Stripe Tax
/// registration list when Stripe is not configured.
/// </summary>
public sealed class AdminBillingEndpointsTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private readonly TestWebApplicationFactory _factory;
    private readonly DevAuthEnv _devAuth = DevAuthEnv.Enable();

    public AdminBillingEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public void Dispose()
    {
        _devAuth.Dispose();
    }

    // ─────────────────────── Auth gating ───────────────────────

    [Fact]
    public async Task ProductList_RejectsUnauthenticatedCallers()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/v1/admin/products");
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected 401/403 for anonymous, got {(int)response.StatusCode} {response.StatusCode}.");
    }

    [Fact]
    public async Task ProductList_RejectsLearners()
    {
        using var client = CreateClient(role: "learner", permissions: null);
        var response = await client.GetAsync("/v1/admin/products");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ProductList_AllowsBillingReader()
    {
        using var client = CreateClient(role: "admin", permissions: AdminPermissions.BillingRead);
        var response = await client.GetAsync("/v1/admin/products");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProductCreate_RequiresCatalogWritePermission()
    {
        using var client = CreateClient(role: "admin", permissions: AdminPermissions.BillingRead);
        var response = await client.PostAsJsonAsync("/v1/admin/products", new
        {
            code = $"pkg_test_{Guid.NewGuid():N}",
            name = "Read-only test",
            productType = "package",
            prices = new[] { new { currency = "USD", amount = 1900m, interval = (string?)null, intervalCount = 1, country = (string?)null, stripePriceId = (string?)null } }
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ─────────────────────── Product CRUD round-trip ───────────────────────

    [Fact]
    public async Task ProductCrud_FullRoundTrip()
    {
        using var client = CreateClient(role: "admin", permissions: AdminPermissions.BillingRead + "," + AdminPermissions.BillingCatalogWrite);

        var code = $"pkg_b4_{Guid.NewGuid():N}";
        var create = await client.PostAsJsonAsync("/v1/admin/products", new
        {
            code,
            name = "B4 test product",
            description = "Initial description",
            productType = "package",
            prices = new[]
            {
                new { currency = "USD", amount = 1900m, interval = (string?)null, intervalCount = (int?)1, country = (string?)null, stripePriceId = (string?)null }
            }
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<AdminProductView>();
        Assert.NotNull(created);
        Assert.Equal(code, created!.Code);
        Assert.Single(created.Prices);

        // List should include it
        var list = await client.GetAsync("/v1/admin/products");
        list.EnsureSuccessStatusCode();
        var listed = await list.Content.ReadFromJsonAsync<List<AdminProductView>>();
        Assert.NotNull(listed);
        Assert.Contains(listed!, p => p.Code == code);

        // Patch the name
        var patch = await client.PatchAsJsonAsync($"/v1/admin/products/{code}", new
        {
            name = "B4 patched name",
            description = "Patched description"
        });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var patched = await patch.Content.ReadFromJsonAsync<AdminProductView>();
        Assert.Equal("B4 patched name", patched!.Name);
        Assert.Equal("Patched description", patched.Description);
        Assert.True(patched.IsActive);

        // Archive (soft delete)
        var archive = await client.DeleteAsync($"/v1/admin/products/{code}");
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var row = await db.BillingProducts.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code);
        Assert.NotNull(row);
        Assert.False(row!.IsActive);

        // Default list (active-only) should hide it
        var listAfter = await client.GetAsync("/v1/admin/products");
        var listedAfter = await listAfter.Content.ReadFromJsonAsync<List<AdminProductView>>();
        Assert.DoesNotContain(listedAfter!, p => p.Code == code);

        // includeInactive=true should resurface it
        var listInactive = await client.GetAsync("/v1/admin/products?includeInactive=true");
        var listedInactive = await listInactive.Content.ReadFromJsonAsync<List<AdminProductView>>();
        Assert.Contains(listedInactive!, p => p.Code == code);
    }

    [Fact]
    public async Task ProductCreate_RejectsDuplicateCode()
    {
        using var client = CreateClient(role: "admin", permissions: AdminPermissions.BillingCatalogWrite);
        var code = $"pkg_dup_{Guid.NewGuid():N}";
        var body = new
        {
            code,
            name = "Dup test",
            productType = "package",
            prices = new[]
            {
                new { currency = "USD", amount = 100m, interval = (string?)null, intervalCount = (int?)1, country = (string?)null, stripePriceId = (string?)null }
            }
        };

        var first = await client.PostAsJsonAsync("/v1/admin/products", body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/v1/admin/products", body);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // ─────────────────────── Coupon CRUD ───────────────────────

    [Fact]
    public async Task CouponCrud_RoundTrip()
    {
        using var client = CreateClient(role: "admin", permissions: AdminPermissions.BillingCatalogWrite);
        var code = $"PROMO_{Guid.NewGuid():N}";

        var create = await client.PostAsJsonAsync("/v1/admin/coupons", new
        {
            code,
            name = "Test promo",
            discountValue = 10m,
            discountType = "Percentage",
            currency = "USD",
            isStackable = false
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var patch = await client.PatchAsJsonAsync($"/v1/admin/coupons/{code}", new
        {
            name = "Renamed promo",
            discountValue = 15m
        });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var patched = await patch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Renamed promo", patched.GetProperty("name").GetString());
        Assert.Equal(15m, patched.GetProperty("discountValue").GetDecimal());

        var del = await client.DeleteAsync($"/v1/admin/coupons/{code}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var coupon = await db.BillingCoupons.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code);
        Assert.NotNull(coupon);
        Assert.Equal(BillingCouponStatus.Archived, coupon!.Status);
    }

    // ─────────────────────── Refund ───────────────────────

    [Fact]
    public async Task IssueRefund_CreatesOrderRefundRow_Idempotent()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        var sessionId = $"cs_test_{Guid.NewGuid():N}";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                LearnerUserId = learnerId,
                Gateway = "stripe",
                GatewayTransactionId = sessionId,
                TransactionType = "one_time_purchase",
                Status = "completed",
                Amount = 100m,
                Currency = "USD",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateClient(role: "admin", permissions: AdminPermissions.BillingRead + "," + AdminPermissions.BillingRefundWrite);
        var idempotencyKey = $"refund-{Guid.NewGuid():N}";
        var firstBody = new
        {
            checkoutSessionId = sessionId,
            amountCents = 5000L,
            reason = "requested_by_customer",
            adminNote = "Customer requested",
            idempotencyKey
        };
        var first = await client.PostAsJsonAsync("/v1/admin/refunds", firstBody);
        var firstBodyText = await first.Content.ReadAsStringAsync();
        Assert.True(first.IsSuccessStatusCode, $"REFUND_DIAG status={(int)first.StatusCode} headers=[{string.Join(";", first.Headers.Select(h => h.Key))}] body={firstBodyText}");
        var firstResult = await first.Content.ReadFromJsonAsync<RefundResultView>();
        Assert.NotNull(firstResult);
        Assert.False(firstResult!.Idempotent);
        Assert.Equal(50m, firstResult.Amount);

        var second = await client.PostAsJsonAsync("/v1/admin/refunds", firstBody);
        Assert.True(second.IsSuccessStatusCode);
        var secondResult = await second.Content.ReadFromJsonAsync<RefundResultView>();
        Assert.True(secondResult!.Idempotent);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var refunds = await verifyDb.OrderRefunds.AsNoTracking()
            .Where(r => r.PaymentTransactionId == sessionId)
            .ToListAsync();
        Assert.Single(refunds);
        Assert.Equal(50m, refunds[0].Amount);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("o"));
        var listResponse = await client.GetAsync($"/v1/admin/refunds?from={from}");
        listResponse.EnsureSuccessStatusCode();
        var listed = await listResponse.Content.ReadFromJsonAsync<List<RefundListView>>();
        Assert.Contains(listed!, r => r.PaymentTransactionId == sessionId);
    }

    [Fact]
    public async Task IssueRefund_RequiresRefundWritePermission()
    {
        using var client = CreateClient(role: "admin", permissions: AdminPermissions.BillingRead);
        var response = await client.PostAsJsonAsync("/v1/admin/refunds", new
        {
            checkoutSessionId = "cs_test_x",
            amountCents = 100L,
            reason = "test",
            idempotencyKey = Guid.NewGuid().ToString()
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ─────────────────────── Analytics ───────────────────────

    [Fact]
    public async Task RevenueEndpoint_ReturnsZeros_WhenEmpty()
    {
        using var client = CreateClient(role: "admin", permissions: AdminPermissions.BillingRead);
        var response = await client.GetAsync("/v1/admin/billing/revenue?from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("grossAmount", out _));
        Assert.True(json.TryGetProperty("netAmount", out _));
    }

    [Fact]
    public async Task AnalyticsEndpoint_ReturnsFrontendContractShape()
    {
        using var client = CreateClient(role: "admin", permissions: AdminPermissions.BillingRead);
        var response = await client.GetAsync("/v1/admin/billing/analytics?from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("available").GetBoolean());
        Assert.Equal("AUD", body.GetProperty("currency").GetString());
        Assert.Equal(JsonValueKind.Array, body.GetProperty("mrr").ValueKind);
        Assert.Equal(JsonValueKind.Array, body.GetProperty("churnRate").ValueKind);
        Assert.Equal(JsonValueKind.Array, body.GetProperty("ltv").ValueKind);
    }

    [Fact]
    public async Task MrrEndpoint_ReturnsZero_WhenNoSubscriptions()
    {
        using var client = CreateClient(role: "admin", permissions: AdminPermissions.BillingRead);
        var response = await client.GetAsync("/v1/admin/billing/mrr");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("mrrCents", out _));
    }

    [Fact]
    public async Task ChurnEndpoint_AcceptsPeriodParameter()
    {
        using var client = CreateClient(role: "admin", permissions: AdminPermissions.BillingRead);
        var response = await client.GetAsync("/v1/admin/billing/churn?period=30d");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task LtvEndpoint_AcceptsSegmentParameter()
    {
        using var client = CreateClient(role: "admin", permissions: AdminPermissions.BillingRead);
        var response = await client.GetAsync("/v1/admin/billing/ltv?segment=plan");
        response.EnsureSuccessStatusCode();
    }

    // ─────────────────────── Billing page copy ───────────────────────

    [Fact]
    public async Task BillingContentDelete_RemovesStoredOverrideRow()
    {
        const string key = "billing.page.title";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.BillingContentStrings.Add(new BillingContentString
            {
                Key = key,
                Section = "Page & hero",
                Value = "Custom billing title",
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                UpdatedByAdminId = "seed-admin",
                UpdatedByAdminName = "Seed Admin",
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateClient(role: "admin", permissions: AdminPermissions.BillingCatalogWrite);
        var response = await client.DeleteAsync($"/v1/admin/billing/content/{key}");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(key, body.GetProperty("key").GetString());
        Assert.True(body.GetProperty("deleted").GetBoolean());

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.False(await verifyDb.BillingContentStrings.AsNoTracking().AnyAsync(x => x.Key == key));
    }

    [Fact]
    public async Task BillingContentDelete_RejectsInvalidKey()
    {
        using var client = CreateClient(role: "admin", permissions: AdminPermissions.BillingCatalogWrite);

        var response = await client.DeleteAsync("/v1/admin/billing/content/billing.page.%20title");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("billing_content_invalid_key", body);
    }

    [Fact]
    public async Task BillingContentDelete_ReturnsDeletedFalseWhenOverrideIsMissing()
    {
        const string key = "billing.page.missing";
        using var client = CreateClient(role: "admin", permissions: AdminPermissions.BillingCatalogWrite);

        var response = await client.DeleteAsync($"/v1/admin/billing/content/{key}");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(key, body.GetProperty("key").GetString());
        Assert.False(body.GetProperty("deleted").GetBoolean());
    }

    // ─────────────────────── Stripe Tax ───────────────────────

    [Fact]
    public async Task StripeTaxList_ReturnsStripeNotConfigured_WhenNoSecretKey()
    {
        using var client = CreateClient(role: "admin", permissions: AdminPermissions.BillingRead);
        var response = await client.GetAsync("/v1/admin/billing/stripe-tax/registrations");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("stripe_not_configured", body.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task StripeTaxCreate_Returns501NotImplemented_ForCountryOptions()
    {
        using var client = CreateClient(role: "admin", permissions: AdminPermissions.BillingRead + "," + AdminPermissions.BillingCatalogWrite);
        var response = await client.PostAsJsonAsync("/v1/admin/billing/stripe-tax/registrations", new
        {
            country = "US",
            activeFrom = DateTimeOffset.UtcNow,
            type = "standard"
        });
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    // ─────────────────────── Helpers ───────────────────────

    private HttpClient CreateClient(string role, string? permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-Role", role);
        client.DefaultRequestHeaders.Add("X-Debug-UserId", $"admin-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{role}-{Guid.NewGuid():N}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", $"{role} probe");
        if (!string.IsNullOrWhiteSpace(permissions))
        {
            client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", permissions);
        }
        return client;
    }

    private sealed class DevAuthEnv : IDisposable
    {
        private const string Key = "Auth__UseDevelopmentAuth";
        private readonly string? _previous;
        private DevAuthEnv()
        {
            _previous = Environment.GetEnvironmentVariable(Key);
            Environment.SetEnvironmentVariable(Key, "true");
        }
        public static DevAuthEnv Enable() => new();
        public void Dispose() => Environment.SetEnvironmentVariable(Key, _previous);
    }

    private sealed record AdminProductView(
        Guid Id,
        string Code,
        string Name,
        string? Description,
        string ProductType,
        string? StripeProductId,
        bool IsActive,
        string? MetadataJson,
        List<AdminPriceView> Prices);

    private sealed record AdminPriceView(
        Guid Id,
        string? StripePriceId,
        string Currency,
        decimal Amount,
        string? Interval,
        int IntervalCount,
        string? Country,
        bool IsActive);

    private sealed record RefundResultView(
        Guid RefundId,
        string Status,
        string RefundType,
        decimal Amount,
        decimal RemainingAuthorisedAmount,
        bool ReversedWalletCredits,
        bool ReversedEntitlements,
        bool Idempotent);

    private sealed record RefundListView(
        Guid Id,
        string PaymentTransactionId,
        string LearnerUserId,
        string Gateway,
        string Status,
        string RefundType,
        decimal Amount,
        string Currency,
        string? Reason,
        string? RequestedByAdminId,
        DateTimeOffset CreatedAt);
}
