using System.Text.Json;

namespace OetLearner.Api.Services.Reading;

internal static class ReadingAttemptScope
{
    public static ReadingScope FromJson(string? scopeJson)
    {
        if (string.IsNullOrWhiteSpace(scopeJson))
        {
            return new ReadingScope(false, null);
        }

        try
        {
            using var document = JsonDocument.Parse(scopeJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("kind", out var kind)
                || kind.ValueKind != JsonValueKind.String
                || !string.Equals(kind.GetString(), "part-practice", StringComparison.OrdinalIgnoreCase))
            {
                return new ReadingScope(false, null);
            }

            if (!document.RootElement.TryGetProperty("partCode", out var partCodeElement)
                || partCodeElement.ValueKind != JsonValueKind.String)
            {
                return new ReadingScope(true, null);
            }

            var partCode = partCodeElement.GetString()?.Trim().ToUpperInvariant();
            return partCode is "A" or "B" or "C"
                ? new ReadingScope(true, partCode)
                : new ReadingScope(true, null);
        }
        catch (JsonException)
        {
            return new ReadingScope(false, null);
        }
    }
}

internal sealed record ReadingScope(bool IsPartPractice, string? PartCode);
