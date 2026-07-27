using OetLearner.Api.Domain;

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
    private static readonly HashSet<string> GeneralEnglishFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Basic English Course", "Basic English", "General English", "Academic / General English",
    };

    public static bool IsProfession(string? value) =>
        Professions.Any(p => string.Equals(p.Id, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string[] ExpectedVideoTargets(string language, string subtest, string sourceProfession)
    {
        language = language.Trim().ToLowerInvariant();
        subtest = subtest.Trim().ToLowerInvariant();
        sourceProfession = sourceProfession.Trim().ToLowerInvariant();

        if (language == "en" || subtest is "listening" or "reading") return [];
        if (language != "ar" || subtest is not ("writing" or "speaking"))
            throw new ArgumentException("Unsupported course-video language or subtest.");

        if (!IsProfession(sourceProfession))
            throw new ArgumentException("Unsupported profession for Arabic Writing/Speaking.");
        return [sourceProfession];
    }

    public static bool TryValidateVideo(string? language, string? subtest, IReadOnlyCollection<string> targets, out string message)
    {
        var lang = language?.Trim().ToLowerInvariant();
        var section = subtest?.Trim().ToLowerInvariant();
        var supplied = targets.Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0).Distinct().ToArray();
        var normalized = supplied.Where(IsProfession).Order().ToArray();

        if (normalized.Length != supplied.Length)
        {
            message = "Video profession targets contain an unsupported profession id.";
            return false;
        }

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

        var valid = normalized.Length > 0;
        message = valid ? string.Empty : "Arabic Writing/Speaking must target at least one profession.";
        return valid;
    }

    public static bool VideoAppearsFor(string professionId, string? language, string? subtest, IReadOnlyCollection<string> targets)
    {
        professionId = professionId.Trim().ToLowerInvariant();
        var section = subtest?.Trim().ToLowerInvariant();
        if (!TryValidateVideo(language, subtest, targets, out _)) return false;
        return targets.Count == 0 || targets.Contains(professionId, StringComparer.OrdinalIgnoreCase);
    }

    public static string VideoSourceLabel(string? language, string? subtest, IReadOnlyCollection<string> targets)
    {
        if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)) return "Shared English";
        if (subtest?.Trim().ToLowerInvariant() is "listening" or "reading") return "Shared Arabic";
        var labels = Professions
            .Where(p => targets.Contains(p.Id, StringComparer.OrdinalIgnoreCase))
            .Select(p => p.Label)
            .ToArray();
        return labels.Length == 0 ? "Arabic" : $"{string.Join(" + ", labels)} Arabic";
    }

    public static string? ResolveCourseFolder(string? subtest, string? courseFolder, string? title)
    {
        if (subtest?.Trim().ToLowerInvariant() is not ("writing" or "speaking")) return null;
        var normalized = courseFolder?.Trim().ToLowerInvariant();
        if (normalized is "sessions" or "workshops") return normalized;
        return title?.Contains("workshop", StringComparison.OrdinalIgnoreCase) == true ? "workshops" : "sessions";
    }

    public static (string? Kind, string? ProfessionId) ResolveMaterialScope(
        MaterialFolder folder, IReadOnlyDictionary<string, MaterialFolder> allFolders)
    {
        var current = folder;
        var guard = 0;
        while (current is not null && guard++ < 64)
        {
            if (MaterialScopeKinds.IsValid(current.ScopeKind)) return (current.ScopeKind, current.ProfessionId);
            if (GeneralEnglishFolderNames.Contains(current.Name?.Trim() ?? string.Empty))
                return (MaterialScopeKinds.GeneralEnglish, null);
            var profession = Professions.FirstOrDefault(p =>
                string.Equals(p.Id, current.Name?.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Label, current.Name?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (profession is not null) return (MaterialScopeKinds.Profession, profession.Id);
            if (current.ParentFolderId is null) break;
            allFolders.TryGetValue(current.ParentFolderId, out current);
        }
        var subtest = ResolveMaterialSubtest(folder, allFolders);
        return subtest is "listening" or "reading" ? (MaterialScopeKinds.Shared, null) : (null, null);
    }

    public static string? ResolveMaterialSubtest(
        MaterialFolder folder, IReadOnlyDictionary<string, MaterialFolder> allFolders)
    {
        var current = folder;
        var guard = 0;
        while (current is not null && guard++ < 64)
        {
            var explicitCode = current.SubtestCode?.Trim().ToLowerInvariant();
            if (Subtests.Contains(explicitCode ?? string.Empty)) return explicitCode;
            var byName = current.Name?.Trim().ToLowerInvariant();
            if (Subtests.Contains(byName ?? string.Empty)) return byName;
            if (current.ParentFolderId is null) break;
            allFolders.TryGetValue(current.ParentFolderId, out current);
        }
        return null;
    }
}

public static class MaterialScopeKinds
{
    public const string Shared = "shared";
    public const string Profession = "profession";
    public const string GeneralEnglish = "general_english";

    public static bool IsValid(string? value) => value is Shared or Profession or GeneralEnglish;
}
