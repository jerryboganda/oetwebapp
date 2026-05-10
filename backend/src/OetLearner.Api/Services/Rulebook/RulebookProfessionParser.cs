namespace OetLearner.Api.Services.Rulebook;

public static class RulebookProfessionParser
{
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