using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Listening;

/// <summary>
/// Listening V2 R06.10 — HMAC-signed confirm-token for the two-step section
/// advance protocol. First <c>POST /v1/listening/attempts/{id}/advance</c>
/// returns 412 with this token; second call must echo it. TTL is configurable
/// via <see cref="EffectiveListeningPolicy.ConfirmTokenTtlMs"/>.
///
/// Token format: <c>{base64url(payload)}.{base64url(hmac)}</c> where payload
/// is JSON: <c>{a:&lt;attemptId&gt;,f:&lt;fromState&gt;,t:&lt;toState&gt;,e:&lt;expiresUnixMs&gt;}</c>.
/// </summary>
public sealed class ListeningConfirmTokenService
{
    private readonly byte[] _key;

    public ListeningConfirmTokenService(IOptions<AuthTokenOptions> options)
    {
        var k = options.Value.AccessTokenSigningKey
            ?? throw new InvalidOperationException(
                "AuthTokens:AccessTokenSigningKey not configured — required for ListeningConfirmTokenService.");
        _key = Encoding.UTF8.GetBytes(k);
    }

    public string Issue(string attemptId, string fromState, string toState, int ttlMs, DateTimeOffset now)
    {
        var payload = new ConfirmPayload(attemptId, fromState, toState,
            now.AddMilliseconds(ttlMs).ToUnixTimeMilliseconds());
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        var sig = HMACSHA256.HashData(_key, json);
        return $"{Base64Url.Encode(json)}.{Base64Url.Encode(sig)}";
    }

    public ConfirmTokenValidation Validate(string token, string attemptId, string fromState, string toState, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(token)) return ConfirmTokenValidation.Invalid("missing");
        var parts = token.Split('.');
        if (parts.Length != 2) return ConfirmTokenValidation.Invalid("malformed");

        byte[] payloadBytes;
        byte[] sigBytes;
        try
        {
            payloadBytes = Base64Url.Decode(parts[0]);
            sigBytes = Base64Url.Decode(parts[1]);
        }
        catch
        {
            return ConfirmTokenValidation.Invalid("base64");
        }

        var expected = HMACSHA256.HashData(_key, payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(expected, sigBytes))
            return ConfirmTokenValidation.Invalid("signature");

        ConfirmPayload? p;
        try
        {
            p = JsonSerializer.Deserialize<ConfirmPayload>(payloadBytes);
        }
        catch
        {
            return ConfirmTokenValidation.Invalid("payload");
        }
        if (p is null) return ConfirmTokenValidation.Invalid("payload");

        if (!string.Equals(p.a, attemptId, StringComparison.Ordinal))
            return ConfirmTokenValidation.Invalid("attempt-mismatch");
        if (!string.Equals(p.f, fromState, StringComparison.Ordinal))
            return ConfirmTokenValidation.Invalid("from-mismatch");
        if (!string.Equals(p.t, toState, StringComparison.Ordinal))
            return ConfirmTokenValidation.Invalid("to-mismatch");
        if (now.ToUnixTimeMilliseconds() > p.e)
            return ConfirmTokenValidation.Invalid("expired");

        return ConfirmTokenValidation.Ok();
    }

    private sealed record ConfirmPayload(string a, string f, string t, long e);
}

public readonly record struct ConfirmTokenValidation(bool IsValid, string? Reason)
{
    public static ConfirmTokenValidation Ok() => new(true, null);
    public static ConfirmTokenValidation Invalid(string reason) => new(false, reason);
}

internal static class Base64Url
{
    public static string Encode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
