using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using OetWithDrHesham.Api.Tests.Infrastructure;

namespace OetWithDrHesham.Api.Tests;

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

    [Fact]
    public async Task PaymentGateways_Methods_CarryModeMetadataForStripeAndPayPal()
    {
        using var client = CreateLearnerClient(_factory, $"gw-methods-{Guid.NewGuid():N}");

        var response = await client.GetAsync("/v1/billing/payment-gateways");

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var methods = json.RootElement.GetProperty("methods").EnumerateArray().ToList();

        var stripe = methods.Single(m => m.GetProperty("name").GetString() == "stripe");
        Assert.Equal("redirect", stripe.GetProperty("mode").GetString());

        var paypal = methods.Single(m => m.GetProperty("name").GetString() == "paypal");
        // PayPal renders the in-page SDK (Smart Buttons / card fields), not a redirect.
        Assert.Equal("embedded", paypal.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task PaymentGateways_WithCheckoutComConfigured_AdvertisesItAsRedirectMethod()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Sandbox off so only credential-backed gateways surface; a
                    // Checkout.com secret makes that gateway "configured".
                    ["Billing:AllowSandboxFallbacks"] = "false",
                    ["Billing:CheckoutCom:SecretKey"] = "sk_cko_test_stub",
                });
            });
        });
        using var client = CreateLearnerClient(factory, $"gw-cko-{Guid.NewGuid():N}");

        var response = await client.GetAsync("/v1/billing/payment-gateways");

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var gateways = json.RootElement.GetProperty("gateways")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("checkoutcom", gateways);
        Assert.DoesNotContain("stripe", gateways); // no Stripe key + sandbox off

        var methods = json.RootElement.GetProperty("methods").EnumerateArray().ToList();
        var cko = methods.Single(m => m.GetProperty("name").GetString() == "checkoutcom");
        Assert.Equal("redirect", cko.GetProperty("mode").GetString());
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
