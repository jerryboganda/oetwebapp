using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Contracts;
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
        // Production validation caps idempotency keys at 38 ASCII token chars; a full
        // "chk-idem-{N}" string is 41 chars and would be rejected. Use a shortened
        // unique suffix that stays within the limit.
        var idempotencyKey = $"chk-{Guid.NewGuid():N}".Substring(0, 36);

        var first = await client.PostAsJsonAsync("/v1/billing/checkout-sessions", new
        {
            productType = "review_credits",
            quantity = 1,
            gateway = "whop",
            idempotencyKey
        });
        var firstBody = await first.Content.ReadAsStringAsync();
        Assert.True(first.IsSuccessStatusCode, firstBody);

        var second = await client.PostAsJsonAsync("/v1/billing/checkout-sessions", new
        {
            productType = "review_credits",
            quantity = 1,
            gateway = "whop",
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

    [Fact]
    public async Task CheckoutSession_AppliedUnpaidQuote_SameGateway_ReusesExistingSession()
    {
        var userId = $"chk-reuse-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);
        var quoteId = $"quote-reuse-{Guid.NewGuid():N}";
        var checkoutSessionId = $"whop_sandbox_{Guid.NewGuid():N}";
        var checkoutUrl = $"https://app.example.test/sandbox/whop?ref={quoteId}";
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == userId);
            db.BillingQuotes.Add(CreateOpenQuote(quoteId, userId, subscription.Id, subscription.PlanId, now, BillingQuoteStatus.Applied, checkoutSessionId));
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                LearnerUserId = userId,
                Gateway = "whop",
                GatewayTransactionId = checkoutSessionId,
                TransactionType = "one_time_purchase",
                Status = "pending",
                Amount = 20m,
                Currency = "AUD",
                ProductType = "addon",
                ProductId = quoteId,
                QuoteId = quoteId,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    quoteId,
                    productType = "review_credits",
                    providerIntentId = "plan_reuse1",
                    checkoutUrl
                }),
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync("/v1/billing/checkout-sessions", new
        {
            productType = "review_credits",
            quantity = 1,
            quoteId,
            gateway = "whop",
            idempotencyKey = $"reuse-{Guid.NewGuid():N}"[..36]
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(checkoutSessionId, json.RootElement.GetProperty("checkoutSessionId").GetString());
        Assert.Equal(checkoutUrl, json.RootElement.GetProperty("checkoutUrl").GetString());
        Assert.Equal("plan_reuse1", json.RootElement.GetProperty("clientSecret").GetString());

        await using var assertScope = _factory.Services.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(1, await assertDb.PaymentTransactions.CountAsync(x => x.QuoteId == quoteId));
        var quote = await assertDb.BillingQuotes.SingleAsync(x => x.Id == quoteId);
        Assert.Equal(BillingQuoteStatus.Applied, quote.Status);
        Assert.Equal(checkoutSessionId, quote.CheckoutSessionId);
    }

    [Fact]
    public async Task CheckoutSession_AppliedUnpaidQuote_OtherGateway_ReplacesSession()
    {
        var userId = $"chk-switch-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);
        var quoteId = $"quote-switch-{Guid.NewGuid():N}";
        var originalSessionId = $"whop_sandbox_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == userId);
            db.BillingQuotes.Add(CreateOpenQuote(quoteId, userId, subscription.Id, subscription.PlanId, now, BillingQuoteStatus.Applied, originalSessionId));
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                LearnerUserId = userId,
                Gateway = "whop",
                GatewayTransactionId = originalSessionId,
                TransactionType = "one_time_purchase",
                Status = "pending",
                Amount = 20m,
                Currency = "AUD",
                ProductType = "addon",
                ProductId = quoteId,
                QuoteId = quoteId,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    quoteId,
                    productType = "review_credits",
                    providerIntentId = "plan_switch1",
                    checkoutUrl = $"https://app.example.test/sandbox/whop?ref={quoteId}"
                }),
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync("/v1/billing/checkout-sessions", new
        {
            productType = "review_credits",
            quantity = 1,
            quoteId,
            gateway = "fawaterak",
            idempotencyKey = $"switch-{Guid.NewGuid():N}"[..36]
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        using var json = JsonDocument.Parse(body);
        var newSessionId = json.RootElement.GetProperty("checkoutSessionId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(newSessionId));
        Assert.NotEqual(originalSessionId, newSessionId);
        Assert.Equal("fawaterak", json.RootElement.GetProperty("gateway").GetString());

        await using var assertScope = _factory.Services.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var transactions = await assertDb.PaymentTransactions.Where(x => x.QuoteId == quoteId).ToListAsync();
        Assert.Equal(2, transactions.Count);
        var original = Assert.Single(transactions, x => x.GatewayTransactionId == originalSessionId);
        Assert.Equal("pending", original.Status);
        Assert.DoesNotContain("completed", original.Status, StringComparison.OrdinalIgnoreCase);
        var replacement = Assert.Single(transactions, x => x.GatewayTransactionId == newSessionId);
        Assert.Equal("fawaterak", replacement.Gateway);
        Assert.Equal("pending", replacement.Status);
        var quote = await assertDb.BillingQuotes.SingleAsync(x => x.Id == quoteId);
        Assert.Equal(BillingQuoteStatus.Applied, quote.Status);
        Assert.Equal(newSessionId, quote.CheckoutSessionId);
    }

    [Fact]
    public async Task CheckoutSession_CompletedQuote_StillConflicts()
    {
        var userId = $"chk-done-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);
        var quoteId = $"quote-done-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == userId);
            db.BillingQuotes.Add(CreateOpenQuote(quoteId, userId, subscription.Id, subscription.PlanId, now, BillingQuoteStatus.Completed, $"paid_{Guid.NewGuid():N}"));
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync("/v1/billing/checkout-sessions", new
        {
            productType = "review_credits",
            quantity = 1,
            quoteId,
            gateway = "fawaterak"
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("billing_quote_already_consumed", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PaymentStatus_AppliedQuote_ReturnsPendingByQuoteOrProviderSession()
    {
        var userId = $"chk-status-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);
        var quoteId = $"quote-status-{Guid.NewGuid():N}";
        var checkoutSessionId = $"cs_test_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == userId);
            var items = new[]
            {
                new BillingQuoteLineItem(
                    "plan",
                    subscription.PlanId ?? "starter",
                    "Starter course",
                    20m,
                    "AUD",
                    1,
                    "Locked course quote")
            };
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
                Status = BillingQuoteStatus.Applied,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(30),
                CheckoutSessionId = checkoutSessionId,
                SnapshotJson = JsonSerializer.Serialize(new { summary = "Starter course.", items })
            });
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                LearnerUserId = userId,
                Gateway = "stripe",
                GatewayTransactionId = checkoutSessionId,
                TransactionType = "subscription_payment",
                Status = "pending",
                Amount = 20m,
                Currency = "AUD",
                ProductType = "plan",
                ProductId = subscription.PlanId,
                QuoteId = quoteId,
                MetadataJson = JsonSerializer.Serialize(new { productType = "plan_purchase" }),
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        var byQuote = await client.GetAsync($"/v1/billing/payment-status?quoteId={Uri.EscapeDataString(quoteId)}");
        var byQuoteBody = await byQuote.Content.ReadAsStringAsync();
        Assert.True(byQuote.IsSuccessStatusCode, byQuoteBody);
        using (var json = JsonDocument.Parse(byQuoteBody))
        {
            Assert.Equal("pending", json.RootElement.GetProperty("status").GetString());
            Assert.Equal(quoteId, json.RootElement.GetProperty("quoteId").GetString());
            Assert.Equal(checkoutSessionId, json.RootElement.GetProperty("checkoutSessionId").GetString());
            Assert.Equal("plan_purchase", json.RootElement.GetProperty("productType").GetString());
            Assert.Equal(20m, json.RootElement.GetProperty("totalAmount").GetDecimal());
            Assert.Equal("Starter course", json.RootElement.GetProperty("items")[0].GetProperty("name").GetString());
        }

        var bySession = await client.GetAsync($"/v1/billing/payment-status?sessionId={Uri.EscapeDataString(checkoutSessionId)}");
        var bySessionBody = await bySession.Content.ReadAsStringAsync();
        Assert.True(bySession.IsSuccessStatusCode, bySessionBody);
        using var bySessionJson = JsonDocument.Parse(bySessionBody);
        Assert.Equal("pending", bySessionJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(quoteId, bySessionJson.RootElement.GetProperty("quoteId").GetString());
    }

    private static BillingQuote CreateOpenQuote(
        string quoteId,
        string userId,
        string subscriptionId,
        string? planId,
        DateTimeOffset now,
        BillingQuoteStatus status,
        string? checkoutSessionId)
        => new()
        {
            Id = quoteId,
            UserId = userId,
            SubscriptionId = subscriptionId,
            PlanCode = planId,
            Currency = "AUD",
            SubtotalAmount = 20m,
            DiscountAmount = 0m,
            TotalAmount = 20m,
            Status = status,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(30),
            CheckoutSessionId = checkoutSessionId,
            SnapshotJson = JsonSerializer.Serialize(new
            {
                summary = "Review credits.",
                items = new[]
                {
                    new BillingQuoteLineItem("addon", "review-credits", "Review credits", 20m, "AUD", 1, "Locked quote")
                }
            })
        };

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
