namespace OetLearner.Api.Services.Rulebook;

public static class RulebookProfessionParser
{
    public static string ToCanonicalId(ExamProfession profession) => profession switch
    {
        ExamProfession.OccupationalTherapy => "occupational-therapy",
        ExamProfession.SpeechPathology => "speech-pathology",
        ExamProfession.OtherAlliedHealth => "other-allied-health",
        ExamProfession.Medicine => "medicine",
        ExamProfession.Nursing => "nursing",
        ExamProfession.Dentistry => "dentistry",
        ExamProfession.Pharmacy => "pharmacy",
        ExamProfession.Physiotherapy => "physiotherapy",
        ExamProfession.Veterinary => "veterinary",
        ExamProfession.Optometry => "optometry",
        ExamProfession.Radiography => "radiography",
        ExamProfession.Podiatry => "podiatry",
        ExamProfession.Dietetics => "dietetics",
        _ => throw new ArgumentOutOfRangeException(nameof(profession), profession, "Unknown exam profession."),
    };

    public static bool TryParse(string? value, out ExamProfession profession)
    {
        var normalized = Normalize(value);
        foreach (var candidate in Enum.GetValues<ExamProfession>())
        {
            if (Normalize(candidate.ToString()) == normalized)
            {
                profession = candidate;
                return true;
            }
        }

        profession = default;
        return false;
    }

    private static string Normalize(string? value)
        => new((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
}