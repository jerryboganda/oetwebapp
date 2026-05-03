using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class BillingTopUpIdempotencyTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BillingTopUpIdempotencyTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task WalletTopUp_SameIdempotencyKey_ReturnsSamePayloadAndCreatesOneTransaction()
    {
        var userId = $"topup-idem-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);
        var idempotencyKey = $"idem-{Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync("/v1/billing/wallet/top-up", new
        {
            amount = 25,
            gateway = "stripe",
            idempotencyKey
        });
        var firstBody = await first.Content.ReadAsStringAsync();
        Assert.True(first.IsSuccessStatusCode, firstBody);

        var second = await client.PostAsJsonAsync("/v1/billing/wallet/top-up", new
        {
            amount = 25,
            gateway = "stripe",
            idempotencyKey
        });
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.True(second.IsSuccessStatusCode, secondBody);

        using var firstJson = JsonDocument.Parse(firstBody);
        using var secondJson = JsonDocument.Parse(secondBody);
        Assert.Equal(
            firstJson.RootElement.GetProperty("sessionId").GetString(),
            secondJson.RootElement.GetProperty("sessionId").GetString());
        Assert.Equal(
            firstJson.RootElement.GetProperty("checkoutUrl").GetString(),
            secondJson.RootElement.GetProperty("checkoutUrl").GetString());
        Assert.Equal(
            firstJson.RootElement.GetProperty("totalCredits").GetInt32(),
            secondJson.RootElement.GetProperty("totalCredits").GetInt32());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var transactionCount = await db.PaymentTransactions
            .CountAsync(x => x.LearnerUserId == userId && x.TransactionType == "wallet_top_up");
        Assert.Equal(1, transactionCount);
    }

    [Fact]
    public async Task WalletTopUp_DifferentIdempotencyKeys_CreateDistinctTransactions()
    {
        var userId = $"topup-distinct-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);

        var first = await client.PostAsJsonAsync("/v1/billing/wallet/top-up", new
        {
            amount = 10,
            gateway = "stripe",
            idempotencyKey = $"idem-a-{Guid.NewGuid():N}"
        });
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());

        var second = await client.PostAsJsonAsync("/v1/billing/wallet/top-up", new
        {
            amount = 10,
            gateway = "stripe",
            idempotencyKey = $"idem-b-{Guid.NewGuid():N}"
        });
        Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());

        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.NotEqual(
            firstJson.RootElement.GetProperty("sessionId").GetString(),
            secondJson.RootElement.GetProperty("sessionId").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var transactionCount = await db.PaymentTransactions
            .CountAsync(x => x.LearnerUserId == userId && x.TransactionType == "wallet_top_up");
        Assert.Equal(2, transactionCount);
    }

    [Fact]
    public async Task WalletTopUp_MissingIdempotencyKey_IsAccepted()
    {
        var userId = $"topup-noidem-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);

        var response = await client.PostAsJsonAsync("/v1/billing/wallet/top-up", new
        {
            amount = 50,
            gateway = "paypal"
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);

        using var json = JsonDocument.Parse(body);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("sessionId").GetString()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25)]
    public async Task WalletTopUp_NonPositiveAmount_IsRejected(int amount)
    {
        var userId = $"topup-bad-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);

        var response = await client.PostAsJsonAsync("/v1/billing/wallet/top-up", new
        {
            amount,
            gateway = "stripe"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid_amount", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task WalletTopUp_AmountNotInTierSet_IsRejectedWithListedTiers()
    {
        var userId = $"topup-tierless-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);

        var response = await client.PostAsJsonAsync("/v1/billing/wallet/top-up", new
        {
            amount = 7,
            gateway = "stripe"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid_amount", json.RootElement.GetProperty("code").GetString());

        var message = json.RootElement.GetProperty("message").GetString() ?? string.Empty;
        Assert.Contains("$10", message);
        Assert.Contains("$25", message);
        Assert.Contains("$50", message);
        Assert.Contains("$100", message);
    }

    [Fact]
    public async Task WalletTopUp_FrozenLearner_IsRejectedWithFreezeCode()
    {
        var userId = $"topup-frozen-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.AccountFreezeRecords.Add(new AccountFreezeRecord
            {
                Id = $"frz-{Guid.NewGuid():N}",
                UserId = userId,
                Status = FreezeStatus.Active,
                IsCurrent = true,
                IsSelfService = true,
                EntitlementConsumed = true,
                RequestedAt = now,
                StartedAt = now,
                DurationDays = 14,
                Reason = "Top-up freeze test",
                PolicySnapshotJson = "{}",
                EligibilitySnapshotJson = "{}"
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync("/v1/billing/wallet/top-up", new
        {
            amount = 25,
            gateway = "stripe"
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("account_frozen", json.RootElement.GetProperty("code").GetString());
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
