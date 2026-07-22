namespace OetLearner.Api.Services.Content;

/// <summary>
/// Canonical profession-first course map shared by admin projections and write validation.
/// Empty video targets mean shared across every profession.
/// </summary>
public static class CourseContentMatrix
{
    public sealed record Profession(string Id, string Label);

    public static readonly IReadOnlyList<Profession> Professions =
    [
        new("medicine", "Medicine"),
        new("nursing", "Nursing"),
        new("pharmacy", "Pharmacy"),
        new("physiotherapy", "Physiotherapy"),
        new("dentistry", "Dentistry"),
        new("radiography", "Radiography"),
    ];

    public static readonly IReadOnlyList<string> Subtests = ["listening", "reading", "writing", "speaking"];

    public static bool IsProfession(string? value) =>
        Professions.Any(p => string.Equals(p.Id, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string[] ExpectedVideoTargets(string language, string subtest, string sourceProfession)
    {
        language = language.Trim().ToLowerInvariant();
        subtest = subtest.Trim().ToLowerInvariant();
        sourceProfession = sourceProfession.Trim().ToLowerInvariant();

        if ((sourceProfession is "dentistry" or "radiography") && (subtest is "writing" or "speaking"))
            throw new ArgumentException("Dentistry and Radiography do not have Writing or Speaking videos.");
        if (language == "en" || subtest is "listening" or "reading") return [];
        if (language != "ar" || subtest is not ("writing" or "speaking"))
            throw new ArgumentException("Unsupported course-video language or subtest.");

        return sourceProfession switch
        {
            "medicine" or "physiotherapy" => ["medicine", "physiotherapy"],
            "nursing" => ["nursing"],
            "pharmacy" => ["pharmacy"],
            _ => throw new ArgumentException("Dentistry and Radiography do not have Writing or Speaking videos."),
        };
    }

    public static bool TryValidateVideo(string? language, string? subtest, IReadOnlyCollection<string> targets, out string message)
    {
        var lang = language?.Trim().ToLowerInvariant();
        var section = subtest?.Trim().ToLowerInvariant();
        var normalized = targets.Select(x => x.Trim().ToLowerInvariant()).Where(IsProfession).Distinct().Order().ToArray();

        if (lang is not ("en" or "ar") || !Subtests.Contains(section ?? string.Empty))
        {
            message = "Course videos require English or Arabic and a Listening, Reading, Writing, or Speaking subtest.";
            return false;
        }

        if (lang == "en" || section is "listening" or "reading")
        {
            message = normalized.Length == 0 ? string.Empty : "English and Listening/Reading videos are shared and must target all professions.";
            return normalized.Length == 0;
        }

        var allowed = new[]
        {
            Array.Empty<string>(), // not valid; retained only to keep comparisons explicit below
            new[] { "medicine", "physiotherapy" },
            new[] { "nursing" },
            new[] { "pharmacy" },
        };
        var valid = allowed.Skip(1).Any(a => a.Order().SequenceEqual(normalized));
        message = valid ? string.Empty : "Arabic Writing/Speaking must use Medicine + Physiotherapy, Nursing, or Pharmacy. Dentistry and Radiography are not available.";
        return valid;
    }

    public static bool VideoAppearsFor(string professionId, string? language, string? subtest, IReadOnlyCollection<string> targets)
    {
        if ((professionId is "dentistry" or "radiography") && (subtest is "writing" or "speaking")) return false;
        if (!TryValidateVideo(language, subtest, targets, out _)) return false;
        return targets.Count == 0 || targets.Contains(professionId, StringComparer.OrdinalIgnoreCase);
    }

    public static string VideoSourceLabel(string? language, string? subtest, IReadOnlyCollection<string> targets)
    {
        if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)) return "Shared English";
        if (subtest is "listening" or "reading") return "Shared Arabic";
        if (targets.Contains("nursing", StringComparer.OrdinalIgnoreCase)) return "Nursing Arabic";
        if (targets.Contains("pharmacy", StringComparer.OrdinalIgnoreCase)) return "Pharmacy Arabic";
        return "Medicine Arabic (shared with Physiotherapy)";
    }
}

public static class MaterialScopeKinds
{
    public const string Shared = "shared";
    public const string Profession = "profession";
    public const string GeneralEnglish = "general_english";

    public static bool IsValid(string? value) => value is Shared or Profession or GeneralEnglish;
}
