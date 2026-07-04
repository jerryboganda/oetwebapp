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
            StorageSizeBytes: ReadLong(root, "storageSize") ?? 0,
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

    // ── Collections (live Bunny library management) ──────────────────────────

    public async Task<BunnyCollectionListPage> ListCollectionsAsync(
        int page, int itemsPerPage, string? search, string? orderBy, CancellationToken ct)
    {
        var s = await RequireConfiguredAsync(ct);
        var client = httpClientFactory.CreateClient(HttpClientName);
        var url = $"{ApiBase}/library/{Uri.EscapeDataString(s.LibraryId!)}/collections?page={page}&itemsPerPage={itemsPerPage}";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(orderBy)) url += $"&orderBy={Uri.EscapeDataString(orderBy)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("AccessKey", s.ApiKey);

        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("Bunny ListCollections failed with HTTP {Status}: {Body}", (int)resp.StatusCode, Truncate(body));
            throw ApiException.ServiceUnavailable("bunny_collection_list_failed",
                $"Bunny Stream collection listing failed (HTTP {(int)resp.StatusCode}).");
        }

        // VERIFY field casing against a live Bunny /collections response before first prod use.
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var items = new List<BunnyCollectionInfo>();
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray()) items.Add(ParseCollection(el));
        }
        return new BunnyCollectionListPage(
            TotalItems: ReadInt(root, "totalItems") ?? items.Count,
            CurrentPage: ReadInt(root, "currentPage") ?? page,
            ItemsPerPage: ReadInt(root, "itemsPerPage") ?? itemsPerPage,
            Items: items);
    }

    public async Task<BunnyCollectionInfo> GetCollectionAsync(string collectionId, CancellationToken ct)
    {
        var s = await RequireConfiguredAsync(ct);
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            $"{ApiBase}/library/{Uri.EscapeDataString(s.LibraryId!)}/collections/{Uri.EscapeDataString(collectionId)}");
        req.Headers.TryAddWithoutValidation("AccessKey", s.ApiKey);

        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Bunny GetCollection {CollectionId} failed with HTTP {Status}: {Body}",
                collectionId, (int)resp.StatusCode, Truncate(body));
            throw ApiException.ServiceUnavailable("bunny_collection_fetch_failed",
                $"Bunny Stream collection lookup failed (HTTP {(int)resp.StatusCode}).");
        }

        // VERIFY field casing against a live Bunny /collections/{id} response before first prod use.
        using var doc = JsonDocument.Parse(body);
        return ParseCollection(doc.RootElement);
    }

    public async Task<BunnyCollectionInfo> CreateCollectionAsync(string name, CancellationToken ct)
    {
        var s = await RequireConfiguredAsync(ct);
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"{ApiBase}/library/{Uri.EscapeDataString(s.LibraryId!)}/collections");
        req.Headers.TryAddWithoutValidation("AccessKey", s.ApiKey);
        req.Content = JsonContent.Create(new { name });

        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("Bunny CreateCollection failed with HTTP {Status}: {Body}", (int)resp.StatusCode, Truncate(body));
            throw ApiException.ServiceUnavailable("bunny_collection_create_failed",
                $"Bunny Stream collection creation failed (HTTP {(int)resp.StatusCode}).");
        }

        // VERIFY field casing against a live Bunny create-collection response before first prod use.
        using var doc = JsonDocument.Parse(body);
        var created = ParseCollection(doc.RootElement);
        if (string.IsNullOrWhiteSpace(created.Guid))
        {
            throw ApiException.ServiceUnavailable("bunny_collection_create_failed",
                "Bunny Stream did not return a collection guid.");
        }
        // A freshly created collection is empty; use the requested name if Bunny echoes none.
        return created with { Name = string.IsNullOrWhiteSpace(created.Name) ? name : created.Name };
    }

    public async Task<BunnyCollectionInfo> UpdateCollectionAsync(string collectionId, string name, CancellationToken ct)
    {
        var s = await RequireConfiguredAsync(ct);
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ApiBase}/library/{Uri.EscapeDataString(s.LibraryId!)}/collections/{Uri.EscapeDataString(collectionId)}");
        req.Headers.TryAddWithoutValidation("AccessKey", s.ApiKey);
        req.Content = JsonContent.Create(new { name });

        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            logger.LogError("Bunny UpdateCollection {CollectionId} failed with HTTP {Status}: {Body}",
                collectionId, (int)resp.StatusCode, Truncate(body));
            throw ApiException.ServiceUnavailable("bunny_collection_update_failed",
                $"Bunny Stream collection update failed (HTTP {(int)resp.StatusCode}).");
        }
        // Bunny's update endpoint returns 200 with no reliable body — re-fetch for accurate counts.
        return await GetCollectionAsync(collectionId, ct);
    }

    public async Task DeleteCollectionAsync(string collectionId, CancellationToken ct)
    {
        var s = await RequireConfiguredAsync(ct);
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{ApiBase}/library/{Uri.EscapeDataString(s.LibraryId!)}/collections/{Uri.EscapeDataString(collectionId)}");
        req.Headers.TryAddWithoutValidation("AccessKey", s.ApiKey);
        using var resp = await client.SendAsync(req, ct);
        // 404 = already gone at Bunny — treat as success (idempotent delete).
        if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            logger.LogError("Bunny DeleteCollection {CollectionId} failed with HTTP {Status}: {Body}",
                collectionId, (int)resp.StatusCode, Truncate(body));
            throw ApiException.ServiceUnavailable("bunny_collection_delete_failed",
                $"Bunny Stream collection deletion failed (HTTP {(int)resp.StatusCode}).");
        }
    }

    public async Task<BunnyVideoListPage> ListCollectionVideosAsync(
        string collectionId, int page, int itemsPerPage, string? search, string? orderBy, CancellationToken ct)
    {
        var s = await RequireConfiguredAsync(ct);
        var client = httpClientFactory.CreateClient(HttpClientName);
        var url = $"{ApiBase}/library/{Uri.EscapeDataString(s.LibraryId!)}/videos"
                  + $"?collection={Uri.EscapeDataString(collectionId)}&page={page}&itemsPerPage={itemsPerPage}";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(orderBy)) url += $"&orderBy={Uri.EscapeDataString(orderBy)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("AccessKey", s.ApiKey);

        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("Bunny ListCollectionVideos {CollectionId} failed with HTTP {Status}: {Body}",
                collectionId, (int)resp.StatusCode, Truncate(body));
            throw ApiException.ServiceUnavailable("bunny_collection_videos_failed",
                $"Bunny Stream video listing failed (HTTP {(int)resp.StatusCode}).");
        }

        // VERIFY field casing against a live Bunny /videos?collection= response before first prod use.
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var items = new List<BunnyVideoListItem>();
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray()) items.Add(ParseVideoListItem(el, s.CdnHostname));
        }
        return new BunnyVideoListPage(
            TotalItems: ReadInt(root, "totalItems") ?? items.Count,
            CurrentPage: ReadInt(root, "currentPage") ?? page,
            ItemsPerPage: ReadInt(root, "itemsPerPage") ?? itemsPerPage,
            Items: items);
    }

    public async Task MoveVideoToCollectionAsync(string bunnyVideoId, string? collectionId, CancellationToken ct)
    {
        var s = await RequireConfiguredAsync(ct);
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ApiBase}/library/{Uri.EscapeDataString(s.LibraryId!)}/videos/{Uri.EscapeDataString(bunnyVideoId)}");
        req.Headers.TryAddWithoutValidation("AccessKey", s.ApiKey);
        // Empty string clears collection membership (moves to "no collection").
        req.Content = JsonContent.Create(new { collectionId = collectionId ?? string.Empty });

        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            logger.LogError("Bunny MoveVideo {VideoId} -> {CollectionId} failed with HTTP {Status}: {Body}",
                bunnyVideoId, collectionId, (int)resp.StatusCode, Truncate(body));
            throw ApiException.ServiceUnavailable("bunny_video_move_failed",
                $"Bunny Stream video move failed (HTTP {(int)resp.StatusCode}).");
        }
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

    private static BunnyCollectionInfo ParseCollection(JsonElement el)
    {
        var previewCsv = ReadString(el, "previewVideoIds");
        return new BunnyCollectionInfo(
            Guid: ReadString(el, "guid") ?? string.Empty,
            Name: ReadString(el, "name") ?? string.Empty,
            VideoCount: ReadInt(el, "videoCount") ?? 0,
            TotalSize: ReadLong(el, "totalSize") ?? 0,
            PreviewVideoIds: string.IsNullOrWhiteSpace(previewCsv)
                ? Array.Empty<string>()
                : previewCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static BunnyVideoListItem ParseVideoListItem(JsonElement el, string? cdnHostname)
    {
        var guid = ReadString(el, "guid") ?? string.Empty;
        var thumbnailFileName = ReadString(el, "thumbnailFileName");
        var resolutionsCsv = ReadString(el, "availableResolutions");
        var collectionId = ReadString(el, "collectionId");
        return new BunnyVideoListItem(
            VideoId: guid,
            Title: ReadString(el, "title") ?? string.Empty,
            CollectionId: string.IsNullOrWhiteSpace(collectionId) ? null : collectionId,
            Status: ReadInt(el, "status") ?? 0,
            EncodeProgress: ReadInt(el, "encodeProgress") ?? 0,
            LengthSeconds: ReadInt(el, "length") ?? 0,
            StorageSizeBytes: ReadLong(el, "storageSize") ?? 0,
            ThumbnailUrl: string.IsNullOrWhiteSpace(thumbnailFileName) || string.IsNullOrWhiteSpace(cdnHostname)
                ? null
                : $"https://{cdnHostname}/{guid}/{thumbnailFileName}",
            Width: ReadInt(el, "width"),
            Height: ReadInt(el, "height"),
            AvailableResolutions: string.IsNullOrWhiteSpace(resolutionsCsv)
                ? Array.Empty<string>()
                : resolutionsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
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

    private static long? ReadLong(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var i)) return i;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return (long)Math.Round(d);
        return null;
    }

    private static string Truncate(string value)
        => value.Length > 300 ? value[..300] : value;
}
