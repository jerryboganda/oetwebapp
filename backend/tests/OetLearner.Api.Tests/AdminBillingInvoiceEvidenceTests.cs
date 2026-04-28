using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

[Collection("AuthFlows")]
public class AdminBillingInvoiceEvidenceTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public AdminBillingInvoiceEvidenceTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");
    }

    [Fact]
    public async Task AdminBillingInvoiceEvidence_ReturnsLinkedLocalEvidenceWithoutRawPayloads()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = $"invoice-evidence-user-{suffix}";
        var quoteId = $"quote-evidence-{suffix}";
        var checkoutSessionId = $"checkout-evidence-{suffix}";
        var subscriptionId = $"subscription-evidence-{suffix}";
        var invoiceId = $"invoice-evidence-{suffix}";
        const string rawSecret = "SECRET_RAW_PROVIDER_PAYLOAD_DO_NOT_EXPOSE";
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Users.Add(new LearnerUser
            {
                Id = userId,
                DisplayName = "Invoice Evidence Learner",
                Email = $"invoice-evidence-{suffix}@example.com",
                CreatedAt = now,
                LastActiveAt = now
            });
            db.BillingQuotes.Add(new BillingQuote
            {
                Id = quoteId,
                UserId = userId,
                SubscriptionId = subscriptionId,
                PlanCode = "starter",
                PlanVersionId = "plan-version-evidence-v1",
                AddOnCodesJson = JsonSerializer.Serialize(new[] { "credits-5" }),
                AddOnVersionIdsJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["credits-5"] = "addon-version-evidence-v1" }),
                CouponCode = "WELCOME10",
                CouponVersionId = "coupon-version-evidence-v1",
                Currency = "AUD",
                SubtotalAmount = 55m,
                DiscountAmount = 5.5m,
                TotalAmount = 49.5m,
                Status = BillingQuoteStatus.Completed,
                CreatedAt = now.AddMinutes(-10),
                ExpiresAt = now.AddDays(1),
                CheckoutSessionId = checkoutSessionId,
                SnapshotJson = JsonSerializer.Serialize(new
                {
                    summary = "Starter with credits",
                    items = new[]
                    {
                        new { kind = "plan", code = "starter", name = "Starter", amount = 49m, currency = "AUD", quantity = 1, description = "Monthly access" },
                        new { kind = "addon", code = "credits-5", name = "Credits 5", amount = 6m, currency = "AUD", quantity = 1, description = "Review credits" }
                    },
                    validation = new { valid = true }
                })
            });
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                LearnerUserId = userId,
                Gateway = "paypal",
                GatewayTransactionId = checkoutSessionId,
                TransactionType = "subscription_payment",
                Status = "completed",
                Amount = 49.5m,
                Currency = "AUD",
                ProductType = "plan",
                ProductId = "starter",
                QuoteId = quoteId,
                PlanVersionId = "plan-version-evidence-v1",
                AddOnVersionIdsJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["credits-5"] = "addon-version-evidence-v1" }),
                CouponVersionId = "coupon-version-evidence-v1",
                MetadataJson = JsonSerializer.Serialize(new { rawSecret }),
                CreatedAt = now.AddMinutes(-9),
                UpdatedAt = now.AddMinutes(-1)
            });
            db.BillingCouponRedemptions.Add(new BillingCouponRedemption
            {
                Id = $"redemption-evidence-{suffix}",
                CouponCode = "WELCOME10",
                CouponId = $"coupon-{suffix}",
                CouponVersionId = "coupon-version-evidence-v1",
                UserId = userId,
                QuoteId = quoteId,
                CheckoutSessionId = checkoutSessionId,
                SubscriptionId = subscriptionId,
                DiscountAmount = 5.5m,
                Currency = "AUD",
                Status = BillingRedemptionStatus.Applied,
                RedeemedAt = now.AddMinutes(-8)
            });
            db.SubscriptionItems.Add(new SubscriptionItem
            {
                Id = $"subscription-item-evidence-{suffix}",
                SubscriptionId = subscriptionId,
                ItemType = "addon",
                ItemCode = "credits-5",
                AddOnVersionId = "addon-version-evidence-v1",
                Quantity = 1,
                Status = SubscriptionItemStatus.Active,
                StartsAt = now.AddMinutes(-7),
                EndsAt = now.AddDays(30),
                QuoteId = quoteId,
                CheckoutSessionId = checkoutSessionId,
                CreatedAt = now.AddMinutes(-7),
                UpdatedAt = now.AddMinutes(-7)
            });
            db.Invoices.Add(new Invoice
            {
                Id = invoiceId,
                UserId = userId,
                IssuedAt = now,
                Amount = 49.5m,
                Currency = "AUD",
                Status = "Paid",
                Description = "Starter with credits",
                PlanVersionId = "plan-version-evidence-v1",
                AddOnVersionIdsJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["credits-5"] = "addon-version-evidence-v1" }),
                CouponVersionId = "coupon-version-evidence-v1",
                QuoteId = quoteId,
                CheckoutSessionId = checkoutSessionId
            });
            db.BillingEvents.Add(new BillingEvent
            {
                Id = $"billing-event-evidence-{suffix}",
                UserId = userId,
                SubscriptionId = subscriptionId,
                QuoteId = quoteId,
                EventType = "checkout_completed",
                EntityType = "PaymentTransaction",
                EntityId = checkoutSessionId,
                PayloadJson = JsonSerializer.Serialize(new { rawSecret }),
                OccurredAt = now
            });
            db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
            {
                Id = Guid.NewGuid(),
                Gateway = "paypal",
                EventType = "payment.completed",
                GatewayEventId = $"gateway-event-{suffix}",
                ProcessingStatus = "completed",
                PayloadJson = JsonSerializer.Serialize(new { rawSecret }),
                ReceivedAt = now.AddMinutes(-6),
                ProcessedAt = now.AddMinutes(-5)
            });

            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/v1/admin/billing/invoices/{Uri.EscapeDataString(invoiceId)}/evidence");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(rawSecret, body, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(invoiceId, root.GetProperty("invoice").GetProperty("id").GetString());
        Assert.Equal("Invoice Evidence Learner", root.GetProperty("invoice").GetProperty("userName").GetString());
        Assert.Equal(quoteId, root.GetProperty("quote").GetProperty("id").GetString());
        Assert.Equal(checkoutSessionId, root.GetProperty("payments")[0].GetProperty("gatewayTransactionId").GetString());
        Assert.Equal("WELCOME10", root.GetProperty("redemptions")[0].GetProperty("couponCode").GetString());
        Assert.Equal("credits-5", root.GetProperty("subscriptionItems")[0].GetProperty("itemCode").GetString());
        Assert.Equal("checkout_completed", root.GetProperty("events")[0].GetProperty("eventType").GetString());
        Assert.Equal("invoice", root.GetProperty("catalogAnchors").GetProperty("source").GetString());
        Assert.Equal("plan-version-evidence-v1", root.GetProperty("catalogAnchors").GetProperty("planVersionId").GetString());
        Assert.Empty(root.GetProperty("notRecorded").EnumerateArray());
        Assert.Empty(root.GetProperty("integrityFlags").EnumerateArray());
    }

    [Fact]
    public async Task AdminBillingInvoiceEvidence_ReturnsPartialLegacyEvidence()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = $"legacy-invoice-user-{suffix}";
        var invoiceId = $"legacy-invoice-{suffix}";
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Users.Add(new LearnerUser
            {
                Id = userId,
                DisplayName = "Legacy Invoice Learner",
                Email = $"legacy-invoice-{suffix}@example.com",
                CreatedAt = now,
                LastActiveAt = now
            });
            db.Invoices.Add(new Invoice
            {
                Id = invoiceId,
                UserId = userId,
                IssuedAt = now,
                Amount = 25m,
                Currency = "AUD",
                Status = "Paid",
                Description = "Legacy wallet top-up"
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/v1/admin/billing/invoices/{Uri.EscapeDataString(invoiceId)}/evidence");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(invoiceId, root.GetProperty("invoice").GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("quote").ValueKind);
        Assert.Empty(root.GetProperty("payments").EnumerateArray());
        Assert.Empty(root.GetProperty("events").EnumerateArray());
        Assert.Equal("not_recorded", root.GetProperty("catalogAnchors").GetProperty("source").GetString());
        var notRecorded = root.GetProperty("notRecorded").EnumerateArray().Select(item => item.GetString()).ToList();
        Assert.Contains("quote", notRecorded);
        Assert.Contains("payment", notRecorded);
        Assert.Contains("events", notRecorded);
        Assert.Contains("catalogAnchors", notRecorded);
    }

    [Fact]
    public async Task AdminBillingInvoiceEvidence_CorrelatesLegacyWalletTopUpInvoiceByInvoiceId()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = $"wallet-invoice-user-{suffix}";
        var gatewayTransactionId = $"wallet-checkout-{suffix}-{new string('x', 80)}";
        var invoiceId = $"inv-topup-{gatewayTransactionId}"[..64];
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Users.Add(new LearnerUser
            {
                Id = userId,
                DisplayName = "Wallet Invoice Learner",
                Email = $"wallet-invoice-{suffix}@example.com",
                CreatedAt = now,
                LastActiveAt = now
            });
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                LearnerUserId = userId,
                Gateway = "stripe",
                GatewayTransactionId = gatewayTransactionId,
                TransactionType = "wallet_top_up",
                Status = "completed",
                Amount = 25m,
                Currency = "AUD",
                ProductType = "wallet_top_up",
                ProductId = $"wallet-{suffix}",
                CreatedAt = now.AddMinutes(-5),
                UpdatedAt = now
            });
            db.Invoices.Add(new Invoice
            {
                Id = invoiceId,
                UserId = userId,
                IssuedAt = now,
                Amount = 25m,
                Currency = "AUD",
                Status = "Paid",
                Description = "Wallet top-up: 28 credits"
            });
            db.BillingEvents.Add(new BillingEvent
            {
                Id = $"wallet-event-{suffix}",
                UserId = userId,
                EventType = "wallet_top_up_completed",
                EntityType = "PaymentTransaction",
                EntityId = gatewayTransactionId,
                PayloadJson = JsonSerializer.Serialize(new { internalOnly = true }),
                OccurredAt = now
            });

            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/v1/admin/billing/invoices/{Uri.EscapeDataString(invoiceId)}/evidence");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(gatewayTransactionId, root.GetProperty("payments")[0].GetProperty("gatewayTransactionId").GetString());
        Assert.Equal("wallet_top_up_completed", root.GetProperty("events")[0].GetProperty("eventType").GetString());
        var notRecorded = root.GetProperty("notRecorded").EnumerateArray().Select(item => item.GetString()).ToList();
        Assert.DoesNotContain("payment", notRecorded);
        Assert.DoesNotContain("events", notRecorded);
        Assert.Contains("quote", notRecorded);
    }

    [Fact]
    public async Task AdminBillingInvoiceEvidence_FlagsAmbiguousLegacyWalletTopUpCorrelation()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = $"wallet-ambiguous-user-{suffix}";
        var gatewayPrefix = $"wallet-ambiguous-{suffix}";
        var firstGatewayTransactionId = $"{gatewayPrefix}-first-{new string('x', 80)}";
        var secondGatewayTransactionId = $"{gatewayPrefix}-second-{new string('y', 80)}";
        var invoiceId = $"inv-topup-{gatewayPrefix}";
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Users.Add(new LearnerUser
            {
                Id = userId,
                DisplayName = "Ambiguous Wallet Learner",
                Email = $"wallet-ambiguous-{suffix}@example.com",
                CreatedAt = now,
                LastActiveAt = now
            });
            db.PaymentTransactions.AddRange(
                new PaymentTransaction
                {
                    Id = Guid.NewGuid(),
                    LearnerUserId = userId,
                    Gateway = "stripe",
                    GatewayTransactionId = firstGatewayTransactionId,
                    TransactionType = "wallet_top_up",
                    Status = "completed",
                    Amount = 25m,
                    Currency = "AUD",
                    ProductType = "wallet_top_up",
                    ProductId = $"wallet-{suffix}",
                    CreatedAt = now.AddMinutes(-5),
                    UpdatedAt = now
                },
                new PaymentTransaction
                {
                    Id = Guid.NewGuid(),
                    LearnerUserId = userId,
                    Gateway = "stripe",
                    GatewayTransactionId = secondGatewayTransactionId,
                    TransactionType = "wallet_top_up",
                    Status = "completed",
                    Amount = 25m,
                    Currency = "AUD",
                    ProductType = "wallet_top_up",
                    ProductId = $"wallet-{suffix}",
                    CreatedAt = now.AddMinutes(-4),
                    UpdatedAt = now
                });
            db.Invoices.Add(new Invoice
            {
                Id = invoiceId,
                UserId = userId,
                IssuedAt = now,
                Amount = 25m,
                Currency = "AUD",
                Status = "Paid",
                Description = "Wallet top-up: ambiguous legacy invoice"
            });

            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/v1/admin/billing/invoices/{Uri.EscapeDataString(invoiceId)}/evidence");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        var integrityFlags = json.RootElement.GetProperty("integrityFlags").EnumerateArray().Select(item => item.GetString()).ToList();
        Assert.Contains("legacy_wallet_top_up_correlation_ambiguous", integrityFlags);
    }

    [Fact]
    public async Task AdminBillingInvoiceEvidence_ReturnsNotFoundForMissingInvoice()
    {
        var response = await _client.GetAsync("/v1/admin/billing/invoices/missing-invoice/evidence");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("billing_invoice_not_found", body);
    }
}
