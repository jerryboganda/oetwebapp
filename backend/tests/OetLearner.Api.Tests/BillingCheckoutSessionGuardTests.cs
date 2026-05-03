using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class BillingCheckoutSessionGuardTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BillingCheckoutSessionGuardTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CheckoutSession_ExpiredQuoteId_ReturnsBillingQuoteExpired()
    {
        var userId = $"chk-expired-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);
        var quoteId = $"quote-expired-{Guid.NewGuid():N}";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == userId);
            var now = DateTimeOffset.UtcNow;
            db.BillingQuotes.Add(new BillingQuote
            {
                Id = quoteId,
                UserId = userId,
                SubscriptionId = subscription.Id,
                PlanCode = subscription.PlanId,
                Currency = "AUD",
                SubtotalAmount = 20m,
                DiscountAmount = 0m,
                TotalAmount = 20m,
                Status = BillingQuoteStatus.Created,
                CreatedAt = now.AddHours(-2),
                ExpiresAt = now.AddMinutes(-30),
                SnapshotJson = "{}"
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync("/v1/billing/checkout-sessions", new
        {
            productType = "review_credits",
            quantity = 1,
            quoteId,
            gateway = "paypal"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("billing_quote_expired", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CheckoutSession_QuoteOwnedByAnotherUser_ReturnsNotFound()
    {
        var ownerUserId = $"chk-owner-{Guid.NewGuid():N}";
        var attackerUserId = $"chk-attacker-{Guid.NewGuid():N}";
        using var ownerClient = await CreateClientForUserAsync(ownerUserId);
        using var attackerClient = await CreateClientForUserAsync(attackerUserId);
        _ = ownerClient; // silence unused: we only need the owner profile created

        var quoteId = $"quote-owned-{Guid.NewGuid():N}";
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == ownerUserId);
            var now = DateTimeOffset.UtcNow;
            db.BillingQuotes.Add(new BillingQuote
            {
                Id = quoteId,
                UserId = ownerUserId,
                SubscriptionId = subscription.Id,
                PlanCode = subscription.PlanId,
                Currency = "AUD",
                SubtotalAmount = 20m,
                DiscountAmount = 0m,
                TotalAmount = 20m,
                Status = BillingQuoteStatus.Created,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(30),
                SnapshotJson = "{}"
            });
            await db.SaveChangesAsync();
        }

        var response = await attackerClient.PostAsJsonAsync("/v1/billing/checkout-sessions", new
        {
            productType = "review_credits",
            quantity = 1,
            quoteId,
            gateway = "paypal"
        });

        // The quote lookup is scoped to the requesting user, so cross-user access
        // must be rejected. Implementation surfaces this as a 404 not_found rather
        // than leaking that the quote exists for another user.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var code = json.RootElement.GetProperty("code").GetString();
        Assert.True(
            code is "billing_quote_not_found" or "quote_mismatch",
            $"Expected billing_quote_not_found or quote_mismatch but got '{code}'.");
    }

    [Fact]
    public async Task CheckoutSession_SameIdempotencyKey_ReturnsSameCheckoutUrl()
    {
        var userId = $"chk-idem-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);
        var idempotencyKey = $"chk-idem-{Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync("/v1/billing/checkout-sessions", new
        {
            productType = "review_credits",
            quantity = 1,
            gateway = "paypal",
            idempotencyKey
        });
        var firstBody = await first.Content.ReadAsStringAsync();
        Assert.True(first.IsSuccessStatusCode, firstBody);

        var second = await client.PostAsJsonAsync("/v1/billing/checkout-sessions", new
        {
            productType = "review_credits",
            quantity = 1,
            gateway = "paypal",
            idempotencyKey
        });
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.True(second.IsSuccessStatusCode, secondBody);

        using var firstJson = JsonDocument.Parse(firstBody);
        using var secondJson = JsonDocument.Parse(secondBody);

        Assert.Equal(
            firstJson.RootElement.GetProperty("checkoutUrl").GetString(),
            secondJson.RootElement.GetProperty("checkoutUrl").GetString());
        Assert.Equal(
            firstJson.RootElement.GetProperty("checkoutSessionId").GetString(),
            secondJson.RootElement.GetProperty("checkoutSessionId").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var paymentTransactionCount = await db.PaymentTransactions.CountAsync(x => x.LearnerUserId == userId);
        Assert.Equal(1, paymentTransactionCount);
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
