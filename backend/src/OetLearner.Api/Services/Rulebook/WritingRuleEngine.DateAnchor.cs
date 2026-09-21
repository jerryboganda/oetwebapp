using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Owner decisions of 19 Sep 2026 on scenarios whose SOURCE gives no usable letter date.
///
/// Until now a date line was mandatory (<c>model_answer_layout</c>, <c>letter_structure_order</c>)
/// while <c>letter_date_unsupported</c> had nothing to check a date against when a scenario carried
/// no date at all. So a Model Answer had to contain a date the validator could not verify, and ANY
/// date passed: 6 September 2026, 1 January 2020 and 29 February 2044 all returned zero findings on
/// the same letter. That is how fabricated dates reached the catalogue.
///
/// The classification below says how much date the source actually gives, and the rules follow it:
/// <list type="bullet">
/// <item><b>Day</b> — a day-level date exists. Nothing changes.</item>
/// <item><b>MonthOnly</b> — the source names a month and year but no day. The letter date is
/// optional, and a Model Answer must not state a day it does not have ("February 2018", never
/// "28 February 2018").</item>
/// <item><b>None</b> — the source gives no date. The letter date is optional, and a Model Answer
/// must omit it rather than invent one.</item>
/// <item><b>Unknown</b> — the caller did not classify. Behaviour is exactly as before.</item>
/// </list>
/// A candidate is never penalised for omitting the date when the anchor is MonthOnly or None, and
/// is not penalised for a reasonable assumed date either: the unverifiable-date finding is Model
/// Answer only.
///
/// Implemented as a third branch of the existing <c>letter_date_unsupported</c> check rather than
/// as a new check id, so it needs no new catalogue, provenance or generated-rulebook entry.
/// </summary>
public sealed partial class WritingRuleEngine
{
    private const string LineMonthYear =
        @"^\s*(?:" + MonthNames + @")\s+(?:19|20)\d{2}\s*$";

    private static readonly Regex MonthYearDateLineRe = new(LineMonthYear, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "February 2018" inside running text. The lookbehind stops it matching the tail of a full date
    // ("28 February 2018" is a Day anchor and is handled before this is ever reached).
    private static readonly Regex MonthYearInTextRe = new(
        @"(?<![\d/.\-])\b(?:" + MonthNames + @")\s+(?:19|20)\d{2}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Classifies how much date the source gives. Callers pass what they have; a caller with neither
    /// a todayDate nor any notes text gets <see cref="LetterDateAnchor.Unknown"/>, never
    /// <see cref="LetterDateAnchor.None"/>, so missing data can never switch the date requirement off.
    /// </summary>
    public static LetterDateAnchor ClassifyDateAnchor(string? todayDate, string? caseNotesText, string? taskText)
    {
        var notes = caseNotesText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(todayDate) && string.IsNullOrWhiteSpace(notes))
            return LetterDateAnchor.Unknown;
        if (!string.IsNullOrWhiteSpace(todayDate)) return LetterDateAnchor.Day;

        // "Assume that today's date is ..." stated in the notes or in the task text.
        var stated = new HashSet<DateTime>();
        foreach (var source in new[] { notes, taskText ?? string.Empty })
        {
            foreach (Match label in SaG6TodayLabelRe.Matches(source))
                SaG6AddStatedDate(label.Groups["rest"].Value, stated, atStart: true);
        }
        if (stated.Count > 0) return LetterDateAnchor.Day;

        // Any documented day-level date (dates of birth are already excluded by the scanner).
        if (SaG6NoteDates(notes).Count > 0) return LetterDateAnchor.Day;

        foreach (Match m in MonthYearInTextRe.Matches(notes))
            if (SaG6IsTreatmentDateCandidate(notes, m)) return LetterDateAnchor.MonthOnly;

        return LetterDateAnchor.None;
    }

    /// <summary>True for a date-optional anchor: the letter may legitimately have no date line.</summary>
    private static bool DateLineOptional(LetterDateAnchor anchor)
        => anchor is LetterDateAnchor.MonthOnly or LetterDateAnchor.None;

    private static bool IsMonthYearDateLine(string line) => MonthYearDateLineRe.IsMatch(line);

    // letter_date_unsupported — third branch (Model Answers only): a letter date the source cannot
    // support. With no date at all, ANY date is unverifiable; with a month and year only, a DAY is.
    private static IEnumerable<LintFinding> DetectLetterDateUnverifiable(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || !DateLineOptional(input.DateAnchor)) yield break;
        var severity = ModeSeverity(input, RuleSeverity.Critical);

        if (s.DateIndex is int dateIndex)
        {
            var line = s.Lines[dateIndex].Trim();
            yield return new LintFinding(rule.Id, severity,
                input.DateAnchor == LetterDateAnchor.None
                    ? "The source gives no date for this scenario, so the letter date (" + line + ") cannot be verified. Omit the date line rather than inventing one."
                    : "The source gives only a month and year, so the letter cannot state a day (" + line + "). Write the date as \"Month YYYY\" or omit it; do not invent a day.",
                Quote: line);
            yield break;
        }

        // A "Month YYYY" line is not a day-level date, but with NO source date at all it is just as
        // unverifiable. With a month-and-year source it is exactly the precision the source gives.
        if (input.DateAnchor != LetterDateAnchor.None) yield break;
        var limit = s.SalutationIndex ?? s.Lines.Length;
        for (var i = 0; i < limit && i < s.Lines.Length; i++)
        {
            if (!IsMonthYearDateLine(s.Lines[i])) continue;
            var line = s.Lines[i].Trim();
            yield return new LintFinding(rule.Id, severity,
                "The source gives no date for this scenario, so the letter date (" + line + ") cannot be verified. Omit the date line rather than inventing one.",
                Quote: line);
            yield break;
        }
    }
}
