namespace OetWithDrHesham.Api.Configuration;

/// <summary>
/// FX rate provider configuration. Defaults work offline using the seeded
/// May 2026 reference table; production should set ApiKey + ApiBaseUrl
/// to point at openexchangerates.org or compatible.
/// </summary>
public sealed class FxOptions
{
    public string BaseCurrency { get; set; } = "USD";

    /// <summary>App id / API key for the upstream FX provider.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Base URL for the upstream FX provider. Default null = use offline seed.</summary>
    public string? ApiBaseUrl { get; set; } = "https://openexchangerates.org/api";

    /// <summary>When true, all checkout amounts are FX-converted into the buyer's display currency.</summary>
    public bool DynamicPricingEnabled { get; set; } = false;
}
