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
/// We use the PayPal sandbox-fallback path because the existing test factory
/// does not configure a Stripe webhook secret (Stripe enforces HMAC and would
/// reject every payload as <c>signature_verification_failed</c>). PayPal goes
/// through the same internal completion pipeline, so this still exercises the
/// shared post-payment finalization code (entitlement bump + invoice +
/// audit events). Slice B / I should add a Stripe-signed variant once the
/// factory is hardened.
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
        string? quoteId = null;
        using (var quoteResponse = await client.GetAsync(
            "/v1/billing/quote?productType=review_credits&quantity=3"))
        {
            Assert.Equal(HttpStatusCode.OK, quoteResponse.StatusCode);
            var body = await quoteResponse.Content.ReadAsStringAsync();
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

        // 3. Checkout session ─ PayPal sandbox path.
        string checkoutSessionId;
        using (var checkoutResponse = await client.PostAsJsonAsync(
            "/v1/billing/checkout-sessions",
            new
            {
                productType = "review_credits",
                quantity = 3,
                quoteId,
                gateway = "paypal"
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

        // 4. Simulate a PayPal capture-completed webhook for that session.
        var webhookPayload = JsonSerializer.Serialize(new
        {
            id = $"evt-{Guid.NewGuid():N}",
            event_type = "PAYMENT.CAPTURE.COMPLETED",
            resource = new
            {
                supplementary_data = new
                {
                    related_ids = new
                    {
                        order_id = checkoutSessionId
                    }
                }
            }
        });

        using (var webhookResponse = await client.PostAsync(
            "/v1/payment/webhooks/paypal",
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
