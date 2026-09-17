using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Senior Assessor Release Audit (owner, 16 Sep 2026), group G5: register,
/// judgmental wording and medication syntax classes that the deployed
/// validator missed. Every member here is Model-Answer-only (DECISIONS §B):
/// candidate grading keeps its existing behaviour and is never touched.
/// Detectors that extend an existing check id emit ONLY the new shapes and
/// are composed with the existing detector under the same id:
/// <list type="bullet">
/// <item>register_colloquial: DetectSaG5EmotionalObservation (G5-a1),
/// DetectSaG5NoteStyleQuery (G5-b), DetectSaG5Tiredness (G5-c),
/// DetectSaG5LexicalMisuse (G5-h).</item>
/// <item>judgmental_labels: DetectSaG5JudgmentalBehaviour (G5-a2).</item>
/// <item>medication_list_punctuation: DetectSaG5MedicationListExtendedDoses (G5-e),
/// DetectSaG5StrandedFormulation (G5-g).</item>
/// <item>NEW medication_frequency_conflict: DetectSaG5MedicationFrequencyConflict (G5-f).</item>
/// </list>
/// </summary>
public sealed partial class WritingRuleEngine
{
    // ---------------------------------------------------------------------
    // G5-a1 — emotional/affective observations (DECISIONS §C.12): "He is
    // anxious", "was anxious and worried", "is quite worried", "presented
    // today in distress", "no obvious anxiety", "bored", "embarrassing".
    // Supersedes the 10 Sep governance for MODEL ANSWERS only (the latest
    // owner audit flags the bare copula form "He is anxious and dyspnoeic").
    // Guards: (1) anxiety/worry tied to a stated object or trigger ("is
    // anxious about coping", "became anxious on entering the scanner") is
    // reported content, unless intensified ("quite worried about") or doubled
    // ("anxious and worried"); (2) mental-health letters are exempt — when the
    // INTRODUCTION or the recipient block names a psychiatric/psychological
    // purpose, a mental-state observation is a clinical finding. The
    // exemption reads the letter's purpose, never the case notes, so the
    // register rule keeps its deliberate no-case-notes-bypass design.
    // Nouns ("anxiety disorder", "symptoms of anxiety"), "respiratory
    // distress", "abdominal distress" and "concerned" never match.
    // ---------------------------------------------------------------------

    private static readonly Regex SaG5EmotionalObservationRe = new(
        @"\b(?:is|was|are|were|became|becomes|remains|remained)\s+(?:(?<intens>very|quite|extremely|rather|visibly|increasingly|really|so)\s+)?(?:anxious|worried|distressed)\b(?:\s+and\s+(?<pair>anxious|worried|distressed)\b)?(?<ctx>\s+(?:about|regarding|over|that|because|given|when|whenever|while|to|on\s+[a-z]+ing|at\s+the\s+(?:prospect|thought|idea)|for\s+(?:his|her|their))\b)?"
        + @"|\b(?:presented|presents|attended|attends|arrived|appeared|appears|was|is|remains|remained)\s+(?:today\s+)?in\s+(?:(?:considerable|great|obvious|visible|significant|marked)\s+)?distress\b"
        + @"|\bno\s+(?:obvious|apparent|evident|visible)\s+anxiety\b"
        + @"|\bbored(?:om)?\b"
        + @"|\b(?:feels|feel|feeling|felt)\s+(?:(?:very|quite|extremely|rather|increasingly|really|so)\s+)?discouraged\b"
        + @"|\bembarrass(?:ed|ing)\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex SaG5MentalHealthPurposeRe = new(
        @"\b(?:psychiatr\w*|psycholog\w*|mental\s+health|anxiety|depressi\w*|counsell?ing|psychotherap\w*|schizophreni\w*|bipolar|psychos[ie]s|psychotic|eating\s+disorders?|anorexia|bulimia)\b",
        RegexOptions.IgnoreCase);

    private static bool SaG5IsMentalHealthLetter(LetterStructure s)
        => (s.BodyParagraphs.Count > 0 && SaG5MentalHealthPurposeRe.IsMatch(s.BodyParagraphs[0]))
           || s.Lines.Take(s.SalutationIndex ?? 0).Any(l => SaG5MentalHealthPurposeRe.IsMatch(l));

    private static IEnumerable<LintFinding> DetectSaG5EmotionalObservation(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (s.Body.Length == 0 || SaG5IsMentalHealthLetter(s)) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in SaG5EmotionalObservationRe.Matches(s.Body))
        {
            if (m.Groups["ctx"].Success && !m.Groups["intens"].Success && !m.Groups["pair"].Success) continue;
            var quote = m.Value.Trim();
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Minor),
                "\"" + quote + "\" is an emotional observation, not a clinical finding. State the relevant source fact neutrally (the symptom or concern the patient reported) or omit it; a documented anxiety or depression diagnosis may be stated factually.",
                Quote: quote, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length);
        }
    }

    // ---------------------------------------------------------------------
    // G5-a2 — judgmental behaviour wording (DECISIONS §C.12): "adherence"
    // not "compliance"/"non-compliance"; "did not attend" not "defaulted";
    // "bizarre behaviour" -> the objective observations; "drinks heavily" ->
    // exact quantity + frequency. The existing detector already reports
    // "non-compliant" (both modes) and the smoker/drinker person nouns, so
    // this branch never re-emits those: "non-compliant" is excluded by the
    // lookbehind and "heavy drinker" is a different shape. Physiological
    // compliance ("lung compliance", "bladder compliance") and "in compliance
    // with" are standard English and never match. No mental-health exemption:
    // the owner flagged "bizarre behaviour" inside a psychiatric referral.
    // ---------------------------------------------------------------------

    private static readonly Regex SaG5JudgmentalBehaviourRe = new(
        @"\b(?<n>non[\-\s]?compliance)\b"
        + @"|(?<!\b(?:non[\-\s]?|in\s+|lung\s+|chest\s+|wall\s+|bladder\s+|pulmonary\s+|respiratory\s+|arterial\s+|vascular\s+|ventricular\s+|venous\s+|static\s+|dynamic\s+))\b(?<c>complian(?:ce|t))\b"
        + @"|\b(?<d>default(?:ed|ing|ers?))\b"
        + @"|\b(?<b>bizarre\s+(?:behaviou?rs?|conduct|manner))\b"
        + @"|\b(?<h>dr(?:inks|ank|inking)\s+(?:alcohol\s+)?(?:heavily|excessively|a\s+lot))\b",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectSaG5JudgmentalBehaviour(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in SaG5JudgmentalBehaviourRe.Matches(s.Body))
        {
            string message;
            string? fix = null;
            if (m.Groups["n"].Success || m.Groups["c"].Success)
            {
                message = "\"" + m.Value + "\" is judgmental. Model Answers use adherence: \"medication adherence\", \"adhered to\", \"reported difficulty adhering to ...\".";
                fix = m.Groups["n"].Success
                    ? "non-adherence"
                    : m.Value.EndsWith("t", StringComparison.OrdinalIgnoreCase) ? "adherent" : "adherence";
            }
            else if (m.Groups["d"].Success)
            {
                message = "\"" + m.Value + "\" is judgmental. State the fact neutrally, e.g. \"did not attend her follow-up appointment\".";
            }
            else if (m.Groups["b"].Success)
            {
                message = "\"" + m.Value + "\" is a judgmental label. Describe the objective behaviour documented in the case notes.";
            }
            else
            {
                message = "\"" + m.Value + "\" is vague and judgmental. State the source quantity and frequency, e.g. \"drinks ten to fifteen standard drinks daily\".";
            }
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major), message,
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length, FixSuggestion: fix);
        }
    }

    // ---------------------------------------------------------------------
    // G5-b — note-style "query X" / "?X" (DECISIONS §C.12): write
    // "suspected X" or "possible X", keeping the source's certainty. The
    // plural "queries" of the universal contact sentence never matches, and
    // the noun/verb uses ("any query regarding ...", "query whether ...",
    // "query the need ...") are excluded by the lookahead. A "?" that follows
    // a word character (an ordinary question mark) never matches.
    // ---------------------------------------------------------------------

    private static readonly Regex SaG5NoteStyleQueryRe = new(
        @"\bquery\s+(?:of\s+)?(?!(?:about|regarding|concerning|re|on|of|from|to|with|for|as|or|and|is|was|has|had|can|could|will|would|should|please|if|whether|that|this|these|those|the|a|an|his|her|their|its|my|your|our|any|raised|remains?)\b)(?<term>[A-Za-z][A-Za-z'’\-]*)"
        + @"|(?<![\w?!.)\]])\?\s?(?<term>[A-Za-z][A-Za-z'’\-]*)",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectSaG5NoteStyleQuery(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in SaG5NoteStyleQueryRe.Matches(s.Body))
        {
            var term = m.Groups["term"].Value;
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Minor),
                "\"" + m.Value + "\" is note-form. Write \"suspected " + term + "\" or \"possible " + term + "\", keeping the source's level of certainty.",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: "suspected " + term);
        }
    }

    // ---------------------------------------------------------------------
    // G5-c — premium register "fatigue", never "tiredness" (OWN-W-033,
    // DECISIONS §C.12). The existing both-mode "\btired\b" alternative cannot
    // see "tiredness" (no word boundary). Model Answer only: G-W-114 protects
    // a candidate's "tiredness".
    // ---------------------------------------------------------------------

    private static readonly Regex SaG5TirednessRe = new(@"\btiredness\b", RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectSaG5Tiredness(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in SaG5TirednessRe.Matches(s.Body))
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Minor),
                "\"" + m.Value + "\" is weaker than the canonical clinical term. Use \"fatigue\".",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: "fatigue");
    }

    // ---------------------------------------------------------------------
    // G5-h — logical/lexical misuse (audit #4, #14): asking the reader to
    // "confirm the working diagnosis of possible X" (a doubly hedged
    // diagnosis cannot be confirmed as such), and a CONDITION as the passive
    // subject of "assessed" ("A viral infection was assessed") — a patient is
    // assessed; a condition is diagnosed or suspected. Only an indefinite
    // article opens the subject, so "the severity of her disease was
    // assessed" and "He was assessed for hypercholesterolaemia" never match,
    // and "was assessed as/by/for/with/..." stays valid.
    // ---------------------------------------------------------------------

    private static readonly Regex SaG5ConfirmHedgedDiagnosisRe = new(
        @"\bconfirm(?:s|ed|ing)?\s+(?:(?:the|her|his|their|this|a)\s+)?(?:working|provisional)\s+(?:diagnosis|assessment)\s+of\s+(?:possible|probable|query|suspected|likely)\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex SaG5ConditionWasAssessedRe = new(
        @"\b(?:a|an)\s+(?:(?!(?:and|or|but|her|his|their|its|the|was|were|is|are|had|has|have|been|which|that|who)\b)[A-Za-z\-]+\s+){0,4}?(?:infection|disease|disorder|syndrome|illness)\s+was\s+assessed\b(?!\s+(?:as|by|for|using|with|at|in|on|to|under|via|through|during|over|and|clinically|further)\b)",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectSaG5LexicalMisuse(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in SaG5ConfirmHedgedDiagnosisRe.Matches(s.Body))
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Minor),
                "\"" + m.Value + "\" asks the reader to confirm a hedged working diagnosis. Request the action directly, e.g. assess the patient and clarify or exclude the possible diagnosis, keeping the source's certainty.",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length);
        foreach (Match m in SaG5ConditionWasAssessedRe.Matches(s.Body))
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Minor),
                "\"" + m.Value + "\" misuses \"assessed\": the patient is assessed; a condition is diagnosed or suspected. Keep the source certainty, e.g. \"The assessment was a viral infection.\"",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length);
    }

    // ---------------------------------------------------------------------
    // G5-e — medication comma chains the parser could not see (DECISIONS
    // §C.19): weight-based doses ("isoniazid, 5 mg/kg daily") and
    // alphanumeric brand names ("NovoMix30 25 units"). MedicationItemRe
    // rejects "mg/kg" (its "/" lookahead keeps concentrations such as
    // "110 g/L" out) and its drug token has no digits, so those lists were
    // never evaluated. SaG5MedicationItemRe recognises weight-based DOSE
    // units only (g/L, mg/dL, g/kg and mL/kg stay excluded; "mg/kg/day" and
    // "mL/kg/h" still fail the lookahead). This detector re-parses each
    // sentence and reports ONLY what the extended parse adds: a missing
    // drug-dose comma on a newly visible item, and a list-grammar verdict the
    // existing parse did not reach. Sentences whose parse is unchanged are
    // skipped, so the existing detector's findings are never duplicated.
    // ---------------------------------------------------------------------

    private const string SaG5DoseUnitPattern = @"(?:mg|mcg|µg|micrograms?|IU|units?)\/kg|" + DoseUnitPattern;

    private static readonly Regex SaG5MedicationItemRe = new(
        @"\b(?<drug>[A-Za-z][A-Za-z\-]{2,}\d*)(?<comma>,)?\s+" + MedicationCountWordPattern + @"(?<dose>" + MedicationDosePattern + @")\s*(?<unit>" + SaG5DoseUnitPattern + @")\b(?!\s*\/)"
        + @"|\b(?<drug>[A-Za-z][A-Za-z\-]{2,}\d*)(?<comma>,)?\s+(?<dose>" + MedicationDosePattern + @")" + MedicationFrequencyLookahead,
        RegexOptions.None);

    private static List<Match> SaG5MedicationItems(Regex itemRe, string sentence)
        => itemRe.Matches(sentence).Cast<Match>()
            .Where(m => !MedicationStopWords.Contains(m.Groups["drug"].Value))
            .ToList();

    // Mirrors the list-grammar decision in DetectMedicationListPunctuation
    // (OA2-16) for the Model Answer lane; null = no list finding.
    private static string? SaG5ListGrammarMessage(string sentence, List<Match> items)
    {
        if (items.Count < 2) return null;
        var separators = new List<string>();
        for (var i = 1; i < items.Count; i++)
            separators.Add(sentence[(items[i - 1].Index + items[i - 1].Length)..items[i].Index]);
        if (separators.Any(x => x.Length > 60 || NonListConnectiveRe.IsMatch(x))) return null;
        if (items.Count == 2)
        {
            return Regex.IsMatch(separators[0], @"\band\b", RegexOptions.IgnoreCase) && !separators[0].Contains(';')
                ? null
                : "Two medicines: write \"Drug, dose and Drug, dose\" (no semicolon; \"and\" before the second).";
        }
        var ok = separators.Take(separators.Count - 1).All(x => x.Contains(';'))
                 && Regex.IsMatch(separators[^1], @"\band\b", RegexOptions.IgnoreCase)
                 && !separators[^1].Contains(';');
        return ok
            ? null
            : "Three or more medicines: write \"Drug, dose; Drug, dose and Drug, dose\" (semicolons BETWEEN items, then \"and\" before the final item with NO semicolon before it).";
    }

    private static IEnumerable<LintFinding> DetectSaG5MedicationListExtendedDoses(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (var sentenceMatch in Regex.Matches(s.Body, @"[^.!?\n]+(?:\.(?=\d)[^.!?\n]*)*[.!?]?").Cast<Match>())
        {
            var sentence = sentenceMatch.Value;
            var oldItems = SaG5MedicationItems(MedicationItemRe, sentence);
            var newItems = SaG5MedicationItems(SaG5MedicationItemRe, sentence);
            if (newItems.Count == oldItems.Count
                && newItems.Zip(oldItems).All(p => p.First.Index == p.Second.Index && p.First.Length == p.Second.Length))
                continue;
            foreach (var item in newItems.Where(i => !i.Groups["comma"].Success && !oldItems.Any(o => o.Index == i.Index)))
            {
                var drug = item.Groups["drug"].Value;
                var dose = (item.Groups["dose"].Value + " " + item.Groups["unit"].Value).Trim();
                yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                    "Medication notation: put a comma between the medicine and its dose (\"" + drug + ", " + dose + "\").",
                    Quote: item.Value, Start: bodyOffset + sentenceMatch.Index + item.Index,
                    End: bodyOffset + sentenceMatch.Index + item.Index + item.Length,
                    FixSuggestion: drug + ", " + dose);
            }
            var message = SaG5ListGrammarMessage(sentence, newItems);
            if (message is null || SaG5ListGrammarMessage(sentence, oldItems) is not null) continue;
            var trimmed = sentence.Trim();
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major), message,
                Quote: trimmed.Length > 100 ? trimmed[..100] + "…" : trimmed);
        }
    }

    // ---------------------------------------------------------------------
    // G5-g — stranded formulation (audit #43, DECISIONS §C.19): the
    // formulation belongs with the drug ("salbutamol nebules, 5 mg"), never
    // after the dose ("Salbutamol, 5 mg, nebules"). The formulation noun must
    // follow the dose comma IMMEDIATELY, so "Metoclopramide, 10 mg, was
    // prescribed", "Atenolol, 50 mg, half a tablet each morning" and
    // "colchicine, 1 mg, and NSAIDs" never match.
    // ---------------------------------------------------------------------

    private static readonly Regex SaG5StrandedFormulationRe = new(
        @"\b(?<drug>[A-Za-z][A-Za-z\-]{2,}\d*),\s+(?<dose>" + MedicationDosePattern + @")\s*(?<unit>" + SaG5DoseUnitPattern + @"),\s+(?<form>nebules?|nebulisers?|nebulizers?|tablets?|capsules?|puffs?|patch(?:es)?|inhalers?|sachets?|ampoules?|vials?|drops|cream|gel|ointment|injections?|infusions?)\b",
        RegexOptions.None);

    private static IEnumerable<LintFinding> DetectSaG5StrandedFormulation(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in SaG5StrandedFormulationRe.Matches(s.Body))
        {
            var drug = m.Groups["drug"].Value;
            if (MedicationStopWords.Contains(drug)) continue;
            var form = m.Groups["form"].Value;
            var dose = m.Groups["dose"].Value + " " + m.Groups["unit"].Value;
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "The formulation is stranded after the dose (\"" + m.Value + "\"). Put it with the medicine name: \"" + drug + " " + form + ", " + dose + "\".",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: drug + " " + form + ", " + dose);
        }
    }

    // ---------------------------------------------------------------------
    // G5-f — NEW check id medication_frequency_conflict (audit #10, DECISIONS
    // §C.19 "one coherent frequency"): a multiple daily count followed
    // directly by ONE time-of-day slot ("glipizide, 5 mg twice daily each
    // morning") gives two incompatible regimens. Medication context is
    // required: either an explicit daily/a-day token, or a dose immediately
    // before the count, which keeps "woke twice at night" silent. Two slots
    // ("twice daily in the morning and at night", "each morning and
    // evening") are coherent and excluded; a following "and <next drug>" is
    // not a slot and does not excuse the conflict. No fix suggestion: the
    // correct regimen must come from the source.
    // ---------------------------------------------------------------------

    private static readonly Regex SaG5FrequencyConflictRe = new(
        @"\b(?<count>twice|two\s+times|three\s+times|four\s+times|bd|bid|tds|tid|qds|qid)\s+(?:(?<daily>(?:a|per|each|every)\s+day|daily)\s+)?(?<slot>(?:each|every|in\s+the)\s+(?:morning|evening|night)|at\s+(?:night|bedtime)|nightly|mane|nocte)\b(?!\s*,?\s*(?:and|or)\s+(?:(?:each|every|in\s+the|at)\s+)?(?:morning|afternoon|evening|night|bedtime|lunchtime|midday|noon)\b)",
        RegexOptions.IgnoreCase);

    private static readonly Regex SaG5DoseEndsHereRe = new(
        @"\d+(?:\.\d+)?\s*(?:mg|mcg|µg|micrograms?|g|IU|units?|mL|ml|tablets?|capsules?|puffs?)(?:\/kg)?\s*,?\s*$",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectSaG5MedicationFrequencyConflict(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in SaG5FrequencyConflictRe.Matches(s.Body))
        {
            if (!m.Groups["daily"].Success && !SaG5DoseEndsHereRe.IsMatch(s.Body[Math.Max(0, m.Index - 30)..m.Index])) continue;
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "\"" + m.Value + "\" gives two incompatible frequencies (a multiple daily dose and a single time of day). State the one source-supported regimen, e.g. \"5 mg twice daily\" or \"two 5 mg tablets each morning\", and never invent a dose or frequency.",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length);
        }
    }
}
