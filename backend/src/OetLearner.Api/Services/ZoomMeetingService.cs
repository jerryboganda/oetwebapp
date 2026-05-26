using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services;

/// <summary>
/// Integrates with Zoom's Server-to-Server OAuth to create and manage meetings.
/// Token is cached until expiry. All calls are idempotent where possible.
/// </summary>
public sealed partial class ZoomMeetingService(
    IHttpClientFactory httpClientFactory,
    IRuntimeSettingsProvider runtimeSettings,
    ILogger<ZoomMeetingService> logger)
{
    private string? _cachedToken;
    private string? _cachedTokenSettingsHash;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public async Task<string?> GetMeetingSdkKeyAsync(CancellationToken ct)
        => (await GetSettingsAsync(ct)).MeetingSdkKey;

    public async Task<bool> IsEnabledAsync(CancellationToken ct)
        => (await GetSettingsAsync(ct)).Enabled;

    public async Task<ZoomMeetingResult> CreateMeetingAsync(
        string topic,
        DateTimeOffset startTime,
        int durationMinutes,
        string timezone,
        CancellationToken ct)
    {
        var settings = await GetSettingsAsync(ct);
        if (!settings.Enabled)
        {
            throw new InvalidOperationException("Zoom integration is disabled.");
        }

        if (settings.AllowSandboxFallback && string.IsNullOrEmpty(settings.ClientId))
        {
            logger.LogWarning("Zoom credentials not configured; returning sandbox meeting");
            return new ZoomMeetingResult(
                MeetingId: 999_000_000 + Random.Shared.Next(1000, 99999),
                JoinUrl: $"https://zoom.us/j/sandbox-{Guid.NewGuid():N}",
                StartUrl: $"https://zoom.us/s/sandbox-{Guid.NewGuid():N}",
                Password: "sandbox123");
        }

        var hostUserId = RequireOption(settings.HostUserId, nameof(ZoomOptions.HostUserId));
        var token = await GetAccessTokenAsync(settings, ct);
        var client = httpClientFactory.CreateClient("ZoomApi");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            topic,
            type = 2,
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
        var response = await client.PostAsync($"{settings.ApiBaseUrl}/users/{hostUserId}/meetings", content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var safeErrorBody = SanitizeProviderErrorBody(errorBody);
            logger.LogError("Zoom API error {Status}: {Body}", response.StatusCode, safeErrorBody);
            throw new InvalidOperationException($"Zoom API returned {(int)response.StatusCode}: {safeErrorBody}");
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
        var settings = await GetSettingsAsync(ct);
        if (!settings.Enabled)
        {
            logger.LogInformation("Zoom integration disabled; skipping meeting deletion for {MeetingId}", meetingId);
            return;
        }

        if (settings.AllowSandboxFallback && string.IsNullOrEmpty(settings.ClientId))
        {
            logger.LogWarning("Zoom sandbox mode; skipping meeting deletion for {MeetingId}", meetingId);
            return;
        }

        var token = await GetAccessTokenAsync(settings, ct);
        var client = httpClientFactory.CreateClient("ZoomApi");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.DeleteAsync($"{settings.ApiBaseUrl}/meetings/{meetingId}", ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var safeErrorBody = SanitizeProviderErrorBody(errorBody);
            logger.LogWarning("Zoom meeting deletion failed for {MeetingId}: {Status} {Body}",
                meetingId, response.StatusCode, safeErrorBody);
            return;
        }

        logger.LogInformation("Deleted Zoom meeting {MeetingId}", meetingId);
    }

    public async Task CopyRecordingFileAsync(long meetingId, string? recordingFileId, Stream destination, CancellationToken ct)
    {
        var settings = await GetSettingsAsync(ct);
        if (!settings.Enabled)
        {
            throw new InvalidOperationException("Zoom recording download requires Zoom integration to be enabled.");
        }

        if (settings.AllowSandboxFallback && string.IsNullOrEmpty(settings.ClientId))
        {
            throw new InvalidOperationException("Zoom recording download requires Zoom credentials.");
        }

        var token = await GetAccessTokenAsync(settings, ct);
        var client = httpClientFactory.CreateClient("ZoomApi");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var metadataResponse = await client.GetAsync($"{settings.ApiBaseUrl}/meetings/{meetingId}/recordings", ct);
        if (!metadataResponse.IsSuccessStatusCode)
        {
            var errorBody = await metadataResponse.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Zoom recording metadata returned {(int)metadataResponse.StatusCode}: {SanitizeProviderErrorBody(errorBody)}");
        }

        var metadataJson = await metadataResponse.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(metadataJson);
        if (!document.RootElement.TryGetProperty("recording_files", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Zoom recording metadata did not include recording files.");
        }

        string? downloadUrl = null;
        foreach (var file in files.EnumerateArray())
        {
            var fileType = file.TryGetProperty("file_type", out var typeProperty) ? typeProperty.GetString() : null;
            if (!string.Equals(fileType, "MP4", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileId = file.TryGetProperty("id", out var idProperty) ? idProperty.ToString() : null;
            if (!string.IsNullOrWhiteSpace(recordingFileId) && !string.Equals(fileId, recordingFileId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            downloadUrl = file.TryGetProperty("download_url", out var urlProperty) ? urlProperty.GetString() : null;
            break;
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new InvalidOperationException("Zoom recording metadata did not include a downloadable MP4 file.");
        }

        using var downloadRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        downloadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var downloadResponse = await client.SendAsync(downloadRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!downloadResponse.IsSuccessStatusCode)
        {
            var errorBody = await downloadResponse.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Zoom recording download returned {(int)downloadResponse.StatusCode}: {SanitizeProviderErrorBody(errorBody)}");
        }

        await downloadResponse.Content.CopyToAsync(destination, ct);
    }

    public async Task<string?> GenerateMeetingSdkSignatureAsync(string meetingNumber, int role, DateTimeOffset expiresAt, CancellationToken ct)
    {
        var settings = await GetSettingsAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.MeetingSdkKey) || string.IsNullOrWhiteSpace(settings.MeetingSdkSecret))
        {
            return null;
        }

        var issuedAt = DateTimeOffset.UtcNow.AddSeconds(-30).ToUnixTimeSeconds();
        var expires = expiresAt.ToUnixTimeSeconds();
        var header = new { alg = "HS256", typ = "JWT" };
        var payload = new
        {
            sdkKey = settings.MeetingSdkKey,
            mn = meetingNumber,
            role,
            iat = issuedAt,
            exp = expires,
            appKey = settings.MeetingSdkKey,
            tokenExp = expires
        };

        var headerEncoded = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadEncoded = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = $"{headerEncoded}.{payloadEncoded}";
        var signatureBytes = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(settings.MeetingSdkSecret),
            Encoding.UTF8.GetBytes(signingInput));
        return $"{signingInput}.{Base64UrlEncode(signatureBytes)}";
    }

    /// <summary>
    /// Fetch the ZAK (Zoom Access Key) token for <paramref name="zoomUserId"/>
    /// so the host can join their own meeting as role=1 from the embedded
    /// Meeting SDK. Calls <c>GET /users/{userId}/token?type=zak</c> with the
    /// S2S bearer token and returns the <c>token</c> field. See Zoom docs:
    /// https://developers.zoom.us/docs/meeting-sdk/auth/#start-meetings-and-webinars-with-a-zoom-users-zak-token
    /// </summary>
    public async Task<string?> GetZakTokenAsync(string zoomUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(zoomUserId))
        {
            return null;
        }

        var settings = await GetSettingsAsync(ct);
        if (!settings.Enabled)
        {
            return null;
        }

        if (settings.AllowSandboxFallback && string.IsNullOrEmpty(settings.ClientId))
        {
            logger.LogInformation("Zoom sandbox mode; returning sandbox ZAK for {ZoomUserId}", zoomUserId);
            return $"sandbox-zak-{Guid.NewGuid():N}";
        }

        var token = await GetAccessTokenAsync(settings, ct);
        var client = httpClientFactory.CreateClient("ZoomApi");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var url = $"{settings.ApiBaseUrl}/users/{Uri.EscapeDataString(zoomUserId)}/token?type=zak";
        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning(
                "Zoom ZAK fetch failed for {ZoomUserId}: {Status} {Body}",
                zoomUserId,
                response.StatusCode,
                SanitizeProviderErrorBody(errorBody));
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(responseJson);
        if (!document.RootElement.TryGetProperty("token", out var tokenProperty)
            || tokenProperty.ValueKind != JsonValueKind.String)
        {
            logger.LogWarning("Zoom ZAK response missing token field for {ZoomUserId}", zoomUserId);
            return null;
        }

        return tokenProperty.GetString();
    }

    public async Task<bool> VerifyWebhookSignatureAsync(string rawBody, IHeaderDictionary headers, CancellationToken ct)
    {
        var settings = await GetSettingsAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.WebhookSecretToken))
        {
            return false;
        }

        if (!headers.TryGetValue("x-zm-request-timestamp", out var timestampValues)
            || !headers.TryGetValue("x-zm-signature", out var signatureValues))
        {
            return false;
        }

        var timestamp = timestampValues.ToString();
        var providedSignature = signatureValues.ToString();
        if (!long.TryParse(timestamp, out var unixTimestamp))
        {
            return false;
        }

        var requestTime = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
        var skew = DateTimeOffset.UtcNow - requestTime;
        if (skew.Duration() > TimeSpan.FromSeconds(settings.WebhookRetryToleranceSeconds))
        {
            return false;
        }

        var message = $"v0:{timestamp}:{rawBody}";
        var digest = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(settings.WebhookSecretToken),
            Encoding.UTF8.GetBytes(message));
        var expectedSignature = "v0=" + Convert.ToHexString(digest).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSignature),
            Encoding.UTF8.GetBytes(providedSignature));
    }

    public async Task<object?> TryBuildWebhookUrlValidationResponseAsync(string rawBody, CancellationToken ct)
    {
        var settings = await GetSettingsAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.WebhookSecretToken))
        {
            return null;
        }

        using var document = JsonDocument.Parse(rawBody);
        var root = document.RootElement;
        if (!root.TryGetProperty("event", out var eventProperty)
            || !string.Equals(eventProperty.GetString(), "endpoint.url_validation", StringComparison.OrdinalIgnoreCase)
            || !root.TryGetProperty("payload", out var payload)
            || !payload.TryGetProperty("plainToken", out var tokenProperty))
        {
            return null;
        }

        var plainToken = tokenProperty.GetString() ?? string.Empty;
        var digest = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(settings.WebhookSecretToken),
            Encoding.UTF8.GetBytes(plainToken));
        return new
        {
            plainToken,
            encryptedToken = Convert.ToHexString(digest).ToLowerInvariant()
        };
    }

    private async Task<string> GetAccessTokenAsync(ZoomSettings settings, CancellationToken ct)
    {
        var tokenSettingsHash = TokenSettingsHash(settings);
        if (_cachedToken is not null
            && _cachedTokenSettingsHash == tokenSettingsHash
            && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-2))
        {
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null
                && _cachedTokenSettingsHash == tokenSettingsHash
                && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-2))
            {
                return _cachedToken;
            }

            var client = httpClientFactory.CreateClient("ZoomAuth");
            var accountId = RequireOption(settings.AccountId, nameof(ZoomOptions.AccountId));
            var clientId = RequireOption(settings.ClientId, nameof(ZoomOptions.ClientId));
            var clientSecret = RequireOption(settings.ClientSecret, nameof(ZoomOptions.ClientSecret));

            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "account_credentials",
                ["account_id"] = accountId
            });

            var response = await client.PostAsync(settings.TokenUrl, form, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("Zoom token error {Status}: {Body}", response.StatusCode, SanitizeProviderErrorBody(errorBody));
                throw new InvalidOperationException($"Failed to obtain Zoom access token: {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var tokenResponse = JsonSerializer.Deserialize<ZoomTokenResponse>(json, JsonOpts)
                ?? throw new InvalidOperationException("Failed to deserialize Zoom token response");
            if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                throw new InvalidOperationException("Zoom token response did not include an access token");
            }

            _cachedToken = tokenResponse.AccessToken;
            _cachedTokenSettingsHash = tokenSettingsHash;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

            logger.LogInformation("Acquired Zoom access token, expires at {Expiry}", _tokenExpiresAt);
            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<ZoomSettings> GetSettingsAsync(CancellationToken ct)
        => (await runtimeSettings.GetAsync(ct)).Zoom;

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
        {
            throw new InvalidOperationException($"Zoom option {name} is required when sandbox fallback is disabled.");
        }

        return value;
    }

    private static string TokenSettingsHash(ZoomSettings settings)
    {
        var material = $"{settings.AccountId}\n{settings.ClientId}\n{settings.ClientSecret}\n{settings.TokenUrl}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] value)
        => Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    [GeneratedRegex("(?i)(\"(?:access_token|refresh_token|token|api_key|client_secret|secret|password)\"\\s*:\\s*)\"[^\"]*\"")]
    private static partial Regex SensitiveJsonValueRegex();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };
}

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