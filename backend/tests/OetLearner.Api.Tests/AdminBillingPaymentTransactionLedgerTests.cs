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
public class AdminBillingPaymentTransactionLedgerTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public AdminBillingPaymentTransactionLedgerTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");
    }

    [Fact]
    public async Task AdminBillingPaymentTransactions_RequiresBillingReadPermission()
    {
        var previous = Environment.GetEnvironmentVariable("Auth__UseDevelopmentAuth");
        Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", "true");
        try
        {
            using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
            client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", AdminPermissions.ContentRead);

            var response = await client.GetAsync("/v1/admin/billing/payment-transactions");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", previous);
        }
    }

    [Fact]
    public async Task AdminBillingPaymentTransactions_ReturnsPagedNewestFirstSanitizedRows()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = $"payment-ledger-user-{suffix}";
        var gatewayTransactionId = $"payment-ledger-gateway-{suffix}";
        const string rawSecret = "SECRET_RAW_PROVIDER_PAYLOAD_DO_NOT_EXPOSE";
        var now = DateTimeOffset.UtcNow;
        var lowerPaymentId = Guid.Parse($"00000000-0000-0000-0000-0001{suffix}");
        var higherPaymentId = Guid.Parse($"00000000-0000-0000-0000-0002{suffix}");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Users.Add(new LearnerUser
            {
                Id = userId,
                DisplayName = "Payment Ledger Learner",
                Email = $"payment-ledger-{suffix}@example.com",
                CreatedAt = now,
                LastActiveAt = now
            });
            db.PaymentTransactions.AddRange(
                new PaymentTransaction
                {
                    Id = lowerPaymentId,
                    LearnerUserId = userId,
                    Gateway = "stripe",
                    GatewayTransactionId = $"{gatewayTransactionId}-older",
                    TransactionType = "subscription_payment",
                    Status = "completed",
                    Amount = 39m,
                    Currency = "AUD",
                    ProductType = "plan",
                    ProductId = "starter",
                    QuoteId = $"quote-payment-ledger-{suffix}-older",
                    PlanVersionId = "plan-version-ledger-v1",
                    AddOnVersionIdsJson = JsonSerializer.Serialize(new Dictionary<string, string>()),
                    CouponVersionId = null,
                    MetadataJson = JsonSerializer.Serialize(new { rawSecret }),
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new PaymentTransaction
                {
                    Id = higherPaymentId,
                    LearnerUserId = userId,
                    Gateway = "paypal",
                    GatewayTransactionId = $"{gatewayTransactionId}-newer",
                    TransactionType = "wallet_top_up",
                    Status = "completed",
                    Amount = 25m,
                    Currency = "AUD",
                    ProductType = "wallet_top_up",
                    ProductId = "credits-wallet",
                    QuoteId = $"quote-payment-ledger-{suffix}-newer",
                    PlanVersionId = null,
                    AddOnVersionIdsJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["credits-5"] = "addon-version-ledger-v1" }),
                    CouponVersionId = "coupon-version-ledger-v1",
                    MetadataJson = JsonSerializer.Serialize(new { rawSecret }),
                    CreatedAt = now,
                    UpdatedAt = now
                });
            db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
            {
                Id = Guid.NewGuid(),
                Gateway = "paypal",
                EventType = "payment.completed",
                GatewayEventId = $"payment-ledger-event-{suffix}",
                ProcessingStatus = "completed",
                PayloadJson = JsonSerializer.Serialize(new { rawSecret }),
                ReceivedAt = now,
                ProcessedAt = now
            });

            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/v1/admin/billing/payment-transactions?search={Uri.EscapeDataString(gatewayTransactionId)}&page=1&pageSize=1");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(rawSecret, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("metadataJson", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payloadJson", body, StringComparison.OrdinalIgnoreCase);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(2, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(1, root.GetProperty("pageSize").GetInt32());

        var item = root.GetProperty("items")[0];
        Assert.Equal(higherPaymentId.ToString("D"), item.GetProperty("id").GetString());
        Assert.Equal("Payment Ledger Learner", item.GetProperty("learnerName").GetString());
        Assert.Equal("paypal", item.GetProperty("gateway").GetString());
        Assert.Equal("wallet_top_up", item.GetProperty("transactionType").GetString());
        Assert.Equal("completed", item.GetProperty("status").GetString());
        Assert.Equal("addon-version-ledger-v1", item.GetProperty("addOnVersionIds").GetProperty("credits-5").GetString());
    }

    [Fact]
    public async Task AdminBillingPaymentTransactions_FiltersAndClampsPaging()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = $"payment-filter-user-{suffix}";
        var searchToken = $"payment-filter-{suffix}";
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Users.Add(new LearnerUser
            {
                Id = userId,
                DisplayName = "Filtered Payment Learner",
                Email = $"payment-filter-{suffix}@example.com",
                CreatedAt = now,
                LastActiveAt = now
            });
            db.PaymentTransactions.AddRange(
                new PaymentTransaction
                {
                    Id = Guid.NewGuid(),
                    LearnerUserId = userId,
                    Gateway = "paypal",
                    GatewayTransactionId = $"{searchToken}-match",
                    TransactionType = "wallet_top_up",
                    Status = "pending",
                    Amount = 15m,
                    Currency = "AUD",
                    ProductType = "wallet_top_up",
                    ProductId = "wallet",
                    QuoteId = null,
                    AddOnVersionIdsJson = JsonSerializer.Serialize(new Dictionary<string, string>()),
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new PaymentTransaction
                {
                    Id = Guid.NewGuid(),
                    LearnerUserId = userId,
                    Gateway = "stripe",
                    GatewayTransactionId = $"{searchToken}-miss",
                    TransactionType = "subscription_payment",
                    Status = "completed",
                    Amount = 49m,
                    Currency = "AUD",
                    ProductType = "plan",
                    ProductId = "starter",
                    QuoteId = $"quote-{searchToken}",
                    AddOnVersionIdsJson = JsonSerializer.Serialize(new Dictionary<string, string>()),
                    CreatedAt = now.AddMinutes(-1),
                    UpdatedAt = now
                });

            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/v1/admin/billing/payment-transactions?search={Uri.EscapeDataString(searchToken)}&status=pending&gateway=paypal&transactionType=wallet_top_up&page=0&pageSize=500");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(100, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, root.GetProperty("total").GetInt32());
        Assert.Equal($"{searchToken}-match", root.GetProperty("items")[0].GetProperty("gatewayTransactionId").GetString());
    }
}
