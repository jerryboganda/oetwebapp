using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public class PaymentGatewaySecurityTests
{
    [Fact]
    public async Task StripeWebhook_MissingSecret_RejectsEvenWhenSandboxFallbacksAreEnabled()
    {
        var gateway = new StripeGateway(new HttpClient(), Options.Create(new BillingOptions
        {
            AllowSandboxFallbacks = true,
            Stripe = new StripeBillingOptions
            {
                WebhookSecret = null
            }
        }));

        var result = await gateway.HandleWebhookAsync("{}", new Dictionary<string, string>(), default);

        Assert.False(result.Processed);
        Assert.Equal("signature_verification_failed", result.EventType);
        Assert.Contains("webhook secret", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StripeCheckout_MissingConfiguration_ThrowsWhenSandboxFallbacksAreDisabled()
    {
        var gateway = new StripeGateway(new HttpClient(), Options.Create(new BillingOptions
        {
            AllowSandboxFallbacks = false,
            Stripe = new StripeBillingOptions()
        }));

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
}
