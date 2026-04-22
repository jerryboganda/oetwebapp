using System.Text;
using System.Text.Json;

namespace OetLearner.Api.Services;

/// <summary>
/// Opaque cursor pagination helper. Encodes a (timestamp, id) tuple as a
/// base64url JSON payload so clients can resume enumeration without exposing
/// ordering internals. Used by list endpoints that want to honour the blueprint
/// "cursor pagination for catalogs/history/invoices/reviews" requirement.
/// </summary>
public static class CursorPagination
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public static int NormalizeLimit(int? requested, int defaultLimit = DefaultLimit)
    {
        var value = requested ?? defaultLimit;
        if (value <= 0) return defaultLimit;
        return value > MaxLimit ? MaxLimit : value;
    }

    public readonly record struct Cursor(DateTimeOffset Timestamp, string Id);

    public static string Encode(DateTimeOffset timestamp, string id)
    {
        var payload = JsonSerializer.Serialize(new CursorPayload(timestamp, id));
        var bytes = Encoding.UTF8.GetBytes(payload);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(string? cursor, out Cursor result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(cursor)) return false;
        try
        {
            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            switch (normalized.Length % 4)
            {
                case 2: normalized += "=="; break;
                case 3: normalized += "="; break;
                case 1: return false;
            }
            var bytes = Convert.FromBase64String(normalized);
            var json = Encoding.UTF8.GetString(bytes);
            var payload = JsonSerializer.Deserialize<CursorPayload>(json);
            if (payload is null || string.IsNullOrEmpty(payload.Id)) return false;
            result = new Cursor(payload.Timestamp, payload.Id);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record CursorPayload(DateTimeOffset Timestamp, string Id);
}
