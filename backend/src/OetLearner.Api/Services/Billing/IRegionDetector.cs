using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Detects the buyer's billing region from request context. Order of preference:
/// 1. user's stored PreferredRegion (when authenticated and stored),
/// 2. Cloudflare/Vercel geo header,
/// 3. Accept-Language country hint,
/// 4. BillingOptions.DefaultRegion fallback.
/// </summary>
public interface IRegionDetector
{
    RegionDetectionResult Detect(HttpContext http, string? storedCountry = null, string? storedRegion = null, string? storedCurrency = null);
}

public sealed record RegionDetectionResult(string Region, string Country, string Currency, string Source);

public sealed class RegionDetector : IRegionDetector
{
    private readonly BillingOptions _options;

    public RegionDetector(IOptions<BillingOptions> options)
    {
        _options = options.Value;
    }

    public RegionDetectionResult Detect(HttpContext http, string? storedCountry = null, string? storedRegion = null, string? storedCurrency = null)
    {
        // 1) Stored preference (post-onboarding).
        if (!string.IsNullOrWhiteSpace(storedRegion))
        {
            var country = (storedCountry ?? string.Empty).ToUpperInvariant();
            var currency = (storedCurrency ?? DefaultCurrencyForRegion(storedRegion)).ToUpperInvariant();
            return new RegionDetectionResult(storedRegion.ToUpperInvariant(), country, currency, "stored");
        }

        // 2) Edge geo header.
        var headers = http.Request.Headers;
        var geoCountry = headers["CF-IPCountry"].ToString();
        if (string.IsNullOrWhiteSpace(geoCountry))
        {
            geoCountry = headers["X-Vercel-IP-Country"].ToString();
        }
        if (string.IsNullOrWhiteSpace(geoCountry))
        {
            geoCountry = headers["X-AppEngine-Country"].ToString();
        }

        if (!string.IsNullOrWhiteSpace(geoCountry) && geoCountry.Length == 2 && geoCountry != "XX" && geoCountry != "T1")
        {
            var country = geoCountry.ToUpperInvariant();
            var region = BillingRegions.FromCountry(country);
            return new RegionDetectionResult(region, country, DefaultCurrencyForRegion(region), "geo_header");
        }

        // 3) Accept-Language hint (best-effort, e.g. "en-GB" → GB).
        var acceptLang = headers["Accept-Language"].ToString();
        if (!string.IsNullOrEmpty(acceptLang))
        {
            var dash = acceptLang.IndexOf('-');
            if (dash > 0 && dash + 3 <= acceptLang.Length)
            {
                var maybeCountry = acceptLang.Substring(dash + 1, 2).ToUpperInvariant();
                if (maybeCountry.All(char.IsLetter))
                {
                    var region = BillingRegions.FromCountry(maybeCountry);
                    return new RegionDetectionResult(region, maybeCountry, DefaultCurrencyForRegion(region), "accept_language");
                }
            }
        }

        // 4) Configured default.
        var defaultRegion = string.IsNullOrWhiteSpace(_options.DefaultRegion) ? BillingRegions.RestOfWorld : _options.DefaultRegion;
        return new RegionDetectionResult(defaultRegion, string.Empty, _options.DefaultCurrency, "default");
    }

    private string DefaultCurrencyForRegion(string region) => region.ToUpperInvariant() switch
    {
        BillingRegions.UnitedKingdom => "GBP",
        BillingRegions.Gulf => "AED",
        BillingRegions.Egypt => "EGP",
        BillingRegions.Pakistan => "PKR",
        _ => _options.DefaultCurrency,
    };
}
