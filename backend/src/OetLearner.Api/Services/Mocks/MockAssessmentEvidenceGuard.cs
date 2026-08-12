using System.Text.Json;

namespace OetLearner.Api.Services.Mocks;

/// <summary>
/// Identifies mock reports that contain a governed Reading or Listening
/// section. Their mock-wide numeric score cannot be used for a pass or
/// readiness claim unless the owner-approved subtest conversion evidence has
/// already been evaluated by the canonical report projection.
/// </summary>
internal static class MockAssessmentEvidenceGuard
{
    public static bool ContainsGovernedScore(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return false;

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (!TryGetSubTests(document.RootElement, out var subTests)
                || subTests.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var subTest in subTests.EnumerateArray())
            {
                var id = ReadString(subTest, "id")
                    ?? ReadString(subTest, "subtest")
                    ?? ReadString(subTest, "name");
                if (id?.Trim().ToLowerInvariant() is "reading" or "listening") return true;
            }
        }
        catch (JsonException)
        {
            // Malformed reports are not eligible for a numeric readiness claim,
            // but the caller handles malformed payloads through its existing
            // missing-score path.
        }
        catch (InvalidOperationException)
        {
            // A structurally invalid JSON value (for example, a scalar
            // subTests property) is also ineligible and must fail closed.
        }

        return false;
    }

    private static bool TryGetSubTests(JsonElement root, out JsonElement subTests)
    {
        subTests = default;
        if (root.ValueKind != JsonValueKind.Object) return false;
        return root.TryGetProperty("subTests", out subTests)
            || root.TryGetProperty("subtests", out subTests);
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
