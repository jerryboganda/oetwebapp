using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// On-demand evidence for the Writing Model Answer validator's clinical-abbreviation rules (owner directive,
/// 9 Oct 2026; owner-confirmed in-app self-check carve-out, same pattern as the AI Pipeline self-check).
/// An admin runs it from the Writing hub: it feeds known golden cases (QD, QID, QDS, QOD, BD, TDS, PRN,
/// scan and table forms, OD vs right eye, completed vs pending actions) through the REAL deployed
/// <see cref="WritingRuleEngine"/> on the server and reports pass/fail per case. It never runs in CI, never
/// touches the database and never calls an AI provider ($0 Writing rule).
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
        var passed = results.Count(r => r.Ok);
        return new SelfCheckReport(now, WritingRuleEngine.ValidatorVersion, results.Count, passed, results.Count - passed, results);
    }

    private static bool IsCheck(WritingRuleEngine engine, LintFinding finding, string checkId)
        => string.Equals(finding.RuleId, "BUILTIN." + checkId, StringComparison.Ordinal)
           || string.Equals(engine.CheckIdForRule(ExamProfession.Medicine, finding.RuleId), checkId, StringComparison.Ordinal);
}
