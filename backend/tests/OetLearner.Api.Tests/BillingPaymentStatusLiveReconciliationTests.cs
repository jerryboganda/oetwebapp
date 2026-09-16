using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// Checkout expiry/payment-flow hardening: <c>GetBillingPaymentStatusAsync</c> now
/// attempts a live provider re-check (via <c>BillingReconciliationWorker.RecoverPaymentAsync</c>)
/// for any pending transaction on a gateway with a server-to-server status lookup — not just
/// Fawaterak — before ever answering "expired". This pins the safe-degradation contract: with
/// no gateway credentials configured (the default test/CI state), the live check cannot confirm
/// payment, so the endpoint must still answer cleanly (200, "expired") rather than hang or throw,
/// and it must carry the exact copy the candidate is shown ("Your checkout session has expired.
/// Please start a new checkout.") — never a bare "Payment Failed".
/// </summary>
public sealed class BillingPaymentStatusLiveReconciliationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BillingPaymentStatusLiveReconciliationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ExpiredWhopQuote_WithNoProviderConfirmation_ReportsExpired_NotFailed()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"whop-status-user-{suffix}";
        var quoteId = $"whop-quote-{suffix}";
        var checkoutSessionId = $"ch_{suffix}";

        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", "Whop Checkout Candidate");

        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var now = DateTimeOffset.UtcNow;

            db.BillingQuotes.Add(new BillingQuote
            {
                Id = quoteId,
                UserId = userId,
                Currency = "GBP",
                SubtotalAmount = 100m,
                TotalAmount = 100m,
                Status = BillingQuoteStatus.Applied,
                CreatedAt = now.AddMinutes(-20),
                ExpiresAt = now.AddMinutes(-5), // the 15-minute window is long over
                CheckoutSessionId = checkoutSessionId,
                SnapshotJson = "{}",
            });

            // The checkout session was created and a payment attempt is (still) in
            // flight at the gateway — this is the exact 15 Sep 2026 P0 shape: the
            // local quote clock ran out before the gateway resolved the payment.
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                LearnerUserId = userId,
                Gateway = PaymentGatewayNames.Whop,
                GatewayTransactionId = checkoutSessionId,
                TransactionType = "one_time_purchase",
                Status = "pending",
                Amount = 100m,
                Currency = "GBP",
                ProductType = "addon",
                ProductId = "pkg_test",
                QuoteId = quoteId,
                CreatedAt = now.AddMinutes(-20),
                UpdatedAt = now.AddMinutes(-20),
            });

            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);

        var response = await client.GetAsync($"/v1/billing/payment-status?quoteId={quoteId}");

        // The live re-check must never turn a missing/unavailable provider answer
        // into a hang or a 500 — the endpoint always answers.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = payload.RootElement;

        Assert.Equal("expired", root.GetProperty("status").GetString());
        Assert.Equal(
            "Your checkout session has expired. Please start a new checkout.",
            root.GetProperty("failureReason").GetString());
    }
}
