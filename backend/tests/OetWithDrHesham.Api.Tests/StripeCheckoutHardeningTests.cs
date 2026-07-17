using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Settings;

namespace OetWithDrHesham.Api.Tests;

/// <summary>
/// Regression coverage for the checkout-hardening work: Stripe API rejections must
/// surface as a recognizable <see cref="PaymentGatewayApiException"/> (mapped to a clean
/// 503 by the caller) rather than a raw HttpRequestException → opaque 500; relative
/// success/cancel URLs must either be absolutized from the admin-configured app base URL
/// or rejected as a clean configuration error; and the Stripe
/// <c>{CHECKOUT_SESSION_ID}</c> template token must survive URL building.
/// </summary>
public class StripeCheckoutHardeningTests
{
    private static CreatePaymentIntentRequest MakeRequest(string? successUrl, string? cancelUrl)
        => new(
            UserId: "learner-1",
            Amount: 160m,
            Currency: "GBP",
            ProductType: "addon_purchase",
            ProductId: "quote-1",
            Description: "Full Crash Course",
            Metadata: null,
            SuccessUrl: successUrl,
            CancelUrl: cancelUrl);

    private static BillingSettings Billing(string? secretKey, string? publicAppBaseUrl = null)
        => new(
            StripeSecretKey: secretKey,
            StripePublishableKey: null,
            StripeWebhookSecret: null,
            StripeSuccessUrl: null,
            StripeCancelUrl: null,
            PayPalClientId: null,
            PayPalClientSecret: null,
            PayPalWebhookId: null,
            PayPalSuccessUrl: null,
            PayPalCancelUrl: null,
            PublicAppBaseUrl: publicAppBaseUrl);

    private static StripeGateway BuildGateway(HttpMessageHandler handler, BillingSettings billing, bool sandbox = false)
    {
        var options = new BillingOptions
        {
            AllowSandboxFallbacks = sandbox,
            Stripe = new StripeBillingOptions { ApiBaseUrl = "https://stripe.example/" }
        };
        return new StripeGateway(
            new HttpClient(handler),
            Options.Create(options),
            TestRuntimeSettingsProvider.FromBillingSettings(billing),
            NullLogger<StripeGateway>.Instance);
    }

    [Fact]
    public async Task CreatePaymentIntent_StripeReturns400_ThrowsPaymentGatewayApiExceptionWithDetails()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest,
            """{"error":{"message":"Invalid API Key provided","type":"invalid_request_error","code":"api_key_invalid"}}""");
        var gateway = BuildGateway(handler, Billing("sk_test_bad"));

        var ex = await Assert.ThrowsAsync<PaymentGatewayApiException>(() =>
            gateway.CreatePaymentIntentAsync(
                MakeRequest("https://app.example/return?session={CHECKOUT_SESSION_ID}", "https://app.example/cancel"),
                default));

        Assert.Equal("stripe", ex.Gateway);
        Assert.Equal(400, ex.UpstreamStatusCode);
        Assert.Equal("api_key_invalid", ex.UpstreamErrorCode);
        Assert.Equal("invalid_request_error", ex.UpstreamErrorType);
    }

    [Fact]
    public async Task CreatePaymentIntent_StripeReturns401_ThrowsPaymentGatewayApiException()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized,
            """{"error":{"message":"Invalid API Key provided","type":"invalid_request_error"}}""");
        var gateway = BuildGateway(handler, Billing("sk_live_revoked"));

        var ex = await Assert.ThrowsAsync<PaymentGatewayApiException>(() =>
            gateway.CreatePaymentIntentAsync(
                MakeRequest("https://app.example/return", "https://app.example/cancel"),
                default));

        Assert.Equal(401, ex.UpstreamStatusCode);
    }

    [Fact]
    public async Task CreatePaymentIntent_RelativeUrlsAndNoAppBaseUrl_ThrowsNotFullyConfigured()
    {
        // Key is present but the return URLs are relative (Platform:PublicWebBaseUrl unset)
        // and no public app base URL is configured → clean config error (→ gateway_unavailable),
        // never a Stripe call / opaque 500.
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var gateway = BuildGateway(handler, Billing("sk_test_ok"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateway.CreatePaymentIntentAsync(
                MakeRequest("/billing/payment-return?status=success", "/billing/payment-return?status=cancelled"),
                default));

        Assert.Contains("not fully configured", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatePaymentIntent_RelativeUrlsWithAppBaseUrl_AbsolutizesAndPreservesSessionToken()
    {
        var handler = new CapturingCheckoutHandler();
        var gateway = BuildGateway(handler, Billing("sk_test_ok", publicAppBaseUrl: "https://app.oet.example"));

        var result = await gateway.CreatePaymentIntentAsync(
            MakeRequest(
                "/billing/payment-return?status=success&session={CHECKOUT_SESSION_ID}",
                "/billing/payment-return?status=cancelled"),
            default);

        Assert.Equal("cs_1", result.GatewayTransactionId);
        Assert.NotNull(handler.SuccessUrl);
        Assert.StartsWith("https://app.oet.example/billing/payment-return", handler.SuccessUrl);
        // Stripe requires the literal, un-encoded template token in success_url.
        Assert.Contains("{CHECKOUT_SESSION_ID}", handler.SuccessUrl);
        Assert.StartsWith("https://app.oet.example/billing/payment-return", handler.CancelUrl);
    }

    [Fact]
    public void PlatformLinkService_BuildWebUrl_PreservesCheckoutSessionToken()
    {
        var platform = new PlatformLinkService(
            TestRuntimeSettingsProvider.FromPlatformOptions(new PlatformOptions { PublicWebBaseUrl = "https://app.example" }),
            Options.Create(new BillingOptions()));

        var url = platform.BuildWebUrl("/billing/payment-return?status=success&session={CHECKOUT_SESSION_ID}");

        Assert.StartsWith("https://app.example/billing/payment-return", url);
        Assert.Contains("{CHECKOUT_SESSION_ID}", url);
        Assert.DoesNotContain("%7BCHECKOUT_SESSION_ID%7D", url);
    }

    private sealed class StubHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
    }

    private sealed class CapturingCheckoutHandler : HttpMessageHandler
    {
        public string? SuccessUrl { get; private set; }
        public string? CancelUrl { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            // application/x-www-form-urlencoded: fields split on '&'; each value is percent-encoded.
            foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0) continue;
                var name = pair[..eq];
                var value = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                if (name == "success_url") SuccessUrl = value;
                else if (name == "cancel_url") CancelUrl = value;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"cs_1","url":"https://checkout.stripe.example/pay/cs_1","status":"open","payment_intent":"pi_1"}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
