using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Settings;

namespace OetWithDrHesham.Api.Tests;

/// <summary>
/// LIVE integration tests that drive <see cref="PayPalGateway"/> against the REAL PayPal
/// sandbox REST API. They exercise this app's own gateway plumbing (effective-settings
/// resolution → OAuth → order create / host selection), not just raw PayPal calls.
///
/// They are gated on the <c>PAYPAL_SANDBOX_CLIENT_ID</c> / <c>PAYPAL_SANDBOX_SECRET</c>
/// environment variables and no-op (return) when those are absent, so CI and other
/// developers are never blocked or charged. To run them:
///
///   PAYPAL_SANDBOX_CLIENT_ID=... PAYPAL_SANDBOX_SECRET=... \
///     dotnet test --filter FullyQualifiedName~PayPalSandboxLiveTests
/// </summary>
public class PayPalSandboxLiveTests
{
    private static (string ClientId, string Secret)? SandboxCreds()
    {
        var clientId = Environment.GetEnvironmentVariable("PAYPAL_SANDBOX_CLIENT_ID");
        var secret = Environment.GetEnvironmentVariable("PAYPAL_SANDBOX_SECRET");
        return string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret)
            ? null
            : (clientId, secret);
    }

    private static IRuntimeSettingsProvider RuntimeFor(string clientId, string secret, bool useSandbox)
        => TestRuntimeSettingsProvider.FromBillingSettings(new BillingSettings(
            StripeSecretKey: null,
            StripePublishableKey: null,
            StripeWebhookSecret: null,
            StripeSuccessUrl: null,
            StripeCancelUrl: null,
            PayPalClientId: clientId,
            PayPalClientSecret: secret,
            PayPalWebhookId: null,
            PayPalSuccessUrl: "https://app.example/billing/payment-return",
            PayPalCancelUrl: "https://app.example/checkout/cancel",
            PayPalUseSandbox: useSandbox));

    [Fact]
    public async Task PayPalGateway_AgainstRealSandbox_CreatesOrder()
    {
        var creds = SandboxCreds();
        if (creds is null)
        {
            return; // live test — only runs when sandbox creds are supplied via env vars
        }

        var options = new BillingOptions { AllowSandboxFallbacks = false };
        var gateway = new PayPalGateway(
            new HttpClient(),
            Options.Create(options),
            RuntimeFor(creds.Value.ClientId, creds.Value.Secret, useSandbox: true));

        var result = await gateway.CreatePaymentIntentAsync(new CreatePaymentIntentRequest(
            UserId: "live-sandbox-test",
            Amount: 24.00m,
            Currency: "GBP",
            ProductType: "review_credits",
            ProductId: null,
            Description: "Live sandbox smoke test",
            Metadata: null), default);

        // A real PayPal sandbox order was created (proves OAuth + order-create against the
        // sandbox host via our effective-settings resolution).
        Assert.False(string.IsNullOrWhiteSpace(result.GatewayTransactionId));
        Assert.Equal("CREATED", result.Status, ignoreCase: true);
        Assert.Contains("sandbox.paypal.com", result.CheckoutUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PayPalGateway_LiveHostWithSandboxCreds_Throws()
    {
        var creds = SandboxCreds();
        if (creds is null)
        {
            return;
        }

        var options = new BillingOptions { AllowSandboxFallbacks = false };
        // UseSandbox=false → the gateway must call the LIVE host. Sandbox creds against the
        // live host are rejected (401 invalid_client), so order creation throws. This is the
        // empirical proof that honouring the runtime sandbox/live flag is what unblocks go-live.
        // The gateway now parses PayPal's error envelope and throws a typed
        // PaymentGatewayApiException (carrying the upstream 401) instead of a raw
        // HttpRequestException — see EnsurePayPalSuccessAsync.
        var gateway = new PayPalGateway(
            new HttpClient(),
            Options.Create(options),
            RuntimeFor(creds.Value.ClientId, creds.Value.Secret, useSandbox: false));

        var ex = await Assert.ThrowsAsync<PaymentGatewayApiException>(() => gateway.CreatePaymentIntentAsync(new CreatePaymentIntentRequest(
            UserId: "live-sandbox-test",
            Amount: 24.00m,
            Currency: "GBP",
            ProductType: "review_credits",
            ProductId: null,
            Description: "Live host rejection check",
            Metadata: null), default));
        Assert.Equal(401, ex.UpstreamStatusCode);
    }
}
