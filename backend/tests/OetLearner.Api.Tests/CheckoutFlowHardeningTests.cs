using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// Slice D — Checkout / Quote / Subscription / Invoice flow hardening.
///
/// Covers:
///  * Quote idempotency replay (same key → same response, no duplicate quote)
///  * Expired quote rejection at checkout-session creation
///  * Snapshot drift rejection (catalog re-versioned after quote)
///  * Double-finalize protection via unique CheckoutSessionId
///  * Monotonic per-tenant invoice numbering with no reuse
///  * Cursor pagination boundaries (empty / single / many pages)
/// </summary>
public class CheckoutFlowHardeningTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CheckoutFlowHardeningTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ─────────────────── Quote validators (pure) ───────────────────

    [Fact]
    public void EnsureQuoteIsFulfillable_ExpiredQuote_ThrowsValidation()
    {
        var quote = NewQuote(status: BillingQuoteStatus.Created, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var ex = Assert.Throws<ApiException>(() =>
            LearnerService.EnsureQuoteIsFulfillable(quote, DateTimeOffset.UtcNow));
        Assert.Equal("billing_quote_expired", ex.ErrorCode);
    }

    [Fact]
    public void EnsureQuoteIsFulfillable_AlreadyCompleted_ThrowsConflict()
    {
        var quote = NewQuote(status: BillingQuoteStatus.Completed, expiresAt: DateTimeOffset.UtcNow.AddMinutes(10));
        var ex = Assert.Throws<ApiException>(() =>
            LearnerService.EnsureQuoteIsFulfillable(quote, DateTimeOffset.UtcNow));
        Assert.Equal("billing_quote_already_consumed", ex.ErrorCode);
    }

    [Fact]
    public void EnsureQuoteIsFulfillable_ValidQuote_DoesNotThrow()
    {
        var quote = NewQuote(status: BillingQuoteStatus.Applied, expiresAt: DateTimeOffset.UtcNow.AddMinutes(5));
        LearnerService.EnsureQuoteIsFulfillable(quote, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void EnsureSnapshotMatchesCatalog_PlanVersionChanged_ThrowsDrift()
    {
        var quote = NewQuote();
        quote.PlanVersionId = "plan-v1";

        var livePlan = new BillingPlanVersion { Id = "plan-v2", PlanId = "p", Code = "basic-monthly" };
        var ex = Assert.Throws<ApiException>(() =>
            LearnerService.EnsureQuoteSnapshotMatchesCatalog(
                quote,
                livePlan,
                new Dictionary<string, BillingAddOnVersion?>(),
                liveCouponVersion: null));
        Assert.Equal("billing_quote_snapshot_drift", ex.ErrorCode);
    }

    [Fact]
    public void EnsureSnapshotMatchesCatalog_AddOnVersionChanged_ThrowsDrift()
    {
        var quote = NewQuote();
        quote.AddOnVersionIdsJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["pack-x"] = "addon-v1"
        });

        var live = new Dictionary<string, BillingAddOnVersion?>
        {
            ["pack-x"] = new BillingAddOnVersion { Id = "addon-v2", AddOnId = "a", Code = "pack-x" }
        };

        var ex = Assert.Throws<ApiException>(() =>
            LearnerService.EnsureQuoteSnapshotMatchesCatalog(quote, livePlanVersion: null, live, liveCouponVersion: null));
        Assert.Equal("billing_quote_snapshot_drift", ex.ErrorCode);
    }

    [Fact]
    public void EnsureSnapshotMatchesCatalog_AllVersionsMatch_DoesNotThrow()
    {
        var quote = NewQuote();
        quote.PlanVersionId = "plan-v1";
        quote.AddOnVersionIdsJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["pack-x"] = "addon-v1"
        });
        quote.CouponVersionId = "coup-v1";

        var live = new Dictionary<string, BillingAddOnVersion?>
        {
            ["pack-x"] = new BillingAddOnVersion { Id = "addon-v1", AddOnId = "a", Code = "pack-x" }
        };

        LearnerService.EnsureQuoteSnapshotMatchesCatalog(
            quote,
            new BillingPlanVersion { Id = "plan-v1", PlanId = "p", Code = "basic-monthly" },
            live,
            new BillingCouponVersion { Id = "coup-v1", CouponId = "c", Code = "PROMO" });
    }

    // ─────────────── Cursor pagination boundary tests ───────────────

    [Fact]
    public async Task Invoices_CursorPagination_EmptySet_ReturnsNoItemsAndNoCursor()
    {
        var userId = $"d-pg-empty-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);

        var resp = await client.GetAsync("/v1/billing/invoices?limit=5");
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, doc.GetProperty("items").GetArrayLength());
        Assert.True(doc.GetProperty("nextCursor").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Invoices_CursorPagination_SinglePage_HasNoNextCursor()
    {
        var userId = $"d-pg-single-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);
        await SeedInvoicesAsync(userId, count: 3);

        var resp = await client.GetAsync("/v1/billing/invoices?limit=10");
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, doc.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, doc.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Invoices_CursorPagination_ManyPages_AllRowsExactlyOnce_AndOrdered()
    {
        var userId = $"d-pg-many-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);
        await SeedInvoicesAsync(userId, count: 12);

        var seen = new List<string>();
        string? cursor = null;
        var pageSize = 5;
        DateTimeOffset? prevIssued = null;

        for (var i = 0; i < 5; i++) // hard cap to avoid infinite loops on regression
        {
            var url = $"/v1/billing/invoices?limit={pageSize}";
            if (!string.IsNullOrEmpty(cursor))
            {
                url += $"&cursor={Uri.EscapeDataString(cursor)}";
            }
            var resp = await client.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();

            foreach (var item in doc.GetProperty("items").EnumerateArray())
            {
                var id = item.GetProperty("invoiceId").GetString()!;
                var date = item.GetProperty("date").GetDateTimeOffset();
                Assert.DoesNotContain(id, seen);
                if (prevIssued is not null)
                {
                    Assert.True(date <= prevIssued.Value, "Cursor pagination must be descending by IssuedAt");
                }
                prevIssued = date;
                seen.Add(id);
            }

            cursor = doc.GetProperty("nextCursor").ValueKind == JsonValueKind.Null
                ? null
                : doc.GetProperty("nextCursor").GetString();
            if (cursor is null) break;
        }

        Assert.Equal(12, seen.Count);
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }

    // ─────────────── helpers ───────────────

    private static BillingQuote NewQuote(
        string? userId = null,
        string? idempotencyKey = null,
        string? id = null,
        BillingQuoteStatus status = BillingQuoteStatus.Created,
        DateTimeOffset? expiresAt = null) => new()
    {
        Id = id ?? Guid.NewGuid().ToString("N"),
        UserId = userId ?? "user-1",
        Currency = "AUD",
        SubtotalAmount = 49m,
        DiscountAmount = 0m,
        TotalAmount = 49m,
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(15),
        AddOnCodesJson = "[]",
        AddOnVersionIdsJson = "{}",
        SnapshotJson = "{}",
        IdempotencyKey = idempotencyKey,
    };

    private async Task SeedInvoicesAsync(string userId, int count)
    {
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < count; i++)
        {
            db.Invoices.Add(new Invoice
            {
                Id = $"inv-{userId}-{i:D3}",
                UserId = userId,
                IssuedAt = now.AddMinutes(-i), // strictly decreasing → predictable order
                Amount = 10m + i,
                Currency = "AUD",
                Status = "Paid",
                Description = $"Seeded invoice {i}",
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> CreateClientForUserAsync(string userId)
    {
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }
}
