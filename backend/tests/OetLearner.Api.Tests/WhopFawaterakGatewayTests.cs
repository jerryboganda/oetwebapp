using System.Text.Json;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Billing.Gateways;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Tests;

public class WhopFawaterakGatewayTests
{
    [Fact]
    public async Task FawaterakWebhook_RejectsBadHash()
    {
        var runtime = new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base() with
        {
            Fawaterak = new FawaterakSettings("https://app.fawaterk.com", "hash-secret", "FAWATERAK.1", null, null, null),
        });
        var gateway = new FawaterakGateway(new HttpClient(), Options.Create(new BillingOptions()), runtime);
        var payload = JsonSerializer.Serialize(new
        {
            invoice_id = "1",
            invoice_key = "k",
            hashKey = "nope",
            invoice_status = "paid",
            payLoad = "quote-1",
        });

        var result = await gateway.HandleWebhookAsync(payload, new Dictionary<string, string>(), default);

        Assert.False(result.Processed);
        Assert.Equal("signature_invalid", result.EventType);
    }

    [Fact]
    public async Task FawaterakWebhook_VerifiedPaidCallback_Completes()
    {
        const string hashKey = "hash-secret";
        const string invoiceId = "88";
        const string invoiceKey = "inv-key";
        var runtime = new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base() with
        {
            Fawaterak = new FawaterakSettings("https://app.fawaterk.com", hashKey, "FAWATERAK.1", null, null, null),
        });
        var gateway = new FawaterakGateway(new HttpClient(), Options.Create(new BillingOptions()), runtime);
        var payload = JsonSerializer.Serialize(new
        {
            invoice_id = invoiceId,
            invoice_key = invoiceKey,
            hashKey = PaymentCallbackHmac.HmacSha256Hex(hashKey, invoiceId + invoiceKey),
            invoice_status = "paid",
            payLoad = "quote-1",
        });

        var result = await gateway.HandleWebhookAsync(payload, new Dictionary<string, string>(), default);

        Assert.True(result.Processed);
        Assert.Equal("completed", result.NormalizedStatus);
        Assert.Equal(invoiceId, result.GatewayTransactionId);
    }

    [Fact]
    public async Task WhopWebhook_RejectsBadSignatureWhenSecretConfigured()
    {
        var runtime = new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base() with
        {
            Whop = new WhopSettings("https://api.whop.com/api/v1", "api-key", "biz_1", "whop-secret", null, null),
        });
        var gateway = new WhopGateway(new HttpClient(), Options.Create(new BillingOptions { WebhookMaxAgeSeconds = 300 }), runtime);

        var result = await gateway.HandleWebhookAsync(
            """{"type":"payment.succeeded","data":{"id":"pay_1"}}""",
            new Dictionary<string, string> { ["webhook-signature"] = "t=1,v1=deadbeef" },
            default);

        Assert.False(result.Processed);
        Assert.Equal("signature_invalid", result.EventType);
    }

    [Fact]
    public async Task WhopSandboxCheckout_ReturnsEmbeddedIntent()
    {
        var runtime = new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base());
        var gateway = new WhopGateway(
            new HttpClient(),
            Options.Create(new BillingOptions { AllowSandboxFallbacks = true, CheckoutBaseUrl = "https://app.example/checkout" }),
            runtime);

        var result = await gateway.CreatePaymentIntentAsync(new CreatePaymentIntentRequest(
            "user-1", 10m, "GBP", "plan_purchase", "quote-1", "OET", null), default);

        Assert.StartsWith("whop_sandbox_", result.GatewayTransactionId);
        Assert.Contains("/sandbox/whop", result.CheckoutUrl);
    }

    [Fact]
    public async Task WhopSandboxWebhook_WithoutKeys_CompletesPayment()
    {
        var runtime = new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base());
        var gateway = new WhopGateway(
            new HttpClient(),
            Options.Create(new BillingOptions { AllowSandboxFallbacks = true }),
            runtime);

        var result = await gateway.HandleWebhookAsync(
            """{"type":"payment.succeeded","data":{"id":"whop_sandbox_abc","status":"paid","metadata":{"quote_id":"quote-1"}}}""",
            new Dictionary<string, string>(),
            default);

        Assert.True(result.Processed);
        Assert.Equal("completed", result.NormalizedStatus);
        Assert.Equal("quote-1", result.GatewayTransactionId);
    }
}
