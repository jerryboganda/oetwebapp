using System.Text.Json;

namespace OetLearner.Api.Services.Reading;

public static class ReadingLearnerSafeProjection
{
    private static readonly HashSet<string> OptionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "A", "B", "C", "D", "E", "F",
    };

    public static IReadOnlyList<object> ProjectOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<object>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.Array => ProjectOptionArray(doc.RootElement),
                JsonValueKind.Object => ProjectOptionMap(doc.RootElement),
                _ => Array.Empty<object>(),
            };
        }
        catch (JsonException)
        {
            return Array.Empty<object>();
        }
    }

    public static JsonElement ProjectOptionsElement(string? json)
        => JsonSerializer.SerializeToElement(ProjectOptions(json));

    private static List<object> ProjectOptionArray(JsonElement array)
    {
        var options = new List<object>();
        foreach (var option in array.EnumerateArray())
        {
            if (option.ValueKind == JsonValueKind.String)
            {
                options.Add(option.GetString() ?? string.Empty);
                continue;
            }

            if (option.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var value = ReadSafeOptionText(option, "value")
                ?? ReadSafeOptionText(option, "letter")
                ?? ReadSafeOptionText(option, "label")
                ?? ReadSafeOptionText(option, "text")
                ?? ReadSafeOptionText(option, "title");
            var label = ReadSafeOptionText(option, "label")
                ?? ReadSafeOptionText(option, "text")
                ?? ReadSafeOptionText(option, "title")
                ?? value;
            if (!string.IsNullOrWhiteSpace(label))
            {
                options.Add(new { value = string.IsNullOrWhiteSpace(value) ? label : value, label });
            }
        }

        return options;
    }

    private static List<object> ProjectOptionMap(JsonElement obj)
    {
        var options = new List<object>();
        foreach (var property in obj.EnumerateObject())
        {
            if (!OptionKeys.Contains(property.Name) || property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var label = property.Value.GetString();
            if (!string.IsNullOrWhiteSpace(label))
            {
                options.Add(new { value = property.Name.ToUpperInvariant(), label });
            }
        }

        return options;
    }

    private static string? ReadSafeOptionText(JsonElement option, string propertyName)
    {
        if (!option.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }
}