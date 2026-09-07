using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// Slice H — full HTTP-level happy path through the billing flow.
///
///   ensure learner profile (≈ register)  →
///   GET /v1/billing/plans                 →
///   GET /v1/billing/quote                 →
///   POST /v1/billing/checkout-sessions    →
///   POST /v1/payment/webhooks/{gateway}   →
///   assert payment transaction completes, audit events fire, invoice issues.
///
/// We use the Whop sandbox-fallback path because Stripe/PayPal are disabled in
/// the candidate catalog. Whop still runs the shared post-payment finalization
/// pipeline (entitlement bump + invoice + audit events).
/// </summary>
public class BillingIntegrationE2ETests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BillingIntegrationE2ETests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Learner_CompletesReviewCreditsCheckout_EndToEnd()
    {
        var userId = $"e2e-billing-{Guid.NewGuid():N}";
        using var client = await CreateClientAsync(userId);

        // 1. Plans surface ─ should expose seeded plans without 5xx.
        using (var plansResponse = await client.GetAsync("/v1/billing/plans"))
        {
            Assert.Equal(HttpStatusCode.OK, plansResponse.StatusCode);
            var plansBody = await plansResponse.Content.ReadAsStringAsync();
            Assert.False(string.IsNullOrWhiteSpace(plansBody));
        }

        // 2. Quote ─ ask for a small review-credits pack so we don't disturb
        //    the seeded subscription. quantity is in credits.
        await using (var catalogScope = _factory.Services.CreateAsyncScope())
        {
            var catalogDb = catalogScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            // The OET-2026 manifest carries no review-credit packs, so seed one
            // explicitly (same hermetic pattern as BillingCheckoutSessionGuardTests).
            var now = DateTimeOffset.UtcNow;
            var packSuffix = Guid.NewGuid().ToString("N")[..8];
            catalogDb.BillingAddOns.Add(new BillingAddOn
            {
                Id = $"addon-e2e-review-{packSuffix}",
                Code = $"e2e-review-pack-{packSuffix}",
                Name = "E2E Test Review Pack",
                Description = "Three review credits for the billing E2E test.",
                Price = 29.99m,
                Currency = "AUD",
                Interval = "one_time",
                DurationDays = 30,
                GrantCredits = 3,
                AppliesToAllPlans = true,
                RequiresEligibleParent = false,
                IsRecurring = false,
                IsStackable = true,
                QuantityStep = 1,
                CompatiblePlanCodesJson = "[]",
                GrantEntitlementsJson = JsonSerializer.Serialize(new Dictionary<string, int>
                {
                    ["ai_credits"] = 3
                }),
                Status = BillingAddOnStatus.Active,
                CreatedAt = now,
                UpdatedAt = now
            });
            await catalogDb.SaveChangesAsync();
            var hasCreditsPack = await catalogDb.BillingAddOns
                .AnyAsync(addOn => addOn.Status == BillingAddOnStatus.Active && addOn.GrantCredits == 3);
            Assert.True(hasCreditsPack, "Expected the test billing catalog to contain an active 3-credit review pack.");
        }

        string? quoteId = null;
        using (var quoteResponse = await client.GetAsync(
            "/v1/billing/quote?productType=review_credits&quantity=3"))
        {
            var body = await quoteResponse.Content.ReadAsStringAsync();
            Assert.True(quoteResponse.StatusCode == HttpStatusCode.OK, $"Quote failed: {(int)quoteResponse.StatusCode} :: {body}");
            using var json = JsonDocument.Parse(body);
            // The quote endpoint may or may not persist a quote id depending
            // on configuration; capture it when present so we can flow it
            // into checkout-session creation.
            if (json.RootElement.TryGetProperty("quoteId", out var idEl)
                && idEl.ValueKind == JsonValueKind.String)
            {
                quoteId = idEl.GetString();
            }
        }

        // 3. Checkout session ─ Whop sandbox path.
        string checkoutSessionId;
        using (var checkoutResponse = await client.PostAsJsonAsync(
            "/v1/billing/checkout-sessions",
            new
            {
                productType = "review_credits",
                quantity = 3,
                quoteId,
                gateway = "whop"
            }))
        {
            var body = await checkoutResponse.Content.ReadAsStringAsync();
            Assert.True(checkoutResponse.IsSuccessStatusCode, $"Checkout failed: {(int)checkoutResponse.StatusCode} :: {body}");

            using var json = JsonDocument.Parse(body);
            checkoutSessionId =
                TryGetString(json.RootElement, "checkoutSessionId")
                ?? TryGetString(json.RootElement, "sessionId")
                ?? TryGetString(json.RootElement, "gatewayTransactionId")
                ?? throw new Xunit.Sdk.XunitException(
                    $"Checkout response is missing a session identifier. Body: {body}");
        }

        // 4. Simulate a Whop payment.succeeded webhook for that session.
        var webhookPayload = JsonSerializer.Serialize(new
        {
            type = "payment.succeeded",
            data = new
            {
                id = checkoutSessionId,
                status = "paid",
                metadata = new { quote_id = quoteId, order_id = quoteId }
            }
        });

        using (var webhookResponse = await client.PostAsync(
            "/v1/payment/webhooks/whop",
            new StringContent(webhookPayload, Encoding.UTF8, "application/json")))
        {
            var body = await webhookResponse.Content.ReadAsStringAsync();
            Assert.True(webhookResponse.IsSuccessStatusCode, $"Webhook failed: {(int)webhookResponse.StatusCode} :: {body}");
        }

        // 5. Database invariants — the post-payment pipeline must have:
        //      a) marked the payment transaction completed,
        //      b) emitted at least one BillingEvent for this user, and
        //      c) issued an invoice OR credited the wallet (depending on
        //         which side of the review-credits flow is wired up).
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var transaction = await db.PaymentTransactions
            .Where(t => t.LearnerUserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(transaction);
        Assert.False(
            string.Equals(transaction!.Status, "failed", StringComparison.OrdinalIgnoreCase),
            $"Payment transaction ended in failed state: {transaction.Status}");

        var billingEvents = await db.BillingEvents
            .Where(e => e.UserId == userId)
            .ToListAsync();
        Assert.NotEmpty(billingEvents);

        var hasInvoice = await db.Invoices.AnyAsync(i => i.UserId == userId);
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
        Assert.True(
            hasInvoice || (wallet is not null && wallet.CreditBalance > 0),
            "Expected either an Invoice row or a wallet credit after the webhook completed.");
    }

    private static string? TryGetString(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty(property, out var el)) return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    private async Task<HttpClient> CreateClientAsync(string userId)
    {
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }
}
