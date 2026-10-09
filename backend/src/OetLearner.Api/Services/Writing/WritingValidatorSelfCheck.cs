using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing.Review;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// On-demand evidence for the Writing Model Answer validator's clinical-abbreviation rules (owner directive,
/// 9 Oct 2026; owner-confirmed in-app self-check carve-out, same pattern as the AI Pipeline self-check).
/// An admin runs it from the Writing hub: it feeds known golden cases (QD, QID, QDS, QOD, BD, TDS, PRN,
/// scan and table forms, OD vs right eye, completed vs pending actions) through the REAL deployed
/// <see cref="WritingRuleEngine"/> on the server and reports pass/fail per case. It never runs in CI, never
/// touches the database and never calls an AI provider ($0 Writing rule).
/// <para>
/// It also carries the SOURCE-GROUNDING and SOURCE-PRECEDENCE probes (owner directive, 9 Oct 2026): a fact that IS in the
/// case notes (the Physiotherapy DOB and 90-degree knee flexion) is never called invented or absent by the grader or the
/// reviewer, a real absence is still reported, and a Model Answer never outranks the case notes.
/// </para>
/// </summary>
public static class WritingValidatorSelfCheck
{
    private const string FrequencyCheck = "medication_frequency_source_mismatch";
    private const string LatinCheck = "latin_abbreviations_translated";
    private const string CompletedCheck = "completed_action_unsupported";

    private sealed record Case(
        string Group, string Name, string Notes, string Body, string CheckId, bool ExpectFinding,
        string? ExpectFix = null, string? ForbidFix = null);

    public sealed record CaseResult(string Group, string Name, string CheckId, bool Expected, bool Actual, bool Ok, string Detail);

    public sealed record SelfCheckReport(
        DateTimeOffset RanAt, string ValidatorVersion, int Total, int Passed, int Failed, IReadOnlyList<CaseResult> Cases);

    private static readonly Case[] Cases =
    [
        // Frequency vs the original case notes (the Daniels slip: scan 1q.d. = once daily, answer said four times daily).
        new("Frequency", "QD scan row vs \"four times daily\" (the Daniels error) is held", "Medication history 11/02/10: Indapamide 2.5mg 1q.d.", "Mrs Jones takes indapamide, 2.5 mg four times daily.", FrequencyCheck, true),
        new("Frequency", "QD written as once daily passes", "Medication history 11/02/10: Indapamide 2.5mg 1q.d.", "Mrs Jones takes indapamide, 2.5 mg once daily.", FrequencyCheck, false),
        new("Frequency", "QID written as four times daily passes", "Amoxicillin 500mg QID", "Mrs Jones takes amoxicillin, 500 mg four times daily.", FrequencyCheck, false),
        new("Frequency", "QID written as once daily is held", "Amoxicillin 500mg q.i.d.", "Mrs Jones takes amoxicillin, 500 mg once daily.", FrequencyCheck, true),
        new("Frequency", "QDS written as four times daily passes", "Cefalexin 500mg QDS", "Mrs Jones takes cefalexin, 500 mg four times daily.", FrequencyCheck, false),
        new("Frequency", "QDS written as once daily is held (q.d.s is never read as q.d)", "Cefalexin 500mg q.d.s.", "Mrs Jones takes cefalexin, 500 mg once daily.", FrequencyCheck, true),
        new("Frequency", "QOD written as every other day passes", "Warfarin 5mg QOD", "Mrs Jones takes warfarin, 5 mg every other day.", FrequencyCheck, false),
        new("Frequency", "QOD written as daily is held", "Warfarin 5mg q.o.d.", "Mrs Jones takes warfarin, 5 mg daily.", FrequencyCheck, true),
        new("Frequency", "BD scan row written as twice daily passes", "Verapamil 80mg 1b.d.", "Mrs Jones takes verapamil, 80 mg twice daily.", FrequencyCheck, false),
        new("Frequency", "BD written as three times daily is held", "Verapamil 80mg BD", "Mrs Jones takes verapamil, 80 mg three times daily.", FrequencyCheck, true),
        new("Frequency", "TDS written as three times daily passes", "Metoclopramide 10mg TDS", "Mrs Jones takes metoclopramide, 10 mg three times daily.", FrequencyCheck, false),
        new("Frequency", "TID written as four times daily is held", "Metoclopramide 10mg TID", "Mrs Jones takes metoclopramide, 10 mg four times daily.", FrequencyCheck, true),
        new("Frequency", "PRN written as as needed passes", "Paracetamol 1g PRN", "Mrs Jones takes paracetamol, 1 g as needed.", FrequencyCheck, false),
        new("Frequency", "A scheduled dose presented as as needed is held", "Paracetamol 1g 4 times a day", "Mrs Jones takes paracetamol, 1 g as needed.", FrequencyCheck, true),
        new("Frequency", "Medication table row 1noct written as at night passes", "Nitrazepam 5mg 1noct", "Mrs Jones takes nitrazepam, 5 mg at night.", FrequencyCheck, false),
        new("Frequency", "Medication table row 1noct written as every morning is held", "Nitrazepam 5mg 1noct", "Mrs Jones takes nitrazepam, 5 mg every morning.", FrequencyCheck, true),
        new("Frequency", "OD beside an eye cue is the right eye, not a frequency", "Latanoprost 0.005% eye drops OD nocte", "Mrs Jones uses latanoprost, 0.005 mg at night.", FrequencyCheck, false),
        new("Frequency", "A weaning rate is not a dosing frequency", "Prednisolone 40mg mane\nPrednisolone weaning the dose by 5mg per week", "Mrs Jones takes prednisolone, 40 mg each morning. The prednisolone will be weaned by 5 mg weekly.", FrequencyCheck, false),
        // The abbreviation must be written in full in a Model Answer.
        new("Latin", "QD must be written in full", "Indapamide 2.5mg QD", "Mrs Jones takes indapamide, 2.5 mg QD.", LatinCheck, true, ExpectFix: "once daily"),
        new("Latin", "q.o.d. must be written in full", "Warfarin 5mg q.o.d.", "Mrs Jones takes warfarin, 5 mg q.o.d.", LatinCheck, true, ExpectFix: "every other day"),
        new("Latin", "q.d.s. is explained as four times daily, never as once daily", "Cefalexin 500mg q.d.s.", "Mrs Jones takes cefalexin, 500 mg q.d.s.", LatinCheck, true, ExpectFix: "four times daily", ForbidFix: "once daily"),
        new("Latin", "Plain English passes", "Indapamide 2.5mg QD", "Mrs Jones takes indapamide, 2.5 mg once daily.", LatinCheck, false),
        new("Latin", "OD beside an eye cue is not flagged", "Latanoprost 0.005% eye drops OD nocte", "Mrs Jones puts one drop of latanoprost into the right eye, OD, at night.", LatinCheck, false),
        // Completed versus pending actions.
        new("Action", "A pending note presented as done is held", "Notify the patient's doctor", "Her doctor, Dr Sotto, has been notified.", CompletedCheck, true),
        new("Action", "A completed note stated as done passes", "Notified Mrs Jones' doctor, Dr Sotto", "Her doctor, Dr Sotto, has been notified.", CompletedCheck, false),
        new("Action", "A request is not a completion claim", "Notify the patient's doctor", "I would be grateful if you could notify her doctor.", CompletedCheck, false),
        // Found by the 9 Oct 2026 final source audit: plan items written as done.
        new("Action", "'To be informed' written as 'has been informed' is held", "To be informed of the possible side effects of the antibiotics", "Mrs Jones has been informed of the possible side effects of the antibiotics.", CompletedCheck, true),
        new("Action", "'Informed of ...' stated as done passes", "Informed of the possible side effects of the antibiotics", "Mrs Jones has been informed of the possible side effects of the antibiotics.", CompletedCheck, false),
        new("Action", "Referrals 'to be initiated' are not written as initiated", "Referrals are to be initiated to a dietician and a district nurse", "Referrals have been initiated to a dietician and a district nurse.", CompletedCheck, true),
        new("Action", "'Plan: counsel' written as 'I counselled' is held", "Plan: counsel on lifestyle, exercise and diet", "I counselled Mrs Jones on lifestyle, exercise and diet.", CompletedCheck, true),
        new("Action", "'Quitline contact to be encouraged' written as done is held", "Contact with Quitline to be encouraged", "Quitline contact has been encouraged.", CompletedCheck, true),
    ];

    // ---------------------------------------------------------------------------------------------------------
    // Source grounding + source precedence (the Physiotherapy live-test regression and the CASE NOTES > MODEL ANSWER rule)
    // ---------------------------------------------------------------------------------------------------------

    private sealed record Probe(string Group, string Name, string CheckId, bool Expected, Func<(bool Actual, string Detail)> Run);

    // The real shape of the stored Physiotherapy case notes (Mr Anthony Miller): the DOB line and the AROM line are exactly
    // the two facts the grader and the reviewer once called "invented" while they were in the source.
    private const string MillerNotes =
        "Mr Anthony Miller attended physiotherapy following a knee arthroscopy\n"
        + "DOB: 28 February 1968 (58 y.o.)\n"
        + "Initial physiotherapy assessment on 28 October 2026 found mild right knee swelling and pain rated 4/10\n"
        + "AROM R knee: flexion 90\u00B0, extension -5\u00B0.\n"
        + "He was walking with one stick";

    private const string MillerNotesOtherDobAndSide =
        "Mr Anthony Miller attended physiotherapy following a knee arthroscopy\n"
        + "DOB: 30 March 1956 (70 y.o.)\n"
        + "Initial physiotherapy assessment on 28 October 2026 found mild right knee swelling and pain rated 4/10\n"
        + "AROM L knee: flexion 90\u00B0, extension 0\u00B0.\n"
        + "He was walking with one stick";

    private const string MillerLetter =
        "Dr Sarah Patel\nEmergency Assessment Unit\nCity General Hospital\n\n3 November 2026\n\nDear Dr Patel,\n"
        + "Re: Mr Anthony Miller (DOB: 28 February 1968) \u2014 suspected right deep vein thrombosis\n\n"
        + "I am writing to refer Mr Anthony Miller for same-day assessment.\n\n"
        + "At his initial physiotherapy assessment on 28 October 2026, right knee flexion was 90 degrees with extension to -5 degrees, and he was walking with one stick.\n\n"
        + "Yours sincerely,\n\nPhysiotherapist";

    private const string DobClaim = "The date of birth 28 February 1968 is not in the case notes, so it is invented.";
    private const string FlexionQuote = "right knee flexion was 90 degrees";
    private const string FlexionClaim = "The right knee flexion of 90 degrees is not recorded in the case notes.";
    private const string DoseClaim = "The dose of amlodipine 5 mg is not in the case notes.";
    private const string DoseNotes = "His hypertension is controlled with amlodipine 5mg daily";

    private const string DoseLetter =
        "Dr Sarah Patel\nEmergency Assessment Unit\n\n3 November 2026\n\nDear Dr Patel,\nRe: Mr Anthony Miller, aged 58\n\n"
        + "His hypertension is controlled with amlodipine 5 mg daily.\n\nYours sincerely,\n\nPhysiotherapist";

    private static (bool Actual, string Detail) Removed(string quote, string message, string letter, string notes)
    {
        var removed = WritingSourcePresence.IsFalseAbsenceClaim(quote, message, letter, notes, null, out var evidence);
        return (removed, removed ? "claim removed; source line: " + evidence : "claim kept");
    }

    private static (bool Actual, string Detail) Evidence(string quote, string message, string notes, string mustContain)
    {
        var seen = WritingSourcePresence.ValueLookup(quote, message, notes, null);
        return (seen is not null && seen.Contains(mustContain, StringComparison.Ordinal), seen ?? "no source line found");
    }

    private static (bool Actual, string Detail) ReviewerPromptCarriesSourceCheck()
    {
        var finding = new WritingAssessmentRuleFinding(
            "AI:content", "Content", "critical", DobClaim, "28 February 1968", null, null, null, "C2");
        var request = new WritingReviewRequest(
            Guid.Empty, "self-check", 0, null, "practice", "physiotherapy", "LT-UR", string.Empty, MillerNotes, MillerLetter,
            new WritingReviewScores(2, 5, 5, 5, 5, 5, 350), "self-check",
            [WritingReviewFinding.From("f1", WritingReviewFindingOrigin.Ai, finding)],
            [], [], null, (_, _) => Task.CompletedTask, WritingReviewMode.Shadow);
        var prompt = WritingReviewPrompt.Build(request, new WritingReviewOptions(), enhanced: false, issues: []);
        var ok = prompt.Contains("SOURCE GROUNDING", StringComparison.Ordinal)
            && prompt.Contains("source check (computed from the case notes)", StringComparison.Ordinal);
        return (ok, ok ? "reviewer prompt tells the reviewer the DOB IS in the case notes" : "source check line missing from the reviewer prompt");
    }

    private static readonly Probe[] Probes =
    [
        // The two facts the owner saw wrongly flagged in live Physiotherapy testing.
        new("SourceGrounding", "DOB is in the case notes: the 'invented DOB' claim is removed", "source_grounding", true,
            () => Removed("28 February 1968", DobClaim, MillerLetter, MillerNotes)),
        new("SourceGrounding", "Right knee flexion 90 degrees is in the case notes: the 'not recorded' claim is removed (the real sentence, with extension to -5 degrees)", "source_grounding", true,
            () => Removed(FlexionQuote, FlexionClaim, MillerLetter, MillerNotes)),
        // A real absence is still reported (the guard only ever removes a claim it can prove false).
        new("SourceGrounding", "A DOB the notes do not carry is still reported", "source_grounding", false,
            () => Removed("28 February 1968", DobClaim, MillerLetter, MillerNotesOtherDobAndSide)),
        new("SourceGrounding", "A value recorded for the other side (left knee) never proves the right knee", "source_grounding", false,
            () => Removed(FlexionQuote, FlexionClaim, MillerLetter, MillerNotesOtherDobAndSide)),
        new("SourceGrounding", "A dose is never suppressed by code, even when the dose is in the notes", "source_grounding", false,
            () => Removed("amlodipine 5 mg daily", DoseClaim, DoseLetter, DoseNotes)),
        // The reviewer is given the matching source line for every absence claim.
        new("SourceGrounding", "The reviewer is shown the DOB source line", "source_grounding", true,
            () => Evidence("28 February 1968", DobClaim, MillerNotes, "dob: 28 february 1968")),
        new("SourceGrounding", "The reviewer is shown the knee-flexion source line", "source_grounding", true,
            () => Evidence(FlexionQuote, FlexionClaim, MillerNotes, "flexion 90deg")),
        new("SourceGrounding", "The reviewer is shown the dose source line", "source_grounding", true,
            () => Evidence("amlodipine 5 mg daily", DoseClaim, DoseNotes, "amlodipine 5mg")),
        new("SourceGrounding", "The reviewer prompt carries the computed source check", "source_grounding", true,
            ReviewerPromptCarriesSourceCheck),
        new("SourceGrounding", "The grader prompt orders a full case-note check before any 'invented' finding", "source_grounding", true,
            () =>
            {
                var ok = WritingRev8HouseStyle.CandidateGradingRules.Contains("SOURCE-GROUNDING CHECK", StringComparison.Ordinal);
                return (ok, ok ? "SOURCE-GROUNDING CHECK is in the candidate grading rules" : "rule missing from the candidate grading rules");
            }),
        // Source precedence: CASE NOTES / SOURCE PDF > MODEL ANSWER.
        new("Precedence", "Every Writing AI prompt says the original notes outrank any Model Answer", "source_precedence", true,
            () =>
            {
                var text = ClinicalAbbreviationGlossary.PromptSection(false);
                var ok = text.Contains("primary source of truth", StringComparison.Ordinal)
                    && text.Contains("never overrides", StringComparison.Ordinal);
                return (ok, ok ? "candidate prompt: notes are the primary source of truth, a Model Answer never overrides them" : "precedence wording missing");
            }),
        new("Precedence", "A medicine-frequency disagreement with the notes blocks publishing and holds a published answer", "source_precedence", true,
            () => (WritingTaskModelAnswerService.IsSourceFidelityCheck(FrequencyCheck), FrequencyCheck)),
        new("Precedence", "A pending action shown as done blocks publishing and holds a published answer", "source_precedence", true,
            () => (WritingTaskModelAnswerService.IsSourceFidelityCheck(CompletedCheck), CompletedCheck)),
        new("Precedence", "A style-only finding never holds a published answer", "source_precedence", false,
            () => (WritingTaskModelAnswerService.IsSourceFidelityCheck("dob_colon_format"), "dob_colon_format")),
    ];

    private const string LetterTemplate =
        "The Registrar\nAdverse Drug Reactions Data Bank\nCentreville\n\n1 March 2010\n\nDear Sir/Madam,\nRe: Mrs Mary Jones, aged 78\n\n{0}\n\nYours faithfully,\n\nPharmacist";

    public static SelfCheckReport Run(WritingRuleEngine engine, DateTimeOffset now)
    {
        var results = new List<CaseResult>();
        foreach (var c in Cases)
        {
            try
            {
                var findings = engine.Lint(new WritingLintInput(
                    LetterText: string.Format(LetterTemplate, c.Body),
                    LetterType: "LT-OT",
                    CaseNotesText: c.Notes,
                    TodayDate: "1 March 2010",
                    DateAnchor: LetterDateAnchor.Day,
                    Profession: ExamProfession.Medicine,
                    IsModelAnswer: true))
                    .Where(f => IsCheck(engine, f, c.CheckId))
                    .ToList();
                var actual = findings.Count > 0;
                var ok = actual == c.ExpectFinding
                    && (c.ExpectFix is null || findings.Any(f => string.Equals(f.FixSuggestion, c.ExpectFix, StringComparison.OrdinalIgnoreCase)))
                    && (c.ForbidFix is null || !findings.Any(f => string.Equals(f.FixSuggestion, c.ForbidFix, StringComparison.OrdinalIgnoreCase)));
                var detail = findings.Count == 0
                    ? "no finding"
                    : string.Join(" | ", findings.Select(f => (f.Quote ?? "?") + " -> " + (f.FixSuggestion ?? "?")));
                results.Add(new CaseResult(c.Group, c.Name, c.CheckId, c.ExpectFinding, actual, ok, detail));
            }
            catch (Exception ex)
            {
                results.Add(new CaseResult(c.Group, c.Name, c.CheckId, c.ExpectFinding, false, false, "check crashed: " + ex.GetType().Name));
            }
        }
        foreach (var p in Probes)
        {
            try
            {
                var (actual, detail) = p.Run();
                results.Add(new CaseResult(p.Group, p.Name, p.CheckId, p.Expected, actual, actual == p.Expected, detail));
            }
            catch (Exception ex)
            {
                results.Add(new CaseResult(p.Group, p.Name, p.CheckId, p.Expected, false, false, "check crashed: " + ex.GetType().Name));
            }
        }

        var passed = results.Count(r => r.Ok);
        return new SelfCheckReport(now, WritingRuleEngine.ValidatorVersion, results.Count, passed, results.Count - passed, results);
    }

    private static bool IsCheck(WritingRuleEngine engine, LintFinding finding, string checkId)
        => string.Equals(finding.RuleId, "BUILTIN." + checkId, StringComparison.Ordinal)
           || string.Equals(engine.CheckIdForRule(ExamProfession.Medicine, finding.RuleId), checkId, StringComparison.Ordinal);
}
