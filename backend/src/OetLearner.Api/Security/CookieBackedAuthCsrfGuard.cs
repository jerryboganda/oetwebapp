using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services;

namespace OetLearner.Api.Security;

public sealed class CookieBackedAuthCsrfGuard(
    IOptions<PlatformOptions> platformOptions,
    IConfiguration configuration,
    IWebHostEnvironment environment)
{
    private const string RefreshCookieName = "oet_rt";
    private const string CsrfCookieName = "oet_csrf";
    private const string CsrfHeaderName = "x-csrf-token";
    private const string ClientPlatformHeader = "X-OET-Client-Platform";

    public void ValidateCookieBackedAuthMutation(HttpContext context, string? bodyRefreshToken)
    {
        if (!context.Request.Cookies.ContainsKey(RefreshCookieName))
        {
            return;
        }

        if (HasExplicitNativeBodyToken(context, bodyRefreshToken))
        {
            return;
        }

        ValidateOriginOrReferer(context);
        ValidateDoubleSubmitToken(context);
    }

    private static bool HasExplicitNativeBodyToken(HttpContext context, string? bodyRefreshToken)
    {
        if (string.IsNullOrWhiteSpace(bodyRefreshToken))
        {
            return false;
        }

        var platform = context.Request.Headers[ClientPlatformHeader].ToString();
        return platform.Equals("capacitor", StringComparison.OrdinalIgnoreCase)
            || platform.Equals("desktop", StringComparison.OrdinalIgnoreCase)
            || platform.Equals("native", StringComparison.OrdinalIgnoreCase);
    }

    private void ValidateOriginOrReferer(HttpContext context)
    {
        var source = context.Request.Headers.Origin.FirstOrDefault()
            ?? context.Request.Headers.Referer.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(source))
        {
            if (environment.IsDevelopment())
            {
                return;
            }

            throw ApiException.Forbidden("csrf_origin_required", "Origin or Referer is required for cookie-backed auth mutations.");
        }

        if (!Uri.TryCreate(source, UriKind.Absolute, out var sourceUri))
        {
            throw ApiException.Forbidden("csrf_origin_invalid", "Origin or Referer is invalid for cookie-backed auth mutations.");
        }

        var sourceOrigin = ToOrigin(sourceUri);
        if (AllowedOrigins(context).Contains(sourceOrigin, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        if (environment.IsDevelopment()
            && (sourceUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || sourceUri.Host.Equals("127.0.0.1", StringComparison.Ordinal)))
        {
            return;
        }

        throw ApiException.Forbidden("csrf_origin_forbidden", "Origin is not allowed for cookie-backed auth mutations.");
    }

    private void ValidateDoubleSubmitToken(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CsrfCookieName, out var cookieToken)
            || string.IsNullOrWhiteSpace(cookieToken)
            || !context.Request.Headers.TryGetValue(CsrfHeaderName, out var headerValues)
            || string.IsNullOrWhiteSpace(headerValues.FirstOrDefault()))
        {
            throw ApiException.Forbidden("csrf_token_required", "CSRF token is required for cookie-backed auth mutations.");
        }

        var headerToken = headerValues.First()!;
        var cookieBytes = Encoding.UTF8.GetBytes(cookieToken);
        var headerBytes = Encoding.UTF8.GetBytes(headerToken);
        if (cookieBytes.Length != headerBytes.Length
            || !CryptographicOperations.FixedTimeEquals(cookieBytes, headerBytes))
        {
            throw ApiException.Forbidden("csrf_token_invalid", "CSRF token is invalid for cookie-backed auth mutations.");
        }
    }

    private HashSet<string> AllowedOrigins(HttpContext context)
    {
        var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"{context.Request.Scheme}://{context.Request.Host.Value}"
        };

        AddConfiguredOrigin(origins, platformOptions.Value.PublicWebBaseUrl);
        AddConfiguredOrigin(origins, platformOptions.Value.PublicApiBaseUrl);

        var configuredCors = configuration["Cors:AllowedOriginsCsv"];
        if (!string.IsNullOrWhiteSpace(configuredCors))
        {
            foreach (var rawOrigin in configuredCors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddConfiguredOrigin(origins, rawOrigin);
            }
        }

        return origins;
    }

    private static void AddConfiguredOrigin(HashSet<string> origins, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return;
        }

        origins.Add(ToOrigin(uri));
    }

    private static string ToOrigin(Uri uri)
    {
        var builder = new UriBuilder(uri.Scheme, uri.Host, uri.IsDefaultPort ? -1 : uri.Port);
        return builder.Uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }
}
