namespace OetLearner.Api.Configuration;

/// <summary>
/// Checkout.com hosted payments configuration. 3DS2 cards, Apple Pay, Google Pay,
/// mada. Tokenized vault for recurring renewals across MENA.
/// </summary>
public sealed class CheckoutComOptions
{
    public string ApiBaseUrl { get; set; } = "https://api.checkout.com";

    public string? SecretKey { get; set; }
    public string? PublicKey { get; set; }
    public string? ProcessingChannelId { get; set; }

    /// <summary>Webhook signing secret used for signature verification.</summary>
    public string? WebhookSecret { get; set; }

    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }
}
