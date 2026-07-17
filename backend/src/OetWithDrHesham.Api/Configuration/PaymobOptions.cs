namespace OetWithDrHesham.Api.Configuration;

/// <summary>
/// Paymob payment configuration. Egypt-first: cards, Meeza, Fawry (cash voucher),
/// Vodafone Cash, Orange Cash, Etisalat Cash, Aman. Two-phase auth: order →
/// payment-key → iframe redirect.
/// </summary>
public sealed class PaymobOptions
{
    public string ApiBaseUrl { get; set; } = "https://accept.paymob.com";

    public string? ApiKey { get; set; }

    /// <summary>Merchant id; required by /api/auth/tokens.</summary>
    public string? MerchantId { get; set; }

    /// <summary>HMAC secret used to verify SHA-512 webhook signature.</summary>
    public string? HmacSecret { get; set; }

    /// <summary>Map of method name (card | fawry | vodafone_cash | aman) → integration id.</summary>
    public Dictionary<string, int> IntegrationIds { get; set; } = new();

    /// <summary>iframe id used when constructing the hosted iframe URL.</summary>
    public int IframeId { get; set; }

    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }
}
