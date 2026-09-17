using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Senior Assessor Release Audit (owner, 16 Sep 2026), group G4: request
/// function, closure request, discharge introduction, background placement,
/// letter-type function and unidiomatic request wording.
/// <list type="bullet">
/// <item>Every detector in this file is MODEL ANSWER ONLY (implementation
/// decisions §B). Candidate letters are assessed semantically by the grader and
/// never reach these branches.</item>
/// <item>A detector that EXTENDS an existing check id emits only the new
/// shapes and stands down whenever the existing detector already reports the
/// letter, so one defect is never reported twice under the same check id.</item>
/// </list>
/// </summary>
public sealed partial class WritingRuleEngine
{
    // ---------------------------------------------------------------------
    // Shared helpers (G4)
    // ---------------------------------------------------------------------

    // A reader request in any wording: "I would be grateful", "I would
    // appreciate", "Please review ...", "could you". "Please find enclosed" and
    // the contact template's "please do not hesitate" are not requests.
    private static readonly Regex SaG4RequestMeaningRe = new(
        @"\bwould\s+(?:be\s+(?:most\s+|very\s+)?grateful|appreciate)\b|\bplease\s+(?!find\b|do\s+not\b)[a-z]+\b|\b(?:could|would)\s+you\b",
        RegexOptions.IgnoreCase);

    // OA2-03 exception: a pure information task that explicitly requests no
    // action never needs a request.
    private static readonly Regex SaG4TaskNoActionRe = new(
        @"\bno\s+(?:specific\s+|further\s+)?action\s+is\s+(?:required|requested)\b|\bfor\s+(?:your\s+)?information\s+only\b",
        RegexOptions.IgnoreCase);

    private static bool SaG4TaskRequestsNoAction(WritingLintInput input)
        => !string.IsNullOrEmpty(input.TaskText) && SaG4TaskNoActionRe.IsMatch(input.TaskText);

    private static string SaG4Excerpt(string text, int max)
        => text.Length > max ? text[..max] + "…" : text;

    private static int? SaG4ParagraphOffset(LetterStructure s, string paragraph)
    {
        var index = s.Body.IndexOf(paragraph, StringComparison.Ordinal);
        return index < 0 ? null : BodyOffset(s) + index;
    }

    // ---------------------------------------------------------------------
    // G4a — no_duplicated_request: semantic paraphrase of the introduction's
    // request in the closure (Mathis, Irving, Khaze, Karen Smith, Barry Jones,
    // Collister, Cochrane, Greenbaum, Seymour, McDonald, Weir, Newton, Evans,
    // Foster, Karen Jackson, Walter, Woods, Sullivne, Sarah Miller). The
    // existing detector only sees a 4-word verbatim run.
    // ---------------------------------------------------------------------

    // One introductory adverbial before the request ("Given ...,", "At Mrs
    // Woods's request,") is not part of the requested action.
    private static readonly Regex SaG4RequestLeadInRe = new(
        @"^[A-Z][^,.;]{1,120},\s+(?=I\s+would\b|please\b)");

    private static readonly Regex SaG4PatientRequestRe = new(
        @"\bat\s+(?:his|her|their|(?:Mr|Mrs|Ms|Miss)\s+[A-Z][\w'’-]*)\s+request\b,?");

    // Request families. A compound noun such as "medication review" or
    // "endocrinologist review" is deliberately NOT an assess match, the
    // adjective "surgical" is not "surgery", and the history forms "admitted",
    // "managed" and "treated" are not requests.
    private static readonly (string Label, Regex Pattern)[] SaG4RequestFamilies =
    [
        ("#assess", new Regex(@"\b(?:assess(?:es|ed|ing|ments?)?|evaluat(?:e|ion)|examine)\b|\b(?:your|for|could)\s+review\b|\breview(?=\s+(?:him|her|Mr|Mrs|Ms|Miss)\b)|\bsee(?=\s+(?:him|her|Mr|Mrs|Ms|Miss)\b)", RegexOptions.IgnoreCase)),
        ("#manage", new Regex(@"\b(?:manag(?:e|es|ing|ement)|advi(?:se|ce)|guidance|treatment|opinion)\b", RegexOptions.IgnoreCase)),
        ("#admit", new Regex(@"\b(?:admit|admission)\b", RegexOptions.IgnoreCase)),
        ("#biopsy", new Regex(@"\bbiops(?:y|ies)\b", RegexOptions.IgnoreCase)),
        ("#investigate", new Regex(@"\binvestigat(?:e|ions?)\b", RegexOptions.IgnoreCase)),
        ("#counsel", new Regex(@"\bcounsel(?:l)?ing\b", RegexOptions.IgnoreCase)),
        ("#surgery", new Regex(@"\bsurgery\b", RegexOptions.IgnoreCase)),
    ];

    // Function words plus generic qualifiers that never distinguish one
    // request from another.
    private static readonly HashSet<string> SaG4RequestStopWords = new(StringComparer.Ordinal)
    {
        "i", "am", "writing", "to", "refer", "referral", "request", "requesting", "would", "be", "grateful", "if",
        "you", "could", "appreciate", "for", "your", "the", "a", "an", "of", "and", "on", "in", "with", "at",
        "earliest", "convenience", "his", "her", "him", "their", "them", "this", "these", "that", "who", "which",
        "has", "have", "is", "was", "are", "presents", "presented", "regarding", "including", "as", "by", "from",
        "given", "urgent", "urgently", "please", "kindly", "mr", "mrs", "ms", "miss", "dr", "patient", "practice", "e",
        "further", "ongoing", "long-term", "appropriate", "full", "specialist", "possible", "query", "suspected",
        "diagnosis", "care", "condition", "review",
    };

    private static readonly string[] SaG4GenericRequestPair = ["#assess", "#manage"];

    private static (HashSet<string> Labels, List<string> Tokens) SaG4RequestSignature(string text, PatientNameForms? name)
    {
        foreach (var (label, pattern) in SaG4RequestFamilies)
            text = pattern.Replace(text, " " + label + " ");
        var drop = new HashSet<string>(SaG4RequestStopWords, StringComparer.Ordinal);
        var first = name?.First?.ToLowerInvariant();
        if (!string.IsNullOrEmpty(first)) drop.Add(first);
        var last = name?.Last?.ToLowerInvariant();
        if (!string.IsNullOrEmpty(last))
        {
            drop.Add(last);
            drop.Add(last + "'s");
            drop.Add(last + "’s");
            drop.Add(last + "'");
            drop.Add(last + "’");
        }
        var tokens = Regex.Matches(text.ToLowerInvariant(), @"[a-z#][a-z#0-9'’-]*")
            .Select(m => m.Value)
            .Where(t => !drop.Contains(t) && !t.Any(char.IsDigit))
            .ToList();
        var labels = tokens.Where(t => t[0] == '#').ToHashSet(StringComparer.Ordinal);
        return (labels, tokens);
    }

    // Adjacent token pairs carrying exactly ONE request label, e.g.
    // "neurological #assess", "reconstructive #surgery", "#manage anxiety".
    private static HashSet<string> SaG4OneLabelBigrams(List<string> tokens)
    {
        var bigrams = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            if ((tokens[i][0] == '#') != (tokens[i + 1][0] == '#'))
                bigrams.Add(tokens[i] + " " + tokens[i + 1]);
        }
        return bigrams;
    }

    // Mirror of the existing 4-gram verbatim branch in
    // DetectNoDuplicatedRequest: when it already reports the letter, this
    // extension stays silent.
    private static bool SaG4VerbatimRequestRepeat(LetterStructure s)
    {
        var introWords = Regex.Matches(s.BodyParagraphs[0].ToLowerInvariant(), @"[a-z']+").Select(m => m.Value).ToArray();
        var closureNorm = " " + Regex.Replace(string.Join(" ", s.BodyParagraphs.TakeLast(2)).ToLowerInvariant(), @"[^a-z' ]+", " ") + " ";
        for (var i = 0; i + 4 <= introWords.Length; i++)
        {
            if (closureNorm.Contains(" " + string.Join(' ', introWords.Skip(i).Take(4)) + " ", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static IEnumerable<LintFinding> DetectSaG4DuplicatedRequestParaphrase(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (s.BodyParagraphs.Count < 3) yield break;
        if (SaG4VerbatimRequestRepeat(s)) yield break;
        // A missing request paragraph is closure_request_paragraph's job.
        if (!ContactOfferMeaningRe.IsMatch(s.BodyParagraphs[^1])) yield break;
        var requestParagraph = s.BodyParagraphs[^2];
        if (!SaG4RequestMeaningRe.IsMatch(requestParagraph)) yield break;

        var requestSentence = SplitSentences(requestParagraph).FirstOrDefault() ?? requestParagraph;
        var closure = SaG4PatientRequestRe.Replace(SaG4RequestLeadInRe.Replace(requestSentence, string.Empty), " ");
        var intro = s.BodyParagraphs[0];
        var name = ResolvePatientName(input, s);
        var (introLabels, introTokens) = SaG4RequestSignature(intro, name);
        var (closureLabels, closureTokens) = SaG4RequestSignature(closure, name);
        var shared = introLabels.Where(x => closureLabels.Contains(x)).OrderBy(x => x, StringComparer.Ordinal).ToList();

        string? reason = null;
        if (shared.Count >= 2)
        {
            reason = "the same " + string.Join(" and ", shared.Select(x => x[1..])) + " request";
        }
        else if (shared.Count == 1 && (shared[0] == "#admit" || shared[0] == "#biopsy"))
        {
            reason = "the same " + shared[0][1..] + " request";
        }
        else if (shared.Count == 1)
        {
            var sharedObject = SaG4OneLabelBigrams(introTokens).Intersect(SaG4OneLabelBigrams(closureTokens)).FirstOrDefault();
            if (sharedObject is not null)
                reason = "the same request object \"" + sharedObject.Replace("#", string.Empty) + "\"";
        }
        // A bare "assess and advise/manage" closure after an introduction that
        // already asks for assessment or management (Seymour, Newton, Foster).
        if (reason is null
            && closureLabels.SetEquals(SaG4GenericRequestPair)
            && closureTokens.All(t => t[0] == '#')
            && introLabels.Overlaps(SaG4GenericRequestPair))
        {
            reason = "a generic assessment-and-advice closure that repeats the introduction's purpose";
        }
        if (reason is null) yield break;

        var offset = SaG4ParagraphOffset(s, requestParagraph);
        yield return new LintFinding(rule.Id, ModeSeverity(input, rule.Severity),
            "The closure request repeats the introduction's request (" + reason + "): introduction \"" + SaG4Excerpt(intro, 120)
            + "\" / closure \"" + SaG4Excerpt(requestSentence, 120)
            + "\". The introduction carries the primary purpose; the closure must carry a DISTINCT remaining action supported by the task (e.g. \"consider MRI if clinically indicated\"), never the same assessment, management, admission or biopsy request reworded (OA-07, OA2-05; senior assessor audit 16 Sep 2026).",
            Quote: SaG4Excerpt(requestSentence, 120),
            Start: offset, End: offset + requestSentence.Length,
            FixSuggestion: "I would be grateful if you could [one distinct, source-supported next step, e.g. consider MRI if clinically indicated].");
    }

    // ---------------------------------------------------------------------
    // G4b — closure_request_paragraph: no canonical "I would be grateful if you
    // could ..." request paragraph before the contact offer (Ling Wu "Please
    // monitor ...", Yanlin Ma, Janet Pristiely, Sandra Peterson; also Betty
    // Johnson "Please review ...", Sullivne "..., please see him ...", live
    // Lucy Clarke "I counselled ... and would be grateful ..."). The existing
    // detector goes silent when no paragraph matches ClosureRequestRe and
    // accepts "please assess/review/see/monitor" as a request.
    // ---------------------------------------------------------------------

    // One introductory adverbial ("Given ...,", "At Mrs Woods's request,",
    // "On discharge,") may precede the canonical request family.
    private static readonly Regex SaG4CanonicalRequestOpenRe = new(
        @"^(?:[A-Z][^,.;]{1,120},\s+)?I\s+would\s+(?:be\s+(?:most\s+|very\s+)?grateful|appreciate)\b");

    // Other letters (LT-OT) are often information letters with no reader
    // action, so they are out of scope.
    private static readonly HashSet<string> SaG4RequestBearingLetterTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "routine_referral", "urgent_referral", "discharge", "transfer_letter", "non_medical_referral",
    };

    private static IEnumerable<LintFinding> DetectSaG4CanonicalRequestParagraphMissing(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        var count = s.BodyParagraphs.Count;
        if (count < 3 || !SaG4RequestBearingLetterTypes.Contains(input.LetterType ?? string.Empty)) yield break;
        if (SaG4TaskRequestsNoAction(input)) yield break;
        var contact = s.BodyParagraphs[count - 1];
        // A request merged into the contact paragraph stays with the existing branch.
        if (!ContactOfferMeaningRe.IsMatch(contact) || SaG4RequestMeaningRe.IsMatch(contact)) yield break;

        var beforeContact = s.BodyParagraphs[count - 2];
        var opener = SplitSentences(beforeContact).FirstOrDefault() ?? beforeContact;
        // The existing detector already reports a request stranded mid-letter,
        // a request that does not start its paragraph, or a request merged
        // into the final paragraph. Only speak when it is silent.
        var existingRequestIndex = s.BodyParagraphs.FindLastIndex(p => ClosureRequestRe.IsMatch(p));
        if (existingRequestIndex >= 0 && !(existingRequestIndex == count - 2 && ClosureRequestRe.IsMatch(opener))) yield break;
        if (SaG4CanonicalRequestOpenRe.IsMatch(opener)) yield break;

        var offset = SaG4ParagraphOffset(s, beforeContact);
        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
            "There is no canonical request paragraph before the contact offer. The paragraph immediately before the contact sentence must START with the task-specific reader action in the \"I would be grateful if you could ...\" family. A \"Please monitor/review/see ...\" instruction, a request merged into a clinical sentence, or no request at all does not meet OA-06/OA2-03 (senior assessor audit 16 Sep 2026: Ling Wu, Yanlin Ma, Janet Pristiely, Sandra Peterson).",
            Quote: SaG4Excerpt(opener, 90),
            Start: offset, End: offset + opener.Length,
            FixSuggestion: "I would be grateful if you could [the source-supported action the task requires].");
    }

    // ---------------------------------------------------------------------
    // G4c — intro_purpose_vague: a discharge/"introduce" introduction that
    // states admission and discharge facts but no ongoing-care request
    // (Pristiely, Peterson, Betty Johnson). The existing branch needs a
    // request elsewhere in the letter and accepts the history noun
    // "admission" as an action.
    // ---------------------------------------------------------------------

    private static readonly Regex SaG4IntroduceOpeningRe = new(@"^I am writing to introduce\b");

    // "admission"/"admitted" deliberately do NOT count as a reader action.
    // Any other reader action phrase is accepted (precision over recall).
    private static readonly Regex SaG4OngoingCareActionRe = new(
        @"\b(?:request(?:s|ing)?|ask(?:ing)?\s+you\s+to|seek(?:ing)?\s+your|advis(?:e|ing)\s+you\s+(?:of|on)|follow-up|monitor(?:ing)?|review|hand(?:ing)?\s+over|continu(?:e|ation\s+of))\b|\b(?:ongoing|continued|continuing|further)\s+(?:care|management|monitoring|treatment)\b",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectSaG4DischargeIntroWithoutCareRequest(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (s.BodyParagraphs.Count < 2) yield break;
        var intro = s.BodyParagraphs[0];
        // Shapes the existing intro_purpose_vague branches already report.
        if (VagueWorkingAssessmentRe.IsMatch(intro)) yield break;
        if (s.BodyParagraphs.Skip(1).Any(p => ClosureRequestRe.IsMatch(p)) && !IntroActionMarkerRe.IsMatch(intro)) yield break;

        var isDischarge = string.Equals(input.LetterType, "discharge", StringComparison.OrdinalIgnoreCase);
        if (!isDischarge && !SaG4IntroduceOpeningRe.IsMatch(intro)) yield break;
        if (SaG4TaskRequestsNoAction(input)) yield break;
        if (SaG4OngoingCareActionRe.IsMatch(intro)) yield break;

        var offset = SaG4ParagraphOffset(s, intro);
        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
            "A discharge or \"introduce\" introduction must state the ongoing-care action required of the reader, not only the admission and discharge facts (OA-01, OA2-03; senior assessor audit 16 Sep 2026: Pristiely, Peterson, Betty Johnson). Example: \"I am writing to update you regarding Ms Garcia, who was discharged home today after treatment for bacterial meningitis, and to request your ongoing monitoring during her recovery.\"",
            Quote: SaG4Excerpt(intro, 100),
            Start: offset, End: offset + intro.Length,
            FixSuggestion: "..., and to request your ongoing [monitoring/management] of [the source-supported issue].");
    }

    // ---------------------------------------------------------------------
    // G4d — background_paragraph_placement: remote history opening a body
    // paragraph that comes before current-episode content (Sorocco, Evans,
    // Foster, Browne, pack Lucy Clarke), or background mixed into a
    // current-episode paragraph (Macalaque, Mary Clarke, Bennet, Hall,
    // Newton, Poulos). The existing branch scans BodyParagraphs[1] only.
    // ---------------------------------------------------------------------

    // Background: habits, family and social history, allergies, labelled
    // history and childhood history. "smoking cessation", "alcohol intake",
    // "has had X since YEAR" and "diagnosed with X" are deliberately absent
    // (advice given today and referred-condition history).
    private static readonly Regex SaG4BackgroundSentenceRe = new(
        @"\b(?:smokes|smoked|has\s+never\s+smoked|does\s+not\s+smoke|continues\s+to\s+smoke|ex-smok\w*|cigarettes?|tobacco|units\s+of\s+alcohol|standard\s+drinks|glasses\s+of\s+wine|beers?\s+(?:daily|weekly|a\s+day)|drinks\s+(?:heavily|alcohol|socially)|does\s+not\s+drink|rarely\s+drinks)\b"
        + @"|\bfamily\s+history\b"
        + @"|\b(?:his|her)\s+(?:mother|father|brother|sister|parents|uncle|aunt|grandmother|grandfather)\s+(?:died\b|(?:has|had)\s+(?:a\s+history\s+of\s+)?(?:[a-z]+['’]s\s+disease|diabetes|asthma|hypertension|gout|cancer|carcinoma|migraines?|depression|epilepsy|dementia|heart\s+disease|stroke|thrombosis|glaucoma|eczema|arthritis|osteoporosis|[a-z]+\s+cancer)\b)"
        + @"|\bfather\s+who\s+died\b"
        + @"|\blives\s+(?:alone|with|at\s+home)\b|\bis\s+(?:married|divorced|widowed)\b|\bwas\s+widowed\b|\ba\s+widow(?:er)?\b|\bmain\s+carer\b|\bretired\s+[a-z]+|\bworks\s+as\b|\benjoys\b|\bno\s+children\b"
        + @"|\ballerg(?:y|ies)\b|\ballergic\b(?!\s+reactions?\b)"
        + @"|\b(?:past\s+)?(?:medical|surgical|social|obstetric|psychiatric)\s+history\b|\bpast\s+history\b|\bhistory\s+includes\b|\bhas\s+a\s+(?:past\s+)?history\s+of\b|\bbackground\s+of\b"
        + @"|\bin\s+(?:infancy|childhood)\b|\bat\s+the\s+age\s+of\s+\w+\b|\bprevious\s+hospital\s+admissions?\b|\brisk\s+factors\s+include\b",
        RegexOptions.IgnoreCase);

    // A chronic condition the patient "has". It is background only when the
    // introduction does not name it (a referred condition is the current
    // problem, never background), and never when it is a recent complaint
    // ("has had depression for six weeks").
    private static readonly Regex SaG4ConditionHistoryRe = new(
        @"\b(?:has|had)\s+(?:had\s+)?(?:type\s+(?:one|two|1|2)\s+)?(?<cond>hypertension|asthma|depression|osteoporosis|hay\s?fever|eczema|migraines?|diabetes|chronic\s+obstructive\s+pulmonary\s+disease|Alzheimer['’]s\s+dementia|hypercholesterolaemia|hyperlipidaemia)\b(?!\s+symptoms?\b|\s+for\s+(?:the\s+(?:past|last)\s+)?(?:[\w-]+\s+){0,3}?(?:days?|weeks?|months?)\b)",
        RegexOptions.IgnoreCase);

    private static readonly Regex SaG4CurrentEpisodeRe = new(
        @"\btoday\b|\bOn\s+examination\b|\bExamination\s+(?:today\s+)?(?:showed|shows|revealed|reveals|found|demonstrated|confirmed)\b|\b(?:provisional|working)\s+diagnosis\b|\b(?:The|My)\s+assessment\s+is\b|\bI\s+(?:advised|counselled|commenced|prescribed)\b|\bpresents\s+with\b",
        RegexOptions.IgnoreCase);

    // "now"/"currently" mark the current episode ("Ms Bennet now reports
    // dizziness", "the ulcer has now recurred"), except in a medication or
    // social/occupational status statement ("He currently takes no other
    // medication.", "He is currently unemployed.", "He is now retired."),
    // which belongs to background.
    private static readonly Regex SaG4CurrentAdverbRe = new(@"\bnow\b|\bcurrently\b|\bat\s+present\b", RegexOptions.IgnoreCase);

    private static readonly Regex SaG4MedicationMentionRe = new(
        @"\b(?:takes|taking|medications?|mg|mcg|unemployed|employed|retired|works?|working|job|lives?|living|student|studying|married|single|divorced|separated|widowed|pensioner|carer|school|university|overweight|obese)\b",
        RegexOptions.IgnoreCase);

    // 'B' = pure background sentence, 'C' = pure current-episode sentence,
    // 'N' = neither, or both at once (a mixed sentence is never evidence).
    private static char SaG4ClassifySentence(string sentence, string intro)
    {
        var current = SaG4CurrentEpisodeRe.IsMatch(sentence)
            || (SaG4CurrentAdverbRe.IsMatch(sentence) && !SaG4MedicationMentionRe.IsMatch(sentence));
        var background = SaG4BackgroundSentenceRe.IsMatch(sentence);
        if (!background)
        {
            foreach (Match m in SaG4ConditionHistoryRe.Matches(sentence))
            {
                if (intro.IndexOf(m.Groups["cond"].Value, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    background = true;
                    break;
                }
            }
        }
        if (background == current) return 'N';
        return background ? 'B' : 'C';
    }

    private static LintFinding SaG4BackgroundFinding(OetRule rule, WritingLintInput input, LetterStructure s,
        string paragraph, string backgroundSentence, string message)
    {
        var offset = SaG4ParagraphOffset(s, paragraph);
        var within = paragraph.IndexOf(backgroundSentence, StringComparison.Ordinal);
        int? start = offset is null || within < 0 ? null : offset + within;
        return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major), message,
            Quote: SaG4Excerpt(backgroundSentence, 120),
            Start: start, End: start + backgroundSentence.Length,
            FixSuggestion: "Move \"" + SaG4Excerpt(backgroundSentence, 80) + "\" into a dedicated background paragraph immediately before the closure request.");
    }

    private static IEnumerable<LintFinding> DetectSaG4BackgroundPlacementInBody(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        var n = s.BodyParagraphs.Count;
        if (n < 4) yield break;
        // The existing branch already reports background in the opening
        // current-problem paragraph: one placement finding per letter.
        if (BackgroundMarkerRe.IsMatch(s.BodyParagraphs[1])) yield break;

        var closureStart = ContactOfferMeaningRe.IsMatch(s.BodyParagraphs[n - 1]) && SaG4RequestMeaningRe.IsMatch(s.BodyParagraphs[n - 2])
            ? n - 2
            : n - 1;
        var intro = s.BodyParagraphs[0];
        var sentences = s.BodyParagraphs.Select(p => SplitSentences(p)).ToList();

        for (var i = 1; i < closureStart; i++)
        {
            string? background = null;
            string? current = null;
            foreach (var sentence in sentences[i])
            {
                var role = SaG4ClassifySentence(sentence, intro);
                if (role == 'B' && background is null) background = sentence;
                if (role == 'C' && current is null) current = sentence;
            }

            // (M) background mixed into a current-episode paragraph.
            if (background is not null && current is not null)
            {
                yield return SaG4BackgroundFinding(rule, input, s, s.BodyParagraphs[i], background,
                    "This paragraph mixes background (\"" + SaG4Excerpt(background, 90) + "\") with current-episode findings or management (\""
                    + SaG4Excerpt(current, 90) + "\"). Keep this paragraph on the current episode and place medical, family, social, habit and allergy background in a dedicated paragraph immediately before the closure (OA2-12; senior assessor audit 16 Sep 2026).");
                yield break;
            }

            // (O) background opens a paragraph that precedes current-episode content.
            if (sentences[i].Count == 0 || SaG4ClassifySentence(sentences[i][0], intro) != 'B') continue;
            for (var j = i + 1; j < closureStart; j++)
            {
                var later = sentences[j].FirstOrDefault(x => SaG4ClassifySentence(x, intro) == 'C');
                if (later is null) continue;
                yield return SaG4BackgroundFinding(rule, input, s, s.BodyParagraphs[i], sentences[i][0],
                    "Background (\"" + SaG4Excerpt(sentences[i][0], 90) + "\") opens a body paragraph that comes before current-episode content (\""
                    + SaG4Excerpt(later, 90) + "\"). Lead with the current presentation and move the background to a dedicated paragraph immediately before the closure (OA2-12; urgent referrals keep today's symptoms, examination and immediate assessment together first).");
                yield break;
            }
        }
    }

    // ---------------------------------------------------------------------
    // G4e — letter_type_function_mismatch (NEW): the case notes or the exact
    // task prove an urgent function while the task is catalogued as a routine
    // referral (Cochrane: Emergency Registrar, "needs admission ... for
    // stabilisation"; OET test 14 / Brian Morgan: Emergency Department
    // Medical Officer, "urgent surgical opinion; send to hospital"). Suspected
    // cancer alone is never an urgency marker (house rule 0).
    // ---------------------------------------------------------------------

    // Present-tense plan lines only; a negated line ("No urgent systemic
    // signs", "non-urgent referral") never matches, and past events
    // ("required ICU admission", "stabilised by day 3") are not plans.
    private static readonly Regex SaG4NotesUrgentPlanRe = new(
        @"^(?!.*\b(?:no|not|non)[\s-]+urgent).*?(?<evidence>\burgent(?:ly)?\s+(?:referral|assessment|surgical\s+(?:opinion|assessment|review)|opinion|admission|review|evaluation|treatment)\b|\b(?:needs|requires)\s+(?:urgent\s+)?(?:hospital\s+)?admission\b|\bfor\s+stabilisation\b|\bsend\s+to\s+hospital\b|\bfor\s+acute\s+(?:management|assessment|admission)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    // The RECIPIENT of the task letter is an emergency role. It must follow
    // "to", so a past ED attendance in the task narrative never matches.
    private static readonly Regex SaG4TaskUrgentRecipientRe = new(
        @"\bto\s+(?:the\s+)?(?:[A-Z][A-Za-z]*\s+){0,3}?(?:Emergency\s+(?:Department|Registrar)|ED\s+(?:Medical\s+Officer|Registrar)|A&E|Admitting\s+Officer|Duty\s+Registrar)\b");

    private static IEnumerable<LintFinding> DetectSaG4LetterTypeFunctionMismatch(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (!string.Equals(input.LetterType, "routine_referral", StringComparison.OrdinalIgnoreCase)) yield break;

        string? evidence = null;
        if (!string.IsNullOrEmpty(input.CaseNotesText))
        {
            var notes = input.CaseNotesText;
            var m = SaG4NotesUrgentPlanRe.Match(notes);
            if (m.Success)
            {
                var lineEnd = notes.IndexOf('\n', m.Index);
                evidence = (lineEnd < 0 ? notes[m.Index..] : notes[m.Index..lineEnd]).Trim();
            }
        }
        if (evidence is null && !string.IsNullOrEmpty(input.TaskText))
        {
            var m = SaG4TaskUrgentRecipientRe.Match(input.TaskText);
            if (m.Success) evidence = Regex.Replace(m.Value, @"\s+", " ").Trim();
        }
        if (evidence is null) yield break;

        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
            "The case notes or the exact Writing Task prove an urgent function (\"" + SaG4Excerpt(evidence, 140)
            + "\") but the task is catalogued as a routine referral (LT-RR). Letter type derives from the case notes and the exact task, never from the catalogue code alone (OA-05; senior assessor audit 16 Sep 2026: Cochrane, Brian Morgan). Re-catalogue the task as an urgent referral (LT-UR) and rebuild the Model Answer to the urgent contract: urgency in the introduction, \"at your earliest convenience\" in the closure request, then the separate contact-offer paragraph.",
            Quote: SaG4Excerpt(evidence, 140),
            FixSuggestion: "Re-catalogue the task as LT-UR, then write \"I am writing to request the urgent ...\" and \"I would be grateful if you could ... at your earliest convenience.\"");
    }

    // ---------------------------------------------------------------------
    // G4f — register_colloquial: unidiomatic request wording (Cathy Jones
    // "per this referral"; Sullivne "please see him for dermatologist
    // review"; Meadows "an outpatient endocrinologist review").
    // ---------------------------------------------------------------------

    // A personal specialist noun used as a modifier ("dermatologist review").
    // Not matched: "dermatological assessment", "occupational therapy
    // assessment", "dietitian review", "as per the protocol", "exercise
    // physiologist", and the verb use "asked that a neurologist review him".
    private static readonly Regex SaG4UnidiomaticRequestRe = new(
        @"\b[Pp]er\s+this\s+(?:referral|letter)\b"
        + @"|(?<!\bexercise\s)\b(?<stem>[A-Za-z][a-z]*olog)ist\s+(?<noun>review|assessment|opinion|input)\b(?!\s+(?:him|her|them|the|this|his|their|Mr|Mrs|Ms|Miss|Dr)\b)"
        + @"|\b[Pp]sychiatrist\s+(?<psychNoun>review|assessment|opinion|input)\b(?!\s+(?:him|her|them|the|this|his|their|Mr|Mrs|Ms|Miss|Dr)\b)");

    private static IEnumerable<LintFinding> DetectSaG4UnidiomaticRequestWording(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        var count = 0;
        foreach (Match m in SaG4UnidiomaticRequestRe.Matches(s.Body))
        {
            string advice;
            string fix;
            if (m.Groups["stem"].Success || m.Groups["psychNoun"].Success)
            {
                fix = m.Groups["stem"].Success
                    ? m.Groups["stem"].Value + "ical " + m.Groups["noun"].Value
                    : "psychiatric " + m.Groups["psychNoun"].Value;
                advice = "Use the adjective form \"" + fix + "\" (e.g. \"dermatological review\", never \"dermatologist review\").";
            }
            else
            {
                fix = string.Empty;
                advice = "Delete it: the letter itself is the referral.";
            }
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Minor),
                "\"" + m.Value + "\" is unidiomatic request wording for a clinical letter (senior assessor audit 16 Sep 2026). " + advice,
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: fix);
            if (++count >= 5) yield break;
        }
    }
}
