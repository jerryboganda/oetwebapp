using System.Text.Json;

namespace OetLearner.Api.Services.Grammar;

/// <summary>
/// Canonical answer-comparison helpers used by every grading strategy.
/// Canonicalisation: trim, fold whitespace, lowercase (invariant),
/// strip surrounding punctuation. This policy is shared with
/// <c>lib/grammar/grading.ts</c> — keep them in lock-step.
/// </summary>
public static class GrammarCanonicaliser
{
    private static readonly char[] EdgePunctuation =
        { '.', ',', ';', ':', '!', '?', '\'', '"', '`', '(', ')', '[', ']', '{', '}' };

    public static string Canonicalise(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var trimmed = input.Trim();
        var collapsed = System.Text.RegularExpressions.Regex.Replace(trimmed, @"\s+", " ");
        var cleaned = collapsed.Trim(EdgePunctuation).Trim();
        return cleaned.ToLowerInvariant();
    }

    public static bool Matches(string? user, string expected)
        => Canonicalise(user) == Canonicalise(expected);

    public static bool MatchesAny(string? user, IEnumerable<string> expecteds)
    {
        var canon = Canonicalise(user);
        foreach (var e in expecteds)
            if (Canonicalise(e) == canon) return true;
        return false;
    }

    public static IReadOnlyList<string> ReadStringArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            var list = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrEmpty(s)) list.Add(s);
                }
            }
            return list;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
