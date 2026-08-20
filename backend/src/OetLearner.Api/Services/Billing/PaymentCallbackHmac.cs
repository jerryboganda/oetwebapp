using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OetLearner.Api.Services.Billing;

/// <summary>Shared constant-time HMAC helpers for payment-provider callbacks.</summary>
public static class PaymentCallbackHmac
{
    public static string HmacSha256Hex(string key, string data)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }

    public static string Sha256Hex(string data)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool FixedEquals(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return false;
        }

        var a = Encoding.UTF8.GetBytes(left.Trim().ToLowerInvariant());
        var b = Encoding.UTF8.GetBytes(right.Trim().ToLowerInvariant());
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Fawaterak callback authenticity: HMAC-SHA256(invoiceId + invoiceKey) or
    /// SHA256(invoiceId + invoiceKey + hashKey), compared constant-time.
    /// </summary>
    public static bool FawaterakHashMatches(string hashApiKey, string invoiceId, string invoiceKey, string providedHash)
    {
        if (string.IsNullOrWhiteSpace(hashApiKey) || string.IsNullOrWhiteSpace(providedHash))
        {
            return false;
        }

        var payload = invoiceId + invoiceKey;
        var hmac = HmacSha256Hex(hashApiKey, payload);
        var sha = Sha256Hex(payload + hashApiKey);
        return FixedEquals(hmac, providedHash) || FixedEquals(sha, providedHash);
    }

    /// <summary>
    /// Whop webhook header format: <c>t=timestamp,v1=hex</c> over <c>{timestamp}.{payload}</c>.
    /// </summary>
    public static bool WhopSignatureMatches(string webhookSecret, string payload, string signatureHeader, long nowUnixSeconds, int maxAgeSeconds)
    {
        if (string.IsNullOrWhiteSpace(webhookSecret) || string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        string? timestamp = null;
        string? signature = null;
        foreach (var part in signatureHeader.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var key = part[..eq];
            var value = part[(eq + 1)..];
            if (key is "t") timestamp = value;
            else if (key is "v1") signature = value;
        }

        if (string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ts))
        {
            return false;
        }

        if (Math.Abs(nowUnixSeconds - ts) > Math.Max(30, maxAgeSeconds))
        {
            return false;
        }

        var expected = HmacSha256Hex(webhookSecret, $"{timestamp}.{payload}");
        return FixedEquals(expected, signature);
    }
}
