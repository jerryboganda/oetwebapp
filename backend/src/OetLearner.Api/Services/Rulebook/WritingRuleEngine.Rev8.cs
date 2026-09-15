using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Writing Rule Enforcement &amp; Model Answer Validation Addendum, Revisions
/// 7-8 (owner, 11 Sep 2026). Owner overrides/clarifications that must be
/// enforced identically by the Model Answer generator, the independent Model
/// Answer validator and the candidate grader:
/// <list type="bullet">
/// <item>Model Answer mode (<see cref="WritingLintInput.IsModelAnswer"/>) applies the
/// stricter canonical house style; every non-Info finding blocks publication
/// (see <see cref="ModelAnswerBlockingFindings"/>).</item>
/// <item>Candidate mode accepts professional alternatives and semantic
/// equivalents and never requires Model Answer wording (no template matching).</item>
/// </list>
/// Canonical registry rows: OWN-W-001..OWN-W-038 (docs/canonical-rules/).
/// </summary>
public sealed partial class WritingRuleEngine
{
    /// <summary>
    /// Deterministic validator version. Any change to a detector, a severity or
    /// the Model Answer blocking policy MUST bump this string: a stored
    /// VERIFIED/CLEAN Model Answer is only valid for the exact validator
    /// version it was verified under (Addendum Rev8 §7, §14).
    /// Owner Clarifications Addendum (14 Sep 2026): +OA-01..OA-15 battery —
    /// intro_purpose_vague, closure_request_paragraph, treatment_change_grammar,
    /// results_comma_splice, diabetes_type_words, respiratory_rate_unit_style,
    /// illogical_quantity_range, vague_clinical_object, letter_date_unsupported,
    /// recipient_name_mismatch, semicolon_overuse; OA-03 introduction
    /// full-name allowance; OA-07 duplicate-request gated to Model Answers
    /// (candidates are assessed semantically, never by phrase matching).
    /// Owner Clarifications Addendum TWO (14 Sep 2026): +OA2-01..OA2-20 —
    /// new detectors discharge_function_missed, result_noun_fragment,
    /// supine_position_wording, result_head_noun,
    /// background_paragraph_placement, vital_sign_interpretation_unsupported,
    /// role_salutation_matches_task, canonical_contact_template,
    /// re_line_identity_unsupported; REVERSED medication_list_punctuation (no
    /// semicolon before the final "and", OA2-16); tightened semicolon_overuse
    /// (any narrative semicolon in a Model Answer, OA2-07); widened
    /// number_style_words_vs_digits and lifestyle_frequency_precision to
    /// number-words (OA2-15); role-aware yours_sincerely_vs_faithfully
    /// (OA2-17); connective-position-only "also" in linker_avoid_words.
    /// Every stored answer affected by this rule-pack change must be
    /// revalidated before it can remain Ready.
    /// </summary>
    public const string ValidatorVersion = "writing-rules.owner-clarifications-3.2026-09-16.1";

    /// <summary>
    /// Everything that blocks a Model Answer from being stored/published:
    /// Addendum Rev8 §7 "VERIFIED/CLEAN means zero unresolved ACTIVE-rule
    /// violations" — every Critical, Major and Minor finding counts, not only
    /// Critical ones (the pre-Rev8 gate held on Critical only, which is how
    /// Major violations such as a duplicated age or a repeated "urgent"
    /// reached candidates while the catalogue reported "clean").
    /// </summary>
    public static IReadOnlyList<LintFinding> ModelAnswerBlockingFindings(IEnumerable<LintFinding> findings)
        => findings.Where(f => f.Severity != RuleSeverity.Info).ToList();

    /// <summary>
    /// Short, stable fingerprint of the exact active rule pack used for a
    /// profession: validator version + rulebook version + every rule's id,
    /// severity, scope and text. Recorded on every verified Model Answer so a
    /// later rule-pack change is detectable as staleness.
    /// </summary>
    public string RulePackFingerprint(ExamProfession profession)
    {
        var book = loader.Load(RuleKind.Writing, profession);
        var sb = new StringBuilder();
        sb.Append(ValidatorVersion).Append('|').Append(book.Version).Append('|');
        foreach (var r in book.Rules.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            sb.Append(r.Id).Append(':').Append(r.Severity).Append(':')
              .Append(r.AppliesTo?.GetRawText() ?? "all").Append(':')
              .Append(r.CheckId).Append(':').Append(r.Title).Append(':').Append(r.Body).Append('\n');
        }
        foreach (var id in SupportedCheckIdSet.OrderBy(x => x, StringComparer.Ordinal))
            sb.Append(id).Append(',');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return "rp-" + Convert.ToHexString(hash)[..20].ToLowerInvariant();
    }

    public string RulebookVersion(ExamProfession profession) => loader.Load(RuleKind.Writing, profession).Version;

    private static RuleSeverity ModeSeverity(WritingLintInput input, RuleSeverity candidate)
        => input.IsModelAnswer ? RuleSeverity.Critical : candidate;

    // ---------------------------------------------------------------------
    // Patient name forms (Addendum Rev8 §2 "Patient naming rule - exact
    // operational form"): adults = title + surname at the first mention in
    // every body paragraph; children = first name; pronouns afterwards; the
    // full first + last name is never repeated after the Re: line.
    // ---------------------------------------------------------------------

    private sealed record PatientNameForms(string? First, string? Last, string? Title, bool Child)
    {
        public bool HasFullName => !string.IsNullOrEmpty(First) && !string.IsNullOrEmpty(Last);
    }

    private static readonly Regex ReTitledNameRe = new(
        @"\b(Mr|Mrs|Ms|Miss|Master|Dr)\.?\s+([A-Z][a-zA-Z'’\-]+)(?:\s+([A-Z][a-zA-Z'’\-]+))?(?:\s+([A-Z][a-zA-Z'’\-]+))?");

    private static readonly Regex ReUntitledNameRe = new(
        @"^\s*Re\s*:\s*([A-Z][a-zA-Z'’\-]+)\s+([A-Z][a-zA-Z'’\-]+)\b");

    private static readonly HashSet<string> NonNameReWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Referral", "Refer", "Medication", "Medications", "Review", "Urgent", "Discharge", "Transfer",
        "Update", "Patient", "Letter", "Continuing", "Ongoing", "Follow", "Request", "Assessment",
        "Care", "Home", "Management", "Plan", "Your", "Dietary", "Nursing", "Physiotherapy",
    };

    private static readonly HashSet<string> ReIdentifierWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "DOB", "Age", "Aged", "NHS", "MRN", "Hospital", "No", "Born", "Date", "Year", "Years", "Yrs",
    };

    private static PatientNameForms? ResolvePatientName(WritingLintInput input, LetterStructure s)
    {
        if (s.ReLineIndex is null) return null;
        var reLine = s.Lines[s.ReLineIndex.Value];
        var titled = ReTitledNameRe.Match(reLine);
        if (titled.Success)
        {
            var title = titled.Groups[1].Value;
            // "Re: Mrs Rose Garcia DOB: 1 January 1945" — trailing identifier
            // words (DOB, NHS, ...) are not part of the name.
            var tokens = new List<string> { titled.Groups[2].Value };
            if (titled.Groups[3].Success) tokens.Add(titled.Groups[3].Value);
            if (titled.Groups[4].Success) tokens.Add(titled.Groups[4].Value);
            while (tokens.Count > 1 && (NonNameReWords.Contains(tokens[^1]) || ReIdentifierWords.Contains(tokens[^1])))
                tokens.RemoveAt(tokens.Count - 1);
            string? first = tokens.Count > 1 ? tokens[0] : null;
            var last = tokens[^1];
            if (first is not null && NonNameReWords.Contains(first)) first = null;
            return new PatientNameForms(first, last, title, Child: input.PatientIsMinor || string.Equals(title, "Master", StringComparison.OrdinalIgnoreCase));
        }
        var untitled = ReUntitledNameRe.Match(reLine);
        if (untitled.Success && !NonNameReWords.Contains(untitled.Groups[1].Value) && !NonNameReWords.Contains(untitled.Groups[2].Value))
            return new PatientNameForms(untitled.Groups[1].Value, untitled.Groups[2].Value, null, Child: true);
        return null;
    }

    /// <summary>Regex matching an acceptable named mention of the patient.</summary>
    private static Regex NameMentionRegex(PatientNameForms name)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(name.Last))
            parts.Add($@"\b(?:Mr|Mrs|Ms|Miss|Dr)\.?\s+(?:{(name.First is null ? "" : Regex.Escape(name.First) + @"\s+")})?{Regex.Escape(name.Last)}\b");
        if (!string.IsNullOrEmpty(name.First))
            parts.Add($@"\b{Regex.Escape(name.First)}\b");
        if (name.First is null && !string.IsNullOrEmpty(name.Last) && name.Title is null)
            parts.Add($@"\b{Regex.Escape(name.Last)}\b");
        return new Regex(string.Join("|", parts));
    }

    private static readonly Regex PatientPronounRe = new(
        @"\b(he|she|his|her|him|hers|himself|herself)\b", RegexOptions.IgnoreCase);

    private static readonly Regex ThePatientRe = new(
        @"\b(?:the|this|our)\s+patient(?:['’]s)?\b|\ba\s+patient\s+(?:at|of|under|in|from|with|who|registered)\b",
        RegexOptions.IgnoreCase);

    // OWN-W-009 — "Do not use 'the patient' as a substitute for the person in
    // final letter prose." Latest owner rule (Rev7/8, EXISTING + RECONFIRMED)
    // supersedes G-W-112 ("patient is not a forbidden word") per the
    // addendum's own precedence rule; re-enables the previously inert
    // body_forbidden_phrase_the_patient check for BOTH modes. Also catches the
    // Weir defect "a patient at this practice".
    private static IEnumerable<LintFinding> DetectThePatientRev8(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        var count = 0;
        foreach (Match m in ThePatientRe.Matches(s.Body))
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                $"Do not refer to the person as \"{m.Value}\". Use the patient's name form (title + surname for an adult, first name for a child) or a pronoun.",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length);
            if (++count >= 3) yield break;
        }
    }

    // OWN-W-011 — full-name repetition. Owner override (15 Sep 2026, OA3-01):
    // the INTRODUCTION may use EITHER title + surname OR the full patient
    // name — both are correct, whether or not the full name is already in
    // the Re: line ("Mr Weir" / "Mr Michael Weir", "Mrs Weston" /
    // "Mrs Betty Weston"). Only the remaining body is restricted to the
    // normal adult reference (title + surname); a full name used in any
    // later paragraph still fails.
    private static IEnumerable<LintFinding> DetectFullNameRepeated(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var name = ResolvePatientName(input, s);
        if (name is null || !name.HasFullName || s.Body.Length == 0) yield break;
        var re = new Regex($@"(?:(?:Mr|Mrs|Ms|Miss|Master|Dr)\.?\s+)?{Regex.Escape(name.First!)}\s+{Regex.Escape(name.Last!)}");
        var introLength = s.BodyParagraphs.Count > 0 ? s.BodyParagraphs[0].Length : 0;
        foreach (Match m in re.Matches(s.Body))
        {
            if (m.Index <= introLength) continue;
            var approved = name.Child ? name.First : $"{name.Title ?? "Mr/Ms"} {name.Last}";
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                $"The full name \"{m.Value}\" appears after the introduction. After the opening, use the normal adult reference \"{approved}\" throughout the body (the full name is welcome in the introduction; it must not recur later).",
                Quote: m.Value, Start: BodyOffset(s) + m.Index, End: BodyOffset(s) + m.Index + m.Length,
                FixSuggestion: approved);
            yield break;
        }
    }

    // OWN-W-010 — "At the first mention of every body paragraph use the
    // correct name form; use pronouns for later mentions within that
    // paragraph." Model Answers: every paragraph after the introduction
    // (closure included — Garcia defect "contact her close family").
    // Candidates: body paragraphs between the introduction and the closure.
    // Only a PRONOUN as the first patient reference is flagged here; "the
    // patient" and relationship labels have their own checks.
    private static IEnumerable<LintFinding> DetectParagraphStartPatientName(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var name = ResolvePatientName(input, s);
        if (name is null || s.BodyParagraphs.Count < 2) yield break;
        var nameRe = NameMentionRegex(name);
        var approved = name.Child ? name.First ?? name.Last : $"{name.Title ?? "Mr/Ms"} {name.Last}";
        var paragraphs = input.IsModelAnswer
            ? s.BodyParagraphs.Skip(1).ToList()
            : s.BodyParagraphs.Skip(1).Take(Math.Max(0, s.BodyParagraphs.Count - 2)).ToList();
        foreach (var p in paragraphs)
        {
            var pronoun = PatientPronounRe.Match(p);
            if (!pronoun.Success) continue;
            var named = nameRe.Match(p);
            var thePatient = ThePatientRe.Match(p);
            var relationship = RelationshipLabelRe.Match(p);
            var firstNonPronoun = new[] { named, thePatient, relationship }
                .Where(x => x.Success).Select(x => x.Index).DefaultIfEmpty(int.MaxValue).Min();
            if (pronoun.Index >= firstNonPronoun) continue;
            var start = Math.Max(0, pronoun.Index - 25);
            var snippet = p.Substring(start, Math.Min(p.Length - start, pronoun.Length + 45)).Trim();
            var offset = BodyOffset(s) + s.Body.IndexOf(p, StringComparison.Ordinal);
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                $"The first mention of the patient in this paragraph is the pronoun \"{pronoun.Value}\". Start each body paragraph with \"{approved}\", then use pronouns.",
                Quote: snippet, Start: offset + pronoun.Index, End: offset + pronoun.Index + pronoun.Length,
                FixSuggestion: approved);
        }
    }

    private static readonly Regex RelationshipLabelRe = new(
        @"\byour\s+(mother|father|mum|mom|dad|son|daughter|wife|husband|partner|child|grandmother|grandfather|grandson|granddaughter|brother|sister|aunt|uncle|niece|nephew|relative|parent)\b",
        RegexOptions.IgnoreCase);

    // OWN-W-012 — "When the patient is named, do not use 'your mother',
    // 'your father' or another relationship label as the normal patient
    // reference." Checked after the introduction (a clarifying appositive in
    // the introduction is allowed; an introduction that OPENS with one is
    // caught by OWN-W-013 for Model Answers).
    private static IEnumerable<LintFinding> DetectRelationshipLabel(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (ResolvePatientName(input, s) is null || s.BodyParagraphs.Count < 2) yield break;
        var count = 0;
        foreach (var p in s.BodyParagraphs.Skip(1))
        {
            foreach (Match m in RelationshipLabelRe.Matches(p))
            {
                var offset = BodyOffset(s) + s.Body.IndexOf(p, StringComparison.Ordinal);
                yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                    $"Do not use \"{m.Value}\" as the patient reference. Use the patient's name form at the first mention in each paragraph, then pronouns.",
                    Quote: m.Value, Start: offset + m.Index, End: offset + m.Index + m.Length);
                if (++count >= 3) yield break;
            }
        }
    }

    // OWN-W-013 — "Every generated Model Answer must start its introduction
    // with 'I am writing to ...'". Candidate answers may use any professional
    // opening whose purpose is immediately clear: never checked for candidates.
    private static IEnumerable<LintFinding> DetectCanonicalOpening(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.BodyParagraphs.Count == 0) yield break;
        var intro = s.BodyParagraphs[0];
        if (!Regex.IsMatch(intro, @"^I am writing to\b"))
            yield return new LintFinding(rule.Id, RuleSeverity.Critical,
                "A Model Answer introduction must begin with \"I am writing to ...\" followed by the task purpose (refer, request, update, inform, transfer, outline).",
                Quote: intro.Length > 60 ? intro[..60] + "…" : intro);
    }

    // Contact-offer meaning (semantic equivalents accepted, Rev7 §13).
    private static readonly Regex ContactOfferMeaningRe = new(
        @"(?:do not|don't|never)\s+hesitate\s+to\s+(?:contact|call|telephone|email|get in touch with)\s+me" +
        @"|(?:please|kindly)\s+(?:feel free to\s+)?(?:contact|call|telephone)\s+me" +
        @"|feel free to (?:contact|call) me" +
        @"|(?:contact|call|telephone)\s+me\s+(?:if|should|when|for|on|at)" +
        @"|should (?:you|there be) (?:have |require |need )?any (?:further )?(?:queries|questions|concerns|information)" +
        @"|if you (?:have|require|need) any (?:further )?(?:queries|questions|concerns|information)" +
        @"|if there are any (?:further )?(?:queries|questions|concerns)" +
        @"|I (?:would be|am) (?:happy|glad|pleased) to (?:provide|discuss|answer|help)",
        RegexOptions.IgnoreCase);

    private static readonly Regex ContactMeRe = new(
        @"\b(?:contact|call|telephone|email|reach|get in touch with)\s+me\b|\bhesitate to contact\b",
        RegexOptions.IgnoreCase);

    // OWN-W-014 / OWN-W-015 — universal contact-offer closure. Model Answer:
    // the closure's FINAL sentence must be a professional offer to contact
    // the writer; in an urgent referral "at your earliest convenience" must
    // appear in the closure BEFORE that final sentence. Candidate: the
    // closure must convey the contact-offer meaning (any equivalent wording).
    private static IEnumerable<LintFinding> DetectContactOfferClosure(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.BodyParagraphs.Count < 2) yield break;
        var closure = s.BodyParagraphs[^1];
        var sentences = SplitSentences(closure);
        if (sentences.Count == 0) yield break;
        if (input.IsModelAnswer)
        {
            var last = sentences[^1];
            if (!ContactOfferMeaningRe.IsMatch(last) || !ContactMeRe.IsMatch(last))
            {
                yield return new LintFinding(rule.Id, RuleSeverity.Critical,
                    "A Model Answer closure must END with a professional offer to contact the writer, e.g. \"Should there be any queries, kindly do not hesitate to contact me.\"",
                    Quote: last.Length > 80 ? last[..80] + "…" : last,
                    FixSuggestion: "Should there be any queries, kindly do not hesitate to contact me.");
            }
            yield break;
        }
        if (!ContactOfferMeaningRe.IsMatch(closure))
            yield return new LintFinding(rule.Id, RuleSeverity.Major,
                "The closure does not offer further contact. End with a professional contact offer, e.g. \"Please do not hesitate to contact me if you require any further information.\" (any equivalent wording is acceptable).",
                Quote: closure.Length > 80 ? closure[..80] + "…" : closure);
    }

    private static List<string> SplitSentences(string paragraph)
        => Regex.Split(paragraph.Trim(), @"(?<=[.!?])\s+(?=[A-Z])")
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();

    // OWN-W-015 (Model Answer ordering) — "at your earliest convenience" in
    // the closure's request sentence, then the final contact-offer
    // paragraph. OA-06 (14 Sep 2026): the closure is the REQUEST paragraph
    // plus the SEPARATE contact-offer paragraph, so the closure region is
    // the last two paragraphs.
    private static IEnumerable<LintFinding> DetectUrgentClosureModelAnswer(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var closureRegion = s.BodyParagraphs.Count > 0
            ? string.Join(" ", s.BodyParagraphs.TakeLast(2))
            : "";
        var sentences = SplitSentences(closureRegion);
        var aeycIndex = sentences.FindIndex(x => x.Contains("at your earliest convenience", StringComparison.OrdinalIgnoreCase));
        if (aeycIndex < 0)
        {
            yield return new LintFinding(rule.Id, RuleSeverity.Critical,
                "Urgent-referral Model Answer closure must include \"at your earliest convenience\".",
                FixSuggestion: "I would be grateful if you could assess [name] at your earliest convenience.");
            yield break;
        }
        if (aeycIndex == sentences.Count - 1)
            yield return new LintFinding(rule.Id, RuleSeverity.Critical,
                "In an urgent-referral Model Answer, \"at your earliest convenience\" belongs in the request sentence, followed by the final contact-offer paragraph.",
                Quote: sentences[aeycIndex]);
    }

    // ---------------------------------------------------------------------
    // Medication syntax (OWN-W-023, R11.2-R11.3) and value-unit spacing
    // (OWN-W-024).
    // ---------------------------------------------------------------------

    private const string DoseUnitPattern = @"mg|mcg|µg|micrograms?|g|IU|units?|mL|ml";

    // A dose is a single value, a decimal, a range ("5-10", "5 to 10") or a
    // combination strength ("20/10", "500/125"). A unit (mg, mL, ...) normally
    // follows; when it does not, the dose only counts as a medication dose if
    // a frequency qualifier immediately follows ("Targin, 20/10 twice daily")
    // — so every legitimate dose format is recognised while prose ("aged 13",
    // "3 blackouts", "temperature 37.8") never matches. Owner directive
    // (13 Sep 2026, McDonald): correct clinical wording drives the validator —
    // the parser must understand ranges and combination strengths, never
    // force letters into unnatural shapes it happens to parse.
    private const string MedicationDosePattern =
        @"\d+(?:\.\d+)?(?:\s*(?:-|\u2013|to)\s*\d+(?:\.\d+)?|\s*\/\s*\d+(?:\.\d+)?)?";

    private const string MedicationFrequencyLookahead =
        @"(?=\s+(?:(?:once|twice|three|four|five|six|eight|twelve)\s+)?(?:times\s+)?(?:a\s+day|per\s+day|daily|hourly|nightly|weekly|at\s+night|in\s+the\s+morning|as\s+needed|when\s+required|nocte|mane|prn|om|od|bd|bid|tds|tid|qds|qid)\b|\s+(?:four|six|eight|twelve)-hourly\b)";

    private static readonly Regex MedicationItemRe = new(
        @"\b(?<drug>[A-Za-z][A-Za-z\-]{2,})(?<comma>,)?\s+(?<dose>" + MedicationDosePattern + @")\s*(?<unit>" + DoseUnitPattern + @")\b(?!\s*\/)"
        + @"|\b(?<drug>[A-Za-z][A-Za-z\-]{2,})(?<comma>,)?\s+(?<dose>" + MedicationDosePattern + @")" + MedicationFrequencyLookahead,
        RegexOptions.None);

    private static readonly HashSet<string> MedicationStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "of", "to", "with", "and", "or", "at", "on", "by", "from", "was", "were", "is", "are", "be", "been",
        "taking", "takes", "take", "took", "receiving", "received", "given", "increased", "reduced", "decreased",
        "dose", "doses", "dosage", "total", "maximum", "max", "up", "approximately", "about", "daily", "plus",
        "weighing", "weighs", "weight", "gained", "lost", "than", "every", "each", "per", "over", "under", "only",
        "then", "also", "once", "twice", "times", "the", "his", "her", "their", "into", "further", "additional",
        "extra", "another", "all", "both", "single", "same", "new", "current", "regular", "oral", "intravenous",
        "subcutaneous", "topical", "inhaled", "slow", "release", "tablet", "tablets", "capsule", "capsules",
        "injection", "injections", "infusion", "bolus", "loading", "morning", "evening", "night", "bedtime",
        "nightly", "weekly", "monthly", "hourly", "starting", "start", "started", "commenced", "prescribed",
        "continue", "continued", "continuing", "stopped", "ceased", "withheld", "for", "as", "a", "an", "in",
        "cumulative", "average", "around", "nearly", "almost", "least", "most", "measuring", "measured", "within",
        "above", "below", "between", "after", "before", "following", "later", "prior", "past", "last", "next",
        "drinks", "drank", "drinking", "consumes", "consumed", "consuming", "smokes", "smoked", "smoking",
        "alcohol", "beer", "wine", "spirits", "approx", "roughly", "some", "only", "just",
        "temperature", "blood", "pressure", "heart", "respiratory", "rate", "cigarettes", "cigarette",
        "minutes", "hours", "days", "weeks", "months", "years",
    };

    // Words between two medication items that mean the sentence is not a
    // plain list ("ceftriaxone, 2 g twice daily, together with dexamethasone,
    // 10 mg") — the list-grammar check is skipped for those.
    private static readonly Regex NonListConnectiveRe = new(
        @"\b(?:with|together|followed|then|before|after|while|until|plus|instead|replaced|switched|changed|increased|reduced|converted|alongside|as well as)\b",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectMedicationListPunctuation(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        var reported = 0;
        foreach (var sentenceMatch in Regex.Matches(s.Body, @"[^.!?\n]+(?:\.(?=\d)[^.!?\n]*)*[.!?]?").Cast<Match>())
        {
            var sentence = sentenceMatch.Value;
            var items = MedicationItemRe.Matches(sentence).Cast<Match>()
                .Where(m => !MedicationStopWords.Contains(m.Groups["drug"].Value))
                .ToList();
            if (items.Count == 0) continue;
            foreach (var item in items.Where(i => !i.Groups["comma"].Success))
            {
                var drug = item.Groups["drug"].Value;
                var dose = $"{item.Groups["dose"].Value} {item.Groups["unit"].Value}".Trim();
                yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                    $"Medication notation: put a comma between the medicine and its dose (\"{drug}, {dose}\").",
                    Quote: item.Value, Start: bodyOffset + sentenceMatch.Index + item.Index,
                    End: bodyOffset + sentenceMatch.Index + item.Index + item.Length,
                    FixSuggestion: $"{drug}, {dose}");
                if (++reported >= 5) yield break;
            }
            if (items.Count < 2) continue;
            var separators = new List<string>();
            for (int i = 1; i < items.Count; i++)
            {
                var from = items[i - 1].Index + items[i - 1].Length;
                separators.Add(sentence[from..items[i].Index]);
            }
            // Only a genuine list: items close together (<= 60 chars apart)
            // and joined by list punctuation, not a narrative connective.
            if (separators.Any(x => x.Length > 60 || NonListConnectiveRe.IsMatch(x))) continue;
            bool ok;
            // A 3+-item list with NO semicolon ANYWHERE is genuinely
            // ambiguous for any reader — "ranitidine, 150 mg, paracetamol,
            // 1 g, and allopurinol, 100 mg." gives no signal where one
            // medicine's entry ends and the next begins. That is a real
            // clarity defect independent of which exact separator pattern is
            // preferred, so it stays candidate-scored (OA2-20/firewall item 7:
            // "the exact house comma/semicolon form is not a universal
            // template; ambiguity ... is what scores"). Only the PATTERN
            // question — semicolons present, but "; and" vs "and" before the
            // final item — is the OA2-16 house nuance, Model-Answer-only.
            var ambiguousForCandidates = false;
            if (items.Count == 2)
            {
                ok = Regex.IsMatch(separators[0], @"\band\b", RegexOptions.IgnoreCase) && !separators[0].Contains(';');
            }
            else
            {
                // OA2-16 (latest owner override, 14 Sep 2026): semicolons go
                // BETWEEN medication-dose pairs, but there is NO additional
                // semicolon immediately before the final "and" —
                // "Drug A, dose; Drug B, dose and Drug C, dose." This
                // supersedes the older "; and final drug" house syntax.
                ok = separators.Take(separators.Count - 1).All(x => x.Contains(';'))
                     && Regex.IsMatch(separators[^1], @"\band\b", RegexOptions.IgnoreCase)
                     && !separators[^1].Contains(';');
                ambiguousForCandidates = !ok && !separators.Any(x => x.Contains(';'));
            }
            if (!ok && (input.IsModelAnswer || ambiguousForCandidates))
            {
                yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                    items.Count == 2
                        ? "Two medicines: write \"Drug, dose and Drug, dose\" (no semicolon; \"and\" before the second)."
                        : "Three or more medicines: write \"Drug, dose; Drug, dose and Drug, dose\" (semicolons BETWEEN items, then \"and\" before the final item with NO semicolon before it).",
                    Quote: sentence.Trim().Length > 100 ? sentence.Trim()[..100] + "…" : sentence.Trim());
                if (++reported >= 5) yield break;
            }
        }
    }

    private static readonly Regex ValueUnitNoSpaceRe = new(
        @"(?<![\w.])(\d+(?:\.\d+)?)(\u00b0C|mmol\/L|µmol\/L|umol\/L|nmol\/L|mg\/kg|mg\/dL|g\/dL|g\/L|U\/L|ng\/mL|mg|mcg|µg|kg|g|mL|ml|L|IU|mmol|mmHg|cm|mm|bpm|kPa|mEq|units)\b",
        RegexOptions.None);

    private static IEnumerable<LintFinding> DetectValueUnitSpacing(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        var count = 0;
        foreach (Match m in ValueUnitNoSpaceRe.Matches(s.Body))
        {
            var fix = $"{m.Groups[1].Value} {m.Groups[2].Value}";
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                $"Leave a space between a numeric value and its unit (\"{fix}\", not \"{m.Value}\").",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length, FixSuggestion: fix);
            if (++count >= 6) yield break;
        }
    }

    // OWN-W-004 — "Latest canonical form is 'DOB:' followed by the date"
    // (Garcia: "DOB 01.01.1995"; Wright: "D.O.B:").
    private static readonly Regex DobDottedRe = new(@"\bD\.\s?O\.\s?B\.?", RegexOptions.None);
    private static readonly Regex DobNoColonRe = new(@"\b(?:DOB|Dob|dob)\b(?!\s?:)", RegexOptions.None);

    private static IEnumerable<LintFinding> DetectDobColonFormat(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var text = input.LetterText ?? string.Empty;
        var dotted = DobDottedRe.Match(text);
        if (dotted.Success)
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "Write the date of birth as \"DOB:\" followed by the date (not \"D.O.B\").",
                Quote: dotted.Value, Start: dotted.Index, End: dotted.Index + dotted.Length, FixSuggestion: "DOB:");
            yield break;
        }
        var noColon = DobNoColonRe.Match(text);
        if (noColon.Success)
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "Write the date of birth as \"DOB:\" (with a colon) followed by the date.",
                Quote: noColon.Value, Start: noColon.Index, End: noColon.Index + noColon.Length, FixSuggestion: "DOB:");
        if (input.IsModelAnswer && s.ReLineIndex is not null
            && Regex.IsMatch(s.Lines[s.ReLineIndex.Value], @"\bdate of birth\b", RegexOptions.IgnoreCase))
            yield return new LintFinding(rule.Id, RuleSeverity.Critical,
                "Model Answer Re: line uses the canonical \"DOB:\" form, not \"date of birth\".",
                Quote: s.Lines[s.ReLineIndex.Value].Trim(), FixSuggestion: "DOB:");
    }

    // ---------------------------------------------------------------------
    // Number style (OWN-W-006, owner override superseding G-W-110):
    // descriptive/general numbers as words; digits only for age, dates,
    // vital signs, investigations/lab values, medication doses and clinical
    // measurements. Deliberately targets count/duration/frequency contexts
    // only (never ages, dates, doses, vitals or lab values), which is where a
    // deterministic check is reliable.
    // ---------------------------------------------------------------------

    private static readonly Regex[] DescriptiveNumberRes =
    {
        new(@"\b(?:for|over|past|last|every|within|after|about|approximately|nearly|almost|around|lasting|spanning)\s+(?:the\s+)?(?:past\s+|last\s+|next\s+)?(\d{1,2})\s+(?:days?|weeks?|months?|years?|hours?|minutes?|nights?|occasions?|episodes?|times|attacks?|sessions?|visits?|cycles?)\b", RegexOptions.IgnoreCase),
        new(@"\b(\d{1,2})\s+(?:days?|weeks?|months?|years?|hours?|minutes?|nights?)\s+(?:ago|prior|earlier|later|previously|beforehand|after|post|following)\b", RegexOptions.IgnoreCase),
        new(@"\b(\d{1,2})[\-\s](?:day|week|month|hour)s?\s+(?:history|course|period|duration|delay|stay|admission|trial|follow-up|review|wait)\b", RegexOptions.IgnoreCase),
        new(@"\b(\d{1,2})[\-\s]year\s+(?:history|course|period|duration|delay|trial|follow-up)\b", RegexOptions.IgnoreCase),
        new(@"\b(\d{1,2})\s+(?:children|siblings|sons|daughters|brothers|sisters|occasions|episodes|attacks|falls|admissions|sessions|visits|pregnancies|flights\s+of\s+stairs|nights)\b", RegexOptions.IgnoreCase),
        new(@"\b(\d{1,2})\s+times\s+(?:a|per|each)\s+(?:day|week|month|night|year)\b", RegexOptions.IgnoreCase),
        new(@"\b(\d{1,2})\s+times\s+(?:daily|weekly|monthly)\b", RegexOptions.IgnoreCase),
        // OA2-15 (14 Sep 2026): the descriptive counts and durations the owner
        // named explicitly — "twenty cigarettes daily", "a forty-eight-hour
        // ketamine infusion". Medication strengths, dates, clinical
        // measurements and doses stay numeric and never match here.
        new(@"\b(\d{1,3})\s+(?:cigarettes?|standard\s+drinks?)\b", RegexOptions.IgnoreCase),
        new(@"\b(\d{1,2})[\-\s](?:hour|day|week|month)s?\s+(?:[a-z]+\s+){0,2}(?:infusion|course|regimen|regime|programme|program|therapy|treatment|block|trial|admission|stay)\b", RegexOptions.IgnoreCase),
    };

    private static IEnumerable<LintFinding> DetectNumberStyleRev8(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        var seen = new HashSet<int>();
        var count = 0;
        foreach (var re in DescriptiveNumberRes)
        {
            foreach (Match m in re.Matches(s.Body))
            {
                var g = m.Groups[1];
                if (!seen.Add(g.Index)) continue;
                yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                    $"Write descriptive numbers as words (\"{m.Value}\"). Digits are only for age, dates, vital signs, investigation values, medication doses and clinical measurements.",
                    Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length);
                if (++count >= 5) yield break;
            }
        }
    }

    // OWN-W-033 — professional clinical register (Weir "tired"/"sluggish",
    // "MRI imaging"; Wright "felt something pop", "with no GP") plus the
    // owner's 13 Sep 2026 additions: vague duration ("for a long time"),
    // emotional/judgmental observation ("appeared anxious") and stripped
    // clinical precision are register failures in their own right, and the
    // 14 Sep 2026 addendum (OA-08) adds the ungrammatical "overweight long
    // term" ("has long been overweight" is the approved form). A Model
    // Answer must render case-note wording in premium clinical English
    // ("fatigue", "lethargy"), never copy colloquial source words verbatim —
    // so there is deliberately NO case-notes exemption here.
    private static readonly Regex ColloquialRe = new(
        @"\bMRI imaging\b|\bCT scan imaging\b|\bfelt something\s+['‘’]?pop['‘’]?|\bsomething\s+['‘’]pop['‘’]|\bsluggish\b|\bwith no GP\b|\bno GP\b|\bkids?\b|\bguys?\b|\ba lot of\b|\blots of\b|\bpretty (?:bad|severe|good|much)\b|\bokay\b|\bOK\b|\bgot (?:better|worse)\b|\bstuff\b|\btired\b|\bfeeling down\b|\bup and down\b|\btummy\b|\bpee\b|\bpoo\b|\bfor a long time\b|\boverweight\s+long\s+term\b|\bbruising\s+to\s+(?:his|her|their|the)\b|\b(?:appeared|seemed)\s+(?:anxious|agitated|confused|distressed)\b",
        RegexOptions.None);

    private static IEnumerable<LintFinding> DetectColloquialRegister(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        var count = 0;
        foreach (Match m in ColloquialRe.Matches(s.Body))
        {
            yield return new LintFinding(rule.Id, input.IsModelAnswer ? RuleSeverity.Critical : RuleSeverity.Minor,
                $"\"{m.Value}\" is informal, vague or judgmental for a clinical letter. Use precise, neutral professional wording (e.g. fatigue, lethargy, long-term obesity, MRI).",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length);
            if (++count >= 5) yield break;
        }
    }

    // Rev8 (owner directive, 13 Sep 2026, McDonald): "amitriptyline ceased
    // due to difficulty urinating" is note-form English. A medication
    // followed by a bare past participle must use the passive ("was
    // discontinued"). Owner addendum OA-09 (14 Sep 2026) adds the Garcia
    // defect "dexamethasone continued six-hourly" — "continued" joins the
    // participle set for drug-name subjects. Only drug-name subjects are
    // flagged; genuinely intransitive subjects ("the pain ceased",
    // "bleeding stopped", "follow-up continued", "she has responded well")
    // and determiner-led general subjects ("the treatment stopped") stay valid.
    private static readonly Regex MedicationPassiveRe = new(
        @"(?:(?<det>\b(?:the|a|an|his|her|its|their)\s+)|(?<aux>\b(?:was|were|is|are|be|been|being|has|have|had)\s+))?\b(?<drug>[A-Za-z][A-Za-z\-]{2,})\s+(?<participle>ceased|discontinued|commenced|initiated|recommenced|stopped|started|continued|weaned|withdrawn)\b",
        RegexOptions.IgnoreCase);

    // Revalidation fix (2026-09-14, part 2): people and institutions are
    // never medication subjects, so "<person> commenced/continued/..." is
    // valid active-voice clinical English, not note-form drug shorthand.
    private static readonly HashSet<string> PersonSubjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "he", "she", "they", "it", "we", "you", "who", "i",
        "mother", "mothers", "father", "fathers", "sister", "brother", "son", "daughter",
        "husband", "wife", "parents", "parent", "grandmother", "grandfather", "family",
        "doctor", "doctors", "nurse", "nurses", "gp", "physician", "specialist",
        "consultant", "surgeon", "practitioner", "patient", "students", "student",
        "friend", "friends", "staff", "team", "hospital", "clinic", "centre", "center",
        "ward", "school", "dr", "mr", "mrs", "ms", "miss", "mx",
    };

    private static readonly HashSet<string> IntransitiveParticipleSubjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "pain", "pains", "seizure", "seizures", "fit", "fits", "bleeding", "symptom", "symptoms",
        "smoking", "vomiting", "nausea", "withdrawal", "tremor", "tremors", "spasm", "spasms",
        "cough", "diarrhoea", "diarrhea", "constipation", "sweating", "ache", "aches",
        "treatment", "therapy", "course", "dose", "medication", "medications", "drug", "drugs",
        // Intransitive nouns that legitimately take "continued" in clinical
        // prose ("follow-up continued", "review continued for four weeks").
        "follow-up", "followup", "review", "reviews", "monitoring", "surveillance",
        "observation", "observations", "care", "rehabilitation", "physiotherapy",
        "admission", "stay", "recovery", "improvement", "treatment", "therapy",
    };

    private static IEnumerable<LintFinding> DetectMedicationPassiveGrammar(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        var count = 0;
        foreach (Match m in MedicationPassiveRe.Matches(s.Body))
        {
            if (m.Groups["aux"].Success || m.Groups["det"].Success) continue;
            var drug = m.Groups["drug"].Value;
            if (MedicationStopWords.Contains(drug) || IntransitiveParticipleSubjects.Contains(drug)) continue;
            // Revalidation fix (2026-09-14): "I would be grateful for your
            // continued care." and "He has become socially withdrawn." are
            // correct clinical English. A possessive determiner or an adverb
            // (the "-ly" form) can never be a medication subject, so the
            // match is a false positive, not a note-form defect.
            if (drug.Equals("your", StringComparison.OrdinalIgnoreCase)
                || drug.Equals("my", StringComparison.OrdinalIgnoreCase)
                || drug.Equals("our", StringComparison.OrdinalIgnoreCase)
                || (drug.Length > 3 && drug.EndsWith("ly", StringComparison.OrdinalIgnoreCase)))
                continue;
            // Revalidation fix (2026-09-14, part 2): a person or institution
            // can never be a medication subject. "She commenced smoking",
            // "a visiting school doctor commenced him on doxycycline" and
            // "the hospital commenced the infusion" are correct clinical
            // English; only a medication-name subject is note-form.
            if (PersonSubjects.Contains(drug)) continue;
            var participle = m.Groups["participle"].Value;
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                $"\"{drug} {participle}\" is note-form English. Use the passive voice, e.g. \"{drug} was discontinued\" or \"{drug} was commenced\".",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length);
            if (++count >= 3) yield break;
        }
    }

    // Rev8 (owner directive, 13 Sep 2026, McDonald): clinical precision must
    // not be trimmed to save words — the source's daily frequency for smoking
    // and alcohol intake has to survive into the Model Answer.
    // OA2-15 (14 Sep 2026): canonical Model Answers write descriptive counts
    // as words ("twenty cigarettes daily"), so a digit-only anchor would let
    // this safety check go silently dead the moment the number is spelled out.
    private const string CountWordOrDigitPattern =
        @"\d{1,3}|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fifteen|twenty|thirty|forty|fifty|sixty";

    private static readonly Regex CigarettesWithoutFrequencyRe = new(
        @"\b(?:" + CountWordOrDigitPattern + @")\s+cigarettes\b(?!\s*(?:a\s+day|per\s+day|daily|each\s+day))",
        RegexOptions.IgnoreCase);

    private static readonly Regex StandardDrinksWithoutFrequencyRe = new(
        @"\bstandard\s+drinks\b(?![^.;\n]{0,15}\b(?:a\s+day|per\s+day|daily))",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectLifestyleFrequencyPrecision(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        var count = 0;
        foreach (Match m in CigarettesWithoutFrequencyRe.Matches(s.Body))
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "State the daily frequency recorded in the case notes (\"twenty cigarettes daily\") — do not drop it to save words.",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length);
            if (++count >= 3) yield break;
        }
        foreach (Match m in StandardDrinksWithoutFrequencyRe.Matches(s.Body))
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "State the daily frequency recorded in the case notes (\"six to ten standard drinks per day\") — do not drop it to save words.",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length);
            if (++count >= 3) yield break;
        }
    }

    // OWN-W-034 — "Management content belongs before the closure; closure
    // should remain a closure" (Wright: request, then "She was advised ...").
    private static readonly Regex ClosureRequestRe = new(
        @"\bI would be (?:most |very )?grateful\b|\bI would appreciate\b|\bcould you\b|\bwould you\b|\bplease (?:assess|review|see|arrange|consider|continue|monitor)\b|\bI would like to request\b|\bI kindly request\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex ManagementAfterRequestRe = new(
        @"\b(?:was|were|has been|have been|is being|had been)\s+(?:advised|commenced|prescribed|started|given|instructed|educated|counselled|counseled|referred|treated|managed|recommended)\b",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectClosureContainsManagement(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.BodyParagraphs.Count < 2) yield break;
        // The request lives in its own closure paragraph (OA-06) or the
        // combined final paragraph (candidate letters) — check both.
        foreach (var paragraph in s.BodyParagraphs.TakeLast(2))
        {
            var sentences = SplitSentences(paragraph);
            var requestIdx = sentences.FindIndex(x => ClosureRequestRe.IsMatch(x));
            if (requestIdx < 0) continue;
            foreach (var sentence in sentences.Skip(requestIdx + 1))
            {
                if (ManagementAfterRequestRe.IsMatch(sentence) && !ContactOfferMeaningRe.IsMatch(sentence))
                {
                    yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                        "Management/history details appear after the closing request. Move them into the body before the closure; the closure should only close the letter.",
                        Quote: sentence.Length > 90 ? sentence[..90] + "…" : sentence);
                    yield break;
                }
            }
        }
    }

    // OWN-W-026 / R04.1 / R06.13 — professional designation present after the
    // closing phrase with the approved 1-2 blank lines between them.
    private static IEnumerable<LintFinding> DetectSignoffDesignationPresent(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.YoursIndex is null) yield break;
        var after = s.Lines.Skip(s.YoursIndex.Value + 1).ToList();
        var firstNonEmpty = after.FindIndex(l => l.Trim().Length > 0);
        if (firstNonEmpty < 0)
        {
            yield return new LintFinding(rule.Id, input.IsModelAnswer ? RuleSeverity.Critical : RuleSeverity.Minor,
                "Add the professional designation (e.g. Doctor, Nurse, Pharmacist, Physiotherapist) after the closing phrase.");
            yield break;
        }
        if (input.IsModelAnswer && (firstNonEmpty < 1 || firstNonEmpty > 2))
            yield return new LintFinding(rule.Id, RuleSeverity.Critical,
                "Leave one (or at most two) blank lines between \"Yours sincerely/faithfully,\" and the professional designation.");
    }

    // OWN-W-036 / R04.1-R04.4 — Model Answer layout: address block before the
    // date, exactly one blank line between every paragraph (no line breaks
    // inside a paragraph, no double blank lines) and exactly one blank line
    // after the Re: line. Candidate layout keeps its existing, lighter checks.
    private static IEnumerable<LintFinding> DetectModelAnswerLayout(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        // Global Model Answer addendum §2: address then date OR date then
        // address are both acceptable — only a missing block is a defect.
        var recipientBeforeDate = s.DateIndex is int d && s.Lines.Take(d).Any(l => l.Trim().Length > 0);
        var recipientAfterDate = s.DateIndex is int d2 && s.SalutationIndex is int si && si > d2
            && s.Lines.Skip(d2 + 1).Take(si - d2 - 1).Any(l => l.Trim().Length > 0);
        if (s.DateIndex is null || !(recipientBeforeDate || recipientAfterDate))
            yield return new LintFinding(rule.Id, RuleSeverity.Critical,
                "A Model Answer needs the recipient's name/address block and the date (in either order), separated by one blank line.");
        if (s.ReLineIndex is null || s.YoursIndex is null) yield break;
        var start = s.ReLineIndex.Value + 1;
        var end = s.YoursIndex.Value;
        if (start + 1 < end && s.Lines[start].Trim().Length == 0 && s.Lines[start + 1].Trim().Length == 0)
            yield return new LintFinding(rule.Id, RuleSeverity.Critical,
                "Leave exactly ONE blank line after the Re: line (found more than one).");
        for (int i = start; i + 1 < end; i++)
        {
            var a = s.Lines[i].Trim();
            var b = s.Lines[i + 1].Trim();
            if (a.Length > 0 && b.Length > 0)
            {
                yield return new LintFinding(rule.Id, RuleSeverity.Critical,
                    "Each paragraph must be one block separated by exactly one blank line; a line break inside the body merges or splits paragraphs.",
                    Quote: b.Length > 60 ? b[..60] + "…" : b);
                yield break;
            }
            // i > start: a double blank straight after Re: is reported above.
            if (i > start && a.Length == 0 && b.Length == 0)
            {
                yield return new LintFinding(rule.Id, RuleSeverity.Critical,
                    "Use exactly one blank line between paragraphs (found two or more).");
                yield break;
            }
        }
    }

    // OWN-W-022 — linker punctuation: sentence-initial "However, ..." (capital +
    // comma); mid-sentence "...; however, ..." (semicolon, lowercase, comma).
    private static readonly Regex SentenceInitialLinkerNoCommaRe = new(
        @"(?:^|[.!?]\s+)(However|Therefore|Consequently|Subsequently|Additionally|Thus|In addition)(?!\s*,)(?!\s+to\b)\b",
        RegexOptions.Multiline);

    private static readonly Regex SemicolonCapitalLinkerRe = new(
        @";\s*(However|Therefore|Consequently|Subsequently|Additionally|Thus|In addition)\b");

    private static readonly Regex SemicolonLinkerNoCommaRe = new(
        @";\s*(however|therefore|consequently|subsequently|additionally|thus|in addition)(?!\s*,)(?!\s+to\b)\b");

    private static IEnumerable<LintFinding> DetectLinkerCommaAndCase(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in SentenceInitialLinkerNoCommaRe.Matches(s.Body))
        {
            var g = m.Groups[1];
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                $"At the start of a sentence, place a comma after \"{g.Value}\" (\"{g.Value}, ...\").",
                Quote: g.Value, Start: bodyOffset + g.Index, End: bodyOffset + g.Index + g.Length, FixSuggestion: g.Value + ",");
        }
        foreach (Match m in SemicolonCapitalLinkerRe.Matches(s.Body))
        {
            var g = m.Groups[1];
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                $"After a semicolon, write the linker in lowercase (\"; {g.Value.ToLowerInvariant()}, ...\").",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: "; " + g.Value.ToLowerInvariant() + ",");
        }
        foreach (Match m in SemicolonLinkerNoCommaRe.Matches(s.Body))
        {
            var g = m.Groups[1];
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                $"When \"{g.Value}\" joins two complete clauses after a semicolon, place a comma after it (\"; {g.Value}, ...\").",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: "; " + g.Value + ",");
        }
    }

    // Subject that starts a new independent clause after a linker — used to
    // tell a genuine run-on/comma splice ("stable however she deteriorated")
    // apart from a parenthetical or adverbial use ("He did not, however,
    // attend" / "He was therefore referred"), which is standard English.
    private const string ClauseSubjectPattern =
        @"(?:I|he|she|they|it|we|you|this|that|there|these|those|his|her|their|its|the|a|an|Mr|Mrs|Ms|Miss|Dr|[A-Z][a-z]+\s+(?:was|is|has|had|were|are|will|would|did|does))\b";

    private static readonly Regex AdverbialLinkerPrecursorRe = new(
        @"\b(?:was|were|is|are|be|been|being|has|have|had|will|would|should|could|may|might|must|can|did|does|do|not)\s*,?\s*$",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectLinkerRunOn(OetRule rule, WritingLintInput input, LetterStructure s, string linkerPattern, string display)
    {
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        var re = new Regex($@"(?<pre>[^\n;.!?]{{3,}}?)(?<sep>,\s*|\s+)(?<lk>{linkerPattern})\b(?!\s+(?:to|with)\b)\s*,?\s+(?<subj>{ClauseSubjectPattern})",
            RegexOptions.IgnoreCase);
        foreach (Match m in re.Matches(s.Body))
        {
            var pre = m.Groups["pre"].Value;
            if (pre.TrimEnd().EndsWith(';')) continue;
            // Adverbial/parenthetical use inside ONE clause ("He was therefore
            // a candidate", "He was, however, the only ...") — standard English.
            if (AdverbialLinkerPrecursorRe.IsMatch(pre)) continue;
            var lk = m.Groups["lk"];
            // OA2-07: a Model Answer avoids the narrative semicolon entirely, so
            // it is never offered the "...; however, ..." repair — only the full
            // stop. Candidates keep both: the semicolon form is standard English
            // and is protected by the candidate false-positive firewall.
            var capitalised = char.ToUpperInvariant(display[0]) + display[1..];
            var repair = input.IsModelAnswer
                ? "Start a new sentence: \"... . " + capitalised + ", ...\"."
                : "Use \"...; " + display + ", ...\" or start a new sentence: \"... . " + capitalised + ", ...\".";
            yield return new LintFinding(rule.Id, ModeSeverity(input, rule.Severity == RuleSeverity.Critical ? RuleSeverity.Critical : RuleSeverity.Major),
                "\"" + lk.Value + "\" joins two complete clauses here. " + repair,
                Quote: m.Value.Trim().Length > 90 ? m.Value.Trim()[..90] + "…" : m.Value.Trim(),
                Start: bodyOffset + lk.Index, End: bodyOffset + lk.Index + lk.Length);
        }
    }

    private static int BodyOffset(LetterStructure s)
    {
        if (s.Body.Length == 0) return 0;
        var firstBody = Math.Max((s.ReLineIndex ?? s.SalutationIndex ?? 0) + 1, (s.SalutationIndex ?? 0) + 1);
        var offset = 0;
        for (int i = 0; i < firstBody && i < s.Lines.Length; i++) offset += s.Lines[i].Length + 1;
        return offset;
    }

    // ---------------------------------------------------------------------
    // Ultimate Final handoff (13 Sep 2026) detectors — the permanent
    // false-READY Garcia regression fixture (§11.5) and the discharge-vs-
    // simple-update clarification (§3.3). A green checkmark never overrides
    // a visible rule breach, so each previously-missed defect class gets a
    // deterministic detector plus an injected-defect regression test.
    // ---------------------------------------------------------------------

    // Re: line must carry the patient's FULL identification (§4.4): adults
    // = title + first + last name; children = first + last with no title.
    // False-READY fixture: "Re: Ms Garcia" (surname only) passed the old
    // gate with "zero findings" — it must fail.
    private static IEnumerable<LintFinding> DetectReLineFullName(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.ReLineIndex is null) yield break;
        var reLine = s.Lines[s.ReLineIndex.Value];
        var titled = ReTitledNameRe.Match(reLine);
        if (titled.Success)
        {
            // Same trailing-identifier trimming as ResolvePatientName so
            // "Re: Mrs Rose Garcia DOB: 1 January 1945" counts the name
            // tokens only.
            var tokens = new List<string> { titled.Groups[2].Value };
            if (titled.Groups[3].Success) tokens.Add(titled.Groups[3].Value);
            if (titled.Groups[4].Success) tokens.Add(titled.Groups[4].Value);
            while (tokens.Count > 1 && (NonNameReWords.Contains(tokens[^1]) || ReIdentifierWords.Contains(tokens[^1])))
                tokens.RemoveAt(tokens.Count - 1);
            if (tokens.Count < 2)
            {
                var title = titled.Groups[1].Value;
                yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                    $"The Re: line identifies the patient by surname only (\"{title} {tokens[0]}\"). Use the full name — title + first + last name for an adult, first + last name for a child.",
                    Quote: reLine.Trim());
            }
            yield break;
        }
        var untitled = ReUntitledNameRe.Match(reLine);
        if (untitled.Success
            && !NonNameReWords.Contains(untitled.Groups[1].Value)
            && !NonNameReWords.Contains(untitled.Groups[2].Value)
            && !input.PatientIsMinor)
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "An adult patient's Re: line uses title + first + last name. Add the professional title (Mr/Mrs/Ms/Dr) before the full name.",
                Quote: reLine.Trim(),
                FixSuggestion: $"Re: Mr/Ms {untitled.Groups[1].Value} {untitled.Groups[2].Value}");
        }
    }

    // Letter-uses discharge/transfer-of-care language while the canonical
    // case notes document NO admission or discharge (§3.3): a simple update
    // must never invent "being discharged" / "ready for discharge" /
    // "returned to your care". Deliberately scoped to NON-discharge-routed
    // letters: for an LT-DG task the classification itself is the semantic
    // validator's decision (notes + exact task, never the catalogue code
    // alone), and this deterministic layer only guards the invented-fact
    // direction it can prove from the notes snapshot.
    private static readonly Regex DischargeLanguageRe = new(
        @"\bready for discharge\b|\bbeing discharged\b|\b(?:was|were|has been|is now|will be|to be) discharged\b" +
        @"|\bdischarged (?:home|today|this (?:morning|afternoon|week)|from)\b|\bfollowing (?:his|her|their) discharge\b" +
        @"|\bon discharge\b|\bdischarge (?:plan|medications?|medication|summary|date|destination|arrangements?)\b" +
        @"|\breturned to (?:your|the) care\b|\btransfer of care back\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex DischargeFalseFriendRe = new(
        @"\b(?:vaginal|ocular|ear|nasal|nipple|wound|urethral|post.?operative)\s+discharge\b",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectDischargeLanguageUnsupported(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (input.CaseNotesMarkers is not { } markers) yield break;
        if (markers.AdmissionDocumented || markers.DischargeDocumented) yield break;
        if (string.Equals(input.LetterType, "discharge", StringComparison.OrdinalIgnoreCase)) yield break;
        if (s.Body.Length == 0) yield break;
        var body = DischargeFalseFriendRe.Replace(s.Body, string.Empty);
        var m = DischargeLanguageRe.Match(body);
        if (!m.Success) yield break;
        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
            "The case notes do not document an admission or discharge, so this discharge/transfer-of-care wording is an invented fact. Derive the letter type from the case notes and the exact task — a task without supported admission/discharge is a simple update.",
            Quote: m.Value, Start: BodyOffset(s) + m.Index, End: BodyOffset(s) + m.Index + m.Length);
    }

    // False-READY fixture §11.5: "Examination showed afebrile ..." — an
    // adjective cannot be the object of "showed"; the construction is
    // grammatically incomplete and must fail Language, not pass as clean.
    private static readonly Regex IncompleteClinicalConstructionRe = new(
        @"\b(?:Examination|Observations?|Obs|Assessment|Examination findings|Initial assessment)\s+(?:showed|revealed|demonstrated|indicated)\s+(?:afebrile|febrile|well|stable|alert|asymptomatic|drowsy|lethargic|breathless|agitated|haemodynamically stable|hemodynamically stable)\b",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectIncompleteClinicalConstruction(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in IncompleteClinicalConstructionRe.Matches(s.Body))
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                $"\"{m.Value}\" is a grammatically incomplete construction — the verb needs a noun phrase or a full clause (e.g. \"on examination she was afebrile\" or \"examination showed no fever\").",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length);
        }
    }

    // ---------------------------------------------------------------------
    // Owner Clarifications Addendum (14 Sep 2026) — OA-01..OA-15. Each
    // detector implements one canonical owner rule; registry rows OA-01..
    // OA-15 live in docs/canonical-rules/OET_AI_Rules_Master.jsonl and the
    // injected-defect regression coverage in
    // WritingRev8RegressionFixtureTests.cs.
    // ---------------------------------------------------------------------

    // OA-01 + OA-04 — introduction purpose. Two deterministic failure modes:
    // (a) the vague hand-off construction the owner banned verbatim ("given
    // a working assessment of possible ..."), in BOTH modes — it is vague
    // professional English in any letter; (b) Model Answer: the letter's
    // closure carries a request while the introduction names only the topic
    // ("I am writing to update you regarding Ms Garcia's diagnosis and
    // treatment ...") — the reader action must be stated immediately.
    private static readonly Regex VagueWorkingAssessmentRe = new(
        @"\bgiven a working assessment\b|\bworking assessment of\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex IntroActionMarkerRe = new(
        @"\b(?:request|requests|requesting|refer|referral|referring|assessment|assess|review|arrange|arranging|assist|assistance|support|transfer|transferring|opinion|follow-up|followup|management of|admission|outline|notify|advise me)\b",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectIntroPurposeVague(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length == 0 || s.BodyParagraphs.Count == 0) yield break;
        var intro = s.BodyParagraphs[0];
        var bodyOffset = BodyOffset(s);
        foreach (Match m in VagueWorkingAssessmentRe.Matches(intro))
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "The introduction hides the request behind a vague working-assessment construction. State the direct task-specific request, e.g. \"I am writing to request your neurological assessment and management of Mr Weir, who has presented with ...\".",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length);
            yield break;
        }
        if (!input.IsModelAnswer || s.BodyParagraphs.Count < 2) yield break;
        var requestExists = s.BodyParagraphs.Skip(1).Any(p => ClosureRequestRe.IsMatch(p));
        if (!requestExists) yield break;
        if (!IntroActionMarkerRe.IsMatch(intro))
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "The introduction states only the topic, not the task-specific purpose or request. State the reader's required action immediately (e.g. \"... and to request your assistance with the required follow-up of her close contacts\"). If the task genuinely requests no action, state the informational purpose precisely instead.",
                Quote: intro.Length > 100 ? intro[..100] + "…" : intro);
        }
    }

    // OA-06 — closure paragraphing. Model Answer only (candidates are never
    // forced to copy house paragraphing). The task-specific request ("I
    // would be grateful if you could ...") must START its own paragraph,
    // and the universal contact-offer sentence must be the SEPARATE final
    // paragraph. Injected defects caught: request merged into the prior
    // body paragraph; contact offer merged into the request paragraph.
    private static IEnumerable<LintFinding> DetectClosureRequestParagraph(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.BodyParagraphs.Count < 2) yield break;
        var requestParagraphIndex = -1;
        for (var i = s.BodyParagraphs.Count - 1; i >= 0; i--)
        {
            if (!ClosureRequestRe.IsMatch(s.BodyParagraphs[i])) continue;
            requestParagraphIndex = i;
            break;
        }
        if (requestParagraphIndex < 0) yield break;
        var requestParagraph = s.BodyParagraphs[requestParagraphIndex];
        var offset = BodyOffset(s) + s.Body.IndexOf(requestParagraph, StringComparison.Ordinal);

        // OA2-05 / OA2-06 — the request paragraph must BE the closure: either
        // the last body paragraph, or the second-to-last with only the
        // contact-offer paragraph after it. The checks below this point only
        // ever look at s.BodyParagraphs[^1], so a request paragraph followed
        // by two or more further body paragraphs (clinical content stranded
        // after the "closure") previously passed with zero findings — the
        // exact OA2-01 "visible defect + validator PASS" class Addendum Two
        // exists to close.
        if (requestParagraphIndex < s.BodyParagraphs.Count - 2)
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
                "The task-specific request is not the closure — it must be the letter's last or second-to-last body paragraph, immediately before the contact-offer paragraph. Move any content that follows it back into the body above.",
                Quote: requestParagraph.Length > 90 ? requestParagraph[..90] + "…" : requestParagraph,
                Start: offset, End: offset + requestParagraph.Length);
        }

        var sentences = SplitSentences(requestParagraph);
        if (!ClosureRequestRe.IsMatch(sentences[0]))
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
                "The task-specific request must START its own closure paragraph — content preceding \"I would be grateful if you could ...\" belongs in the body above.",
                Quote: sentences[0].Length > 90 ? sentences[0][..90] + "…" : sentences[0],
                Start: offset, End: offset + requestParagraph.Length);
        }
        var final = s.BodyParagraphs[^1];
        var finalIsRequest = requestParagraphIndex == s.BodyParagraphs.Count - 1;
        if (finalIsRequest)
        {
            // The request paragraph is also the last paragraph: if it also
            // carries the contact offer, the two canonical paragraphs merged.
            if (ContactOfferMeaningRe.IsMatch(final))
            {
                yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
                    "The universal contact-offer sentence must be a separate final paragraph before the sign-off — it may not share the request paragraph.",
                    Quote: final.Length > 90 ? final[..90] + "…" : final);
            }
            yield break;
        }
        // A paragraph after the request exists (the intended offer paragraph).
        // It must contain ONLY the contact offer.
        var finalSentences = SplitSentences(final);
        if (finalSentences.Count != 1 || !ContactOfferMeaningRe.IsMatch(final))
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
                "The final paragraph must contain only the universal contact-offer sentence (e.g. \"Should there be any queries, please do not hesitate to contact me.\").",
                Quote: final.Length > 90 ? final[..90] + "…" : final);
        }
    }

    // OA-09 — missing auxiliary in the change-of-treatment passive ("treatment
    // changed to benzylpenicillin"). "X was changed to ..." or an explicit
    // active subject is required. Legitimately intransitive subjects
    // ("the plan changed", "his approach changed") are exempt.
    private static readonly Regex TreatmentChangeRe = new(
        @"(?<aux>\b(?:was|were|is|are|be|been|being|has|have|had)\s+)?\b(?<subject>[A-Za-z][A-Za-z\-]{2,})\s+changed\s+(?:to|from)\b",
        RegexOptions.IgnoreCase);

    private static readonly HashSet<string> IntransitiveChangeSubjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "plan", "approach", "policy", "practice", "pattern", "attitude", "language", "wording",
        "name", "title", "address", "landscape", "picture", "situation", "context",
    };

    private static readonly HashSet<string> AuxiliaryVerbSubjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "was", "were", "is", "are", "be", "been", "being", "has", "have", "had",
    };

    private static IEnumerable<LintFinding> DetectTreatmentChangeGrammar(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in TreatmentChangeRe.Matches(s.Body))
        {
            if (m.Groups["aux"].Success) continue;
            var subject = m.Groups["subject"].Value;
            // The optional aux group lets the regex otherwise match a correct
            // "was changed to" with the AUXILIARY as the subject — that form
            // is exactly the required passive and must never fire.
            if (IntransitiveChangeSubjects.Contains(subject) || AuxiliaryVerbSubjects.Contains(subject)) continue;
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                $"\"{subject} changed to ...\" is missing the auxiliary — write \"{subject} was changed to ...\" or supply an explicit active subject.",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: $"{subject} was changed to ...");
        }
    }

    // OA-10 — investigation results must be coordinated grammatically: two
    // finite result clauses joined only by a comma is a comma splice
    // ("the white cell count was 14.0, the CRP was 150"). Use "and", or
    // split into short sentences. The middle segment may contain decimal
    // points (values like 14.0x10^9/L) but never a sentence-ending ". ";
    // scoped to clause-final was/were on both sides so article/telegraphic
    // results lists never false-fire.
    private static readonly Regex ResultsCommaSpliceRe = new(
        @"\b(?:was|were)\s+(?:(?!\.\s)[^;\n]){1,60}?,\s*(?:the\s+)?(?!which\b|that\b|who\b|whom\b|whose\b|because\b|although\b|though\b|since\b|while\b|when\b|if\b|unless\b|until\b|but\b|and\b|or\b|with\b|despite\b|as\b)[A-Za-z][A-Za-z\- ]{1,38}?\s+(?:was|were)\b",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectResultsCommaSplice(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in ResultsCommaSpliceRe.Matches(s.Body))
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "Two independent result clauses are joined only by a comma (comma splice). Use \"and\" between the two linked values, or split into short sentences.",
                Quote: m.Value.Trim().Length > 90 ? m.Value.Trim()[..90] + "…" : m.Value.Trim(),
                Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length);
            yield break;
        }
    }

    // OA-12 — canonical Model Answers write "type two diabetes mellitus" in
    // words (digits remain for clinical values, doses, dates, age). Candidate
    // answers are NEVER penalised for "type 2 diabetes mellitus": this check
    // is deliberately Model Answer only.
    private static readonly Regex DiabetesDigitsRe = new(
        @"\btype\s+(?:2|II|ii)\s+diabetes\b",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectDiabetesTypeWords(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in DiabetesDigitsRe.Matches(s.Body))
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "Model Answers write \"type two diabetes mellitus\" in words (candidate answers may keep \"type 2\"; this is a canonical-house-form rule, not a language error).",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: "type two diabetes mellitus");
        }
    }

    // OA-15 — respiratory rate carries its unit in full: "22 breaths/min".
    // A bare "22 /min" is stripped clinical style in a Model Answer (pulse
    // keeps "bpm"; mL/min and other unit-prefixed rates never match).
    private static readonly Regex BarePerMinuteRe = new(
        @"(?<![\w.])(?<value>\d+(?:\.\d+)?)\s*\/\s*min\b",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectRespiratoryRateUnitStyle(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in BarePerMinuteRe.Matches(s.Body))
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                $"Write the respiratory rate with its unit in full (\"{m.Groups["value"].Value} breaths/min\", never \"{m.Value}\").",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: $"{m.Groups["value"].Value} breaths/min");
        }
    }

    // OA-08 (McDonald) — "drinking over six to ten standard drinks daily" is
    // an impossible quantity: a range is never prefixed with over/above/more
    // than. Restore the exact source-supported quantity ("six to ten
    // standard drinks daily").
    private static readonly Regex IllogicalQuantityRangeRe = new(
        @"\b(?:over|above|more than|fewer than|less than|under)\s+(?:\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)\s+to\s+(?:\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)\b",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectIllogicalQuantityRange(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in IllogicalQuantityRangeRe.Matches(s.Body))
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
                $"\"{m.Value}\" is an illogical quantity — a range cannot sit under over/above/more than. Restore the exact source-supported quantity (e.g. \"six to ten standard drinks daily\").",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length);
        }
    }

    // OA-08 (Taylor) — "possible removal" leaves the clinical object
    // unnamed; the source supports "possible tophus removal" (or "possible
    // removal of the tophus"). Never vague where the source names the
    // object.
    private static readonly Regex VagueClinicalObjectRe = new(
        @"\bpossible\s+(?:removal|excision|extraction|repair|replacement|insertion|drainage|biopsy)(?!\s+(?:of|by)\b)",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectVagueClinicalObject(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in VagueClinicalObjectRe.Matches(s.Body))
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                $"\"{m.Value}\" does not name its object. State the exact source-supported object (e.g. \"possible tophus removal\" or \"possible removal of the tophus\").",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length);
        }
    }

    // OA source-fidelity (Garcia) — the letter date is never invented: a
    // date LATER than every date documented in the canonical case notes is
    // an invented fact. Runs whenever the canonical notes are supplied
    // (Model Answer gate); the date itself must be a whole-line date.
    // Abbreviated month names (Jan/Feb/.../Sept/Dec) are accepted alongside
    // the full forms: canonical case notes routinely write "DOB 14 Nov 1969",
    // and a source-supported date of birth must still ground a Re: line
    // written in full ("DOB: 14 November 1969"). DateTime.TryParse with the
    // invariant culture parses every abbreviation, so only the token regex
    // needed widening.
    private const string MonthNames =
        @"January|February|March|April|May|June|July|August|September|October|November|December|" +
        @"Jan\.?|Feb\.?|Mar\.?|Apr\.?|Jun\.?|Jul\.?|Aug\.?|Sept\.?|Sep\.?|Oct\.?|Nov\.?|Dec\.?";

    private static readonly Regex DateTokenRe = new(
        $@"\b(?:(?<d>\d{{1,2}})(?:st|nd|rd|th)?\s+(?<mon>{MonthNames})\s+(?<y>\d{{4}})|(?<mon2>{MonthNames})\s+(?<d2>\d{{1,2}})(?:st|nd|rd|th)?,?\s+(?<y2>\d{{4}}))\b",
        RegexOptions.IgnoreCase);

    private static bool TryParseDateToken(Match m, out DateTime date)
    {
        var dayGroup = m.Groups["d"].Success ? m.Groups["d"] : m.Groups["d2"];
        var monGroup = m.Groups["mon"].Success ? m.Groups["mon"] : m.Groups["mon2"];
        var yearGroup = m.Groups["y"].Success ? m.Groups["y"] : m.Groups["y2"];
        if (DateTime.TryParse(
                $"{dayGroup.Value} {monGroup.Value} {yearGroup.Value}",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out date))
            return true;
        date = default;
        return false;
    }

    private static IEnumerable<LintFinding> DetectLetterDateUnsupported(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (input.CaseNotesText is not { Length: > 0 } notes || s.DateIndex is null) yield break;
        if (!TryParseDateToken(DateTokenRe.Match(s.Lines[s.DateIndex.Value]), out var letterDate)) yield break;
        var latestNoteDate = (DateTime?)null;
        foreach (Match m in DateTokenRe.Matches(notes))
        {
            if (!TryParseDateToken(m, out var noteDate)) continue;
            // Revalidation fix (2026-09-14, part 3): a date of birth is an
            // identity fact, not a documented treatment date. Greerson's
            // notes carry "DOB 09.10.1951", which made every sane letter
            // date "later than every documented date" — an invented-date
            // finding on a perfectly supported letter. Birth dates are
            // excluded from the treatment-date ceiling.
            // The DOB label scopes only the date it introduces: look back no
            // further than the previous sentence/line boundary (and at most 60
            // characters). A fixed-width look-back swallowed a FOLLOWING
            // admission date whenever the DOB label sat within 60 characters
            // of it ("DOB 09.10.1951. Admitted 24 July 1951"), which left no
            // treatment-date ceiling at all and masked genuinely invented
            // letter dates.
            var lookBack = Math.Max(0, m.Index - 60);
            var windowStart = lookBack;
            for (var i = m.Index - 1; i >= lookBack; i--)
            {
                if (notes[i] is '.' or '\n' or '\r' or ';' or '|') { windowStart = i + 1; break; }
            }
            var window = notes.Substring(windowStart, m.Index - windowStart);
            if (Regex.IsMatch(window, @"\b(?:dob|date\s+of\s+birth|birth\s+date|born)\b", RegexOptions.IgnoreCase)) continue;
            var lineStart = notes.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
            var lineEnd = notes.IndexOf('\n', m.Index + m.Length);
            if (lineEnd < 0) lineEnd = notes.Length;
            var line = notes.Substring(lineStart, lineEnd - lineStart).Trim();
            if (line.Length > 0 && !Regex.IsMatch(line, @"[A-Za-z]")) continue;
            if (latestNoteDate is null || noteDate > latestNoteDate) latestNoteDate = noteDate;
        }
        if (latestNoteDate is null || letterDate <= latestNoteDate.Value) yield break;
        // A forward relative reference ("review in 2 days", "review in two
        // weeks") extends the documented timeline beyond the last absolute
        // date, so a letter written within 31 days of it is not provably
        // invented — the writing date simply was never documented. Only a
        // date beyond any plausible continuation of the documented timeline
        // (Garcia: 30 May vs 23 May with no forward reference; the 2009
        // regression fixture) is flagged.
        var forwardTimeline = Regex.IsMatch(
            notes,
            @"\b(?:review|appointment|follow[- ]?up|see|seen|recheck)\b[^.;\n]{0,60}\bin\s+(?:\d+|one|two|three|four|five|six|seven|a\s+few|a)\s+(?:days?|weeks?|months?)\b",
            RegexOptions.IgnoreCase);
        if (forwardTimeline && (letterDate - latestNoteDate.Value).TotalDays <= 31) yield break;
        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
            $"The letter date ({letterDate:dd MMMM yyyy}) is later than every date documented in the case notes ({latestNoteDate.Value:dd MMMM yyyy} at the latest) — an unsupported, invented date. Use the source-supported date of treatment.",
            Quote: s.Lines[s.DateIndex.Value].Trim());
    }

    // OA source-fidelity (Taylor) — the recipient's name is spelled exactly
    // as the Writing Task spells it ("Dr Malcolm Still", never "Malcom").
    // Runs whenever the exact task text is supplied WITH the task's own
    // address instruction ("Address the letter to Dr ..., ...") — a task
    // that names no recipient block cannot prove a mismatch, so it never
    // fires on one. Every full name in the letter's recipient block must
    // appear in the task.
    private static IEnumerable<LintFinding> DetectRecipientNameMismatch(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (input.TaskText is not { Length: > 0 } task) yield break;
        if (!Regex.IsMatch(task, @"\baddress the letter to\b", RegexOptions.IgnoreCase)) yield break;
        var boundary = s.DateIndex ?? s.SalutationIndex ?? 0;
        if (boundary == 0) yield break;
        var normalisedTask = Regex.Replace(task, @"[^A-Za-z0-9'’\-]+", " ").ToLowerInvariant();
        foreach (var line in s.Lines.Take(boundary))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (!Regex.IsMatch(trimmed, @"^(?:Dr|Mr|Mrs|Ms|Miss)\b")) continue;
            var nameMatch = Regex.Match(trimmed, @"^(?:Dr|Mr|Mrs|Ms|Miss)\.?\s+(?<name>.+)$");
            if (!nameMatch.Success) continue;
            var nameTokens = Regex.Replace(nameMatch.Groups["name"].Value, @"[^A-Za-z0-9'’\-]+", " ").Trim().ToLowerInvariant();
            if (nameTokens.Length == 0) continue;
            // "Dr M McLaren" matches a task "Dr M McLaren"; "Dr Malcom Still"
            // does NOT match "Dr Malcolm Still" — the whole name string must
            // appear (initials allowed, wrong spelling never).
            if (!normalisedTask.Contains(nameTokens, StringComparison.Ordinal))
            {
                yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
                    $"The recipient name \"{trimmed}\" does not match the Writing Task — copy the exact source-supported spelling (task spelling controls).",
                    Quote: trimmed);
            }
            yield break;
        }
    }

    // OA2-07 (14 Sep 2026, supersedes the OA-08 "three or more" threshold):
    // a canonical Model Answer avoids semicolons in ordinary narrative prose
    // ENTIRELY — a single semicolon joining two clinical actions is a clarity
    // failure ("... twice daily; dexamethasone was continued ..."). Prefer a
    // full stop or a normal conjunction. Medication-list separators keep their
    // canonical semicolon grammar (OA2-16), so a sentence whose semicolons all
    // sit BETWEEN parsed medication-dose pairs is exempt; a sentence that
    // merely happens to mention one drug is not.
    private static bool SemicolonsAreMedicationListSeparators(string sentence)
    {
        var items = MedicationItemRe.Matches(sentence).Cast<Match>()
            .Where(m => !MedicationStopWords.Contains(m.Groups["drug"].Value))
            .OrderBy(m => m.Index)
            .ToList();
        if (items.Count < 3) return false;
        var semicolons = new List<int>();
        for (var i = 0; i < sentence.Length; i++)
            if (sentence[i] == ';') semicolons.Add(i);
        if (semicolons.Count == 0 || semicolons.Count > items.Count - 1) return false;
        // Every semicolon must fall in a gap between two consecutive items.
        foreach (var pos in semicolons)
        {
            var between = false;
            for (var i = 1; i < items.Count; i++)
            {
                var from = items[i - 1].Index + items[i - 1].Length;
                var to = items[i].Index;
                if (pos >= from && pos < to) { between = true; break; }
            }
            if (!between) return false;
        }
        return true;
    }

    private static IEnumerable<LintFinding> DetectSemicolonOveruse(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        // Decimal-safe sentence splitter, matching DetectMedicationListPunctuation:
        // the naive [^.!?\n]+ form breaks "14.0x10^9/L", "1.8 g" and "37.8 °C"
        // mid-sentence and mis-segments every clause around them.
        foreach (var sentenceMatch in Regex.Matches(s.Body, @"[^.!?\n]+(?:\.(?=\d)[^.!?\n]*)*[.!?]?").Cast<Match>())
        {
            var sentence = sentenceMatch.Value;
            var semicolons = sentence.Count(c => c == ';');
            if (semicolons == 0) continue;
            if (SemicolonsAreMedicationListSeparators(sentence)) continue;
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                semicolons == 1
                    ? "A canonical Model Answer does not use a semicolon in ordinary narrative prose. Use a full stop, or join the clauses with \"and\"."
                    : "This sentence chains clauses with semicolons. Split it into short, easy-to-process clinical sentences.",
                Quote: sentence.Trim().Length > 90 ? sentence.Trim()[..90] + "…" : sentence.Trim(),
                Start: bodyOffset + sentenceMatch.Index, End: bodyOffset + sentenceMatch.Index + sentenceMatch.Length);
            yield break;
        }
    }

    // ---------------------------------------------------------------------
    // FINAL WRITING OWNER CLARIFICATIONS — ADDENDUM TWO (14 Sep 2026),
    // OA2-01..OA2-20. OA2-01 is the governing principle: a validator that
    // reports zero findings while a visible defect survives is itself
    // defective, so every owner-named defect class below gets a deterministic
    // detector plus an injected-defect regression class (R2-01..R2-18 in
    // WritingOwnerAddendumTwoRegressionFixtureTests.cs). Registry rows
    // OA2-01..OA2-20 live in docs/canonical-rules/OET_AI_Rules_Master.jsonl.
    // ---------------------------------------------------------------------

    // OA2-02 / R2-02 — the mirror of discharge_language_unsupported. When the
    // canonical notes prove BOTH admission and discharge/return to ongoing
    // care and the task is routed as a discharge/update, vague simple-update
    // wording understates the letter's function. Scoped to discharge-routed
    // letters so a referral or transfer can never fire, and source-gated on
    // the markers so a task without proof is never second-guessed.
    private static readonly Regex DischargeFunctionMarkerRe = new(
        @"\bdischarg\w*|\b(?:was|were|has been|had been) admitted\b|\badmission\b|\b(?:into|under|back (?:in|to)|in) (?:your|his|her|their|our) (?:ongoing )?care\b"
        + @"|\btransfer of care\b|\bongoing care\b|\breturn(?:ed|ing)? to (?:your|his|her|their|the) care\b",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectDischargeFunctionMissed(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (input.CaseNotesMarkers is not { } markers) yield break;
        if (!markers.AdmissionDocumented || !markers.DischargeDocumented) yield break;
        if (!string.Equals(input.LetterType, "discharge", StringComparison.OrdinalIgnoreCase)) yield break;
        if (s.Body.Length == 0) yield break;
        if (DischargeFunctionMarkerRe.IsMatch(s.Body)) yield break;
        var intro = s.BodyParagraphs.Count > 0 ? s.BodyParagraphs[0] : s.Body;
        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
            "The canonical notes document an admission AND a discharge/return to ongoing care, but the letter uses only simple-update wording. State the update-on-discharge function: admission, treatment, discharge/return to your care and the ongoing-care request.",
            Quote: intro.Length > 100 ? intro[..100] + "…" : intro);
    }

    // OA2-08 / R2-05 — "Lumbar puncture showed 1000 white cells" states a bare
    // plural where the intended datum is a COUNT. The head noun is required:
    // "a white cell count of 1000". Deliberately narrow — only a digit
    // immediately before a bare cell plural, so an explicit unit ("1000 white
    // cells per microlitre") and ordinary prose never match.
    private static readonly Regex ResultNounFragmentRe = new(
        @"\b(?<value>\d[\d.,]*)\s+(?<kind>white|red|nucleated|polymorphonuclear)\s+(?:blood\s+)?cells\b(?!\s*(?:\/|per\b))",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectResultNounFragment(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in ResultNounFragmentRe.Matches(s.Body))
        {
            var kind = m.Groups["kind"].Value.ToLowerInvariant();
            var value = m.Groups["value"].Value;
            var fix = "a " + kind + " cell count at " + value;
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "\"" + m.Value + "\" is a fragment: the intended datum is a count, so it needs its measurement noun.",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: fix);
        }
    }

    // OA2-10 / R2-07 — supine-position wording. "when supine", "while supine",
    // "when lying supine" and "in the supine position" are all correct; the
    // bare "on/in supine position" is not English.
    private static readonly Regex SupinePositionRe = new(
        @"\b(?:on|in|at)\s+supine\s+position\b", RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectSupinePositionWording(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in SupinePositionRe.Matches(s.Body))
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "\"" + m.Value + "\" is not standard English. Use \"when supine\", \"while supine\", \"when lying supine\" or \"in the supine position\".",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: "when supine");
        }
    }

    // OA2-11 — a measurement noun that needs a head word must have one: "the
    // cholesterol level was 6.37 mmol/L", never the awkward "a cholesterol of
    // 6.37 mmol/L"; "the C-reactive protein level was 150", never "a CRP of
    // 150". Anchored on the indefinite article, so forms that already carry a
    // head noun ("a white cell count of 1000") and the adjectival forms the
    // owner keeps ("reduced glucose of 10 mg/dL") never match.
    private const string HeadlessResultNounPattern =
        @"cholesterol|CRP|C-reactive protein|urea|creatinine|potassium|sodium|haemoglobin|hemoglobin|bilirubin|albumin|ferritin";

    private static readonly Regex ResultHeadNounRe = new(
        @"\ba\s+(?<noun>" + HeadlessResultNounPattern + @")\s+of\s+(?=[\d.])",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectResultHeadNoun(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        // OA2-11 is a stated PREFERENCE ("prefer 'cholesterol level was...'
        // over 'a cholesterol of...'"), and WritingRuleProvenance declares
        // this check id AcceptAlternative, whose contract requires it to
        // "either not run for candidates or run in a semantic,
        // equivalence-accepting form". "a cholesterol of 6.37 mmol/L" is
        // grammatically correct English, so this house-style preference is
        // Model-Answer-only.
        if (!input.IsModelAnswer) yield break;
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in ResultHeadNounRe.Matches(s.Body))
        {
            var noun = m.Groups["noun"].Value;
            var expanded = string.Equals(noun, "CRP", StringComparison.OrdinalIgnoreCase) ? "C-reactive protein" : noun;
            var fix = "the " + expanded + " level was";
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "\"" + m.Value.Trim() + " …\" needs a measurement head noun. Write \"" + fix + " …\" or another natural source-faithful structure.",
                Quote: m.Value.Trim(), Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: fix);
        }
    }

    // OA2-12 / R2-10 — medical, family, social and special-habit background
    // belongs in a dedicated paragraph immediately before the closure, never
    // mixed into the opening current-problem paragraph. Model Answer only:
    // candidates are assessed on logical organisation, not on the house
    // paragraph position (OA2-20). Deliberately keyed on background LABELS and
    // habit facts, never on the word "history" alone, so a presenting
    // complaint ("a three-week history of numbness") never matches.
    private static readonly Regex BackgroundMarkerRe = new(
        @"\b(?:family|social|past medical|medical|surgical|obstetric)\s+history\b"
        + @"|\bhistory includes\b|\bbackground of\b|\bpast history\b"
        + @"|\bsmok(?:es|ed|ing|er)\b|\bcigarettes?\b|\btobacco\b"
        + @"|\bstandard drinks\b|\balcohol\b"
        + @"|\blives (?:alone|with|at)\b|\bworks as\b|\bis (?:married|divorced|widowed)\b"
        + @"|\b(?:penicillin|drug|food)\s+allerg\w*\b|\ballergic to\b",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectBackgroundParagraphPlacement(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        // Needs an introduction, a current-problem paragraph and at least one
        // later paragraph the background could have lived in.
        if (!input.IsModelAnswer || s.BodyParagraphs.Count < 4) yield break;
        var currentProblem = s.BodyParagraphs[1];
        var m = BackgroundMarkerRe.Match(currentProblem);
        if (!m.Success) yield break;
        var offset = BodyOffset(s) + s.Body.IndexOf(currentProblem, StringComparison.Ordinal);
        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
            "Medical, family, social or special-habit background (\"" + m.Value + "\") sits in the opening current-problem paragraph. Move it to a dedicated background paragraph immediately before the closure and keep this paragraph on the current episode.",
            Quote: m.Value, Start: offset + m.Index, End: offset + m.Index + m.Length);
    }

    // OA2-14 / R2-14 — a relevant vital sign is reported as its raw value and
    // unit; the letter never adds an interpretation the case notes did not
    // state ("do not label it hypotension unless the source does"). Source-
    // gated: without the canonical notes nothing can be proven, so the
    // detector no-ops, exactly like letter_date_unsupported.
    private static readonly Regex VitalInterpretationRe = new(
        @"\b(?:hypotensi(?:on|ve)|hypertensi(?:on|ve)|tachycardi(?:a|c)|bradycardi(?:a|c)|tachypnoe(?:a|ic)|tachypne(?:a|ic)|pyrexi(?:a|al)|hypoxi(?:a|c)|hypothermi(?:a|c)|haemodynamically unstable)\b",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectVitalSignInterpretationUnsupported(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (input.CaseNotesText is not { Length: > 0 } notes || s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in VitalInterpretationRe.Matches(s.Body))
        {
            var token = m.Value;
            if (notes.Contains(token, StringComparison.OrdinalIgnoreCase)) continue;
            // A shared stem means the notes DID document the condition
            // ("hypertension" in the notes supports "hypertensive" in the
            // letter), so a documented diagnosis is never re-flagged as an
            // invented interpretation.
            var stem = token.Length > 7 ? token[..7] : token;
            if (notes.Contains(stem, StringComparison.OrdinalIgnoreCase)) continue;
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
                "\"" + token + "\" interprets a vital sign in a way the case notes do not state. Report the exact value and unit and leave the interpretation to the reader.",
                Quote: token, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length);
            yield break;
        }
    }

    // OA2-17 / R2-13 — when the Writing Task addresses a ROLE rather than a
    // person ("The Admissions Officer"), the salutation uses that exact role:
    // "Dear Admissions Officer,". Never "Dear Sir/Madam," and never an
    // invented synonym ("Dear Admitting Officer,"). The recipient block proves
    // the role; when the exact task is supplied it must corroborate it.
    private static readonly Regex RoleRecipientLineRe = new(
        @"^The\s+(?<role>[A-Z][A-Za-z]+(?:\s+[A-Za-z]+){0,3}?\s+(?:Officer|Manager|Coordinator|Co-ordinator|Registrar|Director|Secretary|Lead|Practitioner))\s*$");

    private static IEnumerable<LintFinding> DetectRoleSalutationMatchesTask(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        // Source-gated, like letter_date_unsupported / recipient_name_mismatch
        // / re_line_identity_unsupported: this rule can only prove a mismatch
        // against the exact task, so without it the detector must stay silent.
        // Missing this gate let the rule fire on every candidate submission —
        // WritingEvaluationPipeline.EvaluateAsync calls Lint() with no
        // TaskText — flagging a defensible "Dear Sir/Madam," on any recipient
        // block that happens to contain a role line, exactly what OA2-20's
        // firewall (item 23) exists to prevent.
        if (input.TaskText is not { Length: > 0 } task) yield break;
        if (s.SalutationIndex is null) yield break;
        var boundary = s.DateIndex ?? s.SalutationIndex.Value;
        string? role = null;
        for (var i = 0; i < boundary && i < s.Lines.Length; i++)
        {
            var m = RoleRecipientLineRe.Match(s.Lines[i].Trim());
            if (!m.Success) continue;
            role = m.Groups["role"].Value.Trim();
            break;
        }
        if (role is null) yield break;
        if (!task.Contains(role, StringComparison.OrdinalIgnoreCase)) yield break;
        var salutation = s.Lines[s.SalutationIndex.Value].Trim();
        if (Regex.IsMatch(salutation, @"^Dear\s+" + Regex.Escape(role) + @"\s*,?\s*$", RegexOptions.IgnoreCase)) yield break;
        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
            "The task addresses a named role, so the salutation uses that exact role: \"Dear " + role + ",\". Do not fall back to \"Dear Sir/Madam,\" and do not invent a synonym.",
            Quote: salutation, FixSuggestion: "Dear " + role + ",");
    }

    /// <summary>
    /// True when the salutation addresses a role or an unnamed recipient
    /// rather than a named person — "Dear Sir/Madam,", "Dear Doctor," and
    /// (OA2-17) "Dear Admissions Officer,". Such a letter closes "Yours
    /// faithfully,", so yours_sincerely_vs_faithfully must not read a role as
    /// a personal name.
    /// </summary>
    internal static bool SalutationIsUnnamedRecipient(string? salutationLine)
    {
        var salutation = (salutationLine ?? string.Empty).Trim();
        if (Regex.IsMatch(salutation, @"Sir/?Madam|Dear Doctor\b", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(salutation, @"Dr\s+\w+", RegexOptions.IgnoreCase))
            return true;
        // A role salutation carries no personal name: "Dear Admissions
        // Officer,", "Dear Emergency Registrar,", "Dear Practice Manager,".
        return Regex.IsMatch(
            salutation,
            @"^Dear\s+(?:[A-Z][A-Za-z]+(?:\s+[A-Za-z]+){0,3}\s+)?(?:Officer|Manager|Coordinator|Co-ordinator|Registrar|Director|Secretary|Lead|Practitioner)\s*,?\s*$",
            RegexOptions.IgnoreCase);
    }

    // OA2-19 / R2-16 — the canonical Model Answer contact-offer paragraph.
    // Preferred: "Should there be any queries, kindly do not hesitate to
    // contact me."; the "please" form is the other approved house variant.
    // The appended "... contact me with any queries." is rejected because the
    // fixed template already communicates the meaning cleanly. Model Answer
    // only — candidates keep any professional semantic equivalent (OA2-20).
    private static readonly Regex CanonicalContactTemplateRe = new(
        @"^Should there be any queries,\s+(?:kindly|please)\s+do not hesitate to contact me\.$",
        RegexOptions.IgnoreCase);

    private const string CanonicalContactTemplate =
        "Should there be any queries, kindly do not hesitate to contact me.";

    private static IEnumerable<LintFinding> DetectCanonicalContactTemplate(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.BodyParagraphs.Count < 2) yield break;
        // The contact offer is the FINAL SENTENCE. Whether it also stands in
        // its own paragraph is OA2-06's question (closure_request_paragraph),
        // not this rule's — checking the whole paragraph here would punish a
        // letter type whose closure legitimately carries one preceding
        // sentence.
        var sentences = SplitSentences(s.BodyParagraphs[^1]);
        if (sentences.Count == 0) yield break;
        var final = sentences[^1].Trim();
        // A final sentence that is not a contact offer at all belongs to
        // closure_contact_offer, not to this rule.
        if (!ContactOfferMeaningRe.IsMatch(final)) yield break;
        if (CanonicalContactTemplateRe.IsMatch(final)) yield break;
        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
            "A canonical Model Answer closes with the selected contact template. Use \"" + CanonicalContactTemplate + "\" and do not append \"with any queries\" to it.",
            Quote: final.Length > 90 ? final[..90] + "…" : final,
            FixSuggestion: CanonicalContactTemplate);
    }

    // OA2 Taylor defect / R2-18 — a generic-plus-brand appositive is stated
    // ONCE. "colchicine, also known as Lengout, 1 mg" in the current-treatment
    // paragraph and again in the history paragraph is clumsy duplication: one
    // clear source-supported medication identity is sufficient unless the
    // brand is genuinely needed twice.
    private static readonly Regex BrandAppositiveRe = new(
        @"\b(?<generic>[A-Za-z][A-Za-z\-]{3,})\s*,?\s+also known as\s+(?<brand>[A-Z][A-Za-z\-]+)",
        RegexOptions.None);

    private static IEnumerable<LintFinding> DetectBrandGenericDuplication(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in BrandAppositiveRe.Matches(s.Body))
        {
            var generic = m.Groups["generic"].Value;
            if (seen.Add(generic)) continue;
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "\"" + m.Value + "\" repeats a generic/brand pairing already given earlier in the letter. State one clear source-supported medication identity, then use it consistently.",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: generic);
            yield break;
        }
    }

    // Addendum Two section 2, SOURCE FACTS CONTROL — the Re: line's date of
    // birth or age is a source fact like any other. The Weir owner-review
    // letter carried "DOB: 20 September 1970" while the canonical notes record
    // no date of birth and no age at all, and the validator still reported
    // zero findings: exactly the OA2-01 failure mode. Source-gated, so without
    // the canonical notes nothing can be proven and the detector no-ops.
    private static readonly Regex ReLineDobRe = new(
        @"\bDOB\s*:\s*(?<dob>[^,;]+)", RegexOptions.IgnoreCase);

    private static readonly Regex ReLineAgeRe = new(
        @"\baged\s+(?<age>\d{1,3})\b", RegexOptions.IgnoreCase);

    private static readonly Regex NumericDateRe = new(
        @"\b(?<d>\d{1,2})\s*[./-]\s*(?<m>\d{1,2})\s*[./-]\s*(?<y>\d{2,4})\b");

    private static string DateKey(int day, int month, int year) => day + "/" + month + "/" + (year % 100);

    private static HashSet<string> SourceDateKeys(string notes)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in NumericDateRe.Matches(notes))
        {
            if (!int.TryParse(m.Groups["d"].Value, out var d)) continue;
            if (!int.TryParse(m.Groups["m"].Value, out var mo)) continue;
            if (!int.TryParse(m.Groups["y"].Value, out var y)) continue;
            keys.Add(DateKey(d, mo, y));
        }
        foreach (Match m in DateTokenRe.Matches(notes))
        {
            if (!TryParseDateToken(m, out var dt)) continue;
            keys.Add(DateKey(dt.Day, dt.Month, dt.Year));
        }
        return keys;
    }

    private static IEnumerable<LintFinding> DetectReLineIdentityUnsupported(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (input.CaseNotesText is not { Length: > 0 } notes || s.ReLineIndex is null) yield break;
        var reLine = s.Lines[s.ReLineIndex.Value];

        var dob = ReLineDobRe.Match(reLine);
        if (dob.Success)
        {
            var raw = dob.Groups["dob"].Value.Trim();
            string? key = null;
            var written = DateTokenRe.Match(raw);
            if (written.Success && TryParseDateToken(written, out var wd))
            {
                key = DateKey(wd.Day, wd.Month, wd.Year);
            }
            else
            {
                var numeric = NumericDateRe.Match(raw);
                if (numeric.Success
                    && int.TryParse(numeric.Groups["d"].Value, out var nd)
                    && int.TryParse(numeric.Groups["m"].Value, out var nm)
                    && int.TryParse(numeric.Groups["y"].Value, out var ny))
                    key = DateKey(nd, nm, ny);
            }
            if (key is null || !SourceDateKeys(notes).Contains(key))
            {
                yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
                    "The Re: line states a date of birth (\"" + raw + "\") that the canonical case notes do not record. Carry only source-supported patient identification, and omit the date of birth when the source has none.",
                    Quote: reLine.Trim());
                yield break;
            }
        }

        var age = ReLineAgeRe.Match(reLine);
        if (!age.Success) yield break;
        var value = age.Groups["age"].Value;
        if (Regex.IsMatch(notes, @"\bage[ds]?\s*:?\s*" + value + @"\b", RegexOptions.IgnoreCase)) yield break;
        if (Regex.IsMatch(notes, @"\(\s*age\s*" + value + @"\b", RegexOptions.IgnoreCase)) yield break;
        if (Regex.IsMatch(notes, @"\b" + value + @"[\s-]*(?:year|yr)s?(?:[\s-]*old)?\b", RegexOptions.IgnoreCase)) yield break;
        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
            "The Re: line states an age (\"aged " + value + "\") that the canonical case notes do not record. Carry only source-supported patient identification.",
            Quote: reLine.Trim());
    }

    // ---------------------------------------------------------------------
    // Owner Clarifications Round 3 (15 Sep 2026, OA3-01..OA3-05) — the
    // owner's latest overrides as global rules: introduction full-name
    // freedom (implemented in DetectFullNameRepeated), hard patient-name
    // spelling fidelity, DOB priority over age, the canonical "at" result
    // wording, and the dangling treatment modifier.
    // ---------------------------------------------------------------------

    // OA3-02 — patient-name spelling is HARD source fidelity: one missing or
    // extra letter in the patient's first or last name is an error. The
    // canonical notes are the spelling authority; when they name the patient,
    // the Re:-line surname and every full-name mention in the letter must
    // match exactly (case-insensitive, spelling exact). Notes that never name
    // the patient leave the check inert — nothing may be invented to compare
    // against.
    // Revalidation fix (15 Sep 2026): the notes can name the patient in a
    // clean "Name: <First> <Last>" line, but they also contain the
    // recipient's titled name ("Ms. Nina Gill"), PDF-extraction fragments
    // ("Ms Osbur is") and source typos ("Tallor"). The canonical patient
    // name is therefore resolved from a Name: line when one exists; a
    // stopword last token ("is" from "Mrs Osburn is ...") never counts.
    private static (string first, string last)? NotesCanonicalName(string notes)
    {
        // The title after "Name:" is optional — canonical notes both with
        // ("Name: Mr Michael Weir") and without ("Name: Michael Weir") a
        // title must resolve.
        var nameLine = Regex.Match(notes, @"Name\s*[:\-]\s*(?:(?<t>(?:Mr|Mrs|Ms|Miss|Master|Dr)\.?)\s+)?(?<first>[A-Z][a-zA-Z'’\-]+)\s+(?<last>[A-Z][a-zA-Z'’\-]+)", RegexOptions.IgnoreCase);
        if (nameLine.Success && IsTitleCaseName(nameLine.Groups["first"].Value, nameLine.Groups["last"].Value))
            return (nameLine.Groups["first"].Value, nameLine.Groups["last"].Value);
        foreach (Match m in NotesPatientNameRe.Matches(notes))
        {
            var last = m.Groups["last"].Value;
            if (PersonSubjects.Contains(last) || NonNameReWords.Contains(last)) continue;
            // RegexOptions.IgnoreCase lets "Patient is anxious and believe..."
            // resolve as first="anxious", last="and" — notes that name NOBODY
            // (Mrs Lucy Clarke round, 16 Sep 2026). A real name is title-case;
            // lowercase pseudo-names must not become the canonical name.
            if (!IsTitleCaseName(m.Groups["first"].Value, last)) continue;
            return (m.Groups["first"].Value, last);
        }
        return null;
    }

    // A resolved canonical name must be title-case. Under RegexOptions.
    // IgnoreCase the [A-Z] classes match any letter, so ordinary sentence
    // words after "Patient is" passed the pattern; this guard rejects them
    // so a nameless note set yields null (rule silent) instead of garbage.
    private static bool IsTitleCaseName(string first, string last)
        => first.Length > 0 && last.Length > 0 && char.IsUpper(first[0]) && char.IsUpper(last[0]);

    private static readonly Regex NotesPatientNameRe = new(
        @"\b(?:Mr|Mrs|Ms|Miss)\.?\s+(?<first>[A-Z][a-z'’-]+)\s+(?<last>[A-Z][a-z'’-]+)\b|\bPatient is\s+(?<first>[A-Z][a-z'’-]+)\s+(?<last>[A-Z][a-z'’-]+)\b",
        RegexOptions.IgnoreCase);

private static string? ReLineSurname(string reLine)
    {
        var titled = ReTitledNameRe.Match(reLine);
        if (!titled.Success) return null;
        var tokens = new List<string> { titled.Groups[2].Value };
        if (titled.Groups[3].Success) tokens.Add(titled.Groups[3].Value);
        if (titled.Groups[4].Success) tokens.Add(titled.Groups[4].Value);
        while (tokens.Count > 1 && (NonNameReWords.Contains(tokens[^1]) || ReIdentifierWords.Contains(tokens[^1])))
            tokens.RemoveAt(tokens.Count - 1);
        return tokens[^1];
    }

    // True when the two name tokens are a plausible typo pair: identical,
    // within a small edit distance with the same first letter, or a single
    // adjacent transposition ("Wier" / "Weir" — a Levenshtein distance of 2
    // but exactly the one-missing-letter class the owner's OA3-02 contract
    // names).
    private static bool IsNearSpelling(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        if (a.Length == 0 || b.Length == 0) return false;
        if (!string.Equals(a[0].ToString(), b[0].ToString(), StringComparison.OrdinalIgnoreCase)) return false;
        var sa = a.ToLowerInvariant();
        var sb = b.ToLowerInvariant();
        if (Math.Abs(sa.Length - sb.Length) > 2) return false;
        if (Levenshtein(sa, sb) <= (Math.Max(sa.Length, sb.Length) <= 5 ? 1 : 2)) return true;
        // Adjacent transposition of two neighbouring letters, rest identical.
        if (sa.Length == sb.Length)
        {
            for (int i = 0; i + 1 < sa.Length; i++)
            {
                if (sa[i] != sb[i])
                {
                    if (sa[i] == sb[i + 1] && sa[i + 1] == sb[i] && sa[(i + 2)..] == sb[(i + 2)..])
                        return true;
                    break;
                }
            }
        }
        return false;
    }

    private static int Levenshtein(string s, string t)
    {
        var d = new int[s.Length + 1, t.Length + 1];
        for (var i = 0; i <= s.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= t.Length; j++) d[0, j] = j;
        for (var i = 1; i <= s.Length; i++)
            for (var j = 1; j <= t.Length; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        return d[s.Length, t.Length];
    }

    private static string StripPossessive(string token)
        => token.EndsWith("'s", StringComparison.OrdinalIgnoreCase) || token.EndsWith("\u2019s", StringComparison.OrdinalIgnoreCase)
            ? token[..^2]
            : token;

    private static IEnumerable<LintFinding> DetectPatientNameSpelling(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (input.CaseNotesText is not { Length: > 0 } notes || string.IsNullOrEmpty(input.LetterText)) yield break;
        var named = NotesCanonicalName(notes);
        if (named is null) yield break;
        var sourceFirst = StripPossessive(named.Value.first);
        var sourceLast = StripPossessive(named.Value.last);

        // The Re: line carries the patient surname — it must be the source
        // spelling exactly. The surname is the LAST name token on the Re:
        // line (identifier words such as DOB trimmed), so a full-name Re:
        // line ("Re: Mr David Taylor, DOB: ...") is compared by its surname,
        // never by its first name.
        if (s.ReLineIndex is int reIdx)
        {
            var surname = ReLineSurname(s.Lines[reIdx]);
            // Revalidation follow-up (15 Sep 2026): the canonical extraction
            // can mismatch on PDF fragments even when the letter's surname is
            // spelled exactly as the source spells it somewhere in the notes.
            // If the Re: surname occurs anywhere in the canonical notes, the
            // spelling is source-faithful.
            if (surname is not null
                && !string.Equals(surname, sourceLast, StringComparison.OrdinalIgnoreCase)
                && !IsNearSpelling(surname, sourceLast)
                && !notes.Contains(surname, StringComparison.OrdinalIgnoreCase))
            {
                yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
                    "The patient's surname in the Re: line (\"" + surname + "\") does not match the canonical case notes (\"" + sourceLast + "\"). The exact source spelling controls — never guess, shorten or autocorrect a patient name.",
                    Quote: s.Lines[reIdx].Trim());
                yield break;
            }
        }

        // Every full-name mention in the letter: first and last must each
        // match the source spelling (possessives tolerated: "Ms Isabel
        // Garcia's"). Another person's full name matches neither source token
        // and is skipped — only a NEAR match to the patient's own name is a
        // spelling error.
        foreach (Match m in Regex.Matches(input.LetterText, @"\b(?:Mr|Mrs|Ms|Miss)\.?\s+(?<first>[A-Z][a-z'’-]+)\s+(?<last>[A-Z][a-z'’-]+)\b"))
        {
            var first = m.Groups["first"].Value;
            var last = m.Groups["last"].Value;
            var firstOk = string.Equals(StripPossessive(first), sourceFirst, StringComparison.OrdinalIgnoreCase);
            var lastOk = string.Equals(StripPossessive(last), sourceLast, StringComparison.OrdinalIgnoreCase);
            if (firstOk && lastOk) continue;
            if (!firstOk && !lastOk) continue; // a different person entirely
            // Revalidation fix (15 Sep 2026): a relative sharing the
            // surname ("Mr Krishnan Ramamurthy", husband of the patient)
            // is a different person, not a spelling error. Flag only a
            // NEAR miss of the mismatching token (the OA3-02 examples,
            // "Taylr"/"Davod", are off by one or two characters).
            var mismatch = firstOk ? StripPossessive(last) : first;
            var expected = firstOk ? sourceLast : sourceFirst;
            if (!IsNearSpelling(mismatch, expected)) continue;
            var wrong = mismatch;
            var right = expected;
            var at = input.LetterText.IndexOf(m.Value, StringComparison.Ordinal);
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
                "The patient's name is misspelled: \"" + wrong + "\" should be \"" + right + "\" per the canonical case notes. The exact source spelling controls in the Re: line, the introduction and every later reference.",
                Quote: m.Value, Start: at, End: at + m.Length,
                FixSuggestion: firstOk ? StripPossessive(first) + " " + right : right + " " + StripPossessive(last));
            yield break;
        }
    }

    // OA3-03 — DOB has priority over age in the Re: line. When the canonical
    // notes record a date of birth, the Re: line must carry it ("Re: Mr John
    // Smith, DOB: 1 January 1980"); "aged X" — or an identification without
    // the DOB — fails. (The inverse — inventing a DOB the source does not
    // record — stays with re_line_identity_unsupported.)
    private static readonly Regex NotesDobLabelRe = new(
        @"\b(?:DOB|date of birth)\b\s*:?\s*", RegexOptions.IgnoreCase);

    private static DateTime? FindNotesDob(string notes)
    {
        foreach (Match label in NotesDobLabelRe.Matches(notes))
        {
            var start = label.Index + label.Length;
            var tail = notes.Substring(start, Math.Min(24, notes.Length - start));
            var numeric = NumericDateRe.Match(tail);
            if (numeric.Success
                && int.TryParse(numeric.Groups["d"].Value, out var d)
                && int.TryParse(numeric.Groups["m"].Value, out var m)
                && int.TryParse(numeric.Groups["y"].Value, out var yRaw))
            {
                var y = yRaw < 100 ? (yRaw <= 30 ? 2000 + yRaw : 1900 + yRaw) : yRaw;
                try { return new DateTime(y, m, d); } catch (ArgumentOutOfRangeException) { }
            }
            var written = DateTokenRe.Match(tail);
            if (written.Success && TryParseDateToken(written, out var wd)) return wd;
        }
        return null;
    }

    private static string? ReLineDobKey(string raw)
    {
        var written = DateTokenRe.Match(raw);
        if (written.Success && TryParseDateToken(written, out var wd)) return DateKey(wd.Day, wd.Month, wd.Year);
        var numeric = NumericDateRe.Match(raw);
        if (numeric.Success
            && int.TryParse(numeric.Groups["d"].Value, out var nd)
            && int.TryParse(numeric.Groups["m"].Value, out var nm)
            && int.TryParse(numeric.Groups["y"].Value, out var nyRaw))
        {
            var ny = nyRaw < 100 ? (nyRaw <= 30 ? 2000 + nyRaw : 1900 + nyRaw) : nyRaw;
            return DateKey(nd, nm, ny);
        }
        return null;
    }

    private static IEnumerable<LintFinding> DetectReLineDobPriority(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (input.CaseNotesText is not { Length: > 0 } notes || s.ReLineIndex is null) yield break;
        var notesDob = FindNotesDob(notes);
        if (notesDob is null) yield break;
        var reLine = s.Lines[s.ReLineIndex.Value];

        // A DOB already in the Re: line satisfies the rule when it matches the
        // source (its accuracy against the notes is
        // re_line_identity_unsupported's contract, so a mismatching DOB is
        // that rule's finding, not this one's).
        var dob = ReLineDobRe.Match(reLine);
        if (dob.Success)
        {
            var key = ReLineDobKey(dob.Groups["dob"].Value.Trim());
            if (key is not null && key == DateKey(notesDob.Value.Day, notesDob.Value.Month, notesDob.Value.Year)) yield break;
            yield break;
        }
        var display = notesDob.Value.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
            "The canonical case notes record the patient's date of birth (" + display + "), and DOB takes priority over age in the Re: line. Write \"Re: <title> <full name>, DOB: " + display + "\" — use \"aged X\" only when the source supplies no DOB.",
            Quote: reLine.Trim());
    }

    // OA3-04 — canonical Model Answer result wording: values are stated with
    // the owner's "at" construction on a complete result noun ("a white cell
    // count at 1,000", "a reduced glucose level at 10 mg/dL", "the
    // C-reactive protein level was 150"). Headless values ("reduced glucose
    // 10 mg/dL") and "of" forms ("a white cell count of 1000") fail the
    // Model Answer; candidates keep every grammatical professional
    // alternative (AcceptAlternative). Cholesterol/CRP/"C-reactive protein"
    // headless forms stay with result_head_noun, whose "the ... level was"
    // repair is the owner-approved completion of the same rule.
    private const string ResultAtNounList =
        "white cell count|red cell count|platelet count|glucose|protein|urea|creatinine|potassium|sodium|haemoglobin|hemoglobin|albumin|bilirubin|ferritin|HbA1c";

    private static readonly Regex ResultBareValueRe = new(
        @"\b(?<noun>" + ResultAtNounList + @")\s+(?=[\d.])", RegexOptions.IgnoreCase);

    // A clinical qualifier may sit between the article and the result noun
    // ("a reduced glucose of 10 mg/dL", "a raised white cell count of 14.0"),
    // so up to two qualifier tokens are tolerated; the noun itself must still
    // be one of the canonical result nouns.
    private static readonly Regex ResultOfValueRe = new(
        @"\b(?:a|an)\s+(?:[A-Za-z][A-Za-z'’-]*\s+){0,2}(?<noun>" + ResultAtNounList + @")\s+of\s+(?=[\d.])", RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectResultAtWording(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in ResultOfValueRe.Matches(s.Body))
        {
            var noun = m.Groups["noun"].Value;
            var count = noun.EndsWith("count", StringComparison.OrdinalIgnoreCase);
            var fix = count ? "a " + noun + " at ..." : "a " + noun + " level at ...";
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "State the result with the canonical \"at\" wording on a complete result noun: \"" + fix + "\" — never an \"of\" form.",
                Quote: m.Value.Trim(), Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: fix);
        }
        foreach (Match m in ResultBareValueRe.Matches(s.Body))
        {
            var noun = m.Groups["noun"].Value;
            var count = noun.EndsWith("count", StringComparison.OrdinalIgnoreCase);
            var fix = count ? "a " + noun + " at ..." : "a " + noun + " level at ...";
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "The result value is stated without its measurement noun and connector. Use the canonical wording \"" + fix + "\" (e.g. \"a reduced glucose level at 10 mg/dL\").",
                Quote: m.Value.Trim(), Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: fix);
        }
    }

    // OA3-05 — a dangling treatment modifier: "A catheter urine culture grew
    // Staphylococcus saprophyticus, treated with five days of Keflex." reads
    // as if the CULTURE were treated. The patient was: "..., and Mr McDonald
    // was treated with Keflex for five days." Genuine grammar, both modes.
    private static readonly Regex DanglingTreatmentRe = new(
        @"\b(?:catheter\s+urine\s+culture|catheter\s+specimen\s+of\s+urine|urine\s+culture|CSU|wound\s+swab|throat\s+swab|swab|culture|specimen|sample)\s+(?:grew|grown|yielded|isolated|identified|detected|revealed)\b[^.;\n]{0,120}?,\s*(?:then\s+)?treated\s+with\b",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectDanglingTreatmentModifier(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in DanglingTreatmentRe.Matches(s.Body))
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "The treatment clause dangles on the specimen (\"... culture grew ..., treated with ...\") — the PATIENT was treated. Write \"..., and <patient reference> was treated with ... for ...\".",
                Quote: m.Value.Trim(), Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length);
        }
    }

    // ---------------------------------------------------------------------
    // Final Medicine layout/punctuation patch (16 Sep 2026, OA4-01..OA4-03).
    // ---------------------------------------------------------------------

    // OA4-01a — address contract: "/" is never an address separator. The
    // recipient block must carry every source component on its own line so
    // the web render shows the same structure as storage.
    private static IEnumerable<LintFinding> DetectAddressSlashSeparator(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var boundary = s.DateIndex ?? s.SalutationIndex ?? s.Lines.Length;
        for (int i = 0; i < boundary && i < s.Lines.Length; i++)
        {
            var trimmed = s.Lines[i].Trim();
            if (trimmed.Length > 2 && trimmed.Contains('/') &&
                Regex.IsMatch(trimmed, @"\d|\b(?:St|St\.|Rd|Road|Ave|Avenue|Cl|Ct)\b|Suite|PO|GPO|Level", RegexOptions.IgnoreCase))
            {
                yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                    "Address components must sit on separate lines: \"" + trimmed + "\" joins components with a slash. Copy each component from the task onto its own line.",
                    Quote: trimmed);
            }
        }
    }

    // OA4-01b — render contract: the salutation line must never carry the
    // Re: line. "Dear Dr Bradbury, Re: Ms Garcia, DOB: ..." on one physical
    // line collapses the letter's visual structure.
    private static IEnumerable<LintFinding> DetectSalutationReSameLine(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.SalutationIndex is int sal && sal < s.Lines.Length
            && Regex.IsMatch(s.Lines[sal], @"\bRe\s*:", RegexOptions.IgnoreCase))
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
                "The salutation and the Re: line must NEVER render on the same physical line. Put the Re: line on its own line immediately below the salutation.",
                Quote: s.Lines[sal].Trim());
        }
    }

    // OA4-02 — introductory time/examination phrases are followed by a comma
    // in canonical Model Answers ("Today, Mr Taylor reported...", "On
    // examination, ...", "Initially, ...").
    private static readonly Regex IntroAdverbialNoCommaRe = new(
        @"(?:^|[.!?]\s+)((?:Today|On today's review|On examination|On presentation|On the following visit|On subsequent visits|Initially|Later on|On [A-Z][a-z]+ \d{1,2})(?!,)\s+)(?=[A-Z])",
        RegexOptions.Multiline);

    private static IEnumerable<LintFinding> DetectIntroAdverbialComma(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in IntroAdverbialNoCommaRe.Matches(s.Body))
        {
            var phrase = m.Groups[1].Value.Trim();
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "Introductory time phrases take a comma: \"" + phrase + ", ...\"",
                Quote: m.Value.Trim(), Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: phrase + ",");
        }
    }

    // OA4-03 — title fidelity: once the Re: line fixes the patient's title
    // (Mrs/Ms/Mr/Miss), every later reference carries the SAME title. A
    // Mrs→Ms (or Ms→Mrs) switch is a hard identity error.
    private static IEnumerable<LintFinding> DetectPatientTitleMismatch(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.ReLineIndex is null) yield break;
        var titled = ReTitledNameRe.Match(s.Lines[s.ReLineIndex.Value]);
        if (!titled.Success) yield break;
        var canonical = titled.Groups[1].Value;
        if (string.Equals(canonical, "Dr", StringComparison.OrdinalIgnoreCase)) yield break;
        var surname = ReLineSurname(s.Lines[s.ReLineIndex.Value]);
        if (string.IsNullOrEmpty(surname)) yield break;
        foreach (Match m in Regex.Matches(input.LetterText, @"\b(?:Mr|Mrs|Ms|Miss)\.?\s+(?:(?<first>[A-Z][a-zA-Z'’\-]+)\s+)?(?<surname>" + Regex.Escape(surname) + @")\b"))
        {
            var used = m.Value.TrimEnd(':').Split()[0].TrimEnd('.');
            if (string.Equals(used, canonical, StringComparison.OrdinalIgnoreCase)) continue;
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
                "Title mismatch: the Re: line uses \"" + canonical + "\" but this reference uses \"" + used + "\". The patient's title is fixed by the source — use \"" + canonical + " " + surname + "\" consistently.",
                Quote: m.Value);
            yield break;
        }
    }
}
