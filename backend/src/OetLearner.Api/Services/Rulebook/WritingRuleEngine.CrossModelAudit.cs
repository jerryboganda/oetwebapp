using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Cross-model audit remediation (owner FINAL decision, 17 Sep 2026). Two
/// hard-fail duplicate requests (Isabel Garcia, Sylvia Meadows) and an
/// invented monitoring request (Meadows) passed validator
/// <c>writing-rules.senior-assessor-audit.2026-09-16.2</c>.
/// <list type="bullet">
/// <item>OA6-01, HARD GLOBAL RULE: the same functional request never appears
/// in both the introduction and the closure. The detector compares REQUEST
/// CONCEPTS, not wording: <see cref="DetectCmaDuplicatedRequestConcept"/> is
/// composed under <c>no_duplicated_request</c>.</item>
/// <item>OA6-02: a closure request to monitor, check, repeat or test a
/// clinical parameter must be traceable to a case-note line that plans it
/// (<c>request_action_unsupported</c>, new check id).</item>
/// </list>
/// Both branches run for Model Answers only (implementation decisions §B); the
/// candidate lane is judged semantically by the grader.
/// </summary>
public sealed partial class WritingRuleEngine
{
    // ---------------------------------------------------------------------
    // OA6-01 — no_duplicated_request: request-concept duplication
    // ---------------------------------------------------------------------

    // People around the patient who are followed up, contacted or traced.
    private static readonly Regex CmaContactPersonsRe = new(
        @"\b(?:close\s+contacts?|contacts\b|(?:close\s+)?family\s+(?:and|&)\s+friends|household\s+(?:members|contacts)|close\s+family)",
        RegexOptions.IgnoreCase);

    // Following up those people. "contacts" (noun) and "contact me" are not
    // matches; "advise her close contacts" is a distinct action.
    private static readonly Regex CmaContactActionRe = new(
        @"\b(?:follow[- ]?up|follow(?:ing)?\s+up|contact(?:ing)?\b(?!\s+me\b)|trac(?:e|ing)|screen(?:ing)?|notify(?:ing)?)\b",
        RegexOptions.IgnoreCase);

    // Continuing care of the patient in the interim.
    private static readonly Regex CmaInterimCareRe = new(
        @"\b(?:ongoing|continued|continuing|interim)\s+(?:care|management|monitoring|follow[- ]?up|support)\b|\bmonitor(?:ing)?\b|\blook\s+after\b|\bcare\s+for\b",
        RegexOptions.IgnoreCase);

    // "... pending outpatient endocrinological review", "until her
    // endocrinologist's appointment": the event the interim care runs up to.
    private static readonly Regex CmaPendingAnchorRe = new(
        @"\b(?:pending|until|awaiting|before)\b[^.;]{0,80}?\b(?:review|appointment|assessment|clinic|consultation|follow[- ]?up)\b",
        RegexOptions.IgnoreCase);

    private static readonly HashSet<string> CmaAnchorGenericWords = new(StringComparer.Ordinal)
    {
        "pending", "until", "awaiting", "before", "review", "appointment", "assessment", "clinic", "consultation",
        "outpatient", "follow", "their", "there", "which", "where",
    };

    // The request part of an introduction mentions dressings ("... for regular
    // wound dressing"), not a history clause: a relative or time clause
    // ("whose graft has healed with negative pressure dressings") ends the
    // request span.
    private static readonly Regex CmaIntroDressingRequestRe = new(
        @"\b(?:request\w*|for|refer\w*)\b(?:(?!\b(?:who|whose|which|that|with|after|following)\b)[^.]){0,80}\bdressings?\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex CmaDressingRe = new Regex(@"\bdressings?\b", RegexOptions.IgnoreCase);

    private static readonly Regex CmaAssessmentRequestRe = new(
        @"\b(?:assess(?:es|ed|ing|ments?)?|evaluat(?:e|ion)|examine)\b|\b(?:your|for|could)\s+review\b",
        RegexOptions.IgnoreCase);

    // Confirming a diagnosis presupposes it and is what the requested
    // assessment delivers. "Clarify/establish the diagnosis" stays a distinct
    // objective (rules doc OA5 row: "clarify the possible diagnosis").
    private static readonly Regex CmaConfirmDiagnosisRe = new(
        @"\b(?:confirm|verify)\s+(?:the\s+|her\s+|his\s+|their\s+|a\s+)?(?:working\s+|provisional\s+|probable\s+|possible\s+|suspected\s+)?diagnos[ie]s\b",
        RegexOptions.IgnoreCase);

    private static HashSet<string> CmaAnchorStems(string text)
    {
        var stems = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match anchor in CmaPendingAnchorRe.Matches(text))
        {
            foreach (Match word in Regex.Matches(anchor.Value.ToLowerInvariant(), "[a-z]{5,}"))
            {
                if (!CmaAnchorGenericWords.Contains(word.Value))
                    stems.Add(word.Value.Length > 8 ? word.Value[..8] : word.Value);
            }
        }
        return stems;
    }

    internal static string? CmaDuplicatedRequestConcept(string intro, string closureRequest)
    {
        if (CmaContactPersonsRe.IsMatch(intro) && CmaContactActionRe.IsMatch(intro)
            && CmaContactPersonsRe.IsMatch(closureRequest) && CmaContactActionRe.IsMatch(closureRequest))
            return "the same follow-up of the patient's contacts";
        if (CmaInterimCareRe.IsMatch(intro) && CmaInterimCareRe.IsMatch(closureRequest)
            && CmaAnchorStems(intro).Overlaps(CmaAnchorStems(closureRequest)))
            return "the same interim care until the same review";
        if (CmaIntroDressingRequestRe.IsMatch(intro) && CmaDressingRe.IsMatch(closureRequest))
            return "the same dressing request";
        if (CmaAssessmentRequestRe.IsMatch(intro) && CmaConfirmDiagnosisRe.IsMatch(closureRequest))
            return "a diagnosis confirmation that the requested assessment already covers";
        return null;
    }

    private static IEnumerable<LintFinding> DetectCmaDuplicatedRequestConcept(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.BodyParagraphs.Count < 3) yield break;
        if (!ContactOfferMeaningRe.IsMatch(s.BodyParagraphs[^1])) yield break;
        var requestParagraph = s.BodyParagraphs[^2];
        if (!SaG4RequestMeaningRe.IsMatch(requestParagraph)) yield break;
        // One finding per defect: the verbatim and family branches own the
        // letters they already report.
        if (DetectNoDuplicatedRequest(rule, input, s).Any() || DetectSaG4DuplicatedRequestParaphrase(rule, input, s).Any()) yield break;

        var requestSentence = SplitSentences(requestParagraph).FirstOrDefault() ?? requestParagraph;
        var intro = s.BodyParagraphs[0];
        var reason = CmaDuplicatedRequestConcept(intro, requestSentence);
        if (reason is null) yield break;

        var offset = SaG4ParagraphOffset(s, requestParagraph);
        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
            "The closure request repeats the introduction's functional request in different words (" + reason + "): introduction \""
            + SaG4Excerpt(intro, 120) + "\" / closure \"" + SaG4Excerpt(requestSentence, 120)
            + "\". HARD GLOBAL RULE (owner, 17 Sep 2026): the same functional request never appears in both the introduction and the closure. Keep the request in the introduction and give the closure one DISTINCT, source-supported next step.",
            Quote: SaG4Excerpt(requestSentence, 120),
            Start: offset, End: offset + requestSentence.Length,
            FixSuggestion: "I would be grateful if you could [one distinct, source-supported next step].");
    }

    // ---------------------------------------------------------------------
    // OA6-02 — request_action_unsupported (new check id)
    // ---------------------------------------------------------------------

    private static readonly Regex CmaMonitorRequestVerbRe = new(
        @"\b(?:monitor(?:ing)?|re-?check(?:ing)?|check(?:ing)?|repeat(?:ing)?|measur(?:e|ing)|track(?:ing)?|test(?:ing)?)\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex CmaNotePlansMonitoringRe = new(
        @"\b(?:monitor\w*|check\w*|repeat\w*|measur\w*|test\w*|review\w*|follow\w*|recheck\w*|re-check\w*|maintain\w*|observ\w*|track\w*|reassess\w*)\b",
        RegexOptions.IgnoreCase);

    // A monitored object and the words a case note uses for it.
    private static readonly (string Label, Regex Letter, Regex Note)[] CmaMonitoredObjects =
    [
        ("blood pressure", new Regex(@"\bblood\s+pressures?\b|\bBP\b", RegexOptions.IgnoreCase), new Regex(@"\bblood\s+pressures?\b|\bBP\b", RegexOptions.IgnoreCase)),
        ("electrolytes", new Regex(@"\belectrolytes?\b|\bsodium\b|\bpotassium\b", RegexOptions.IgnoreCase), new Regex(@"\belectrolytes?\b|\bsodium\b|\bpotassium\b|\bNa\b|\bK\b|E\/U\/C|\bU&Es?\b|\bUECs?\b", RegexOptions.IgnoreCase)),
        ("renal function", new Regex(@"\brenal\s+function\b|\bkidney\s+function\b|\bcreatinine\b|\beGFR\b", RegexOptions.IgnoreCase), new Regex(@"\brenal\b|\bkidney\b|\bcreatinine\b|\beGFR\b|\bU&Es?\b|E\/U\/C", RegexOptions.IgnoreCase)),
        ("INR", new Regex(@"\bINR\b", RegexOptions.IgnoreCase), new Regex(@"\bINR\b", RegexOptions.IgnoreCase)),
        ("haemoglobin", new Regex(@"\bhaemoglobin\b|\bhemoglobin\b|\bHb\b", RegexOptions.IgnoreCase), new Regex(@"\bhaemoglobin\b|\bhemoglobin\b|\bHb\b", RegexOptions.IgnoreCase)),
        ("blood glucose", new Regex(@"\b(?:blood\s+)?(?:glucose|sugars?)\b|\bBSLs?\b|\bBGLs?\b|\bHbA1c\b", RegexOptions.IgnoreCase), new Regex(@"\bglucose\b|\bsugars?\b|\bBSLs?\b|\bBGLs?\b|\bHbA1c\b", RegexOptions.IgnoreCase)),
        ("weight", new Regex(@"\bweight\b", RegexOptions.IgnoreCase), new Regex(@"\bweight\b|\bBMI\b", RegexOptions.IgnoreCase)),
        ("symptoms", new Regex(@"\bsymptoms?\b", RegexOptions.IgnoreCase), new Regex(@"\bsymptoms?\b", RegexOptions.IgnoreCase)),
        ("medications", new Regex(@"\bmedications?\b|\bmedicines?\b|\bpolypharmacy\b", RegexOptions.IgnoreCase), new Regex(@"\bmedications?\b|\bmedicines?\b|\bmeds\b|\bpolypharmacy\b|\bdrugs?\b", RegexOptions.IgnoreCase)),
        ("liver function", new Regex(@"\bliver\s+function\b|\bLFTs?\b", RegexOptions.IgnoreCase), new Regex(@"\bliver\b|\bLFTs?\b", RegexOptions.IgnoreCase)),
        ("lipids", new Regex(@"\blipids?\b|\bcholesterol\b", RegexOptions.IgnoreCase), new Regex(@"\blipids?\b|\bcholesterol\b", RegexOptions.IgnoreCase)),
        ("thyroid function", new Regex(@"\bthyroid\b|\bTSH\b", RegexOptions.IgnoreCase), new Regex(@"\bthyroid\b|\bTSH\b", RegexOptions.IgnoreCase)),
        ("full blood count", new Regex(@"\bfull\s+blood\s+count\b|\bFBC\b", RegexOptions.IgnoreCase), new Regex(@"\bfull\s+blood\s+count\b|\bFBC\b", RegexOptions.IgnoreCase)),
        ("oxygen saturation", new Regex(@"\boxygen\s+saturations?\b|\bsaturations?\b", RegexOptions.IgnoreCase), new Regex(@"\bsaturations?\b|\bSpO2\b|\boxygen\b", RegexOptions.IgnoreCase)),
    ];

    internal static IReadOnlyList<string> CmaUnsupportedMonitoringRequests(string requestParagraph, string caseNotes)
    {
        var noteLines = caseNotes.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var unsupported = new List<string>();
        foreach (var sentence in SplitSentences(requestParagraph))
        {
            foreach (Match verb in CmaMonitorRequestVerbRe.Matches(sentence))
            {
                var start = Math.Max(0, verb.Index - 40);
                var window = sentence[start..Math.Min(sentence.Length, verb.Index + verb.Length + 70)];
                foreach (var (label, letterRe, noteRe) in CmaMonitoredObjects)
                {
                    if (!letterRe.IsMatch(window)) continue;
                    var planned = noteLines.Any(line => CmaNotePlansMonitoringRe.IsMatch(line) && noteRe.IsMatch(line));
                    var item = verb.Value.ToLowerInvariant() + " " + label;
                    if (!planned && !unsupported.Contains(item)) unsupported.Add(item);
                }
            }
        }
        return unsupported;
    }

    private static IEnumerable<LintFinding> DetectCmaRequestActionUnsupported(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || string.IsNullOrWhiteSpace(input.CaseNotesText)) yield break;
        if (s.BodyParagraphs.Count < 3 || !ContactOfferMeaningRe.IsMatch(s.BodyParagraphs[^1])) yield break;
        var requestParagraph = s.BodyParagraphs[^2];
        if (!SaG4RequestMeaningRe.IsMatch(requestParagraph)) yield break;
        var unsupported = CmaUnsupportedMonitoringRequests(requestParagraph, input.CaseNotesText);
        if (unsupported.Count == 0) yield break;

        var offset = SaG4ParagraphOffset(s, requestParagraph);
        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
            "The closure asks the reader to " + string.Join(", ", unsupported)
            + ", but no case-note line plans that monitoring. A Model Answer never invents a clinical request because it sounds reasonable (owner, 17 Sep 2026: Sylvia Meadows \"monitor ... symptoms and electrolytes\"). Request only an action the source plans.",
            Quote: SaG4Excerpt(requestParagraph, 120),
            Start: offset, End: offset + requestParagraph.Length,
            FixSuggestion: "I would be grateful if you could [an action the case notes plan].");
    }
}
