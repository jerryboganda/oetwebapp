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
public class AdminWebhookRetryTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public AdminWebhookRetryTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");
    }

    [Fact]
    public async Task AdminWebhooks_SurfaceLegacyFailuresAsNotRetryable()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var eventId = Guid.NewGuid();
        var rawSecret = $"raw-provider-secret-{suffix}";
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
            {
                Id = eventId,
                Gateway = "stripe",
                EventType = "checkout.session.completed",
                GatewayEventId = $"evt-legacy-{suffix}",
                ProcessingStatus = "failed",
                VerificationStatus = "legacy",
                PayloadJson = JsonSerializer.Serialize(new { rawSecret }),
                ErrorMessage = "Legacy failure before retry evidence was captured.",
                ReceivedAt = now,
                ProcessedAt = now
            });

            await db.SaveChangesAsync();
        }

        var listResponse = await _client.GetAsync("/v1/admin/webhooks?status=failed&page=1&pageSize=20");
        var listBody = await listResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.DoesNotContain(rawSecret, listBody, StringComparison.OrdinalIgnoreCase);

        using var listJson = JsonDocument.Parse(listBody);
        var item = listJson.RootElement.GetProperty("items").EnumerateArray()
            .First(e => e.GetProperty("id").GetString() == eventId.ToString());
        Assert.False(item.GetProperty("retryable").GetBoolean());
        Assert.Contains("signature-verified", item.GetProperty("retryBlockedReason").GetString(), StringComparison.OrdinalIgnoreCase);

        var retryResponse = await _client.PostAsync($"/v1/admin/webhooks/{eventId}/retry", content: null);
        var retryBody = await retryResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, retryResponse.StatusCode);
        Assert.Contains("webhook_not_retryable", retryBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminWebhookRetry_ReprocessesVerifiedWalletTopUpOnce()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = $"webhook-retry-user-{suffix}";
        var walletId = $"wallet-{suffix}";
        var gatewayTransactionId = $"retry-gateway-{suffix}";
        var eventId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Users.Add(new LearnerUser
            {
                Id = userId,
                DisplayName = "Webhook Retry Learner",
                Email = $"webhook-retry-{suffix}@example.com",
                CreatedAt = now,
                LastActiveAt = now
            });
            db.Wallets.Add(new Wallet
            {
                Id = walletId,
                UserId = userId,
                CreditBalance = 0,
                LastUpdatedAt = now
            });
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                LearnerUserId = userId,
                Gateway = "paypal",
                GatewayTransactionId = gatewayTransactionId,
                TransactionType = "wallet_top_up",
                Status = "pending",
                Amount = 25m,
                Currency = "AUD",
                ProductType = "wallet_top_up",
                ProductId = "credits-wallet",
                AddOnVersionIdsJson = JsonSerializer.Serialize(new Dictionary<string, string>()),
                MetadataJson = JsonSerializer.Serialize(new { credits = 10, bonus = 2, totalCredits = 12 }),
                CreatedAt = now,
                UpdatedAt = now
            });
            db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
            {
                Id = eventId,
                Gateway = "paypal",
                EventType = "PAYMENT.CAPTURE.COMPLETED",
                GatewayEventId = $"evt-retry-{suffix}",
                ProcessingStatus = "failed",
                VerificationStatus = "verified",
                VerifiedAt = now,
                PayloadSha256 = new string('a', 64),
                ParserVersion = "payment-webhook-v1",
                GatewayTransactionId = gatewayTransactionId,
                NormalizedStatus = "completed",
                AttemptCount = 1,
                PayloadJson = "{}",
                ErrorMessage = "Transient local fulfillment failure.",
                ReceivedAt = now,
                ProcessedAt = now
            });

            await db.SaveChangesAsync();
        }

        var retryResponse = await _client.PostAsync($"/v1/admin/webhooks/{eventId}/retry", content: null);
        var retryBody = await retryResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        using (var retryJson = JsonDocument.Parse(retryBody))
        {
            var root = retryJson.RootElement;
            Assert.Equal("reprocessed", root.GetProperty("status").GetString());
            Assert.Equal("completed", root.GetProperty("processingStatus").GetString());
            Assert.Equal(2, root.GetProperty("attemptCount").GetInt32());
            Assert.Equal(1, root.GetProperty("retryCount").GetInt32());
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var wallet = await db.Wallets.SingleAsync(x => x.Id == walletId);
            var transaction = await db.PaymentTransactions.SingleAsync(x => x.GatewayTransactionId == gatewayTransactionId);
            var webhook = await db.PaymentWebhookEvents.SingleAsync(x => x.Id == eventId);

            Assert.Equal(12, wallet.CreditBalance);
            Assert.Equal("completed", transaction.Status);
            Assert.Equal("completed", webhook.ProcessingStatus);
            Assert.Equal(1, await db.WalletTransactions.CountAsync(x => x.WalletId == walletId && x.ReferenceId == gatewayTransactionId));
            Assert.Equal(1, await db.Invoices.CountAsync(x => x.CheckoutSessionId == gatewayTransactionId));
            Assert.Equal(1, await db.BillingEvents.CountAsync(x => x.EventType == "wallet_top_up_completed" && x.EntityId == gatewayTransactionId));
        }

        var duplicateRetryResponse = await _client.PostAsync($"/v1/admin/webhooks/{eventId}/retry", content: null);
        Assert.Equal(HttpStatusCode.Conflict, duplicateRetryResponse.StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            Assert.Equal(12, await db.Wallets.Where(x => x.Id == walletId).Select(x => x.CreditBalance).SingleAsync());
            Assert.Equal(1, await db.WalletTransactions.CountAsync(x => x.WalletId == walletId && x.ReferenceId == gatewayTransactionId));
            Assert.Equal(1, await db.BillingEvents.CountAsync(x => x.EventType == "wallet_top_up_completed" && x.EntityId == gatewayTransactionId));
        }
    }

    [Fact]
    public async Task PaymentWebhookRedelivery_ReclaimsStaleProcessingEvent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = $"webhook-stale-user-{suffix}";
        var walletId = $"wallet-stale-{suffix}";
        var gatewayTransactionId = $"stale-gateway-{suffix}";
        var gatewayEventId = $"evt-stale-{suffix}";
        var eventId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Users.Add(new LearnerUser
            {
                Id = userId,
                DisplayName = "Webhook Stale Learner",
                Email = $"webhook-stale-{suffix}@example.com",
                CreatedAt = now,
                LastActiveAt = now
            });
            db.Wallets.Add(new Wallet
            {
                Id = walletId,
                UserId = userId,
                CreditBalance = 0,
                LastUpdatedAt = now
            });
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                LearnerUserId = userId,
                Gateway = "paypal",
                GatewayTransactionId = gatewayTransactionId,
                TransactionType = "wallet_top_up",
                Status = "pending",
                Amount = 15m,
                Currency = "AUD",
                ProductType = "wallet_top_up",
                ProductId = "credits-wallet",
                AddOnVersionIdsJson = JsonSerializer.Serialize(new Dictionary<string, string>()),
                MetadataJson = JsonSerializer.Serialize(new { credits = 5, bonus = 1, totalCredits = 6 }),
                CreatedAt = now,
                UpdatedAt = now
            });
            db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
            {
                Id = eventId,
                Gateway = "paypal",
                EventType = "PAYMENT.CAPTURE.COMPLETED",
                GatewayEventId = gatewayEventId,
                ProcessingStatus = "processing",
                VerificationStatus = "verified",
                VerifiedAt = now.AddMinutes(-10),
                PayloadSha256 = new string('b', 64),
                ParserVersion = "payment-webhook-v1",
                GatewayTransactionId = gatewayTransactionId,
                NormalizedStatus = "completed",
                AttemptCount = 1,
                LastAttemptedAt = now.AddMinutes(-10),
                PayloadJson = "{}",
                ReceivedAt = now.AddMinutes(-10)
            });

            await db.SaveChangesAsync();
        }

        var payload = JsonSerializer.Serialize(new
        {
            id = gatewayEventId,
            event_type = "PAYMENT.CAPTURE.COMPLETED",
            resource = new
            {
                supplementary_data = new
                {
                    related_ids = new
                    {
                        order_id = gatewayTransactionId
                    }
                }
            }
        });

        using var webhookClient = _factory.CreateClient();
        using var response = await webhookClient.PostAsync(
            "/v1/payment/webhooks/paypal",
            new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var json = JsonDocument.Parse(body))
        {
            Assert.Equal("completed", json.RootElement.GetProperty("state").GetString());
            Assert.Equal(gatewayTransactionId, json.RootElement.GetProperty("gatewayTransactionId").GetString());
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var wallet = await db.Wallets.SingleAsync(x => x.Id == walletId);
            var webhook = await db.PaymentWebhookEvents.SingleAsync(x => x.Id == eventId);

            Assert.Equal(6, wallet.CreditBalance);
            Assert.Equal("completed", webhook.ProcessingStatus);
            Assert.Equal(2, webhook.AttemptCount);
            Assert.Equal(1, await db.WalletTransactions.CountAsync(x => x.WalletId == walletId && x.ReferenceId == gatewayTransactionId));
        }
    }
}
