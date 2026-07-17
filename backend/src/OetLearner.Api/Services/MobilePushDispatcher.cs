using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Caching.Memory;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services;

public sealed class MobilePushDispatchException(int? statusCode, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public int? StatusCode { get; } = statusCode;
}

public sealed record MobilePushPayload(
    string NotificationId,
    string EventKey,
    string Title,
    string Body,
    string ActionUrl,
    string Severity,
    int UnreadCount);

public interface IMobilePushDispatcher
{
    Task SendAsync(MobilePushToken token, MobilePushPayload payload, CancellationToken cancellationToken = default);
}

public sealed class MobilePushDispatcher(HttpClient httpClient, IRuntimeSettingsProvider runtimeSettingsProvider, IMemoryCache memoryCache)
    : IMobilePushDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string FcmCredentialCacheKeyPrefix = "MobilePushDispatcher.FcmCredential:";

    public async Task SendAsync(MobilePushToken token, MobilePushPayload payload, CancellationToken cancellationToken = default)
    {
        var settings = (await runtimeSettingsProvider.GetAsync(cancellationToken)).Push;
        var platform = token.Platform.ToLowerInvariant();

        if (platform == "android")
        {
            await SendFcmAsync(token, payload, settings, cancellationToken);
            return;
        }

        if (platform == "ios")
        {
            await SendApnsAsync(token, payload, settings, cancellationToken);
            return;
        }

        throw new MobilePushDispatchException(null, $"Unsupported native push platform '{token.Platform}'.");
    }

    /// <summary>
    /// Sends via FCM's HTTP v1 API. Google retired the legacy server-key API
    /// (<c>fcm.googleapis.com/fcm/send</c>) in June 2024 — v1 is now the only way to
    /// send, and it authenticates with an OAuth2 bearer token minted from a Firebase
    /// service-account key rather than a static server key.
    /// </summary>
    private async Task SendFcmAsync(MobilePushToken token, MobilePushPayload payload, PushSettings settings, CancellationToken ct)
    {
        if (!settings.IsFcmConfigured)
        {
            throw new MobilePushDispatchException(null, "FCM service account / project id is not configured.");
        }

        var accessToken = await GetFcmAccessTokenAsync(settings, ct);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(settings.FcmProjectId!)}/messages:send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent(new
        {
            message = new
            {
                token = token.Token,
                notification = new { title = payload.Title, body = payload.Body },
                data = ToData(payload)
            }
        });

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new MobilePushDispatchException((int)response.StatusCode, $"FCM push failed with HTTP {(int)response.StatusCode}: {body}");
        }
    }

    /// <summary>
    /// Resolves a live OAuth2 access token for the configured Firebase service account.
    /// The <see cref="GoogleCredential"/> instance (not just the raw token string) is
    /// cached, keyed by a hash of the service-account JSON — <see cref="ITokenAccess.GetAccessTokenForRequestAsync"/>
    /// already caches and refreshes the underlying bearer token internally as it nears
    /// expiry, so reusing the same credential instance avoids re-deriving that logic here.
    /// Hashing the JSON (rather than a fixed key) means an admin rotating the service
    /// account in the runtime-settings UI is picked up on the next call, not after a
    /// process restart.
    /// </summary>
    private async Task<string> GetFcmAccessTokenAsync(PushSettings settings, CancellationToken ct)
    {
        var cacheKey = FcmCredentialCacheKeyPrefix + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(settings.FcmServiceAccountJson!)));

        if (!memoryCache.TryGetValue(cacheKey, out GoogleCredential? credential) || credential is null)
        {
            try
            {
                credential = GoogleCredential.FromJson(settings.FcmServiceAccountJson)
                    .CreateScoped("https://www.googleapis.com/auth/firebase.messaging");
            }
            catch (Exception ex)
            {
                throw new MobilePushDispatchException(null, "FCM service account JSON is malformed.", ex);
            }

            memoryCache.Set(cacheKey, credential, TimeSpan.FromHours(12));
        }

        // GoogleCredential itself doesn't expose ITokenAccess — the underlying wrapped
        // credential (ServiceAccountCredential, for a service-account JSON key) does.
        if (credential.UnderlyingCredential is not ITokenAccess tokenAccess)
        {
            throw new MobilePushDispatchException(null, "FCM service account JSON did not produce a token-capable credential.");
        }

        try
        {
            return await tokenAccess.GetAccessTokenForRequestAsync(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            throw new MobilePushDispatchException(null, "Failed to obtain an FCM OAuth2 access token from the service account.", ex);
        }
    }

    private async Task SendApnsAsync(MobilePushToken token, MobilePushPayload payload, PushSettings settings, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.ApnsKeyId)
            || string.IsNullOrWhiteSpace(settings.ApnsTeamId)
            || string.IsNullOrWhiteSpace(settings.ApnsBundleId)
            || string.IsNullOrWhiteSpace(settings.ApnsAuthKey))
        {
            throw new MobilePushDispatchException(null, "APNs key id, team id, bundle id, and auth key are required.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.push.apple.com/3/device/{Uri.EscapeDataString(token.Token)}");
        request.Headers.TryAddWithoutValidation("authorization", $"bearer {CreateApnsJwt(settings)}");
        request.Headers.TryAddWithoutValidation("apns-topic", settings.ApnsBundleId);
        request.Headers.TryAddWithoutValidation("apns-push-type", "alert");
        request.Headers.TryAddWithoutValidation("apns-priority", "10");
        request.Content = JsonContent(new
        {
            aps = new
            {
                alert = new { title = payload.Title, body = payload.Body },
                badge = payload.UnreadCount,
                sound = "default"
            },
            data = ToData(payload)
        });

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new MobilePushDispatchException((int)response.StatusCode, $"APNs push failed with HTTP {(int)response.StatusCode}.");
        }
    }

    private static Dictionary<string, string> ToData(MobilePushPayload payload) => new()
    {
        ["notificationId"] = payload.NotificationId,
        ["eventKey"] = payload.EventKey,
        ["actionUrl"] = payload.ActionUrl,
        ["severity"] = payload.Severity,
        ["unreadCount"] = payload.UnreadCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    private static string CreateApnsJwt(PushSettings settings)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(settings.ApnsAuthKey!.Replace("\\n", "\n"));
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", kid = settings.ApnsKeyId }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = settings.ApnsTeamId,
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        }));
        var signingInput = $"{header}.{payload}";
        var signature = ecdsa.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static StringContent JsonContent(object value)
        => new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
