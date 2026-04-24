namespace OetLearner.Api.Configuration;

public sealed class BillingOptions
{
    public string? CheckoutBaseUrl { get; set; }
    public bool AllowSandboxFallbacks { get; set; } = false;
    public StripeBillingOptions Stripe { get; set; } = new();
    public PayPalBillingOptions PayPal { get; set; } = new();
}

public sealed class StripeBillingOptions
{
    public string ApiBaseUrl { get; set; } = "https://api.stripe.com";
    public string? SecretKey { get; set; }
    public string? PublishableKey { get; set; }
    public string? WebhookSecret { get; set; }
    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }
}

public sealed class PayPalBillingOptions
{
    public bool UseSandbox { get; set; } = true;
    public string ApiBaseUrl { get; set; } = "https://api-m.paypal.com";
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? WebhookId { get; set; }
    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }
}
