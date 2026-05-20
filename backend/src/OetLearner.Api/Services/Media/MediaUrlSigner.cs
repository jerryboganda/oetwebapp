using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Media;

/// <summary>
/// Mints and verifies short-lived signed URLs for <c>/v1/media/{id}/content</c>
/// so HTML5 <c>&lt;audio&gt;</c>/<c>&lt;video&gt;</c>/<c>&lt;img&gt;</c> elements
/// (which cannot send <c>Authorization: Bearer …</c> headers) can fetch
/// protected media. The token is bound to the media id + an absolute UTC
/// expiry; HMAC-SHA256 with the existing JWT signing key prevents forgery.
/// Callers (e.g. listening/reading session builders) only mint a signed URL
/// after they have already enforced learner-scope access, so a valid
/// signature is treated as the access proof on the download path.
/// </summary>
public sealed class MediaUrlSigner
{
    // Default TTL: 6 hours — long enough for a full listening/reading sitting
    // (incl. paused tabs) without keeping the surface broadly accessible.
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(6);

    public const string ExpiryParam = "exp";
    public const string SignatureParam = "sig";

    private readonly byte[] _key;
    private readonly TimeProvider _clock;

    public MediaUrlSigner(IOptions<AuthOptions> auth, TimeProvider clock)
    {
        var signingKey = auth?.Value?.SigningKey;
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException(
                "Auth:SigningKey is required to mint signed media URLs.");
        }

        _key = Encoding.UTF8.GetBytes(signingKey);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Returns a relative content URL with appended signed query parameters,
    /// e.g. <c>/v1/media/{id}/content?exp=1747000000&amp;sig=…</c>.
    /// </summary>
    public string SignDownloadPath(string mediaAssetId, TimeSpan? ttl = null)
    {
        if (string.IsNullOrWhiteSpace(mediaAssetId))
            throw new ArgumentException("Media asset id is required.", nameof(mediaAssetId));

        var effectiveTtl = ttl ?? DefaultTtl;
        if (effectiveTtl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be positive.");

        var expUnix = _clock.GetUtcNow().Add(effectiveTtl).ToUnixTimeSeconds();
        var sig = ComputeSignature(mediaAssetId, expUnix);

        return $"/v1/media/{Uri.EscapeDataString(mediaAssetId)}/content"
            + $"?{ExpiryParam}={expUnix}&{SignatureParam}={sig}";
    }

    /// <summary>
    /// Verifies a signed download URL. Returns true only if the signature is
    /// well-formed, valid for <paramref name="mediaAssetId"/>, and the expiry
    /// is in the future. Uses fixed-time comparison.
    /// </summary>
    public bool TryVerify(string mediaAssetId, string? expValue, string? sigValue)
    {
        if (string.IsNullOrWhiteSpace(mediaAssetId)) return false;
        if (string.IsNullOrWhiteSpace(expValue) || string.IsNullOrWhiteSpace(sigValue)) return false;
        if (!long.TryParse(expValue, out var expUnix)) return false;

        var nowUnix = _clock.GetUtcNow().ToUnixTimeSeconds();
        if (expUnix < nowUnix) return false;

        var expected = ComputeSignature(mediaAssetId, expUnix);
        return FixedTimeEquals(expected, sigValue);
    }

    private string ComputeSignature(string mediaAssetId, long expUnix)
    {
        var payload = Encoding.UTF8.GetBytes($"{mediaAssetId}|{expUnix}");
        var hash = HMACSHA256.HashData(_key, payload);
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        var b64 = Convert.ToBase64String(bytes);
        return b64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
