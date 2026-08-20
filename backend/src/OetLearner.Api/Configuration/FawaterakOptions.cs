namespace OetLearner.Api.Configuration;

/// <summary>
/// Fawaterak / Fawaterk (secondary Global / Outside Egypt) configuration.
/// Inactive until <see cref="HashApiKey"/> is set. Secrets must stay server-side.
/// </summary>
public sealed class FawaterakOptions
{
    public string ApiBaseUrl { get; set; } = "https://app.fawaterk.com";

    /// <summary>HASH API key used as the vendor Bearer token and callback HMAC secret.</summary>
    public string? HashApiKey { get; set; }

    /// <summary>Provider key from the Fawaterak dashboard (e.g. FAWATERAK.29958).</summary>
    public string? ProviderKey { get; set; }

    public string? SuccessUrl { get; set; }
    public string? FailUrl { get; set; }
    public string? PendingUrl { get; set; }
}
