using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Completed versus planned actions (owner directive, 9 Oct 2026). "Notify the patient's doctor" is a pending
/// action; "the patient's doctor has already been notified" is a completed one. A Model Answer must never
/// present a pending action as done: <c>completed_action_unsupported</c>.
/// <list type="bullet">
/// <item>Model Answer only and source-gated on the canonical case notes; silent without them.</item>
/// <item>Conservative by design: it fires only when the letter claims an action is done ("has been notified",
/// "was referred", "has been arranged/booked/scheduled", "has been contacted/informed/initiated/encouraged", "I counselled") AND the notes mention that action
/// only in its plan form ("Notify ...", "Refer ...", "Arrange/Book/Schedule ...", "Contact ...") and nowhere in a
/// completed form (notified, referred, arranged, appointment, ...). A completed note anywhere suppresses it.</item>
/// <item>Ceiling: the reverse direction (a request for something the notes show as done) is not checked, because
/// the retyped notes mix tenses ("Follow-up was arranged" for a planned review); the semantic validator and the
/// glossary prompt (rule E) carry it.</item>
/// </list>
/// </summary>
public sealed partial class WritingRuleEngine
{
    private const string CaAux = @"\b(?:has|have|had|was|were)\s+(?:(?:already|also|since|been)\s+)*";

    private static readonly (string Name, Regex Claim, Regex Plan, Regex Done)[] CompletedActionFamilies =
    [
        ("notified", new Regex(CaAux + @"notified\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"\bnotify\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"\b(?:notified|informed|told|let\s+\w+\s+know)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        ("referred", new Regex(CaAux + @"referred\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"\brefer\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"\b(?:referred|referral)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        ("arranged", new Regex(CaAux + @"(?:arranged|booked|scheduled)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"\b(?:arrange|book|schedule)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"\b(?:arranged|booked|scheduled|appointment)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        ("contacted", new Regex(CaAux + @"contacted\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"\bcontact\b(?!\s+lens)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"\b(?:contacted|spoke|spoken)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        // 9 Oct 2026 final source audit: "To be informed of the side effects" was written as "he has been informed", referrals "to be
        // initiated" as "have been initiated", "Quitline contact to be encouraged" as "has been encouraged" and "Plan: counsel on ..."
        // as "I counselled him". "to be informed/initiated/encouraged" is the plan form, so it is never read as the done form.
        ("informed", new Regex(CaAux + @"informed\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"\b(?:to\s+be\s+informed|inform)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"(?<!\bto\s+be\s)\binformed\b|\b(?:told|let\s+\w+\s+know)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        ("initiated", new Regex(CaAux + @"initiated\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"\b(?:to\s+be\s+initiated|initiate)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"(?<!\bto\s+be\s)\binitiated\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        ("encouraged", new Regex(CaAux + @"encouraged\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"\b(?:to\s+be\s+encouraged|encourage)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"(?<!\bto\s+be\s)\bencouraged\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        ("counselled", new Regex(@"\bI\s+(?:have\s+)?counsell?ed\b|" + CaAux + @"counsell?ed\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"\bcounsel\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"\bcounsell?ed\b|\bcounselling\s+(?:provided|given)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
    ];

    private static IEnumerable<LintFinding> DetectCompletedActionUnsupported(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || string.IsNullOrWhiteSpace(input.CaseNotesText) || s.Body.Length == 0) yield break;
        var lines = input.CaseNotesText.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var bodyOffset = BodyOffset(s);
        foreach (var (_, claim, plan, done) in CompletedActionFamilies)
        {
            var claimMatch = claim.Match(s.Body);
            if (!claimMatch.Success) continue;
            if (lines.Any(l => done.IsMatch(l))) continue;
            var planLine = lines.FirstOrDefault(l => plan.IsMatch(l));
            if (planLine is null) continue;
            yield return new LintFinding(rule.Id, RuleSeverity.Critical,
                $"The letter says an action was already done (\"{claimMatch.Value.Trim()}\"), but the case notes only plan it (\"{planLine.Trim()}\"). \"Notify the patient's doctor\" is a pending action; \"the patient's doctor has been notified\" is a completed one. A Model Answer never presents a pending action as completed (owner, 9 Oct 2026).",
                Quote: claimMatch.Value.Trim(),
                Start: bodyOffset + claimMatch.Index,
                End: bodyOffset + claimMatch.Index + claimMatch.Length,
                FixSuggestion: "State the action as the notes give it: a request or plan, not a completed event.");
        }
    }
}
