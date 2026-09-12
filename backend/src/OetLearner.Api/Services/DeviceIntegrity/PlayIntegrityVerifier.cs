using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.DeviceIntegrity;

/// <summary>
/// Google Play Integrity (Android) — security standard MOB-09.
///
/// <para>
/// Calls the Play Integrity REST API
/// (<c>POST https://playintegrity.googleapis.com/v1/{packageName}:decodeIntegrityToken</c>
/// with body <c>{"integrity_token":"&lt;token&gt;"}</c>), authenticated with an
/// OAuth2 bearer token minted from a Google service account scoped to
/// <c>https://www.googleapis.com/auth/playintegrity</c>. The bearer-token
/// plumbing mirrors <c>MobilePushDispatcher</c>: the <see cref="GoogleCredential"/>
/// instance (not just the raw token) is cached in <see cref="IMemoryCache"/>,
/// keyed by the SHA-256 of the service-account JSON, so an admin rotating the key
/// is picked up on the next call rather than after a restart.
/// </para>
///
/// <para>
/// FAILURE POSTURE: every error path (missing config, network failure, non-2xx,
/// unparseable body, nonce/package mismatch) returns a
/// <see cref="DeviceIntegrityVerdict"/> with <c>Trusted = false</c> and verdict
/// "unknown" or "not_trusted". This verifier NEVER throws into the caller and
/// NEVER treats an inconclusive result as trusted.
/// </para>
/// </summary>
public sealed class PlayIntegrityVerifier(
    HttpClient httpClient,
    IOptions<DeviceAttestationOptions> options,
    IRuntimeSettingsProvider runtimeSettingsProvider,
    IMemoryCache memoryCache,
    ILogger<PlayIntegrityVerifier> logger) : IDeviceIntegrityVerifier
{
    private const string PlayIntegrityScope = "https://www.googleapis.com/auth/playintegrity";
    private const string DecodeEndpointRoot = "https://playintegrity.googleapis.com/v1/";
    private const string CredentialCacheKeyPrefix = "PlayIntegrityVerifier.Credential:";

    /// <summary>Google's verdict enum for "app built and signed by Play".</summary>
    public const string VerdictPlayRecognized = "PLAY_RECOGNIZED";

    /// <summary>Google's verdict enum for "genuine, uncorrupted device with
    /// enforced bootloader locking and no root".</summary>
    public const string VerdictMeetsDeviceIntegrity = "MEETS_DEVICE_INTEGRITY";

    private static readonly JsonSerializerOptions SummaryJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly DeviceAttestationOptions _options = options.Value;

    public string Provider => DeviceIntegrityProviders.PlayIntegrity;

    public bool IsConfigured => _options.AndroidConfigured;

    public bool Handles(string platform) => DeviceIntegrityPlatforms.IsAndroid(platform);

    public async Task<DeviceIntegrityVerdict> VerifyAsync(DeviceIntegrityRequest request, CancellationToken ct)
    {
        if (!_options.Enabled || !_options.AndroidEnabled)
        {
            return DeviceIntegrityVerdict.Unknown("play_integrity_disabled", Provider);
        }

        if (string.IsNullOrWhiteSpace(_options.GoogleCloudProjectNumber))
        {
            return DeviceIntegrityVerdict.Unknown("play_integrity_project_number_missing", Provider);
        }

        if (string.IsNullOrWhiteSpace(_options.AndroidPackageName))
        {
            return DeviceIntegrityVerdict.Unknown("play_integrity_package_name_missing", Provider);
        }

        if (string.IsNullOrWhiteSpace(request.IntegrityToken))
        {
            return DeviceIntegrityVerdict.Unknown("integrity_token_missing", Provider);
        }

        try
        {
            var accessToken = await GetAccessTokenAsync(ct);
            var url = $"{DecodeEndpointRoot}{Uri.EscapeDataString(_options.AndroidPackageName!)}:decodeIntegrityToken";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            // Google's documented REST body uses the snake_case proto field name.
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["integrity_token"] = request.IntegrityToken!,
                }),
                Encoding.UTF8,
                "application/json");

            using var response = await httpClient.SendAsync(httpRequest, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Play Integrity decode failed with HTTP {Status}.", (int)response.StatusCode);
                return DeviceIntegrityVerdict.Unknown($"play_integrity_http_{(int)response.StatusCode}", Provider);
            }

            return ParseVerdict(body, request.Nonce);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A provider failure is never a crash and never "trusted".
            logger.LogWarning(ex, "Play Integrity verification failed (network or credential error).");
            return DeviceIntegrityVerdict.Unknown("play_integrity_provider_error", Provider);
        }
    }

    /// <summary>
    /// Parses a <c>DecodeIntegrityTokenResponse</c>. Pure and unit-testable.
    /// A token is trusted only when the app is <c>PLAY_RECOGNIZED</c> AND the
    /// device verdict list contains <c>MEETS_DEVICE_INTEGRITY</c> AND the token is
    /// bound to the server-issued nonce (classic <c>requestDetails.nonce</c>, or
    /// standard <c>requestDetails.requestHash</c> = base64url(SHA-256(nonce))) AND
    /// the token's <c>requestPackageName</c> matches the configured package. Any
    /// other value — including <c>UNRECOGNIZED_VERSION</c>, <c>UNEVALUATED</c>, a
    /// missing device verdict, or a missing/mismatched nonce — is a failure, never
    /// a pass.
    /// </summary>
    internal DeviceIntegrityVerdict ParseVerdict(string json, string? expectedNonce)
    {
        string? packageName;
        string? timestampMillis;
        string? payloadNonce;
        string? payloadRequestHash;
        string? appRecognition;
        string? licensing;
        var deviceVerdicts = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("tokenPayloadExternal", out var payload)
                || payload.ValueKind != JsonValueKind.Object)
            {
                return DeviceIntegrityVerdict.Unknown("play_integrity_payload_missing", Provider);
            }

            if (payload.TryGetProperty("requestDetails", out var requestDetails))
            {
                packageName = ReadString(requestDetails, "requestPackageName");
                timestampMillis = ReadString(requestDetails, "timestampMillis");
                payloadNonce = ReadString(requestDetails, "nonce");
                payloadRequestHash = ReadString(requestDetails, "requestHash");
            }
            else
            {
                packageName = null;
                timestampMillis = null;
                payloadNonce = null;
                payloadRequestHash = null;
            }

            appRecognition = payload.TryGetProperty("appIntegrity", out var appIntegrity)
                ? ReadString(appIntegrity, "appRecognitionVerdict")
                : null;

            if (payload.TryGetProperty("deviceIntegrity", out var deviceIntegrity)
                && deviceIntegrity.TryGetProperty("deviceRecognitionVerdict", out var deviceArray)
                && deviceArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in deviceArray.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String
                        && item.GetString() is { Length: > 0 } value)
                    {
                        deviceVerdicts.Add(value);
                    }
                }
            }

            licensing = payload.TryGetProperty("accountDetails", out var accountDetails)
                ? ReadString(accountDetails, "appLicensingVerdict")
                : null;
        }
        catch (JsonException)
        {
            return DeviceIntegrityVerdict.Unknown("play_integrity_response_unparseable", Provider);
        }
        catch (InvalidOperationException)
        {
            return DeviceIntegrityVerdict.Unknown("play_integrity_response_unparseable", Provider);
        }

        var summary = BuildSummary(packageName, appRecognition, deviceVerdicts, licensing, timestampMillis);

        // Bind the token to our single-use nonce so a captured token cannot be
        // replayed against a fresh challenge. Two shapes are accepted:
        //   - classic API:  requestDetails.nonce == the server nonce;
        //   - standard API: requestDetails.requestHash == base64url(SHA-256(nonce)).
        // Anything else (mismatch, or neither field present) is not trusted.
        var nonceBound = !string.IsNullOrEmpty(expectedNonce)
            && (string.Equals(payloadNonce, expectedNonce, StringComparison.Ordinal)
                || string.Equals(payloadRequestHash, ComputeRequestHash(expectedNonce), StringComparison.Ordinal));

        if (!nonceBound)
        {
            var bindingFailure = string.IsNullOrEmpty(payloadNonce) && string.IsNullOrEmpty(payloadRequestHash)
                ? "nonce_absent"
                : "nonce_mismatch";
            return new DeviceIntegrityVerdict(false, Provider, "not_trusted", bindingFailure, summary);
        }

        if (!string.IsNullOrEmpty(_options.AndroidPackageName)
            && !string.Equals(packageName, _options.AndroidPackageName, StringComparison.Ordinal))
        {
            return new DeviceIntegrityVerdict(false, Provider, "not_trusted", "package_mismatch", summary);
        }

        var meetsDeviceIntegrity = deviceVerdicts.Contains(VerdictMeetsDeviceIntegrity, StringComparer.Ordinal);
        var appRecognized = string.Equals(appRecognition, VerdictPlayRecognized, StringComparison.Ordinal);
        var trusted = appRecognized && meetsDeviceIntegrity;

        var reason = trusted
            ? "play_recognized_and_device_integrity"
            : BuildFailureReason(appRecognition, deviceVerdicts);

        return new DeviceIntegrityVerdict(
            trusted,
            Provider,
            trusted ? "trusted" : "not_trusted",
            reason,
            summary);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var json = _options.GoogleServiceAccountJson;
        if (string.IsNullOrWhiteSpace(json))
        {
            // Fall back to the existing FCM service-account plumbing rather than
            // requiring a second key from an operator who already configured push.
            var push = (await runtimeSettingsProvider.GetAsync(ct)).Push;
            json = push.FcmServiceAccountJson;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                "No Play Integrity service account JSON configured "
                + "(DeviceAttestation:GoogleServiceAccountJson or Push:FcmServiceAccountJson).");
        }

        var cacheKey = CredentialCacheKeyPrefix
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

        if (!memoryCache.TryGetValue(cacheKey, out GoogleCredential? credential) || credential is null)
        {
            // Deliberately identical to MobilePushDispatcher.GetFcmAccessTokenAsync:
            // GoogleCredential.FromJson is flagged obsolete (a future migration to
            // CredentialFactory is intended) but the FCM path that already ships and
            // works uses it, so the two credential paths stay consistent until the
            // repo migrates as one change.
#pragma warning disable CS0618
            credential = GoogleCredential.FromJson(json).CreateScoped(PlayIntegrityScope);
#pragma warning restore CS0618
            memoryCache.Set(cacheKey, credential, TimeSpan.FromHours(12));
        }

        if (credential.UnderlyingCredential is not ITokenAccess tokenAccess)
        {
            throw new InvalidOperationException(
                "Play Integrity service account JSON did not produce a token-capable credential.");
        }

        return await tokenAccess.GetAccessTokenForRequestAsync(cancellationToken: ct);
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>base64url(SHA-256(nonce)) with no padding — the value Google's
    /// Standard Integrity API expects for <c>requestHash</c> when the client binds
    /// the token to our server nonce.</summary>
    private static string ComputeRequestHash(string nonce) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(nonce)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <summary>Names every specific reason the token is not trusted (so an
    /// operator can tell a rooted device apart from an unrecognised build).</summary>
    private static string BuildFailureReason(string? appRecognition, List<string> deviceVerdicts)
    {
        var reason = $"app={appRecognition ?? "unknown"};device=[{string.Join(",", deviceVerdicts)}]";
        if (!string.Equals(appRecognition, VerdictPlayRecognized, StringComparison.Ordinal))
        {
            reason += ";app_not_play_recognized";
        }

        if (!deviceVerdicts.Contains(VerdictMeetsDeviceIntegrity, StringComparer.Ordinal))
        {
            reason += ";device_integrity_not_met";
        }

        return reason;
    }

    /// <summary>
    /// Sanitised summary — VERDICT ENUMS ONLY. The raw integrity token, the token
    /// nonce, and the certificate SHA-256 digests are deliberately excluded: they
    /// are replayable material / fingerprinting data and must never be persisted.
    /// </summary>
    private static string BuildSummary(
        string? packageName,
        string? appRecognition,
        List<string> deviceVerdicts,
        string? licensing,
        string? timestampMillis) =>
        JsonSerializer.Serialize(new
        {
            requestPackageName = packageName,
            appRecognitionVerdict = appRecognition,
            deviceRecognitionVerdict = deviceVerdicts,
            appLicensingVerdict = licensing,
            timestampMillis,
        }, SummaryJsonOptions);
}
