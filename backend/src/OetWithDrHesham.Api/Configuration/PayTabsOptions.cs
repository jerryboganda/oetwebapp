namespace OetWithDrHesham.Api.Configuration;

/// <summary>
/// PayTabs hosted payment configuration. Covers UAE, Saudi, Oman, Qatar,
/// Kuwait, Bahrain, Egypt, Jordan. Local methods: Visa/MC, mada, KNET,
/// BENEFIT, Apple Pay, STC Pay.
/// </summary>
public sealed class PayTabsOptions
{
    /// <summary>Base API URL — defaults to UAE region; override per profile.</summary>
    public string ApiBaseUrl { get; set; } = "https://secure.paytabs.com";

    /// <summary>PayTabs server key (Authorization header).</summary>
    public string? ServerKey { get; set; }

    /// <summary>PayTabs profile id — uniquely identifies the merchant account/region.</summary>
    public string? ProfileId { get; set; }

    /// <summary>Webhook server-key used to verify HMAC-SHA256 signature header.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Default return/return-cancel URLs; can be overridden per request.</summary>
    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }
}
