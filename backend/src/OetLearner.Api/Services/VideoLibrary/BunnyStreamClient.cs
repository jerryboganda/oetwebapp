using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.VideoLibrary;

/// <summary>
/// Default <see cref="IBunnyStreamClient"/>. Scoped; resolves the effective
/// Bunny settings per call via <see cref="IRuntimeSettingsProvider"/> so key
/// rotation from the admin panel takes effect without a restart. HTTP goes
/// through the named "BunnyStream" client.
/// </summary>
public sealed class BunnyStreamClient(
    IHttpClientFactory httpClientFactory,
    IRuntimeSettingsProvider settingsProvider,
    ILogger<BunnyStreamClient> logger) : IBunnyStreamClient
{
    public const string HttpClientName = "BunnyStream";
    public const string TusEndpoint = "https://video.bunnycdn.com/tusupload";
    private const string ApiBase = "https://video.bunnycdn.com";

    public async Task<string> CreateVideoAsync(string title, string? collectionId, CancellationToken ct)
    {
        var s = await RequireConfiguredAsync(ct);
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"{ApiBase}/library/{Uri.EscapeDataString(s.LibraryId!)}/videos");
        req.Headers.TryAddWithoutValidation("AccessKey", s.ApiKey);
        req.Content = JsonContent.Create(collectionId is null
            ? new { title }
            : (object)new { title, collectionId });

        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("Bunny CreateVideo failed with HTTP {Status}: {Body}", (int)resp.StatusCode, Truncate(body));
            throw ApiException.ServiceUnavailable("bunny_create_failed",
                $"Bunny Stream rejected the video creation (HTTP {(int)resp.StatusCode}).");
        }

        using var doc = JsonDocument.Parse(body);
        var guid = ReadString(doc.RootElement, "guid");
        if (string.IsNullOrWhiteSpace(guid))
        {
            throw ApiException.ServiceUnavailable("bunny_create_failed", "Bunny Stream did not return a video guid.");
        }
        return guid;
    }

    public async Task<BunnyTusAuthorization> CreateTusUploadAuthorizationAsync(
        string bunnyVideoId, long expiresUnix, CancellationToken ct)
    {
        var s = await RequireConfiguredAsync(ct);
        var signature = ComputeTusSignature(s.LibraryId!, s.ApiKey!, expiresUnix, bunnyVideoId);
        return new BunnyTusAuthorization(
            BunnyVideoId: bunnyVideoId,
            LibraryId: s.LibraryId!,
            TusEndpoint: TusEndpoint,
            AuthorizationSignature: signature,
            AuthorizationExpire: expiresUnix);
    }

    public async Task<BunnyVideoInfo> GetVideoAsync(string bunnyVideoId, CancellationToken ct)
    {
        var s = await RequireConfiguredAsync(ct);
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            $"{ApiBase}/library/{Uri.EscapeDataString(s.LibraryId!)}/videos/{Uri.EscapeDataString(bunnyVideoId)}");
        req.Headers.TryAddWithoutValidation("AccessKey", s.ApiKey);

        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Bunny GetVideo {VideoId} failed with HTTP {Status}: {Body}",
                bunnyVideoId, (int)resp.StatusCode, Truncate(body));
            throw ApiException.ServiceUnavailable("bunny_fetch_failed",
                $"Bunny Stream video lookup failed (HTTP {(int)resp.StatusCode}).");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var thumbnailFileName = ReadString(root, "thumbnailFileName");
        var resolutionsCsv = ReadString(root, "availableResolutions");
        return new BunnyVideoInfo(
            VideoId: bunnyVideoId,
            Status: ReadInt(root, "status") ?? 0,
            EncodeProgress: ReadInt(root, "encodeProgress") ?? 0,
            LengthSeconds: ReadInt(root, "length") ?? 0,
            ThumbnailUrl: string.IsNullOrWhiteSpace(thumbnailFileName)
                ? null
                : $"https://{s.CdnHostname}/{bunnyVideoId}/{thumbnailFileName}",
            Width: ReadInt(root, "width"),
            Height: ReadInt(root, "height"),
            AvailableResolutions: string.IsNullOrWhiteSpace(resolutionsCsv)
                ? Array.Empty<string>()
                : resolutionsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public async Task DeleteVideoAsync(string bunnyVideoId, CancellationToken ct)
    {
        var s = await RequireConfiguredAsync(ct);
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{ApiBase}/library/{Uri.EscapeDataString(s.LibraryId!)}/videos/{Uri.EscapeDataString(bunnyVideoId)}");
        req.Headers.TryAddWithoutValidation("AccessKey", s.ApiKey);
        using var resp = await client.SendAsync(req, ct);
        // 404 = already gone at Bunny — treat as success (idempotent delete).
        if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            logger.LogError("Bunny DeleteVideo {VideoId} failed with HTTP {Status}: {Body}",
                bunnyVideoId, (int)resp.StatusCode, Truncate(body));
            throw ApiException.ServiceUnavailable("bunny_delete_failed",
                $"Bunny Stream video deletion failed (HTTP {(int)resp.StatusCode}).");
        }
    }

    public async Task UploadCaptionAsync(
        string bunnyVideoId, string languageCode, string label, byte[] vttBytes, CancellationToken ct)
    {
        var s = await RequireConfiguredAsync(ct);
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ApiBase}/library/{Uri.EscapeDataString(s.LibraryId!)}/videos/{Uri.EscapeDataString(bunnyVideoId)}/captions/{Uri.EscapeDataString(languageCode)}");
        req.Headers.TryAddWithoutValidation("AccessKey", s.ApiKey);
        req.Content = JsonContent.Create(new
        {
            srclang = languageCode,
            label,
            captionsFile = Convert.ToBase64String(vttBytes),
        });
        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            logger.LogError("Bunny UploadCaption {VideoId}/{Lang} failed with HTTP {Status}: {Body}",
                bunnyVideoId, languageCode, (int)resp.StatusCode, Truncate(body));
            throw ApiException.ServiceUnavailable("bunny_caption_failed",
                $"Bunny Stream caption upload failed (HTTP {(int)resp.StatusCode}).");
        }
    }

    public async Task SetThumbnailAsync(string bunnyVideoId, string thumbnailUrl, CancellationToken ct)
    {
        var s = await RequireConfiguredAsync(ct);
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ApiBase}/library/{Uri.EscapeDataString(s.LibraryId!)}/videos/{Uri.EscapeDataString(bunnyVideoId)}/thumbnail?thumbnailUrl={Uri.EscapeDataString(thumbnailUrl)}");
        req.Headers.TryAddWithoutValidation("AccessKey", s.ApiKey);
        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Bunny SetThumbnail {VideoId} failed with HTTP {Status}: {Body}",
                bunnyVideoId, (int)resp.StatusCode, Truncate(body));
            throw ApiException.ServiceUnavailable("bunny_thumbnail_failed",
                $"Bunny Stream thumbnail update failed (HTTP {(int)resp.StatusCode}).");
        }
    }

    public async Task<string> SignPlaybackUrlAsync(string bunnyVideoId, long expiresUnix, CancellationToken ct)
    {
        var s = await RequireConfiguredAsync(ct);
        var tokenPath = $"/{bunnyVideoId}/";
        var token = ComputeCdnToken(s.TokenAuthKey!, tokenPath, expiresUnix);
        return BuildSignedPlaybackUrl(s.CdnHostname!, bunnyVideoId, token, expiresUnix, tokenPath);
    }

    // ── Pure signature helpers (unit-test pinned) ─────────────────────────

    /// <summary>
    /// Bunny TUS presigned upload signature: lowercase-hex SHA-256 of the
    /// concatenation <c>libraryId + apiKey + expiresUnix + videoId</c>.
    /// // VERIFY against the Bunny TUS spec (https://docs.bunny.net/reference/tus-resumable-uploads)
    /// before first production upload — the concatenation order is taken from
    /// the published spec and pinned by BunnyStreamClientTests.
    /// </summary>
    public static string ComputeTusSignature(string libraryId, string apiKey, long expiresUnix, string bunnyVideoId)
    {
        var payload = $"{libraryId}{apiKey}{expiresUnix}{bunnyVideoId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Bunny CDN directory token-authentication token: Base64Url(no padding) of
    /// the RAW SHA-256 of
    /// <c>tokenAuthKey + tokenPath + expiresUnix + "token_path=" + tokenPath</c>.
    /// <para>
    /// The trailing <c>token_path=&lt;path&gt;</c> is Bunny's "parameter data":
    /// when directory auth is used, <c>token_path</c> is added to the signed
    /// query parameters, so it appears BOTH as the signature path prefix AND in
    /// the appended parameter data (raw, un-encoded). Omitting the suffix makes
    /// Bunny reject the token with HTTP 403.
    /// </para>
    /// <para>
    /// VERIFIED end-to-end against the live Bunny Stream CDN (2026-07-03): with
    /// this composition the master playlist AND its child sub-playlists/segments
    /// under <c>/{videoId}/</c> authorize with a single token; the previous
    /// suffix-less form returned 403. Pinned by BunnyStreamClientTests.
    /// </para>
    /// <para>
    /// The Bunny library must also have <c>AllowDirectPlay = true</c> and not
    /// block referrer-less requests, or the CDN blocks direct HLS access (403)
    /// before the token is ever evaluated — see docs/VIDEO-LIBRARY-BUNNY-SETUP.md.
    /// </para>
    /// </summary>
    public static string ComputeCdnToken(string tokenAuthKey, string tokenPath, long expiresUnix)
    {
        var payload = $"{tokenAuthKey}{tokenPath}{expiresUnix}token_path={tokenPath}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }

    /// <summary>Compose the signed HLS playlist URL from its parts.</summary>
    public static string BuildSignedPlaybackUrl(
        string cdnHostname, string bunnyVideoId, string token, long expiresUnix, string tokenPath)
        => $"https://{cdnHostname}/{bunnyVideoId}/playlist.m3u8"
           + $"?token={token}&expires={expiresUnix}&token_path={Uri.EscapeDataString(tokenPath)}";

    // ── Internals ──────────────────────────────────────────────────────────

    private async Task<BunnyStreamSettings> RequireConfiguredAsync(CancellationToken ct)
    {
        var settings = (await settingsProvider.GetAsync(ct)).BunnyStream;
        if (!settings.IsConfigured)
        {
            throw new BunnyNotConfiguredException();
        }
        return settings;
    }

    private static string? ReadString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int? ReadInt(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        // Bunny reports length as a float for some containers.
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return (int)Math.Round(d);
        return null;
    }

    private static string Truncate(string value)
        => value.Length > 300 ? value[..300] : value;
}
