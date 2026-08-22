using System.Net.Http.Json;
using System.Text.Json;

namespace OetLearner.Api.Services.Otp;

internal sealed class FirebaseSmsOtpClient(HttpClient http) : IFirebaseSmsOtpClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<FirebaseSmsSendResult> SendVerificationCodeAsync(
        string phoneNumber,
        string recaptchaToken,
        string webApiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webApiKey))
        {
            return new FirebaseSmsSendResult(false, null, "unconfigured");
        }

        try
        {
            using var response = await http.PostAsJsonAsync(
                $"v1/accounts:sendVerificationCode?key={Uri.EscapeDataString(webApiKey)}",
                new { phoneNumber, recaptchaToken },
                JsonOptions,
                cancellationToken);

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            if (response.IsSuccessStatusCode
                && doc.RootElement.TryGetProperty("sessionInfo", out var session)
                && session.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(session.GetString()))
            {
                return new FirebaseSmsSendResult(true, session.GetString(), null);
            }

            return new FirebaseSmsSendResult(false, null, ReadErrorMessage(doc.RootElement));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new FirebaseSmsSendResult(false, null, "timeout", TransportError: true);
        }
        catch (HttpRequestException)
        {
            return new FirebaseSmsSendResult(false, null, "transport", TransportError: true);
        }
        catch (JsonException)
        {
            return new FirebaseSmsSendResult(false, null, "invalid_response", TransportError: true);
        }
    }

    public async Task<FirebaseSmsVerifyResult> VerifyCodeAsync(
        string sessionInfo,
        string code,
        string webApiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webApiKey) || string.IsNullOrWhiteSpace(sessionInfo))
        {
            return new FirebaseSmsVerifyResult(false, null, "unconfigured");
        }

        try
        {
            using var response = await http.PostAsJsonAsync(
                $"v1/accounts:signInWithPhoneNumber?key={Uri.EscapeDataString(webApiKey)}",
                new { sessionInfo, code },
                JsonOptions,
                cancellationToken);

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            if (response.IsSuccessStatusCode
                && doc.RootElement.TryGetProperty("phoneNumber", out var phone)
                && phone.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(phone.GetString()))
            {
                return new FirebaseSmsVerifyResult(true, phone.GetString(), null);
            }

            return new FirebaseSmsVerifyResult(false, null, ReadErrorMessage(doc.RootElement));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new FirebaseSmsVerifyResult(false, null, "timeout", TransportError: true);
        }
        catch (HttpRequestException)
        {
            return new FirebaseSmsVerifyResult(false, null, "transport", TransportError: true);
        }
        catch (JsonException)
        {
            return new FirebaseSmsVerifyResult(false, null, "invalid_response", TransportError: true);
        }
    }

    private static string ReadErrorMessage(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
        {
            return message.GetString() ?? "firebase_error";
        }

        return "firebase_error";
    }
}
