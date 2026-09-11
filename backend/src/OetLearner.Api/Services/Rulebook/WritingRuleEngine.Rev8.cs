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
    /// </summary>
    public const string ValidatorVersion = "writing-rules.rev8.2026-09-11.1";

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

    // OWN-W-011 — "Do not repeat the full first + last name in the body after
    // it has been used in the Re: line." The introduction counts (Weir /
    // Taylor / Ramsey defects: "I am writing to refer Mr David Taylor ...").
    private static IEnumerable<LintFinding> DetectFullNameRepeated(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var name = ResolvePatientName(input, s);
        if (name is null || !name.HasFullName || s.Body.Length == 0) yield break;
        var re = new Regex($@"\b(?:(?:Mr|Mrs|Ms|Miss|Master|Dr)\.?\s+)?{Regex.Escape(name.First!)}\s+{Regex.Escape(name.Last!)}\b");
        var m = re.Match(s.Body);
        if (!m.Success) yield break;
        var approved = name.Child ? name.First : $"{name.Title ?? "Mr/Ms"} {name.Last}";
        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
            $"The full name \"{m.Value}\" is already in the Re: line — do not repeat it in the letter. Use \"{approved}\" instead.",
            Quote: m.Value, Start: BodyOffset(s) + m.Index, End: BodyOffset(s) + m.Index + m.Length,
            FixSuggestion: approved);
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

    // OWN-W-015 (Model Answer ordering) — "at your earliest convenience" in the
    // closure, then the contact-offer sentence last.
    private static IEnumerable<LintFinding> DetectUrgentClosureModelAnswer(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var closure = s.BodyParagraphs.Count > 0 ? s.BodyParagraphs[^1] : "";
        var sentences = SplitSentences(closure);
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
                "In an urgent-referral Model Answer, \"at your earliest convenience\" must come in the request sentence, followed by the final contact-offer sentence.",
                Quote: sentences[aeycIndex]);
    }

    // ---------------------------------------------------------------------
    // Medication syntax (OWN-W-023, R11.2-R11.3) and value-unit spacing
    // (OWN-W-024).
    // ---------------------------------------------------------------------

    private const string DoseUnitPattern = @"mg|mcg|µg|micrograms?|g|IU|units?|mL|ml";

    private static readonly Regex MedicationItemRe = new(
        @"\b(?<drug>[A-Za-z][A-Za-z\-]{2,})(?<comma>,)?\s+(?<dose>\d+(?:\.\d+)?)\s?(?<unit>" + DoseUnitPattern + @")\b(?!\s*\/)",
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
                var dose = $"{item.Groups["dose"].Value} {item.Groups["unit"].Value}";
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
            if (items.Count == 2)
            {
                ok = Regex.IsMatch(separators[0], @"\band\b", RegexOptions.IgnoreCase) && !separators[0].Contains(';');
            }
            else
            {
                ok = separators.Take(separators.Count - 1).All(x => x.Contains(';'))
                     && Regex.IsMatch(separators[^1], @";\s*and\b", RegexOptions.IgnoreCase);
            }
            if (!ok)
            {
                yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                    items.Count == 2
                        ? "Two medicines: write \"Drug, dose and Drug, dose\" (no semicolon; \"and\" before the second)."
                        : "Three or more medicines: write \"Drug, dose; Drug, dose; and Drug, dose\" (semicolons between items and \"and\" before the final item).",
                    Quote: sentence.Trim().Length > 100 ? sentence.Trim()[..100] + "…" : sentence.Trim());
                if (++reported >= 5) yield break;
            }
        }
    }

    private static readonly Regex ValueUnitNoSpaceRe = new(
        @"(?<![\w.])(\d+(?:\.\d+)?)(mmol\/L|µmol\/L|umol\/L|nmol\/L|mg\/kg|mg\/dL|g\/dL|g\/L|U\/L|ng\/mL|mg|mcg|µg|kg|g|mL|ml|L|IU|mmol|mmHg|cm|mm|bpm|kPa|mEq|units)\b",
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
    // "MRI imaging"; Wright "felt something pop", "with no GP").
    private static readonly Regex ColloquialRe = new(
        @"\bMRI imaging\b|\bCT scan imaging\b|\bfelt something\s+['‘’]?pop['‘’]?|\bsomething\s+['‘’]pop['‘’]|\bsluggish\b|\bwith no GP\b|\bno GP\b|\bkids?\b|\bguys?\b|\ba lot of\b|\blots of\b|\bpretty (?:bad|severe|good|much)\b|\bokay\b|\bOK\b|\bgot (?:better|worse)\b|\bstuff\b|\btired\b|\bfeeling down\b|\bup and down\b|\btummy\b|\bpee\b|\bpoo\b",
        RegexOptions.None);

    private static IEnumerable<LintFinding> DetectColloquialRegister(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        var count = 0;
        foreach (Match m in ColloquialRe.Matches(s.Body))
        {
            yield return new LintFinding(rule.Id, input.IsModelAnswer ? RuleSeverity.Critical : RuleSeverity.Minor,
                $"\"{m.Value}\" is informal or redundant for a clinical letter. Use a professional, source-faithful form (e.g. fatigue, MRI, felt a popping sensation, does not currently have a GP).",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length);
            if (++count >= 5) yield break;
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
        var sentences = SplitSentences(s.BodyParagraphs[^1]);
        var requestIdx = sentences.FindIndex(x => ClosureRequestRe.IsMatch(x));
        if (requestIdx < 0) yield break;
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
            yield return new LintFinding(rule.Id, ModeSeverity(input, rule.Severity == RuleSeverity.Critical ? RuleSeverity.Critical : RuleSeverity.Major),
                $"\"{lk.Value}\" joins two complete clauses here. Use \"...; {display}, ...\" or start a new sentence: \"... . {char.ToUpperInvariant(display[0]) + display[1..]}, ...\".",
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
}
