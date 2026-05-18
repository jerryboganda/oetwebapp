using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

public sealed class MobilePushDispatcher(HttpClient httpClient, IRuntimeSettingsProvider runtimeSettingsProvider)
    : IMobilePushDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    private async Task SendFcmAsync(MobilePushToken token, MobilePushPayload payload, PushSettings settings, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.FcmServerKey))
        {
            throw new MobilePushDispatchException(null, "FCM server key is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://fcm.googleapis.com/fcm/send");
        request.Headers.TryAddWithoutValidation("Authorization", $"key={settings.FcmServerKey}");
        request.Content = JsonContent(new
        {
            to = token.Token,
            notification = new { title = payload.Title, body = payload.Body },
            data = ToData(payload)
        });

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new MobilePushDispatchException((int)response.StatusCode, $"FCM push failed with HTTP {(int)response.StatusCode}.");
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
