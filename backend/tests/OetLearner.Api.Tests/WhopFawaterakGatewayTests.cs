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

    [Fact]
    public async Task WhopCreateIntent_ParsesRootCheckoutConfiguration()
    {
        var handler = new StubHandler
        {
            Response = """{"id":"ch_live1","purchase_url":"https://whop.com/embedded/checkout/ch_live1/","plan":{"id":"plan_live1"}}""",
        };
        var runtime = new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base() with
        {
            Whop = new WhopSettings("https://api.whop.com/api/v1", "apik_test", "biz_1", null, null, null),
        });
        var gateway = new WhopGateway(new HttpClient(handler), Options.Create(new BillingOptions()), runtime);

        var result = await gateway.CreatePaymentIntentAsync(new CreatePaymentIntentRequest(
            "user-1", 10m, "GBP", "wallet_top_up", "quote-1", "Wallet top-up", null), default);

        Assert.Equal("ch_live1", result.GatewayTransactionId);
        Assert.Equal("plan_live1", result.ClientSecret);
        Assert.Equal("https://whop.com/embedded/checkout/ch_live1/", result.CheckoutUrl);
        Assert.Equal("https://api.whop.com/api/v1/checkout_configurations", handler.LastUri?.ToString());
        Assert.StartsWith("Bearer apik_test", handler.LastAuthorization);
    }

    [Fact]
    public async Task FawaterakCreateIntent_ParsesNumericInvoiceId()
    {
        var handler = new StubHandler
        {
            Response = """{"status":"success","data":{"invoice_id":2726912869,"invoice_key":"272691286929958","payment_data":{"redirectTo":"https://app.fawaterk.com/ts/demo"}}}""",
        };
        var runtime = new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base() with
        {
            Fawaterak = new FawaterakSettings("https://app.fawaterk.com", "hash-secret", "FAWATERAK.1", null, null, null),
        });
        var gateway = new FawaterakGateway(new HttpClient(handler), Options.Create(new BillingOptions()), runtime);

        var result = await gateway.CreatePaymentIntentAsync(new CreatePaymentIntentRequest(
            "user-1", 10m, "USD", "wallet_top_up", "quote-1", "Wallet top-up", null), default);

        Assert.Equal("2726912869", result.GatewayTransactionId);
        Assert.Equal("272691286929958", result.ClientSecret);
        Assert.Equal("https://app.fawaterk.com/ts/demo", result.CheckoutUrl);
        Assert.Equal("https://app.fawaterk.com/api/v2/invoiceInitPay", handler.LastUri?.ToString());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public string Response { get; set; } = "{}";
        public Uri? LastUri { get; private set; }
        public string? LastAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization?.ToString()
                ?? (request.Headers.TryGetValues("Authorization", out var values) ? values.FirstOrDefault() : null);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(Response, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
