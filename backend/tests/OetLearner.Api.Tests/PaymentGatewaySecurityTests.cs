using System.Net;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Tests;

public class PaymentGatewaySecurityTests
{
    [Fact]
    public async Task StripeWebhook_MissingSecret_RejectsEvenWhenSandboxFallbacksAreEnabled()
    {
        var options = new BillingOptions
        {
            AllowSandboxFallbacks = true,
            Stripe = new StripeBillingOptions
            {
                WebhookSecret = null
            }
        };
        var gateway = new StripeGateway(new HttpClient(), Options.Create(options), TestRuntimeSettingsProvider.FromBillingOptions(options));

        var result = await gateway.HandleWebhookAsync("{}", new Dictionary<string, string>(), default);

        Assert.False(result.Processed);
        Assert.Equal("signature_verification_failed", result.EventType);
        Assert.Contains("webhook secret", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StripeCheckout_MissingConfiguration_ThrowsWhenSandboxFallbacksAreDisabled()
    {
        var options = new BillingOptions
        {
            AllowSandboxFallbacks = false,
            Stripe = new StripeBillingOptions()
        };
        var gateway = new StripeGateway(new HttpClient(), Options.Create(options), TestRuntimeSettingsProvider.FromBillingOptions(options));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateway.CreatePaymentIntentAsync(new CreatePaymentIntentRequest(
                UserId: "learner-1",
                Amount: 10m,
                Currency: "AUD",
                ProductType: "review_credits",
                ProductId: null,
                Description: "Review credits",
                Metadata: null), default));
    }

    [Fact]
    public async Task PayPalCheckout_UsesRuntimeCredentialsAndUrls()
    {
        var billingOptions = new BillingOptions
        {
            AllowSandboxFallbacks = false,
            PayPal = new PayPalBillingOptions
            {
                UseSandbox = true,
                ClientId = null,
                ClientSecret = null,
                SuccessUrl = null,
                CancelUrl = null,
            },
        };
        var handler = new CapturingPayPalHandler();
        var runtime = TestRuntimeSettingsProvider.FromBillingSettings(new BillingSettings(
            StripeSecretKey: null,
            StripePublishableKey: null,
            StripeWebhookSecret: null,
            StripeSuccessUrl: null,
            StripeCancelUrl: null,
            PayPalClientId: "runtime-client",
            PayPalClientSecret: "runtime-secret",
            PayPalWebhookId: "runtime-webhook",
            PayPalSuccessUrl: "https://app.example/success",
            PayPalCancelUrl: "https://app.example/cancel"));
        var gateway = new PayPalGateway(new HttpClient(handler), Options.Create(billingOptions), runtime);

        var result = await gateway.CreatePaymentIntentAsync(new CreatePaymentIntentRequest(
            UserId: "learner-1",
            Amount: 10m,
            Currency: "AUD",
            ProductType: "review_credits",
            ProductId: null,
            Description: "Review credits",
            Metadata: null), default);

        Assert.Equal("ORDER-1", result.GatewayTransactionId);
        Assert.Contains("/v1/oauth2/token", handler.TokenRequestUri);
        Assert.Equal("Basic cnVudGltZS1jbGllbnQ6cnVudGltZS1zZWNyZXQ=", handler.TokenAuthorization);
        Assert.Contains("\"return_url\":\"https://app.example/success\"", handler.OrderPayload);
        Assert.Contains("\"cancel_url\":\"https://app.example/cancel\"", handler.OrderPayload);
    }

    private sealed class CapturingPayPalHandler : HttpMessageHandler
    {
        public string? TokenRequestUri { get; private set; }
        public string? TokenAuthorization { get; private set; }
        public string? OrderPayload { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/v1/oauth2/token", StringComparison.Ordinal) == true)
            {
                TokenRequestUri = request.RequestUri.ToString();
                TokenAuthorization = request.Headers.Authorization?.ToString();
                return JsonResponse("""{"access_token":"token-1"}""");
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/v2/checkout/orders", StringComparison.Ordinal) == true)
            {
                OrderPayload = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                return JsonResponse("""{"id":"ORDER-1","status":"CREATED","links":[{"rel":"approve","href":"https://paypal.example/checkout"}]}""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage JsonResponse(string json)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };
    }
}
