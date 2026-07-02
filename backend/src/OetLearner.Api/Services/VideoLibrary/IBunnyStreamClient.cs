namespace OetLearner.Api.Services.VideoLibrary;

/// <summary>
/// Thrown by <see cref="IBunnyStreamClient"/> when Bunny Stream is not
/// configured (missing library id / API key / CDN host / token key or the
/// master toggle is off). Endpoints translate this to
/// <c>503 bunny_not_configured</c> so the Video Library stays cleanly dormant
/// until an admin supplies credentials.
/// </summary>
public sealed class BunnyNotConfiguredException()
    : InvalidOperationException("Bunny Stream is not configured.");

/// <summary>Metadata snapshot of a Bunny Stream video (subset we consume).</summary>
public sealed record BunnyVideoInfo(
    string VideoId,
    int Status,
    int EncodeProgress,
    int LengthSeconds,
    string? ThumbnailUrl,
    int? Width,
    int? Height,
    IReadOnlyList<string> AvailableResolutions);

/// <summary>Presigned TUS upload authorization for a direct browser upload.</summary>
public sealed record BunnyTusAuthorization(
    string BunnyVideoId,
    string LibraryId,
    string TusEndpoint,
    string AuthorizationSignature,
    long AuthorizationExpire);

/// <summary>
/// Bunny Stream API + CDN token signing. All methods read the effective
/// runtime settings per call (admin can rotate keys without restart) and
/// throw <see cref="BunnyNotConfiguredException"/> while dormant.
/// </summary>
public interface IBunnyStreamClient
{
    /// <summary>Create an empty Bunny video shell; returns its guid.</summary>
    Task<string> CreateVideoAsync(string title, string? collectionId, CancellationToken ct);

    /// <summary>Presign a direct-to-Bunny TUS upload for the given video.</summary>
    Task<BunnyTusAuthorization> CreateTusUploadAuthorizationAsync(string bunnyVideoId, long expiresUnix, CancellationToken ct);

    /// <summary>Fetch video status/duration/thumbnail/resolutions.</summary>
    Task<BunnyVideoInfo> GetVideoAsync(string bunnyVideoId, CancellationToken ct);

    Task DeleteVideoAsync(string bunnyVideoId, CancellationToken ct);

    /// <summary>Push a VTT caption track to Bunny (base64 body per Bunny API).</summary>
    Task UploadCaptionAsync(string bunnyVideoId, string languageCode, string label, byte[] vttBytes, CancellationToken ct);

    /// <summary>Set the video thumbnail from a source URL.</summary>
    Task SetThumbnailAsync(string bunnyVideoId, string thumbnailUrl, CancellationToken ct);

    /// <summary>
    /// Mint a token-signed HLS playback URL:
    /// https://{cdnHost}/{bunnyVideoId}/playlist.m3u8?token=...&amp;expires=...&amp;token_path=/{bunnyVideoId}/
    /// </summary>
    Task<string> SignPlaybackUrlAsync(string bunnyVideoId, long expiresUnix, CancellationToken ct);
}
