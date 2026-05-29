using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services;

public sealed class SoketiPushDispatcher : IWebPushDispatcher
{
    private readonly SoketiOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SoketiPushDispatcher> _logger;

    public SoketiPushDispatcher(
        IOptions<SoketiOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<SoketiPushDispatcher> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task SendAsync(
        OetLearner.Api.Domain.PushSubscription subscription,
        string payload,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Soketi push disabled; skipping dispatch");
            return;
        }

        // The channel is derived from the subscription's user — Soketi uses private channels per user
        var channel = $"private-user-{subscription.AuthAccountId}";
        var eventName = "notification";

        await TriggerEventAsync(channel, eventName, payload, cancellationToken);
    }

    public async Task TriggerEventAsync(
        string channel, string eventName, string data, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Soketi push disabled; skipping trigger for channel={Channel}", channel);
            return;
        }

        var body = JsonSerializer.Serialize(new
        {
            name = eventName,
            channel,
            data
        });

        var path = $"/apps/{_options.AppId}/events";
        var method = "POST";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var bodyMd5 = ComputeMd5Hex(body);

        var queryString = BuildSignedQueryString(method, path, timestamp, bodyMd5, body);
        var scheme = _options.UseTls ? "https" : "http";
        var url = $"{scheme}://{_options.Host}:{_options.Port}{path}?{queryString}";

        var client = _httpClientFactory.CreateClient("Soketi");
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
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

    private string BuildSignedQueryString(string method, string path, string timestamp, string bodyMd5, string body)
    {
        // Pusher HTTP API auth: sorted query params + HMAC-SHA256 signature
        var queryParams = new SortedDictionary<string, string>
        {
            ["auth_key"] = _options.AppKey,
            ["auth_timestamp"] = timestamp,
            ["auth_version"] = "1.0",
            ["body_md5"] = bodyMd5
        };

        var queryString = string.Join("&", queryParams.Select(kv => $"{kv.Key}={kv.Value}"));
        var signPayload = $"{method}\n{path}\n{queryString}";

        var signature = ComputeHmacSha256(_options.AppSecret, signPayload);
        return $"{queryString}&auth_signature={signature}";
    }

    private static string ComputeHmacSha256(string secret, string data)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeMd5Hex(string input)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
