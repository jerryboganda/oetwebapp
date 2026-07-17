using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OetWithDrHesham.Api.Services.Settings;

namespace OetWithDrHesham.Api.Services;

public sealed class SoketiPushDispatcher : IWebPushDispatcher
{
    private readonly IRuntimeSettingsProvider _runtimeSettings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SoketiPushDispatcher> _logger;

    public SoketiPushDispatcher(
        IRuntimeSettingsProvider runtimeSettings,
        IHttpClientFactory httpClientFactory,
        ILogger<SoketiPushDispatcher> logger)
    {
        _runtimeSettings = runtimeSettings;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task SendAsync(
        OetWithDrHesham.Api.Domain.PushSubscription subscription,
        string payload,
        CancellationToken cancellationToken = default)
    {
        var settings = (await _runtimeSettings.GetAsync(cancellationToken)).Soketi;
        if (!settings.Enabled)
        {
            _logger.LogDebug("Soketi push disabled; skipping dispatch");
            return;
        }

        // The channel is derived from the subscription's user — Soketi uses private channels per user
        var channel = $"private-user-{subscription.AuthAccountId}";
        await TriggerEventAsync(channel, "notification", payload, cancellationToken);
    }

    public async Task TriggerEventAsync(
        string channel, string eventName, string data, CancellationToken cancellationToken = default)
    {
        var settings = (await _runtimeSettings.GetAsync(cancellationToken)).Soketi;
        if (!settings.Enabled)
        {
            _logger.LogDebug("Soketi push disabled; skipping trigger for channel={Channel}", channel);
            return;
        }

        var body = JsonSerializer.Serialize(new { name = eventName, channel, data });
        var path = $"/apps/{settings.AppId}/events";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var queryString = SoketiPusherSigner.BuildSignedQueryString(
            "POST", path, timestamp, settings.AppKey, settings.AppSecret ?? string.Empty, body);
        var scheme = settings.UseTls ? "https" : "http";
        var url = $"{scheme}://{settings.Host}:{settings.Port}{path}?{queryString}";

        var client = _httpClientFactory.CreateClient("Soketi");
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Soketi push failed: status={StatusCode} body={Body} channel={Channel}",
                (int)response.StatusCode, responseBody, channel);
        }
        else
        {
            _logger.LogDebug("Soketi push sent to channel={Channel} event={Event}", channel, eventName);
        }
    }
}

/// <summary>
/// Pusher HTTP API request signer (Soketi is Pusher-protocol compatible).
/// Shared between <see cref="SoketiPushDispatcher"/> and the admin
/// connection-test handler so both sign identically.
/// </summary>
public static class SoketiPusherSigner
{
    public static string BuildSignedQueryString(
        string method, string path, string timestamp, string appKey, string appSecret, string body)
    {
        var bodyMd5 = ComputeMd5Hex(body);
        var queryParams = new SortedDictionary<string, string>
        {
            ["auth_key"] = appKey,
            ["auth_timestamp"] = timestamp,
            ["auth_version"] = "1.0",
            ["body_md5"] = bodyMd5,
        };

        var queryString = string.Join("&", queryParams.Select(kv => $"{kv.Key}={kv.Value}"));
        var signPayload = $"{method}\n{path}\n{queryString}";
        var signature = ComputeHmacSha256(appSecret, signPayload);
        return $"{queryString}&auth_signature={signature}";
    }

    public static string ComputeHmacSha256(string secret, string data)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeMd5Hex(string input)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
