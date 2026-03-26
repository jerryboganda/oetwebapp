using System.Text.Json;

namespace OetLearner.Api.Services;

public static class JsonSupport
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string? json, T fallback)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
