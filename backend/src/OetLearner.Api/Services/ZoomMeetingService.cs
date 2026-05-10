using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services;

/// <summary>
/// Integrates with Zoom's Server-to-Server OAuth to create and manage meetings.
/// Token is cached until expiry. All calls are idempotent where possible.
/// </summary>
public sealed partial class ZoomMeetingService(
    IHttpClientFactory httpClientFactory,
    IOptions<ZoomOptions> options,
    ILogger<ZoomMeetingService> logger)
{
    private readonly ZoomOptions _opts = options.Value;
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    // ── Public API ──────────────────────────────────────────────────────

    public async Task<ZoomMeetingResult> CreateMeetingAsync(
        string topic, DateTimeOffset startTime, int durationMinutes,
        string timezone, CancellationToken ct)
    {
        if (_opts.AllowSandboxFallback &&
            string.IsNullOrEmpty(_opts.ClientId))
        {
            // Sandbox fallback for dev/test without real Zoom credentials
            logger.LogWarning("Zoom credentials not configured — returning sandbox meeting");
            return new ZoomMeetingResult(
                MeetingId: 999_000_000 + Random.Shared.Next(1000, 99999),
                JoinUrl: $"https://zoom.us/j/sandbox-{Guid.NewGuid():N}",
                StartUrl: $"https://zoom.us/s/sandbox-{Guid.NewGuid():N}",
                Password: "sandbox123");
        }

        var hostUserId = RequireOption(_opts.HostUserId, nameof(ZoomOptions.HostUserId));
        var token = await GetAccessTokenAsync(ct);
        var client = httpClientFactory.CreateClient("ZoomApi");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            topic,
            type = 2, // Scheduled meeting
            start_time = startTime.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            duration = durationMinutes,
            timezone,
            settings = new
            {
                host_video = true,
                participant_video = true,
                join_before_host = false,
                mute_upon_entry = true,
                waiting_room = true,
                auto_recording = "cloud",
                meeting_authentication = false
            }
        };

        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var url = $"{_opts.ApiBaseUrl}/users/{hostUserId}/meetings";
        var response = await client.PostAsync(url, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var safeErrorBody = SanitizeProviderErrorBody(errorBody);
            logger.LogError("Zoom API error {Status}: {Body}", response.StatusCode, safeErrorBody);
            throw new InvalidOperationException(
                $"Zoom API returned {(int)response.StatusCode}: {safeErrorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var meeting = JsonSerializer.Deserialize<ZoomCreateMeetingResponse>(responseJson, JsonOpts)
            ?? throw new InvalidOperationException("Failed to deserialize Zoom meeting response");

        logger.LogInformation("Created Zoom meeting {MeetingId} for {Topic}", meeting.Id, topic);

        return new ZoomMeetingResult(
            MeetingId: meeting.Id,
            JoinUrl: meeting.JoinUrl,
            StartUrl: meeting.StartUrl,
            Password: meeting.Password);
    }

    public async Task DeleteMeetingAsync(long meetingId, CancellationToken ct)
    {
        if (_opts.AllowSandboxFallback && string.IsNullOrEmpty(_opts.ClientId))
        {
            logger.LogWarning("Zoom sandbox mode — skipping meeting deletion for {MeetingId}", meetingId);
            return;
        }

        var token = await GetAccessTokenAsync(ct);
        var client = httpClientFactory.CreateClient("ZoomApi");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var url = $"{_opts.ApiBaseUrl}/meetings/{meetingId}";
        var response = await client.DeleteAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var safeErrorBody = SanitizeProviderErrorBody(errorBody);
            logger.LogWarning("Zoom meeting deletion failed for {MeetingId}: {Status} {Body}",
                meetingId, response.StatusCode, safeErrorBody);
        }
        else
        {
            logger.LogInformation("Deleted Zoom meeting {MeetingId}", meetingId);
        }
    }

    // ── Token Acquisition ───────────────────────────────────────────────

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-2))
            return _cachedToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-2))
                return _cachedToken;

            var client = httpClientFactory.CreateClient("ZoomAuth");
            var accountId = RequireOption(_opts.AccountId, nameof(ZoomOptions.AccountId));
            var clientId = RequireOption(_opts.ClientId, nameof(ZoomOptions.ClientId));
            var clientSecret = RequireOption(_opts.ClientSecret, nameof(ZoomOptions.ClientSecret));

            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}"));
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "account_credentials",
                ["account_id"] = accountId
            });

            var response = await client.PostAsync(_opts.TokenUrl, form, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("Zoom token error {Status}: {Body}", response.StatusCode, SanitizeProviderErrorBody(errorBody));
                throw new InvalidOperationException(
                    $"Failed to obtain Zoom access token: {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var tokenResponse = JsonSerializer.Deserialize<ZoomTokenResponse>(json, JsonOpts)
                ?? throw new InvalidOperationException("Failed to deserialize Zoom token response");
            if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
                throw new InvalidOperationException("Zoom token response did not include an access token");

            _cachedToken = tokenResponse.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

            logger.LogInformation("Acquired Zoom access token, expires at {Expiry}", _tokenExpiresAt);
            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static string SanitizeProviderErrorBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;

        var sanitized = SensitiveJsonValueRegex().Replace(body, "$1\"[redacted]\"");
        sanitized = sanitized.Replace('\r', ' ').Replace('\n', ' ');
        return sanitized.Length <= 512 ? sanitized : sanitized[..512] + "...";
    }

    private static string RequireOption(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Zoom option {name} is required when sandbox fallback is disabled.");

        return value;
    }

    [GeneratedRegex("(?i)(\"(?:access_token|refresh_token|token|api_key|client_secret|secret|password)\"\\s*:\\s*)\"[^\"]*\"")]
    private static partial Regex SensitiveJsonValueRegex();

    // ── JSON Serialization ──────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };
}

// ── Zoom Response DTOs ──────────────────────────────────────────────────

public record ZoomMeetingResult(
    long MeetingId,
    string JoinUrl,
    string StartUrl,
    string Password);

internal sealed class ZoomCreateMeetingResponse
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("join_url")]
    public string JoinUrl { get; set; } = default!;

    [JsonPropertyName("start_url")]
    public string StartUrl { get; set; } = default!;

    [JsonPropertyName("password")]
    public string Password { get; set; } = default!;
}

internal sealed class ZoomTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = default!;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = default!;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}
