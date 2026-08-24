using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OetLearner.Api.Services.Billing;

/// <summary>Shared constant-time HMAC helpers for payment-provider callbacks.</summary>
public static class PaymentCallbackHmac
{
    public static string HmacSha256Hex(string key, string data)
    {
        byte[] keyBytes;
        if (key.StartsWith("whsec_", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                keyBytes = Convert.FromBase64String(key["whsec_".Length..]);
            }
            catch
            {
                keyBytes = Encoding.UTF8.GetBytes(key);
            }
        }
        else
        {
            keyBytes = Encoding.UTF8.GetBytes(key);
        }

        using var hmac = new HMACSHA256(keyBytes);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }

    public static string Sha256Hex(string data)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string HmacSha256Base64(string key, string data)
    {
        byte[] keyBytes;
        if (key.StartsWith("whsec_", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                keyBytes = Convert.FromBase64String(key["whsec_".Length..]);
            }
            catch
            {
                keyBytes = Encoding.UTF8.GetBytes(key);
            }
        }
        else
        {
            keyBytes = Encoding.UTF8.GetBytes(key);
        }

        using var hmac = new HMACSHA256(keyBytes);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
    }

    public static bool FixedEquals(string? left, string? right, bool ignoreCase = true)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return false;
        }

        var l = ignoreCase ? left.Trim().ToLowerInvariant() : left.Trim();
        var r = ignoreCase ? right.Trim().ToLowerInvariant() : right.Trim();
        var a = Encoding.UTF8.GetBytes(l);
        var b = Encoding.UTF8.GetBytes(r);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Fawaterak callback authenticity. Official format (per Fawaterak docs):
    /// HMAC-SHA256 over "InvoiceId={id}&amp;InvoiceKey={key}&amp;PaymentMethod={method}"
    /// using the vendor key, lowercase hex. Legacy local formats are kept as
    /// constant-time fallbacks for older stored callbacks.
    /// </summary>
    public static bool FawaterakHashMatches(string hashApiKey, string invoiceId, string invoiceKey, string paymentMethod, string providedHash)
    {
        if (string.IsNullOrWhiteSpace(hashApiKey) || string.IsNullOrWhiteSpace(providedHash))
        {
            return false;
        }

        var officialPayload = FormattableString.Invariant(
            $"InvoiceId={invoiceId}&InvoiceKey={invoiceKey}&PaymentMethod={paymentMethod}");
        if (FixedEquals(HmacSha256Hex(hashApiKey, officialPayload), providedHash))
        {
            return true;
        }

        // Legacy fallbacks (pre-2026-08 local formats; never produced by Fawaterak
        // but kept so historical retries/replays still verify identically).
        var payload = invoiceId + invoiceKey;
        var hmac = HmacSha256Hex(hashApiKey, payload);
        var sha = Sha256Hex(payload + hashApiKey);
        return FixedEquals(hmac, providedHash) || FixedEquals(sha, providedHash);
    }

    /// <summary>
    /// Fawaterak cancel/expired callback (Fawry/Aman/Masary) authenticity:
    /// HMAC-SHA256 over "referenceId={id}&amp;PaymentMethod={method}".
    /// </summary>
    public static bool FawaterakCancelHashMatches(string hashApiKey, string referenceId, string paymentMethod, string providedHash)
    {
        if (string.IsNullOrWhiteSpace(hashApiKey) || string.IsNullOrWhiteSpace(providedHash))
        {
            return false;
        }

        var payload = FormattableString.Invariant($"referenceId={referenceId}&PaymentMethod={paymentMethod}");
        return FixedEquals(HmacSha256Hex(hashApiKey, payload), providedHash);
    }

    /// <summary>
    /// Whop webhook verification (Standard Webhooks specification).
    /// Signed content: <c>{webhook-id}.{webhook-timestamp}.{rawPayload}</c>
    /// Signature header: <c>webhook-signature: v1,&lt;base64&gt;</c> (may contain multiple space-separated signatures).
    /// Backward-compatible with legacy <c>t=timestamp,v1=hex</c> format.
    /// </summary>
    public static bool WhopSignatureMatches(
        string webhookSecret,
        string payload,
        string signatureHeader,
        string? webhookId,
        string? webhookTimestamp,
        long nowUnixSeconds,
        int maxAgeSeconds)
    {
        if (string.IsNullOrWhiteSpace(webhookSecret) || string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        // 1. Parse signature header (supports space-separated Standard Webhook tokens and comma-separated legacy tokens)
        var signatures = new List<string>();
        string? embeddedTimestamp = null;

        var spaceTokens = signatureHeader.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawToken in spaceTokens)
        {
            var subTokens = rawToken.Contains(',') && !rawToken.StartsWith("v1,", StringComparison.OrdinalIgnoreCase)
                ? rawToken.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : new[] { rawToken };

            foreach (var token in subTokens)
            {
                if (token.StartsWith("v1,", StringComparison.OrdinalIgnoreCase))
                {
                    signatures.Add(token["v1,".Length..].Trim());
                }
                else if (token.StartsWith("v1=", StringComparison.OrdinalIgnoreCase))
                {
                    signatures.Add(token["v1=".Length..].Trim());
                }
                else if (token.StartsWith("t=", StringComparison.OrdinalIgnoreCase))
                {
                    embeddedTimestamp = token["t=".Length..].Trim();
                }
                else
                {
                    signatures.Add(token.Trim());
                }
            }
        }

        if (signatures.Count == 0)
        {
            return false;
        }

        // 2. Resolve timestamp
        var tsStr = !string.IsNullOrWhiteSpace(webhookTimestamp) ? webhookTimestamp.Trim() : embeddedTimestamp;
        long ts = 0;
        if (!string.IsNullOrWhiteSpace(tsStr))
        {
            if (!long.TryParse(tsStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out ts))
            {
                return false;
            }

            if (Math.Abs(nowUnixSeconds - ts) > Math.Max(30, maxAgeSeconds))
            {
                return false;
            }
        }

        // 3. Compute expected Standard Webhooks signature: {id}.{timestamp}.{payload}
        if (!string.IsNullOrWhiteSpace(webhookId) && !string.IsNullOrWhiteSpace(tsStr))
        {
            var signedPayload = $"{webhookId.Trim()}.{tsStr}.{payload}";
            var expectedBase64 = HmacSha256Base64(webhookSecret, signedPayload);
            var expectedHex = HmacSha256Hex(webhookSecret, signedPayload);

            foreach (var sig in signatures)
            {
                if (FixedEquals(expectedBase64, sig, ignoreCase: false) || FixedEquals(expectedHex, sig, ignoreCase: true))
                {
                    return true;
                }
            }
        }

        // 4. Compute timestamp-only signature: {timestamp}.{payload} (Standard Webhooks fallback or legacy Stripe-style)
        if (!string.IsNullOrWhiteSpace(tsStr))
        {
            var signedPayload = $"{tsStr}.{payload}";
            var expectedBase64 = HmacSha256Base64(webhookSecret, signedPayload);
            var expectedHex = HmacSha256Hex(webhookSecret, signedPayload);

            foreach (var sig in signatures)
            {
                if (FixedEquals(expectedBase64, sig, ignoreCase: false) || FixedEquals(expectedHex, sig, ignoreCase: true))
                {
                    return true;
                }
            }
        }

        // 5. Fallback: raw body HMAC
        var rawExpectedBase64 = HmacSha256Base64(webhookSecret, payload);
        var rawExpectedHex = HmacSha256Hex(webhookSecret, payload);
        foreach (var sig in signatures)
        {
            if (FixedEquals(rawExpectedBase64, sig, ignoreCase: false) || FixedEquals(rawExpectedHex, sig, ignoreCase: true))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Legacy overload maintaining binary compatibility.</summary>
    public static bool WhopSignatureMatches(string webhookSecret, string payload, string signatureHeader, long nowUnixSeconds, int maxAgeSeconds)
        => WhopSignatureMatches(webhookSecret, payload, signatureHeader, null, null, nowUnixSeconds, maxAgeSeconds);
}
