using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services;

public sealed class PlatformLinkService(IOptions<PlatformOptions> platformOptions, IOptions<BillingOptions> billingOptions)
{
    private readonly PlatformOptions _platform = platformOptions.Value;
    private readonly BillingOptions _billing = billingOptions.Value;

    public string BuildApiUrl(string relativePath)
    {
        var normalizedPath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(_platform.PublicApiBaseUrl))
        {
            return normalizedPath;
        }

        return new Uri(new Uri(EnsureTrailingSlash(_platform.PublicApiBaseUrl), UriKind.Absolute), normalizedPath.TrimStart('/')).ToString();
    }

    public string BuildWebUrl(string relativePath)
    {
        var normalizedPath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(_platform.PublicWebBaseUrl))
        {
            return normalizedPath;
        }

        return new Uri(new Uri(EnsureTrailingSlash(_platform.PublicWebBaseUrl), UriKind.Absolute), normalizedPath.TrimStart('/')).ToString();
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

        var domain = string.IsNullOrWhiteSpace(_platform.FallbackEmailDomain)
            ? "example.invalid"
            : _platform.FallbackEmailDomain.Trim().TrimStart('@');

        return $"{localPart}@{domain}";
    }

    private static string NormalizeRelativePath(string relativePath)
        => relativePath.StartsWith('/') ? relativePath : $"/{relativePath}";

    private static string EnsureTrailingSlash(string url)
        => url.EndsWith('/') ? url : $"{url}/";
}
