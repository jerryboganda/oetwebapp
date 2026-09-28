using System.Security.Cryptography;
using System.Text;

namespace OetLearner.Api.Services;

/// <summary>
/// Pusher HTTP API request signer (Soketi is Pusher-protocol compatible).
/// Used by the admin Soketi connection-test handler.
/// </summary>
public static class SoketiPusherSigner
{
    public static string BuildSignedQueryString(
        string method, string path, string timestamp, string appKey, string appSecret, string body)
    {
        var bodyMd5 = ComputeMd5Hex(body);
        var queryParams = new SortedDictionary<string, string>
        {
            ["auth_key"] = appKey,
            ["auth_timestamp"] = timestamp,
            ["auth_version"] = "1.0",
            ["body_md5"] = bodyMd5,
        };

        var queryString = string.Join("&", queryParams.Select(kv => $"{kv.Key}={kv.Value}"));
        var signPayload = $"{method}\n{path}\n{queryString}";
        var signature = ComputeHmacSha256(appSecret, signPayload);
        return $"{queryString}&auth_signature={signature}";
    }

    public static string ComputeHmacSha256(string secret, string data)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeMd5Hex(string input)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
