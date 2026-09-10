using System.Text.Json;

namespace OetLearner.Api.Services.Reading;

internal static class ReadingAttemptScope
{
    public static ReadingScope FromJson(string? scopeJson)
    {
        if (string.IsNullOrWhiteSpace(scopeJson))
        {
            return new ReadingScope(false, null, false);
        }

        try
        {
            using var document = JsonDocument.Parse(scopeJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new ReadingScope(false, null, false);
            }

            // `untimed` is a top-level flag independent of `kind` — it applies to
            // both the "learning" (Full Exam) and "part-practice" (Part A/B/C)
            // Untimed Practice launchers (Final Developer Brief item 7).
            var isUntimed = document.RootElement.TryGetProperty("untimed", out var untimedElement)
                && untimedElement.ValueKind == JsonValueKind.True;

            if (!document.RootElement.TryGetProperty("kind", out var kind)
                || kind.ValueKind != JsonValueKind.String
                || !string.Equals(kind.GetString(), "part-practice", StringComparison.OrdinalIgnoreCase))
            {
                return new ReadingScope(false, null, isUntimed);
            }

            if (!document.RootElement.TryGetProperty("partCode", out var partCodeElement)
                || partCodeElement.ValueKind != JsonValueKind.String)
            {
                return new ReadingScope(true, null, isUntimed);
            }

            var partCode = partCodeElement.GetString()?.Trim().ToUpperInvariant();
            return partCode is "A" or "B" or "C"
                ? new ReadingScope(true, partCode, isUntimed)
                : new ReadingScope(true, null, isUntimed);
        }
        catch (JsonException)
        {
            return new ReadingScope(false, null, false);
        }
    }
}

internal sealed record ReadingScope(bool IsPartPractice, string? PartCode, bool IsUntimed);
