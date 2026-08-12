namespace OetLearner.Api.Services.Assessment;

internal static class MockErrorCategoryClassifier
{
    public static string ClassifyTyped(
        string? learnerAnswer,
        string? correctAnswer,
        string? skillTags)
    {
        var learner = Collapse(learnerAnswer);
        var correct = Collapse(correctAnswer);
        if (string.IsNullOrWhiteSpace(learner) || string.IsNullOrWhiteSpace(correct))
            return "detail";

        if (HasSkillTag(skillTags, "inference"))
            return "inference";

        if (IsNumberFormDifference(learner, correct))
            return "number_form";

        if (Digits(learner) is { Length: > 0 } learnerDigits
            && string.Equals(learnerDigits, Digits(correct), StringComparison.Ordinal)
            && !string.Equals(learner, correct, StringComparison.OrdinalIgnoreCase))
        {
            return "unit";
        }

        if (string.Equals(learner, correct, StringComparison.OrdinalIgnoreCase)
            || LevenshteinAtMostOne(learner.ToUpperInvariant(), correct.ToUpperInvariant()))
        {
            return "spelling";
        }

        if (correct.Contains(learner, StringComparison.OrdinalIgnoreCase)
            && correct.Length > learner.Length)
        {
            return "form";
        }

        return "detail";
    }

    private static bool HasSkillTag(string? skillTags, string expected)
        => (skillTags ?? string.Empty)
            .Split([',', ';', '|', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Any(tag => string.Equals(tag.Trim(), expected, StringComparison.OrdinalIgnoreCase));

    private static string Digits(string value)
        => new(value.Where(char.IsDigit).ToArray());

    private static string Collapse(string? value)
        => string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool IsNumberFormDifference(string left, string right)
    {
        var x = left.ToUpperInvariant();
        var y = right.ToUpperInvariant();
        if (string.Equals(x, y, StringComparison.Ordinal)) return false;
        foreach (var (singular, plural) in new[] { (x, y), (y, x) })
        {
            if (plural == singular + "S" || plural == singular + "ES") return true;
            if (singular.Length > 1
                && singular.EndsWith('Y')
                && plural == singular[..^1] + "IES") return true;
        }
        return false;
    }

    private static bool LevenshteinAtMostOne(string left, string right)
    {
        if (Math.Abs(left.Length - right.Length) > 1) return false;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1];
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                current[j] = left[i - 1] == right[j - 1]
                    ? previous[j - 1]
                    : 1 + Math.Min(previous[j - 1], Math.Min(previous[j], current[j - 1]));
            }
            previous = current;
        }
        return previous[right.Length] <= 1;
    }
}
