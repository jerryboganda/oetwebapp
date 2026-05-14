using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using System.Text.Json;

namespace OetLearner.Api.Services;

public class AnalyticsIngestionService(LearnerDbContext db, TimeProvider timeProvider)
{
    private const int MaxEventNameLength = 64;
    private const int MaxProperties = 24;
    private const int MaxPropertyKeyLength = 64;
    private const int MaxStringValueLength = 256;
    private const int MaxArrayItems = 10;
    private static readonly string[] SensitiveKeyFragments = ["email", "password", "token", "secret", "phone", "cookie", "authorization", "url"];

    public async Task RecordAsync(string userId, AnalyticsTrackRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.EventName))
        {
            throw ApiException.Validation(
                "invalid_analytics_event",
                "An analytics event name is required.",
                [new ApiFieldError("eventName", "required", "Provide a tracked event name.")]);
        }

        var normalizedEventName = request.EventName.Trim();
        if (normalizedEventName.Length > MaxEventNameLength)
        {
            throw ApiException.Validation(
                "invalid_analytics_event",
                $"Analytics event names must be {MaxEventNameLength} characters or fewer.",
                [new ApiFieldError("eventName", "max_length", $"Must be {MaxEventNameLength} characters or fewer.")]);
        }

        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = $"AN-{Guid.NewGuid():N}",
            UserId = userId,
            EventName = normalizedEventName,
            PayloadJson = JsonSupport.Serialize(SanitizeProperties(request.Properties)),
            OccurredAt = timeProvider.GetUtcNow()
        });

        await db.SaveChangesAsync(ct);
    }

    private static Dictionary<string, object?> SanitizeProperties(Dictionary<string, object?>? properties)
    {
        var sanitized = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (properties is null) return sanitized;

        foreach (var (rawKey, rawValue) in properties)
        {
            if (sanitized.Count >= MaxProperties) break;
            var key = rawKey.Trim();
            if (key.Length is 0 or > MaxPropertyKeyLength) continue;
            if (IsSensitiveKey(key))
            {
                sanitized[key] = "[redacted]";
                continue;
            }
            if (TrySanitizeValue(rawValue, out var value)) sanitized[key] = value;
        }

        return sanitized;
    }

    private static bool TrySanitizeValue(object? rawValue, out object? value)
    {
        value = rawValue switch
        {
            null => null,
            string text => Truncate(text),
            bool or byte or short or int or long or float or double or decimal => rawValue,
            JsonElement element => SanitizeJsonElement(element),
            _ => null,
        };
        return rawValue is null || value is not null;
    }

    private static object? SanitizeJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => Truncate(element.GetString() ?? string.Empty),
        JsonValueKind.Number => element.TryGetInt64(out var number) ? number : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => SanitizeArray(element),
        _ => null,
    };

    private static List<object?> SanitizeArray(JsonElement array)
    {
        var values = new List<object?>();
        foreach (var item in array.EnumerateArray().Take(MaxArrayItems))
        {
            var value = SanitizeJsonElement(item);
            if (item.ValueKind is JsonValueKind.Object) continue;
            values.Add(value);
        }
        return values;
    }

    private static string Truncate(string value)
        => value.Length <= MaxStringValueLength ? value : value[..MaxStringValueLength];

    private static bool IsSensitiveKey(string key)
        => SensitiveKeyFragments.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}