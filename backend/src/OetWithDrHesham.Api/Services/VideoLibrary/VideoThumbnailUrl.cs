using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Settings;

namespace OetWithDrHesham.Api.Services.VideoLibrary;

/// <summary>
/// Resolves the thumbnail URL shown on learner + admin video cards.
///
/// Two sources:
///  - a custom uploaded thumbnail → the app's own <c>/v1/media/{id}/content</c>;
///  - Bunny Stream's auto-generated thumbnail → a <c>vz-*.b-cdn.net</c> URL.
///
/// The Bunny pull zone has <b>Token Authentication ON</b> (mandatory — see
/// docs/VIDEO-LIBRARY-BUNNY-SETUP.md §2), so a plain <c>&lt;img&gt;</c> request
/// to an <i>unsigned</i> thumbnail URL is rejected with <c>403</c> and the card
/// renders blank. We therefore append a Bunny CDN token here at read time.
///
/// The token is <b>file-scoped</b> to the exact <c>thumbnail.jpg</c> path — NOT
/// the <c>/{videoId}/</c> directory that <see cref="BunnyStreamClient.SignPlaybackUrlAsync"/>
/// signs — so a token lifted from the (public) card image cannot be replayed to
/// fetch <c>playlist.m3u8</c> and bypass the native-app-only playback attestation.
/// </summary>
public static class VideoThumbnailUrl
{
    /// <summary>
    /// Thumbnails are low-sensitivity still frames, so we sign them with a long
    /// TTL to survive a browse tab left open — the file-scoped token can only
    /// fetch the one JPEG regardless of lifetime.
    /// </summary>
    private static readonly TimeSpan TokenTtl = TimeSpan.FromDays(7);

    /// <summary>Resolve the display URL for a video's thumbnail (custom asset or signed Bunny URL).</summary>
    public static string? Resolve(LibraryVideo video, BunnyStreamSettings bunny)
        => !string.IsNullOrWhiteSpace(video.CustomThumbnailMediaAssetId)
            ? $"/v1/media/{video.CustomThumbnailMediaAssetId}/content"
            : SignBunny(video.BunnyThumbnailUrl, bunny);

    /// <summary>
    /// Append a file-scoped Bunny CDN token to a raw <c>vz-*.b-cdn.net</c>
    /// thumbnail URL. Returns the input unchanged when it's empty or when token
    /// auth is not configured (feature dormant / pull zone open), so the same
    /// code path is correct whether or not token auth is enabled.
    /// </summary>
    public static string? SignBunny(string? rawThumbnailUrl, BunnyStreamSettings bunny)
    {
        if (string.IsNullOrWhiteSpace(rawThumbnailUrl)) return rawThumbnailUrl;
        if (string.IsNullOrWhiteSpace(bunny.TokenAuthKey)) return rawThumbnailUrl;
        var expires = DateTimeOffset.UtcNow.Add(TokenTtl).ToUnixTimeSeconds();
        return BunnyStreamClient.SignThumbnailUrl(rawThumbnailUrl!, expires, bunny.TokenAuthKey!);
    }
}
