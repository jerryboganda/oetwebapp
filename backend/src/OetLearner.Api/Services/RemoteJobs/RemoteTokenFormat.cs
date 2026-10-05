using System.Security.Cryptography;
using System.Text;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>
/// Format, generation and hashing of node and fleet-service bearer tokens (OET-RWP/1 section 2.2).
///
/// <para>
/// <c>orw1_&lt;tokenId&gt;_&lt;secret&gt;</c> (node) and <c>ofs1_&lt;tokenId&gt;_&lt;secret&gt;</c> (fleet): a 16-hex
/// token id (the lookup key), a 43-character base64url secret (256 bits), exactly three
/// underscore-separated parts, never a dot (so the default JwtBearer handler fails fast on parse and
/// never reaches its account-liveness query), at most 80 characters. Only the SHA-256 of the secret
/// is stored. This type is pure: no database, no logging.
/// </para>
/// </summary>
public static class RemoteTokenFormat
{
    public const string NodePrefix = "orw1";
    public const string FleetPrefix = "ofs1";
    public const int MaxLength = 80;
    public const int SecretLength = 43;

    /// <summary>The kinds a credential row may carry.</summary>
    public const string NodeKind = "node";
    public const string FleetKind = "fleet";

    /// <summary>True when a presented credential has one of our token prefixes (for the JWT bypass).</summary>
    public static bool LooksLikeRemoteToken(string? authorizationHeader)
    {
        if (string.IsNullOrEmpty(authorizationHeader)) return false;
        const string bearer = "Bearer ";
        if (!authorizationHeader.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)) return false;
        var token = authorizationHeader.AsSpan(bearer.Length).Trim();
        return token.StartsWith(NodePrefix + "_", StringComparison.Ordinal)
            || token.StartsWith(FleetPrefix + "_", StringComparison.Ordinal);
    }

    public static string PrefixFor(string kind) => kind == FleetKind ? FleetPrefix : NodePrefix;

    /// <summary>
    /// Creates a new token. The secret never contains <c>_</c> (regenerated until it does not), so
    /// the issued value always splits into exactly three underscore-separated parts.
    /// </summary>
    public static GeneratedRemoteToken Generate(string kind)
    {
        var prefix = PrefixFor(kind);
        var tokenId = RemoteIds.NewTokenId();
        string secret;
        do
        {
            secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        }
        while (secret.Contains('_'));

        return new GeneratedRemoteToken(tokenId, secret, $"{prefix}_{tokenId}_{secret}", HashSecretHex(secret));
    }

    /// <summary>
    /// Parses the value of an <c>Authorization</c> header. Returns false (without saying why) for a
    /// missing header, a scheme other than Bearer, a token over <see cref="MaxLength"/>, a wrong
    /// prefix, anything other than exactly three parts, a token id that is not 16 lowercase hex
    /// characters or a secret that is not 43 base64url characters.
    /// </summary>
    public static bool TryParse(string? authorizationHeader, string expectedKind, out string tokenId, out string secret)
    {
        tokenId = string.Empty;
        secret = string.Empty;
        if (string.IsNullOrEmpty(authorizationHeader)) return false;

        const string bearer = "Bearer ";
        if (!authorizationHeader.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)) return false;

        var token = authorizationHeader[bearer.Length..].Trim();
        if (token.Length == 0 || token.Length > MaxLength) return false;

        var parts = token.Split('_');
        if (parts.Length != 3) return false;
        if (!string.Equals(parts[0], PrefixFor(expectedKind), StringComparison.Ordinal)) return false;
        if (!RemoteIds.IsTokenId(parts[1])) return false;
        if (!IsSecret(parts[2])) return false;

        tokenId = parts[1];
        secret = parts[2];
        return true;
    }

    public static bool IsSecret(string? value)
    {
        if (value is not { Length: SecretLength }) return false;
        foreach (var c in value)
        {
            var ok = c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_';
            if (!ok) return false;
        }

        return true;
    }

    /// <summary>SHA-256 of the ASCII bytes of the secret string, raw.</summary>
    public static byte[] HashSecret(string secret) => SHA256.HashData(Encoding.ASCII.GetBytes(secret));

    /// <summary>Lowercase hex of <see cref="HashSecret"/> (the stored form).</summary>
    public static string HashSecretHex(string secret) => Convert.ToHexString(HashSecret(secret)).ToLowerInvariant();

    /// <summary>
    /// Constant-time comparison of a presented secret against a stored lowercase-hex hash. A stored
    /// value that is not 64 hex characters never matches (and still costs the same work).
    /// </summary>
    public static bool Verify(string presentedSecret, string? storedHashHex)
    {
        var computed = HashSecret(presentedSecret);
        byte[] stored;
        try
        {
            stored = storedHashHex is { Length: 64 } ? Convert.FromHexString(storedHashHex) : DummyHash;
        }
        catch (FormatException)
        {
            stored = DummyHash;
        }

        var equal = CryptographicOperations.FixedTimeEquals(computed, stored);
        // A dummy comparison can never succeed: the dummy is all zero bytes, a SHA-256 output is not.
        return equal && !ReferenceEquals(stored, DummyHash);
    }

    /// <summary>Stand-in compared when the token id is unknown, so the work done is identical.</summary>
    public static readonly byte[] DummyHash = new byte[32];

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>A freshly minted token. <see cref="Token"/> is shown to the caller exactly once.</summary>
public sealed record GeneratedRemoteToken(string TokenId, string Secret, string Token, string SecretHashHex);
