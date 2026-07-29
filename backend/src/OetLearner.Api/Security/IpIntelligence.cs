using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Security;

/// <summary>
/// What an external IP-intelligence feed knows about a sign-in address.
/// </summary>
public sealed record IpIntelligence(bool IsAnonymizer, bool IsDatacenter, string? Provider)
{
    public bool IsHighRiskNetwork => IsAnonymizer || IsDatacenter;
}

public interface IIpIntelligenceService
{
    /// <summary>Null when nothing is known (no provider configured, lookup
    /// failed, or private address). Never throws.</summary>
    Task<IpIntelligence?> LookupAsync(string? ipAddress, CancellationToken ct);
}

public sealed class NoopIpIntelligenceService : IIpIntelligenceService
{
    public Task<IpIntelligence?> LookupAsync(string? ipAddress, CancellationToken ct)
        => Task.FromResult<IpIntelligence?>(null);
}

/// <summary>
/// IPinfo-backed privacy/network lookup. Only public, parsed IP addresses are
/// sent. The token travels in an Authorization header (never the URL), the
/// response is cached for one hour, and provider failures return null so an
/// upstream outage cannot lock all learners out.
/// </summary>
public sealed class IpinfoIpIntelligenceService(
    IHttpClientFactory httpClientFactory,
    IRuntimeSettingsProvider settingsProvider,
    IMemoryCache cache,
    ILogger<IpinfoIpIntelligenceService> logger) : IIpIntelligenceService
{
    public const string HttpClientName = "IpinfoIpIntelligence";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    public async Task<IpIntelligence?> LookupAsync(string? ipAddress, CancellationToken ct)
    {
        if (!TryNormalizePublicIp(ipAddress, out var normalizedIp))
        {
            return null;
        }

        var settings = (await settingsProvider.GetAsync(ct)).IpIntelligence;
        if (!settings.IsConfigured)
        {
            return null;
        }

        var cacheKey = $"security:ip-intelligence:{normalizedIp}";
        if (cache.TryGetValue<IpIntelligence>(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"lookup/{normalizedIp}");
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", settings.IpinfoToken);
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await httpClientFactory
                .CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "IP intelligence lookup failed with provider status {StatusCode}; sign-in risk evaluation will fail open.",
                    (int)response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<IpinfoLookupResponse>(
                cancellationToken: ct);
            if (payload is null)
            {
                return null;
            }

            var result = new IpIntelligence(
                IsAnonymizer: payload.IsAnonymous
                    || payload.Anonymous?.IsProxy == true
                    || payload.Anonymous?.IsRelay == true
                    || payload.Anonymous?.IsTor == true
                    || payload.Anonymous?.IsVpn == true
                    || payload.Anonymous?.IsResidentialProxy == true,
                IsDatacenter: payload.IsHosting
                    || string.Equals(payload.AutonomousSystem?.Type, "hosting", StringComparison.OrdinalIgnoreCase),
                Provider: IpIntelligenceProviders.Ipinfo);

            cache.Set(cacheKey, result, CacheDuration);
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "IP intelligence lookup timed out; sign-in risk evaluation will fail open.");
            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(
                ex,
                "IP intelligence provider request failed; sign-in risk evaluation will fail open.");
            return null;
        }
        catch (System.Text.Json.JsonException ex)
        {
            logger.LogWarning(
                ex,
                "IP intelligence provider returned invalid JSON; sign-in risk evaluation will fail open.");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "IP intelligence lookup failed unexpectedly; sign-in risk evaluation will fail open.");
            return null;
        }
    }

    private static bool TryNormalizePublicIp(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (!IPAddress.TryParse(value, out var address))
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            if (bytes[0] is 0 or 10 or 127
                || bytes[0] >= 224
                || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 198 && bytes[1] is 18 or 19))
            {
                return false;
            }
        }
        else if ((bytes[0] & 0xfe) == 0xfc)
        {
            return false;
        }

        normalized = address.ToString();
        return true;
    }

    private sealed class IpinfoLookupResponse
    {
        [JsonPropertyName("anonymous")]
        public IpinfoAnonymous? Anonymous { get; init; }

        [JsonPropertyName("is_anonymous")]
        public bool IsAnonymous { get; init; }

        [JsonPropertyName("is_hosting")]
        public bool IsHosting { get; init; }

        [JsonPropertyName("as")]
        public IpinfoAutonomousSystem? AutonomousSystem { get; init; }
    }

    private sealed class IpinfoAnonymous
    {
        [JsonPropertyName("is_proxy")]
        public bool IsProxy { get; init; }

        [JsonPropertyName("is_relay")]
        public bool IsRelay { get; init; }

        [JsonPropertyName("is_tor")]
        public bool IsTor { get; init; }

        [JsonPropertyName("is_vpn")]
        public bool IsVpn { get; init; }

        [JsonPropertyName("is_res_proxy")]
        public bool IsResidentialProxy { get; init; }
    }

    private sealed class IpinfoAutonomousSystem
    {
        [JsonPropertyName("type")]
        public string? Type { get; init; }
    }
}
