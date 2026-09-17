using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Cross-profession Model Answer repair (18 Sep 2026), letter_date_unsupported branch 3.
///
/// <c>DetectLetterDateUnsupported</c> proves the letter date against the LATEST documented note
/// date, and <c>DetectSaG6LetterDateUnsupported</c> against the scenario's today's date and the
/// numeric shapes. Both go silent when there is nothing to prove the date against: no scenario
/// today's date and not one parseable date anywhere in the canonical notes. A Model Answer for
/// such a task could therefore carry ANY date and pass — Mr Satchell's stored answer was dated
/// "6 September 2026" with zero support in the task or the notes, and the check stayed quiet.
///
/// The date cannot be repaired by writing, only by fixing the scenario data, so this branch says
/// exactly that. Model-Answer-only: a candidate is never penalised for the catalogue's missing
/// today's date. It fires only when BOTH anchors are absent, so every task that carries a today's
/// date or a dated note behaves exactly as before.
/// </summary>
public sealed partial class WritingRuleEngine
{
    // Any date shape the two existing branches can parse: "21 March 2012", "21/03/12", "09.08.14".
    private static readonly Regex CpAnyDateRe = new(
        @"\b\d{1,2}\s+(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{2,4}\b"
        + @"|\b\d{1,2}[./-]\d{1,2}[./-]\d{2,4}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static IEnumerable<LintFinding> DetectCpLetterDateUnanchored(
        OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (input.CaseNotesText is not { Length: > 0 } notes) yield break;
        if (s.DateIndex is null) yield break;
        if (!string.IsNullOrWhiteSpace(input.TodayDate)) yield break;

        var line = s.Lines[s.DateIndex.Value];
        if (!CpAnyDateRe.IsMatch(line)) yield break;

        // A dated note anchors the date; a bare month ("February 2018") does not, and a day
        // invented to complete it is exactly the defect this branch exists to catch.
        foreach (Match m in CpAnyDateRe.Matches(notes))
        {
            var lookBack = Math.Max(0, m.Index - 60);
            var window = notes.Substring(lookBack, m.Index - lookBack);
            if (Regex.IsMatch(window, @"\b(?:dob|date\s+of\s+birth|birth\s+date|born)\b", RegexOptions.IgnoreCase))
                continue;
            yield break; // a usable treatment date exists — the other two branches own this letter
        }

        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
            $"The letter is dated {line.Trim()}, but this task carries no today's date and the case notes "
            + "document no date at all, so no date can be supported. Set the scenario's today's date from "
            + "the source before approving a Model Answer for this task.",
            Quote: line.Trim());
    }
}
