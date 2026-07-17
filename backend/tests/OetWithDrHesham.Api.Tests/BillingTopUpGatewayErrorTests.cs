using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Tests.Infrastructure;

namespace OetWithDrHesham.Api.Tests;

/// <summary>
/// Regression for the wallet top-up hardening: when the payment gateway's API rejects
/// the request (e.g. an expired Stripe live key → HTTP 401), top-up must surface a clean,
/// retryable <c>503 payment_gateway_error</c> — NOT an opaque <c>500</c> "unexpected
/// server error". Before the fix, <c>WalletService.CreateTopUpSessionAsync</c> only caught
/// <see cref="System.InvalidOperationException"/>, so a <see cref="PaymentGatewayApiException"/>
/// leaked through as an unhandled 500 (the production incident: every top-up click showed
/// "An unexpected server error occurred" while the equivalent checkout-session path already
/// returned a clean 503).
/// </summary>
public class BillingTopUpGatewayErrorTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BillingTopUpGatewayErrorTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task WalletTopUp_GatewayApiRejectsRequest_ReturnsClean503NotUnhandled500()
    {
        var userId = $"topup-gwfail-{Guid.NewGuid():N}";
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            // A configured Stripe key forces the real HTTP path instead of the sandbox
            // fallback; the stub handler below then rejects it with 401, exactly like an
            // expired/revoked live key in production.
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Billing:Stripe:SecretKey"] = "sk_test_stub_expired",
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<StripeGateway>()
                    .ConfigurePrimaryHttpMessageHandler(() => new ExpiredKeyHandler());
            });
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);

        var response = await client.PostAsJsonAsync("/v1/billing/wallet/top-up", new
        {
            amount = 25,
            gateway = "stripe",
        });

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using var json = JsonDocument.Parse(body);
        Assert.Equal("payment_gateway_error", json.RootElement.GetProperty("code").GetString());
        Assert.True(json.RootElement.GetProperty("retryable").GetBoolean());
    }

    private sealed class ExpiredKeyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(
                    """{"error":{"message":"Expired API Key provided","type":"invalid_request_error","code":"api_key_expired"}}""",
                    System.Text.Encoding.UTF8,
                    "application/json"),
            });
    }
}
