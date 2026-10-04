using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Text a candidate reads. Internal rule IDs (RULE_13, RULE_20 …) exist for audit and must never reach a
/// candidate (owner spec 4 Oct 2026, section 4). The grader is told not to write them; this is the safety net
/// that removes any that still slip into a rationale or summary at the moment it is shown. The stored text is
/// left as the model wrote it.
/// </summary>
public static class SpeakingLearnerText
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    private const string Separator = @"\s*(?:,|;|/|&|and)\s*";

    // "(RULE_20)", "[RULE_13, RULE_20]" — the whole bracketed group goes, with the space before it.
    private static readonly Regex Parenthesised = new(
        @"\s*[\(\[]\s*RULE_\d{1,3}(?:" + Separator + @"RULE_\d{1,3})*\s*[\)\]]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        Timeout);

    // "see RULE_20", "RULE_13 and RULE_20" — replaced by plain words so the sentence still reads.
    private static readonly Regex Bare = new(
        @"\bRULE_\d{1,3}(?:" + Separator + @"RULE_\d{1,3})*\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        Timeout);

    private static readonly Regex RepeatedSpaces = new(
        @"[ \t]{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        Timeout);

    /// <summary>The text with every internal rule ID removed or replaced by plain wording.</summary>
    public static string ScrubRuleIds(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

        try
        {
            var withoutBracketed = Parenthesised.Replace(text, string.Empty);
            var replaced = Bare.Replace(withoutBracketed, match =>
                Regex.Matches(match.Value, "RULE_").Count > 1
                    ? "the relevant guidelines"
                    : "the relevant guideline");
            return RepeatedSpaces.Replace(replaced, " ").Trim();
        }
        catch (RegexMatchTimeoutException)
        {
            return text;
        }
    }
}
