using System.Text.Json;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class BillingTopUpTiersEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BillingTopUpTiersEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TopUpTiers_ReturnsSeededDefaults_WhenNoOverridesConfigured()
    {
        var userId = $"topup-tiers-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);

        var response = await client.GetAsync("/v1/billing/wallet/top-up-tiers");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"status={response.StatusCode} body={body}");

        using var json = JsonDocument.Parse(body);
        Assert.Equal("AUD", json.RootElement.GetProperty("currency").GetString());

        var tiers = json.RootElement.GetProperty("tiers").EnumerateArray().ToList();
        Assert.NotEmpty(tiers);

        foreach (var tier in tiers)
        {
            Assert.True(tier.TryGetProperty("amount", out var amount));
            Assert.True(amount.GetInt32() > 0);
            Assert.True(tier.TryGetProperty("credits", out var credits));
            Assert.True(credits.GetInt32() >= 0);
            Assert.True(tier.TryGetProperty("bonus", out var bonus));
            Assert.True(bonus.GetInt32() >= 0);
            Assert.True(tier.TryGetProperty("totalCredits", out var totalCredits));
            Assert.Equal(credits.GetInt32() + bonus.GetInt32(), totalCredits.GetInt32());
            Assert.True(tier.TryGetProperty("label", out var label));
            Assert.False(string.IsNullOrWhiteSpace(label.GetString()));
            Assert.True(tier.TryGetProperty("isPopular", out var isPopular));
            Assert.True(isPopular.ValueKind is JsonValueKind.True or JsonValueKind.False);
        }

        var amounts = tiers.Select(t => t.GetProperty("amount").GetInt32()).ToArray();
        Assert.Contains(10, amounts);
        Assert.Contains(25, amounts);
        Assert.Contains(50, amounts);
        Assert.Contains(100, amounts);
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
