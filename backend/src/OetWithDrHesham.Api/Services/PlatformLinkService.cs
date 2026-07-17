using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Services.Settings;

namespace OetWithDrHesham.Api.Services;

public sealed class PlatformLinkService(IRuntimeSettingsProvider settingsProvider, IOptions<BillingOptions> billingOptions)
{
    private readonly IRuntimeSettingsProvider _settingsProvider = settingsProvider;
    private readonly BillingOptions _billing = billingOptions.Value;

    // The public-facing Build* methods are synchronous and called from many
    // services. Read only the atomic last-known DB-over-env snapshot here;
    // synchronous URL composition must never trigger database I/O.
    private PlatformSettings Platform()
        => _settingsProvider.CurrentSnapshot?.Effective.Platform
           ?? throw new InvalidOperationException("Runtime platform settings have not been initialized.");

    public string BuildApiUrl(string relativePath)
    {
        var normalizedPath = NormalizeRelativePath(relativePath);
        var apiBaseUrl = Platform().PublicApiBaseUrl;
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            return normalizedPath;
        }

        return new Uri(new Uri(EnsureTrailingSlash(apiBaseUrl), UriKind.Absolute), normalizedPath.TrimStart('/')).ToString();
    }

    public string BuildWebUrl(string relativePath)
    {
        var normalizedPath = NormalizeRelativePath(relativePath);
        var webBaseUrl = Platform().PublicWebBaseUrl;
        if (string.IsNullOrWhiteSpace(webBaseUrl))
        {
            return normalizedPath;
        }

        return new Uri(new Uri(EnsureTrailingSlash(webBaseUrl), UriKind.Absolute), normalizedPath.TrimStart('/')).ToString();
    }

    public string BuildCheckoutUrl(
        string sessionId,
        string productType,
        int quantity,
        string? planId = null,
        string? couponCode = null,
        IEnumerable<string>? addOnCodes = null,
        string? quoteId = null)
    {
        if (string.IsNullOrWhiteSpace(_billing.CheckoutBaseUrl))
        {
            throw new InvalidOperationException("Billing:CheckoutBaseUrl must be configured.");
        }

        var baseUrl = _billing.CheckoutBaseUrl!;
        var query = new Dictionary<string, string?>
        {
            ["sessionId"] = sessionId,
            ["productType"] = productType,
            ["quantity"] = quantity.ToString(),
            ["planId"] = planId,
            ["couponCode"] = couponCode,
            ["quoteId"] = quoteId,
            ["addOnCodes"] = addOnCodes is null ? null : string.Join(',', addOnCodes.Where(code => !string.IsNullOrWhiteSpace(code)))
        };

        return QueryHelpers.AddQueryString(baseUrl, query);
    }

    public string BuildFallbackEmail(string subjectId)
    {
        var localPart = new string(subjectId
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')
            .ToArray())
            .Trim('-');

        if (string.IsNullOrWhiteSpace(localPart))
        {
            localPart = "user";
        }

        var fallbackDomain = Platform().FallbackEmailDomain;
        var domain = string.IsNullOrWhiteSpace(fallbackDomain)
            ? "example.invalid"
            : fallbackDomain.Trim().TrimStart('@');

        return $"{localPart}@{domain}";
    }

    private static string NormalizeRelativePath(string relativePath)
        => relativePath.StartsWith('/') ? relativePath : $"/{relativePath}";

    private static string EnsureTrailingSlash(string url)
        => url.EndsWith('/') ? url : $"{url}/";
}
