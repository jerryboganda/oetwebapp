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
public class AdminBillingProviderLifecycleSignalsTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public AdminBillingProviderLifecycleSignalsTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");
    }

    [Fact]
    public async Task AdminBillingProviderLifecycleSignals_RequiresBillingReadPermission()
    {
        var previous = Environment.GetEnvironmentVariable("Auth__UseDevelopmentAuth");
        Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", "true");
        try
        {
            using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
            client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", AdminPermissions.ContentRead);

            var response = await client.GetAsync("/v1/admin/billing/provider-lifecycle-signals");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", previous);
        }
    }

    [Fact]
    public async Task AdminBillingProviderLifecycleSignals_ReturnsSanitizedLinkedUnmatchedAndSummaryRows()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = $"provider-signal-user-{suffix}";
        var quoteId = $"quote-provider-signal-{suffix}";
        var checkoutSessionId = $"checkout-provider-signal-{suffix}";
        var subscriptionId = $"subscription-provider-signal-{suffix}";
        var invoiceId = $"invoice-provider-signal-{suffix}";
        var billingEventId = $"billing-event-provider-signal-{suffix}";
        var paymentId = Guid.NewGuid();
        const string rawSecret = "SECRET_PROVIDER_SIGNAL_RAW_PAYLOAD_DO_NOT_EXPOSE";
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Users.Add(new LearnerUser
            {
                Id = userId,
                DisplayName = "Provider Signal Learner",
                Email = $"provider-signal-{suffix}@example.com",
                CreatedAt = now,
                LastActiveAt = now
            });
            db.Subscriptions.Add(new Subscription
            {
                Id = subscriptionId,
                UserId = userId,
                PlanId = "starter",
                Status = SubscriptionStatus.Active,
                NextRenewalAt = now.AddMonths(1),
                StartedAt = now.AddMinutes(-20),
                ChangedAt = now.AddMinutes(-5),
                PriceAmount = 49m,
                Currency = "AUD",
                Interval = "monthly"
            });
            db.BillingQuotes.Add(new BillingQuote
            {
                Id = quoteId,
                UserId = userId,
                SubscriptionId = subscriptionId,
                PlanCode = "starter",
                Currency = "AUD",
                SubtotalAmount = 49m,
                DiscountAmount = 0m,
                TotalAmount = 49m,
                Status = BillingQuoteStatus.Completed,
                CreatedAt = now.AddMinutes(-15),
                ExpiresAt = now.AddDays(1),
                CheckoutSessionId = checkoutSessionId,
                SnapshotJson = JsonSerializer.Serialize(new { summary = "Starter", items = Array.Empty<object>() })
            });
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = paymentId,
                LearnerUserId = userId,
                Gateway = "paypal",
                GatewayTransactionId = checkoutSessionId,
                TransactionType = "subscription_payment",
                Status = "completed",
                Amount = 49m,
                Currency = "AUD",
                ProductType = "plan",
                ProductId = "starter",
                QuoteId = quoteId,
                AddOnVersionIdsJson = JsonSerializer.Serialize(new Dictionary<string, string>()),
                MetadataJson = JsonSerializer.Serialize(new { rawSecret }),
                CreatedAt = now.AddMinutes(-14),
                UpdatedAt = now.AddMinutes(-4)
            });
            db.Invoices.Add(new Invoice
            {
                Id = invoiceId,
                UserId = userId,
                IssuedAt = now.AddMinutes(-3),
                Amount = 49m,
                Currency = "AUD",
                Status = "Paid",
                Description = "Starter",
                QuoteId = quoteId,
                CheckoutSessionId = checkoutSessionId
            });
            db.BillingEvents.Add(new BillingEvent
            {
                Id = billingEventId,
                UserId = userId,
                SubscriptionId = subscriptionId,
                QuoteId = quoteId,
                EventType = "checkout_completed",
                EntityType = "PaymentTransaction",
                EntityId = checkoutSessionId,
                PayloadJson = JsonSerializer.Serialize(new { rawSecret }),
                OccurredAt = now.AddMinutes(-3)
            });
            db.PaymentWebhookEvents.AddRange(
                new PaymentWebhookEvent
                {
                    Id = Guid.NewGuid(),
                    Gateway = "paypal",
                    EventType = "checkout.session.completed",
                    GatewayEventId = $"evt_checkout_provider_signal_{suffix}_abcdef123456",
                    GatewayTransactionId = checkoutSessionId,
                    ProcessingStatus = "completed",
                    VerificationStatus = "verified",
                    NormalizedStatus = "completed",
                    PayloadJson = JsonSerializer.Serialize(new { rawSecret }),
                    ErrorMessage = rawSecret,
                    ReceivedAt = now,
                    ProcessedAt = now.AddMinutes(1)
                },
                new PaymentWebhookEvent
                {
                    Id = Guid.NewGuid(),
                    Gateway = "stripe",
                    EventType = "charge.refunded",
                    GatewayEventId = $"evt_refund_provider_signal_{suffix}_abcdef123456",
                    GatewayTransactionId = $"refund-provider-signal-{suffix}",
                    ProcessingStatus = "failed",
                    VerificationStatus = "verified",
                    NormalizedStatus = "refunded",
                    PayloadJson = JsonSerializer.Serialize(new { rawSecret }),
                    ErrorMessage = rawSecret,
                    ReceivedAt = now.AddMinutes(-1)
                },
                new PaymentWebhookEvent
                {
                    Id = Guid.NewGuid(),
                    Gateway = "stripe",
                    EventType = "charge.dispute.created",
                    GatewayEventId = $"evt_dispute_provider_signal_{suffix}_abcdef123456",
                    GatewayTransactionId = $"dispute-provider-signal-{suffix}",
                    ProcessingStatus = "completed",
                    VerificationStatus = "failed",
                    NormalizedStatus = "disputed",
                    PayloadJson = JsonSerializer.Serialize(new { rawSecret }),
                    ErrorMessage = rawSecret,
                    ReceivedAt = now.AddMinutes(-2)
                },
                new PaymentWebhookEvent
                {
                    Id = Guid.NewGuid(),
                    Gateway = "paypal",
                    EventType = "customer.subscription.deleted",
                    GatewayEventId = $"evt_cancel_provider_signal_{suffix}_abcdef123456",
                    GatewayTransactionId = null,
                    ProcessingStatus = "completed",
                    VerificationStatus = "legacy",
                    NormalizedStatus = "cancelled",
                    PayloadJson = JsonSerializer.Serialize(new { rawSecret }),
                    ErrorMessage = rawSecret,
                    ReceivedAt = now.AddMinutes(-3)
                });

            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/v1/admin/billing/provider-lifecycle-signals?search={Uri.EscapeDataString(suffix)}&page=1&pageSize=10");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(rawSecret, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payloadJson", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("metadataJson", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("errorMessage", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(checkoutSessionId, body, StringComparison.OrdinalIgnoreCase);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var summary = root.GetProperty("summary");
        Assert.Equal(4, root.GetProperty("total").GetInt32());
        Assert.Equal(4, summary.GetProperty("totalSignals").GetInt32());
        Assert.Equal(1, summary.GetProperty("failedSignals").GetInt32());
        Assert.Equal(2, summary.GetProperty("unverifiedSignals").GetInt32());
        Assert.Equal(3, summary.GetProperty("unmatchedSignals").GetInt32());
        Assert.Equal(1, summary.GetProperty("refundSignals").GetInt32());
        Assert.Equal(1, summary.GetProperty("disputeSignals").GetInt32());
        Assert.Equal(1, summary.GetProperty("cancellationSignals").GetInt32());

        var items = root.GetProperty("items").EnumerateArray().ToList();
        var linked = items.Single(item => item.GetProperty("category").GetString() == "checkout");
        Assert.Equal("linked", linked.GetProperty("correlationStatus").GetString());
        Assert.Equal("high", linked.GetProperty("confidence").GetString());
        Assert.Equal("paypal", linked.GetProperty("gateway").GetString());
        Assert.Contains("...", linked.GetProperty("maskedProviderEventId").GetString());
        Assert.Contains("...", linked.GetProperty("maskedProviderTransactionId").GetString());
        var linkedLocalIds = linked.GetProperty("linkedLocalIds");
        Assert.Equal(paymentId.ToString("D"), linkedLocalIds.GetProperty("paymentTransactionIds")[0].GetString());
        Assert.Equal(invoiceId, linkedLocalIds.GetProperty("invoiceIds")[0].GetString());
        Assert.Equal(quoteId, linkedLocalIds.GetProperty("quoteIds")[0].GetString());
        Assert.Equal(subscriptionId, linkedLocalIds.GetProperty("subscriptionIds")[0].GetString());
        Assert.Equal(billingEventId, linkedLocalIds.GetProperty("billingEventIds")[0].GetString());
        Assert.Equal(1, linked.GetProperty("billingEventCount").GetInt32());

        var refund = items.Single(item => item.GetProperty("category").GetString() == "refund");
        Assert.Equal("unmatched", refund.GetProperty("correlationStatus").GetString());
        Assert.Equal("medium", refund.GetProperty("confidence").GetString());
        var refundFlags = refund.GetProperty("integrityFlags").EnumerateArray().Select(item => item.GetString()).ToList();
        Assert.Contains("local_evidence_unmatched", refundFlags);
        Assert.Contains("processing_failed", refundFlags);

        var cancellation = items.Single(item => item.GetProperty("category").GetString() == "cancellation");
        Assert.Equal("not_recorded", cancellation.GetProperty("correlationStatus").GetString());
        Assert.Equal("low", cancellation.GetProperty("confidence").GetString());
        var cancellationFlags = cancellation.GetProperty("integrityFlags").EnumerateArray().Select(item => item.GetString()).ToList();
        Assert.Contains("gateway_transaction_not_recorded", cancellationFlags);
    }

    [Fact]
    public async Task AdminBillingProviderLifecycleSignals_FiltersAndClampsPaging()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.PaymentWebhookEvents.AddRange(
                new PaymentWebhookEvent
                {
                    Id = Guid.NewGuid(),
                    Gateway = "stripe",
                    EventType = "charge.refunded",
                    GatewayEventId = $"evt_filter_refund_{suffix}",
                    GatewayTransactionId = $"filter-refund-{suffix}",
                    ProcessingStatus = "failed",
                    VerificationStatus = "failed",
                    NormalizedStatus = "refunded",
                    PayloadJson = "{}",
                    ErrorMessage = "filter-only-secret",
                    ReceivedAt = now
                },
                new PaymentWebhookEvent
                {
                    Id = Guid.NewGuid(),
                    Gateway = "paypal",
                    EventType = "invoice.paid",
                    GatewayEventId = $"evt_filter_invoice_{suffix}",
                    GatewayTransactionId = $"filter-invoice-{suffix}",
                    ProcessingStatus = "completed",
                    VerificationStatus = "verified",
                    NormalizedStatus = "paid",
                    PayloadJson = "{}",
                    ErrorMessage = "filter-only-secret",
                    ReceivedAt = now.AddMinutes(-1)
                });

            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/v1/admin/billing/provider-lifecycle-signals?category=refund&gateway=stripe&processingStatus=failed&verificationStatus=failed&search={Uri.EscapeDataString($"filter-refund-{suffix}")}&page=0&pageSize=500");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("filter-only-secret", body, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(100, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, root.GetProperty("total").GetInt32());
        var item = root.GetProperty("items")[0];
        Assert.Equal("refund", item.GetProperty("category").GetString());
        Assert.Equal("stripe", item.GetProperty("gateway").GetString());
        Assert.Equal("failed", item.GetProperty("processingStatus").GetString());
        Assert.Equal("failed", item.GetProperty("verificationStatus").GetString());

        var paymentCategoryResponse = await _client.GetAsync($"/v1/admin/billing/provider-lifecycle-signals?category=payment&search={Uri.EscapeDataString(suffix)}");
        var paymentCategoryBody = await paymentCategoryResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, paymentCategoryResponse.StatusCode);
        using var paymentCategoryJson = JsonDocument.Parse(paymentCategoryBody);
        Assert.Equal(0, paymentCategoryJson.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task AdminBillingProviderLifecycleSignals_DoesNotLinkCrossGatewayCheckoutSessionCollisions()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = $"provider-signal-collision-user-{suffix}";
        var quoteId = $"quote-provider-signal-collision-{suffix}";
        var checkoutSessionId = $"checkout-provider-signal-collision-{suffix}";
        var invoiceId = $"invoice-provider-signal-collision-{suffix}";
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Users.Add(new LearnerUser
            {
                Id = userId,
                DisplayName = "Provider Collision Learner",
                Email = $"provider-collision-{suffix}@example.com",
                CreatedAt = now,
                LastActiveAt = now
            });
            db.BillingQuotes.Add(new BillingQuote
            {
                Id = quoteId,
                UserId = userId,
                PlanCode = "starter",
                Currency = "AUD",
                SubtotalAmount = 49m,
                DiscountAmount = 0m,
                TotalAmount = 49m,
                Status = BillingQuoteStatus.Completed,
                CreatedAt = now.AddMinutes(-10),
                ExpiresAt = now.AddDays(1),
                CheckoutSessionId = checkoutSessionId,
                SnapshotJson = "{}"
            });
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                LearnerUserId = userId,
                Gateway = "stripe",
                GatewayTransactionId = checkoutSessionId,
                TransactionType = "subscription_payment",
                Status = "completed",
                Amount = 49m,
                Currency = "AUD",
                ProductType = "plan",
                ProductId = "starter",
                QuoteId = quoteId,
                AddOnVersionIdsJson = JsonSerializer.Serialize(new Dictionary<string, string>()),
                CreatedAt = now.AddMinutes(-9),
                UpdatedAt = now.AddMinutes(-8)
            });
            db.Invoices.Add(new Invoice
            {
                Id = invoiceId,
                UserId = userId,
                IssuedAt = now,
                Amount = 49m,
                Currency = "AUD",
                Status = "Paid",
                Description = "Starter",
                QuoteId = quoteId,
                CheckoutSessionId = checkoutSessionId
            });
            db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
            {
                Id = Guid.NewGuid(),
                Gateway = "paypal",
                EventType = "checkout.session.completed",
                GatewayEventId = $"evt_collision_{suffix}",
                GatewayTransactionId = checkoutSessionId,
                ProcessingStatus = "completed",
                VerificationStatus = "verified",
                NormalizedStatus = "completed",
                PayloadJson = "{}",
                ReceivedAt = now
            });

            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/v1/admin/billing/provider-lifecycle-signals?search={Uri.EscapeDataString(checkoutSessionId)}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(quoteId, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(invoiceId, body, StringComparison.OrdinalIgnoreCase);

        using var json = JsonDocument.Parse(body);
        var item = json.RootElement.GetProperty("items")[0];
        Assert.Equal("unmatched", item.GetProperty("correlationStatus").GetString());
        var linkedLocalIds = item.GetProperty("linkedLocalIds");
        Assert.Empty(linkedLocalIds.GetProperty("paymentTransactionIds").EnumerateArray());
        Assert.Empty(linkedLocalIds.GetProperty("invoiceIds").EnumerateArray());
        Assert.Empty(linkedLocalIds.GetProperty("quoteIds").EnumerateArray());
    }

    [Fact]
    public async Task AdminBillingProviderLifecycleSignals_FullyMasksShortProviderIds()
    {
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
            {
                Id = Guid.NewGuid(),
                Gateway = "stripe",
                EventType = "payment.failed",
                GatewayEventId = "evt7x",
                GatewayTransactionId = "tx7yz",
                ProcessingStatus = "failed",
                VerificationStatus = "verified",
                NormalizedStatus = "failed",
                PayloadJson = "{}",
                ReceivedAt = now
            });

            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/v1/admin/billing/provider-lifecycle-signals?search=evt7x");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("evt7x", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tx7yz", body, StringComparison.OrdinalIgnoreCase);

        using var json = JsonDocument.Parse(body);
        var item = json.RootElement.GetProperty("items")[0];
        Assert.Equal("*****", item.GetProperty("maskedProviderEventId").GetString());
        Assert.Equal("*****", item.GetProperty("maskedProviderTransactionId").GetString());
    }
}