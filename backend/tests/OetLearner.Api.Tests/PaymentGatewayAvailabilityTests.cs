using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// GET /v1/billing/payment-gateways must only advertise gateways the backend
/// can actually create checkout sessions for, so the learner UI never offers
/// a payment method that would fail with a server error.
/// </summary>
public class PaymentGatewayAvailabilityTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public PaymentGatewayAvailabilityTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PaymentGateways_WithSandboxFallbacks_AdvertisesStripeAndPayPal()
    {
        using var client = CreateLearnerClient(_factory, $"gw-sandbox-{Guid.NewGuid():N}");

        var response = await client.GetAsync("/v1/billing/payment-gateways");

        response.EnsureSuccessStatusCode();
        var gateways = await ReadGatewaysAsync(response);
        Assert.Contains("stripe", gateways);
        Assert.Contains("paypal", gateways);
    }

    [Fact]
    public async Task PaymentGateways_NoKeysAndNoSandbox_AdvertisesNothing()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Billing:AllowSandboxFallbacks"] = "false",
                });
            });
        });
        using var client = CreateLearnerClient(factory, $"gw-locked-{Guid.NewGuid():N}");

        var response = await client.GetAsync("/v1/billing/payment-gateways");

        response.EnsureSuccessStatusCode();
        var gateways = await ReadGatewaysAsync(response);
        Assert.DoesNotContain("paypal", gateways);
        Assert.DoesNotContain("stripe", gateways);
    }

    private static HttpClient CreateLearnerClient(WebApplicationFactory<Program> factoryLike, string userId)
    {
        var client = factoryLike.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }

    private static async Task<string[]> ReadGatewaysAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("gateways")
            .EnumerateArray()
            .Select(element => element.GetString() ?? string.Empty)
            .ToArray();
    }
}
