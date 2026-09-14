using System.Text.Json;
using System.Text.RegularExpressions;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingModelAnswerGroundingResult(
    bool IsGrounded,
    IReadOnlyList<string> UnmappedSentences);

public static class WritingModelAnswerGroundingValidator
{
    private static readonly string[] ClinicalTerms =
    ["asthma", "eczema", "hay fever", "diabetes", "cancer", "stroke", "allergy", "inhaler", "medicine", "dose", "mg", "surgery", "admission"];

    // A model answer legitimately GENERALISES specific case-note wording into
    // standard clinical vocabulary (e.g. "coronary artery bypass" -> "surgery",
    // "hospitalised" -> "admission", "beta blockers increased" -> "dose
    // increased") -- that is faithful paraphrase, not an invented fact, and
    // the term-presence check below must accept it. Evidenced against real
    // held letters (2026-09-06): "admission"/"hospitalised", "surgery"/
    // {arthroscopy, bypass, operation}, "dose"/"dosage" all fired as false
    // positives before this map existed.
    private static readonly Dictionary<string, string[]> TermSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["admission"] = ["admission", "admit", "admitted", "hospitalised", "hospitalized"],
        ["surgery"] = ["surgery", "surgical", "operation", "arthroscopy", "bypass", "procedure"],
        ["dose"] = ["dose", "dosage", "dosing"],
        ["allergy"] = ["allergy", "allergies", "allergic"],
        ["diabetes"] = ["diabetes", "diabetic"],
    };

    private static bool SourceSupportsTerm(string term, string source)
        => TermSynonyms.TryGetValue(term, out var synonyms)
            ? synonyms.Any(s => source.Contains(s, StringComparison.OrdinalIgnoreCase))
            : source.Contains(term, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Root-cause fix (2026-09-06): this validator used to split the WHOLE
    /// letter into "sentences" and demand every one map to a case-note fact —
    /// including the address block, date, salutation, and sign-off, which
    /// are never case-note claims and can never map to one. A courtesy
    /// closing fragment like "Yours faithfully, Doctor" was guaranteed to be
    /// flagged unmapped on every single letter. Now scans the BODY only
    /// (same boundary as <see cref="WritingModelAnswerWordCounter"/>), and
    /// skips the fact-overlap heuristic for very short fragments (fewer than
    /// 3 meaningful words) — a generic courtesy sentence like "I would be
    /// grateful for his admission" legitimately shares only one content word
    /// with any single case-note bullet once it combines a polite request
    /// with an already-established fact; the ClinicalTerms check below still
    /// catches a genuinely invented clinical claim regardless of length.
    /// </summary>
    // Addendum Rev8 (11 Sep 2026) makes a professional contact-offer sentence
    // MANDATORY at the end of every Model Answer closure ("Should there be any
    // queries, kindly do not hesitate to contact me."). It is a courtesy
    // formula, never a case-note claim, so it can never be traced to a case
    // note and must not be treated as an unmapped (invented) fact.
    private static readonly Regex ContactOfferCourtesy = new(
        @"^(?:should there be any (?:further )?(?:queries|questions|concerns)|if (?:there are|you have|you require|you need) any (?:further )?(?:queries|questions|concerns|information)|please do not hesitate|kindly do not hesitate|do not hesitate)[^.!?]*\b(?:contact|call|telephone)\s+me\b[^.!?]*[.!?]?$",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// Owner Clarifications Addendum Two (14 Sep 2026). Case notes are written
    /// in clinical shorthand with digits ("BP 88/70", "smoker, 20
    /// cigarettes/day"), while a canonical Model Answer must expand the
    /// abbreviation and spell descriptive numbers as words (OA2-08, OA2-14,
    /// OA2-15). The word-overlap check below compares raw tokens, so without
    /// this the mandated wording — "His blood pressure was 88/70 mmHg.",
    /// "He smokes twenty cigarettes daily." — shares too few words with the very
    /// note it came from and is reported as an INVENTED fact, which would make
    /// OA2-14 and OA2-15 impossible to satisfy. Every rule here only ever ADDS
    /// vocabulary alongside the original text, so nothing that grounded before
    /// can stop grounding now.
    /// </summary>
    private static readonly (string Pattern, string Alias)[] GroundingAliases =
    [
        // Clinical shorthand the source uses and a Model Answer must expand.
        (@"\bBP\b", "blood pressure"),
        (@"\bHR\b", "heart rate"),
        (@"\bRR\b", "respiratory rate"),
        (@"\bPR\b", "pulse rate"),
        (@"\bTemp\b", "temperature"),
        (@"\bSats?\b|\bSpO2\b", "oxygen saturation"),
        (@"\bWCC\b|\bWBC\b", "white cell count"),
        (@"\bRBC\b", "red cell count"),
        (@"\bCRP\b", "C-reactive protein"),
        (@"\bCBC\b|\bFBC\b", "full blood count"),
        (@"\bHb\b", "haemoglobin"),
        (@"\bLFTs?\b", "liver function tests"),
        (@"\bECG\b", "electrocardiogram"),
        (@"\bLP\b", "lumbar puncture"),
        (@"\bPCA\b", "patient-controlled analgesia"),
        (@"\bTKJR\b|\bTKR\b", "total knee replacement"),
        (@"\bOSA\b", "obstructive sleep apnoea"),
        (@"\bCSU\b", "catheter specimen urine"),
        (@"\bBMI\b", "body mass index"),
        (@"\bPMN\b", "polymorphonuclear"),
        (@"\bEMG\b", "electromyography"),
        (@"\bNCS\b", "nerve conduction study"),
        (@"\bPTSD\b", "post-traumatic stress disorder"),
        // Morphology and frequency shorthand: the source writes the noun or the
        // slash form, the letter writes the verb or the adverb.
        (@"\bsmoker\b", "smokes smoking"),
        (@"\bnon-smoker\b", "smokes smoking"),
        (@"\bdrinker\b", "drinks drinking"),
        (@"\/\s*day\b|\bper day\b|\ba day\b", "daily"),
        (@"\/\s*week\b|\bper week\b", "weekly"),
        (@"\bnocte\b", "at night nightly"),
        (@"\bmane\b", "in the morning"),
        (@"\bprn\b", "as needed"),
    ];

    // Descriptive numbers: OA2-15 makes the Model Answer spell them out while
    // the source keeps digits, so each digit carries its word alongside it.
    private static readonly (string Digits, string Word)[] GroundingNumberWords =
    [
        ("100", "hundred"), ("72", "seventy-two"), ("48", "forty-eight"), ("25", "twenty-five"),
        ("24", "twenty-four"), ("90", "ninety"), ("80", "eighty"), ("70", "seventy"),
        ("60", "sixty"), ("50", "fifty"), ("40", "forty"), ("30", "thirty"), ("20", "twenty"),
        ("19", "nineteen"), ("18", "eighteen"), ("17", "seventeen"), ("16", "sixteen"),
        ("15", "fifteen"), ("14", "fourteen"), ("13", "thirteen"), ("12", "twelve"),
        ("11", "eleven"), ("10", "ten"), ("9", "nine"), ("8", "eight"), ("7", "seven"),
        ("6", "six"), ("5", "five"), ("4", "four"), ("3", "three"), ("2", "two"), ("1", "one"),
    ];

    /// <summary>
    /// Widens a case-note fact (or a letter sentence) with the vocabulary a
    /// source-faithful Model Answer is required to use, so the overlap check
    /// can trace the sentence back to the note it came from. Additive only.
    /// </summary>
    internal static string WidenForGrounding(string? text)
    {
        var value = text ?? string.Empty;
        foreach (var (pattern, alias) in GroundingAliases)
            value = Regex.Replace(value, pattern, m => m.Value + " " + alias, RegexOptions.IgnoreCase);
        foreach (var (digits, word) in GroundingNumberWords)
            value = Regex.Replace(value, @"(?<![\w.])" + digits + @"(?![\w.])", digits + " " + word);
        return value;
    }

    public static WritingModelAnswerGroundingResult Validate(string modelAnswer, IReadOnlyList<string> caseNoteFacts)
    {
        caseNoteFacts = (caseNoteFacts ?? Array.Empty<string>())
            .Select(WidenForGrounding)
            .ToList();
        var source = string.Join(" ", caseNoteFacts);
        var body = WritingModelAnswerWordCounter.ExtractBody(modelAnswer);
        var unmapped = new List<string>();
        foreach (var sentence in Regex.Split(body, @"(?<=[.!?])\s+"))
        {
            var value = sentence.Trim();
            if (value.Length == 0) continue;
            // Compare on the widened form; report the author's real sentence.
            var normalised = WidenForGrounding(value);
            if (ContactOfferCourtesy.IsMatch(value)
                && !ClinicalTerms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            var terms = ClinicalTerms.Where(term => value.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (terms.Any(term => !SourceSupportsTerm(term, source)))
            {
                unmapped.Add(value);
                continue;
            }

            var meaningful = Regex.Matches(normalised.ToLowerInvariant(), @"[a-z]{4,}")
                .Select(match => match.Value)
                .Where(word => word is not ("please" or "patient" or "review" or "write" or "your" or "this"
                    or "would" or "grateful" or "could" or "should" or "thank" or "further" or "assistance"
                    or "information" or "contact" or "require" or "should" or "kindly" or "advice"
                    // Addendum Two (14 Sep 2026): request-FRAMING vocabulary carries
                    // no case-note fact, exactly like the courtesy words above. The
                    // owner's own prescribed closure ("I would be grateful if you
                    // could consider MRI if clinically indicated.") was reported as
                    // an unmapped, i.e. invented, sentence purely because
                    // consider/clinically/indicated are not written in the notes.
                    // Invented REQUESTS are OWN-W-037's job in the semantic layer;
                    // grounding exists to catch invented FACTS.
                    or "consider" or "considered" or "clinically" or "indicated" or "possible"
                    or "arrange" or "undertake" or "provide" or "confirm" or "appreciated"))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (meaningful.Length >= 3
                && !caseNoteFacts.Any(fact => meaningful.Count(word => fact.Contains(word, StringComparison.OrdinalIgnoreCase)) >= Math.Min(2, meaningful.Length)))
            {
                unmapped.Add(value);
            }
        }

        return new WritingModelAnswerGroundingResult(unmapped.Count == 0, unmapped);
    }
}

/// <summary>
/// OET convention counts the LETTER BODY only for the 180-200-word target —
/// address, date, salutation, Re: line and sign-off are excluded. Counting
/// the whole letter text (as both model-answer generation paths did before
/// this fix) rejects perfectly well-formed full letters purely for including
/// their own formatting, which is not what the 180-200 target measures.
/// </summary>
public static class WritingModelAnswerWordCounter
{
    private static readonly Regex WordPattern = new(@"\b[\p{L}\p{N}’'-]+\b");
    private static readonly Regex SalutationLine = new(@"^\s*Dear\b", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex ReLine = new(@"^\s*Re\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex ClosingLine = new(@"^\s*Yours\s+(sincerely|faithfully)\b", RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public static int CountBodyWords(string? letterText)
        => WordPattern.Matches(ExtractBody(letterText)).Count;

    /// <summary>
    /// The letter BODY: everything between the Re:/Dear line and the
    /// "Yours sincerely/faithfully" closing. Shared by the word counter and
    /// <see cref="WritingModelAnswerGroundingValidator"/> — the address
    /// block, date, salutation, Re: line and sign-off are boilerplate, never
    /// case-note claims, and must never be run through either check.
    /// </summary>
    public static string ExtractBody(string? letterText)
    {
        if (string.IsNullOrWhiteSpace(letterText)) return string.Empty;
        var lines = letterText.Replace("\r\n", "\n").Split('\n');
        int FindLine(Regex re)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (re.IsMatch(lines[i])) return i;
            }
            return -1;
        }

        var salutationIdx = FindLine(SalutationLine);
        var reIdx = FindLine(ReLine);
        var closingIdx = FindLine(ClosingLine);
        var start = Math.Max(salutationIdx, reIdx) + 1;
        var end = closingIdx == -1 ? lines.Length : closingIdx;
        if (end <= start)
        {
            // Structure not detected (e.g. an in-progress draft) — fall back
            // to the whole text rather than under-counting to zero.
            return letterText;
        }

        return string.Join('\n', lines[start..end]);
    }
}

public sealed record WritingModelAnswerGenerationResult(bool IsReady, string HoldReason);

/// <summary>
/// Generates the post-score model answer through the canonical grounded AI
/// gateway and refuses to publish any sentence that cannot be mapped to the
/// immutable case-note snapshot.
/// </summary>
public sealed class WritingModelAnswerService(
    IAiGatewayService gateway,
    WritingRuleEngine ruleEngine,
    ILogger<WritingModelAnswerService> logger)
{
    public async Task<WritingModelAnswerGenerationResult> PopulateAsync(
        WritingAssessmentReportV11 report,
        WritingAssessmentModelAnswer answer,
        string userId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(answer);

        try
        {
            if (!RulebookProfessionParser.TryParse(report.Profession, out var profession))
                return Hold(answer, "model_answer_profession_pack_unavailable");

            var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                Profession = profession,
                LetterType = report.LetterType,
                Task = AiTaskMode.GenerateContent,
            });
            var result = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = $$"""
                    Produce the post-score OET Writing model answer for this exact task.
                    Return one JSON object only:
                    {"modelAnswerText":"...","correctedCandidateLetter":"... or null","whyThisWorks":["..."],"groundedFactReferences":["case-note-line:1"]}
                    The model answer must be approximately 180-200 words, recipient-appropriate,
                    and use only facts present in the case notes. Do not add diagnoses, tests,
                    treatment, dates, or requests that are not in the case notes.

                    Written task:
                    ---
                    {{report.TaskSnapshot}}
                    ---
                    Case notes:
                    ---
                    {{report.CaseNotesSnapshot}}
                    ---
                    Candidate letter (for learning feedback only):
                    ---
                    {{report.OriginalLetterSnapshot}}
                    ---
                    """,
                Temperature = 0.1,
                MaxTokens = 1800,
                FeatureCode = AiFeatureCodes.WritingGrade,
                PromptTemplateId = "writing.model-answer.v1",
                UserId = userId,
                AssessmentContext = AiAssessmentContext.Practice,
            }, ct);

            var parsed = Parse(result.Completion);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.ModelAnswerText))
                return Hold(answer, "model_answer_unreadable");

            var words = WritingModelAnswerWordCounter.CountBodyWords(parsed.ModelAnswerText);
            if (words < 180 || words > 200)
                return Hold(answer, "model_answer_word_count_out_of_range");

            var grounding = WritingModelAnswerGroundingValidator.Validate(
                parsed.ModelAnswerText,
                report.Facts.Select(x => x.FactText).ToArray());
            if (!grounding.IsGrounded)
            {
                answer.HoldReason = "model_answer_unmapped_sentence";
                answer.UpdatedAt = DateTimeOffset.UtcNow;
                return new WritingModelAnswerGenerationResult(false, answer.HoldReason);
            }

            // Global Model Answer Formatting & Sign-Off Rules (owner addendum,
            // 2026-09-06): same "zero unresolved violations" gate as the
            // pre-generated task Model Answer path (WritingTaskModelAnswerService).
            var lintFindings = ruleEngine.Lint(new WritingLintInput(
                LetterText: parsed.ModelAnswerText,
                LetterType: report.LetterType,
                Profession: profession,
                IsModelAnswer: true));
            var criticalFindings = lintFindings.Where(f => f.Severity == RuleSeverity.Critical).ToList();
            if (criticalFindings.Count > 0)
            {
                logger.LogWarning(
                    "Model-answer rule violations for report {ReportId}: {Findings}",
                    report.Id, string.Join(" | ", criticalFindings.Select(f => $"{f.RuleId}: {f.Message}")));
                answer.HoldReason = "model_answer_rule_violations";
                answer.UpdatedAt = DateTimeOffset.UtcNow;
                return new WritingModelAnswerGenerationResult(false, answer.HoldReason);
            }

            answer.Status = WritingAssessmentModelAnswerStatus.Ready;
            answer.IsCandidateVisible = false;
            answer.ModelAnswerText = parsed.ModelAnswerText.Trim();
            answer.CorrectedCandidateLetter = string.IsNullOrWhiteSpace(parsed.CorrectedCandidateLetter)
                ? null
                : parsed.CorrectedCandidateLetter.Trim();
            answer.WhyThisWorksJson = JsonSerializer.Serialize(parsed.WhyThisWorks ?? []);
            answer.GroundedFactReferencesJson = JsonSerializer.Serialize(parsed.GroundedFactReferences ?? []);
            answer.HoldReason = null;
            answer.UpdatedAt = DateTimeOffset.UtcNow;
            return new WritingModelAnswerGenerationResult(true, "ready_for_release");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Grounded Writing model-answer generation failed for report {ReportId}", report.Id);
            return Hold(answer, "model_answer_generation_failed");
        }
    }

    private static WritingModelAnswerGenerationResult Hold(WritingAssessmentModelAnswer answer, string reason)
    {
        answer.Status = WritingAssessmentModelAnswerStatus.HeldForReview;
        answer.IsCandidateVisible = false;
        answer.HoldReason = reason;
        answer.UpdatedAt = DateTimeOffset.UtcNow;
        return new WritingModelAnswerGenerationResult(false, reason);
    }

    private static Draft? Parse(string? completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;
        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            return JsonSerializer.Deserialize<Draft>(completion[start..(end + 1)], new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Draft(
        string? ModelAnswerText,
        string? CorrectedCandidateLetter,
        IReadOnlyList<string>? WhyThisWorks,
        IReadOnlyList<string>? GroundedFactReferences);
}
