using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>
/// Identifier and wire-format helpers for the remote-worker protocol (OET-RWP/1 section 0.3).
/// Job ids are <c>rj_</c> + a 26-character lowercase Crockford-base32 ULID, node ids <c>rw_</c> + the
/// same. A path id that does not match its pattern is answered <c>404</c> without a database read.
/// </summary>
public static partial class RemoteIds
{
    public const string JobPrefix = "rj_";
    public const string NodePrefix = "rw_";

    // Crockford base32, lowercase, no i / l / o / u.
    private const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    [GeneratedRegex("^rj_[0-7][0-9a-hjkmnp-tv-z]{25}$", RegexOptions.CultureInvariant)]
    private static partial Regex JobIdPattern();

    [GeneratedRegex("^rw_[0-7][0-9a-hjkmnp-tv-z]{25}$", RegexOptions.CultureInvariant)]
    private static partial Regex NodeIdPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^[0-9a-f]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenIdPattern();

    public static bool IsJobId(string? value) => value is { Length: 29 } && JobIdPattern().IsMatch(value);

    public static bool IsNodeId(string? value) => value is { Length: 29 } && NodeIdPattern().IsMatch(value);

    public static bool IsSha256Hex(string? value) => value is { Length: 64 } && Sha256Pattern().IsMatch(value);

    public static bool IsTokenId(string? value) => value is { Length: 16 } && TokenIdPattern().IsMatch(value);

    public static string NewJobId(DateTimeOffset now) => JobPrefix + NewUlid(now);

    public static string NewNodeId(DateTimeOffset now) => NodePrefix + NewUlid(now);

    /// <summary>16 lowercase hex characters (64 random bits).</summary>
    public static string NewTokenId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    /// <summary>26-character lowercase Crockford-base32 ULID: 48-bit millisecond timestamp + 80 random bits.</summary>
    public static string NewUlid(DateTimeOffset now)
    {
        Span<byte> bytes = stackalloc byte[16];
        var millis = now.ToUnixTimeMilliseconds();
        bytes[0] = (byte)(millis >> 40);
        bytes[1] = (byte)(millis >> 32);
        bytes[2] = (byte)(millis >> 24);
        bytes[3] = (byte)(millis >> 16);
        bytes[4] = (byte)(millis >> 8);
        bytes[5] = (byte)millis;
        RandomNumberGenerator.Fill(bytes[6..]);

        // 128 bits are written as 26 groups of 5 bits (130 bits): the two leading bits are zero, so
        // the first character is always 0-7. A small bit buffer keeps this free of 128-bit integers.
        var chars = new char[26];
        var charIndex = 0;
        var bitBuffer = 0;
        var bitCount = 2;
        foreach (var b in bytes)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;
            while (bitCount >= 5)
            {
                chars[charIndex++] = Alphabet[(bitBuffer >> (bitCount - 5)) & 31];
                bitCount -= 5;
            }

            bitBuffer &= (1 << bitCount) - 1;
        }

        return new string(chars);
    }

    /// <summary>Lowercase hex SHA-256 of the UTF-8 bytes of <paramref name="value"/>.</summary>
    public static string Sha256Hex(string value) => Sha256Hex(Encoding.UTF8.GetBytes(value));

    public static string Sha256Hex(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>RFC 3339 UTC with a <c>Z</c> and millisecond precision.</summary>
    public static string FormatTime(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public static string? FormatTime(DateTimeOffset? value) => value is null ? null : FormatTime(value.Value);
}
