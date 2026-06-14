using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
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
        var gateway = new StripeGateway(new HttpClient(), Options.Create(options), TestRuntimeSettingsProvider.FromBillingOptions(options), NullLogger<StripeGateway>.Instance);

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
        var gateway = new StripeGateway(new HttpClient(), Options.Create(options), TestRuntimeSettingsProvider.FromBillingOptions(options), NullLogger<StripeGateway>.Instance);

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

    [Fact]
    public async Task StripeRefund_SendsIdempotencyKeyHeader()
    {
        var options = new BillingOptions
        {
            AllowSandboxFallbacks = false,
            Stripe = new StripeBillingOptions { ApiBaseUrl = "https://stripe.example/" }
        };
        var handler = new CapturingStripeRefundHandler();
        var runtime = TestRuntimeSettingsProvider.FromBillingSettings(new BillingSettings(
            StripeSecretKey: "sk_test_runtime",
            StripePublishableKey: null,
            StripeWebhookSecret: null,
            StripeSuccessUrl: null,
            StripeCancelUrl: null,
            PayPalClientId: null,
            PayPalClientSecret: null,
            PayPalWebhookId: null,
            PayPalSuccessUrl: null,
            PayPalCancelUrl: null));
        IPaymentGateway gateway = new StripeGateway(new HttpClient(handler), Options.Create(options), runtime, NullLogger<StripeGateway>.Instance);

        var result = await gateway.ProcessRefundAsync("pi_123", 10m, "AUD", "requested_by_customer", "refund-idem-123", default);

        Assert.Equal("re_1", result.RefundId);
        Assert.Equal("refund-idem-123", handler.IdempotencyKey);
    }

    [Fact]
    public async Task PayPalRefund_SendsRequestIdHeader()
    {
        var options = new BillingOptions
        {
            AllowSandboxFallbacks = false,
            PayPal = new PayPalBillingOptions { UseSandbox = true }
        };
        var handler = new CapturingPayPalRefundHandler();
        var runtime = TestRuntimeSettingsProvider.FromBillingSettings(new BillingSettings(
            StripeSecretKey: null,
            StripePublishableKey: null,
            StripeWebhookSecret: null,
            StripeSuccessUrl: null,
            StripeCancelUrl: null,
            PayPalClientId: "runtime-client",
            PayPalClientSecret: "runtime-secret",
            PayPalWebhookId: "runtime-webhook",
            PayPalSuccessUrl: null,
            PayPalCancelUrl: null));
        IPaymentGateway gateway = new PayPalGateway(new HttpClient(handler), Options.Create(options), runtime);

        var result = await gateway.ProcessRefundAsync("CAPTURE-123", 10m, "AUD", "requested_by_customer", "refund-idem-456", default);

        Assert.Equal("PAYPAL-REFUND-1", result.RefundId);
        Assert.Equal("refund-idem-456", handler.PayPalRequestId);
    }

    [Fact]
    public async Task PayPalCapture_PostsToCaptureEndpoint_WithRequestIdAndParsesCapture()
    {
        var options = new BillingOptions
        {
            AllowSandboxFallbacks = false,
            PayPal = new PayPalBillingOptions { UseSandbox = true }
        };
        var handler = new CapturingPayPalCaptureHandler();
        var runtime = TestRuntimeSettingsProvider.FromBillingSettings(new BillingSettings(
            StripeSecretKey: null,
            StripePublishableKey: null,
            StripeWebhookSecret: null,
            StripeSuccessUrl: null,
            StripeCancelUrl: null,
            PayPalClientId: "runtime-client",
            PayPalClientSecret: "runtime-secret",
            PayPalWebhookId: "runtime-webhook",
            PayPalSuccessUrl: null,
            PayPalCancelUrl: null));
        IPaymentGateway gateway = new PayPalGateway(new HttpClient(handler), Options.Create(options), runtime);

        var result = await gateway.CaptureOrderAsync("ORDER-1", "capture-ORDER-1", default);

        Assert.Equal("CAP-1", result.CaptureId);
        Assert.Equal("completed", result.Status);
        Assert.Equal(10.00m, result.AmountCaptured);
        Assert.Equal("GBP", result.Currency);
        Assert.Equal("capture-ORDER-1", handler.PayPalRequestId);
        Assert.Contains("/v2/checkout/orders/ORDER-1/capture", handler.CaptureRequestPath);
    }

    [Fact]
    public async Task StripeCapture_IsNotSupported()
    {
        var options = new BillingOptions { AllowSandboxFallbacks = true, Stripe = new StripeBillingOptions() };
        IPaymentGateway gateway = new StripeGateway(
            new HttpClient(),
            Options.Create(options),
            TestRuntimeSettingsProvider.FromBillingOptions(options),
            NullLogger<StripeGateway>.Instance);

        await Assert.ThrowsAsync<NotSupportedException>(() => gateway.CaptureOrderAsync("cs_test", "k", default));
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

    private sealed class CapturingStripeRefundHandler : HttpMessageHandler
    {
        public string? IdempotencyKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/v1/refunds", StringComparison.Ordinal) == true)
            {
                IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var values)
                    ? values.SingleOrDefault()
                    : null;
                return Task.FromResult(JsonResponse("""{"id":"re_1","status":"succeeded"}"""));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class CapturingPayPalCaptureHandler : HttpMessageHandler
    {
        public string? PayPalRequestId { get; private set; }
        public string? CaptureRequestPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/v1/oauth2/token", StringComparison.Ordinal) == true)
            {
                return Task.FromResult(JsonResponse("""{"access_token":"token-1"}"""));
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/capture", StringComparison.Ordinal) == true)
            {
                CaptureRequestPath = request.RequestUri.AbsolutePath;
                PayPalRequestId = request.Headers.TryGetValues("PayPal-Request-Id", out var values)
                    ? values.SingleOrDefault()
                    : null;
                return Task.FromResult(JsonResponse(
                    """{"id":"ORDER-1","status":"COMPLETED","purchase_units":[{"payments":{"captures":[{"id":"CAP-1","status":"COMPLETED","amount":{"currency_code":"GBP","value":"10.00"}}]}}]}"""));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class CapturingPayPalRefundHandler : HttpMessageHandler
    {
        public string? PayPalRequestId { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/v1/oauth2/token", StringComparison.Ordinal) == true)
            {
                return Task.FromResult(JsonResponse("""{"access_token":"token-1"}"""));
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/refund", StringComparison.Ordinal) == true)
            {
                PayPalRequestId = request.Headers.TryGetValues("PayPal-Request-Id", out var values)
                    ? values.SingleOrDefault()
                    : null;
                return Task.FromResult(JsonResponse("""{"id":"PAYPAL-REFUND-1","status":"COMPLETED"}"""));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
}
