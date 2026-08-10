using System.Text.Json;

namespace OetLearner.Api.Services;

public static class ScoringPolicyValidation
{
    public const string CanonicalDefaultPolicyJson = """
    {
      "listening": { "conversionTable": "owner-managed" },
      "reading": { "conversionTable": "owner-managed" },
      "writing": { "passing": { "uk": 350, "ie": 350, "au": 350, "nz": 350, "ca": 350, "us": 300, "qa": 300 } },
      "speaking": { "passing": { "default": 350 } }
    }
    """;

    public static string? ValidateCanonicalPolicyJson(string policyJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(policyJson);
        }
        catch (JsonException)
        {
            return "policyJson must be valid JSON";
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return "policyJson must be a JSON object";
            }

            foreach (var sectionName in new[] { "listening", "reading" })
            {
                if (!HasOwnerManagedConversionTable(root, sectionName))
                {
                    return "policyJson must delegate Listening/Reading conversion to an owner-managed versioned table";
                }
            }

            if (!HasPassingDefault(root, "speaking", OetScoring.ScaledPassGradeB))
            {
                return "policyJson contradicts canonical OET scoring";
            }

            if (!TryGetObject(root, "writing", out var writing)
                || !TryGetObject(writing, "passing", out var writingPassing))
            {
                return "policyJson contradicts canonical OET scoring";
            }

            foreach (var country in OetScoring.WritingGradeBCountries)
            {
                if (!HasWritingCountryScore(writingPassing, country, OetScoring.ScaledPassGradeB))
                {
                    return "policyJson contradicts canonical OET scoring";
                }
            }

            foreach (var country in OetScoring.WritingGradeCPlusCountries)
            {
                if (!HasWritingCountryScore(writingPassing, country, OetScoring.ScaledPassGradeCPlus))
                {
                    return "policyJson contradicts canonical OET scoring";
                }
            }
        }

        return null;
    }

    private static bool HasPassingDefault(JsonElement root, string sectionName, int expected)
        => TryGetObject(root, sectionName, out var section)
            && TryGetObject(section, "passing", out var passing)
            && HasScore(passing, "default", expected);

    private static bool HasOwnerManagedConversionTable(JsonElement root, string sectionName)
    {
        if (!TryGetObject(root, sectionName, out var section)
            || !TryGetProperty(section, "conversionTable", out var conversionTable)
            || conversionTable.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        return string.Equals(conversionTable.GetString(), "owner-managed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasScore(JsonElement parent, string propertyName, int expected)
        => TryGetProperty(parent, propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var actual)
            && actual == expected;

    private static bool HasWritingCountryScore(JsonElement passing, string canonicalCountry, int expected)
    {
        foreach (var property in passing.EnumerateObject())
        {
            if (string.Equals(OetScoring.NormalizeWritingCountry(property.Name), canonicalCountry, StringComparison.Ordinal)
                && property.Value.ValueKind == JsonValueKind.Number
                && property.Value.TryGetInt32(out var actual)
                && actual == expected)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetObject(JsonElement parent, string propertyName, out JsonElement value)
        => TryGetProperty(parent, propertyName, out value) && value.ValueKind == JsonValueKind.Object;

    private static bool TryGetProperty(JsonElement parent, string propertyName, out JsonElement value)
    {
        if (parent.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (var property in parent.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
