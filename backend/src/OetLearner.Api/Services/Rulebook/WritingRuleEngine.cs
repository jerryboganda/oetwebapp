using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// .NET mirror of lib/rulebook/writing-rules.ts. Deterministic detectors
/// keyed by each rule's CheckId in the rulebook JSON.
///
/// Behaviour MUST match the TypeScript engine. Every detector here has a
/// sibling test case in WritingRulesTests.cs that mirrors the Vitest case
/// in writing-rules.test.ts.
/// </summary>
public sealed class WritingRuleEngine(IRulebookLoader loader)
{
    public IReadOnlyList<LintFinding> Lint(WritingLintInput input)
    {
        var book = loader.Load(RuleKind.Writing, input.Profession);
        var structure = ParseLetter(input.LetterText);
        var applicable = RulesApplicableTo(book, input.LetterType);

        var findings = new List<LintFinding>();
        foreach (var rule in applicable)
        {
            if (!string.IsNullOrWhiteSpace(rule.CheckId))
            {
                var det = DetectorFor(rule.CheckId!);
                if (det is not null) findings.AddRange(det(rule, input, structure));
            }
            if (rule.ForbiddenPatterns is { Count: > 0 })
            {
                findings.AddRange(RunForbiddenPatterns(rule, input.LetterText));
            }
        }

        // Dedup
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = new List<LintFinding>();
        foreach (var f in findings)
        {
            var key = $"{f.RuleId}|{f.Quote}|{f.Message}";
            if (!seen.Add(key)) continue;
            unique.Add(f);
        }

        // Sort: critical > major > minor > info, then by start offset
        unique.Sort((a, b) =>
        {
            var s = SeverityRank(a.Severity).CompareTo(SeverityRank(b.Severity));
            if (s != 0) return s;
            return (a.Start ?? 0).CompareTo(b.Start ?? 0);
        });
        return unique;
    }

    // ---------------------------------------------------------------------
    // Letter parsing
    // ---------------------------------------------------------------------

    private sealed record LetterStructure(
        string[] Lines,
        int? DateIndex,
        int? SalutationIndex,
        int? ReLineIndex,
        int? YoursIndex,
        string Body,
        List<string> BodyParagraphs);

    private static LetterStructure ParseLetter(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        int? Find(Regex re)
        {
            for (int i = 0; i < lines.Length; i++)
                if (re.IsMatch(lines[i])) return i;
            return null;
        }

        var salutationIdx = Find(new Regex(@"^\s*Dear\b", RegexOptions.IgnoreCase));
        var reLineIdx = Find(new Regex(@"^\s*Re\s*:", RegexOptions.IgnoreCase));
        var dateIdx = Find(new Regex(@"^\s*(\d{1,2}[\/\s-]\w+|\d{1,2}\/\d{1,2}\/\d{2,4}|\w+\s\d{1,2},?\s\d{2,4})\s*$"));
        var yoursIdx = Find(new Regex(@"^\s*Yours\s+(sincerely|faithfully)\b", RegexOptions.IgnoreCase));

        var bodyParagraphs = new List<string>();
        var body = "";
        if (salutationIdx is not null && yoursIdx is not null)
        {
            var firstBody = Math.Max((reLineIdx ?? salutationIdx!.Value) + 1, salutationIdx!.Value + 1);
            var lastBody = yoursIdx.Value - 1;
            if (lastBody > firstBody)
            {
                var bodyLines = lines.Skip(firstBody).Take(lastBody - firstBody + 1);
                body = string.Join('\n', bodyLines);
                bodyParagraphs = body
                    .Split(new[] { "\n\n" }, StringSplitOptions.None)
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0)
                    .ToList();
            }
        }

        return new LetterStructure(lines, dateIdx, salutationIdx, reLineIdx, yoursIdx, body, bodyParagraphs);
    }

    private static int SeverityRank(RuleSeverity s) => s switch
    {
        RuleSeverity.Critical => 0,
        RuleSeverity.Major => 1,
        RuleSeverity.Minor => 2,
        _ => 3,
    };

    // ---------------------------------------------------------------------
    // Rule filtering
    // ---------------------------------------------------------------------

    internal static List<OetRule> RulesApplicableTo(OetRulebook book, string context)
    {
        var result = new List<OetRule>();
        foreach (var r in book.Rules)
        {
            if (r.AppliesTo is null) { result.Add(r); continue; }
            var el = r.AppliesTo.Value;
            if (el.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var s = el.GetString();
                if (string.Equals(s, "all", StringComparison.OrdinalIgnoreCase)) { result.Add(r); continue; }
            }
            if (el.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var hit = false;
                foreach (var v in el.EnumerateArray())
                {
                    if (string.Equals(v.GetString(), context, StringComparison.OrdinalIgnoreCase)) { hit = true; break; }
                }
                if (hit) result.Add(r);
            }
        }
        return result;
    }

    // ---------------------------------------------------------------------
    // Detector registry
    // ---------------------------------------------------------------------

    private delegate IEnumerable<LintFinding> Detector(OetRule rule, WritingLintInput input, LetterStructure structure);

    private Detector? DetectorFor(string checkId) => checkId switch
    {
        "content_requires_smoking_drinking" => DetectSmokingDrinking,
        "content_requires_allergy_for_atopic" => DetectAllergyForAtopic,
        "letter_body_length" => DetectBodyLength,
        "letter_paragraph_count" => DetectParagraphCount,
        "min_body_paragraphs" => DetectMinBodyParagraphs,
        "letter_structure_order" => DetectStructureOrder,
        "salutation_re_adjacent" => DetectSalutationReAdjacency,
        "blank_line_between_paragraphs" => DetectBlankBetweenParagraphs,
        "no_date_prefix" => DetectNoDatePrefix,
        "date_blank_line_sandwich" => DetectDateBlankSandwich,
        "salutation_last_name_only" => DetectSalutationLastName,
        "body_forbidden_phrase_the_patient" => DetectThePatient,
        "body_uses_last_name_only" => DetectThePatient,
        "minor_naming_convention" => DetectMinorNaming,
        "yours_sincerely_vs_faithfully" => DetectSincerelyVsFaithfully,
        "intro_contains_purpose" => DetectIntroPurpose,
        "urgent_intro_contains_urgent" => DetectUrgentIntro,
        "discharge_intro_no_identity" => DetectDischargeNoIdentity,
        "body_forbidden_phrase_next_visit" => DetectForbidden(@"\bnext visit\b", "Never write 'next visit'. Use 'on the following visit'."),
        "body_forbidden_phrase_yesterday" => DetectForbidden(@"\byesterday\b", "'Yesterday' is never used in medical letters."),
        "body_no_todays_date" => DetectTodaysDateInBody,
        "urgent_closure_phrase" => DetectUrgentClosure,
        "urgent_token_not_repeated" => DetectUrgentTokenNotRepeated,
        "urgent_body_starts_today" => DetectUrgentBodyStartsToday,
        "closure_mentions_review_if_required" => DetectReviewMention,
        "enclosure_results_phrase" => DetectEnclosureResults,
        "latin_abbreviations_translated" => DetectLatinAbbreviations,
        "no_contractions" => DetectContractions,
        "conditions_lowercase" => DetectConditionsLowercase,
        "linker_however_punctuation" => DetectHoweverPunctuation,
        "no_asap_in_letter" => DetectForbidden(@"\bASAP\b", "Never write 'ASAP'. Use 'at your earliest convenience'."),
        "discharge_intro_template" => DetectDischargeIntroTemplate,
        "discharge_omits_knownto_gp" => DetectDischargeOmits,
        "discharge_admitted_with_past_simple" => DetectDischargeAdmittedWith,
        "discharge_plan_present" => DetectDischargePlanPresent,
        "treatment_for_not_from" => DetectForbidden(@"\b(treated|admitted|referred|managed)\s+from\b", "Use 'treatment/admission/referral for ...', not 'from ...'."),
        "non_medical_no_jargon" => DetectNonMedicalJargon,
        "sentence_length_guard" => DetectSentenceLength,
        "linker_density" => DetectLinkerDensity,
        _ => null,
    };

    // ---------------------------------------------------------------------
    // Detectors
    // ---------------------------------------------------------------------

    private static IEnumerable<LintFinding> DetectSmokingDrinking(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var recipient = (input.RecipientSpecialty ?? "").ToLowerInvariant();
        string[] excluded = { "occupational therapist", "ot" };
        if (excluded.Any(e => recipient.Contains(e))) yield break;

        var body = input.LetterText.ToLowerInvariant();
        var hasSmoking = Regex.IsMatch(body, @"\b(smok|tobacco|cigarett)");
        var hasDrinking = Regex.IsMatch(body, @"\b(alcohol|drink(s|ing)?|units? per week)\b");
        if (!hasSmoking)
            yield return new LintFinding(rule.Id, rule.Severity,
                "Smoking status must be mentioned (positive or negative) unless writing to an occupational therapist.");
        if (!hasDrinking)
            yield return new LintFinding(rule.Id, rule.Severity,
                "Drinking status must be mentioned (positive or negative) unless writing to an occupational therapist.");
    }

    private static IEnumerable<LintFinding> DetectAllergyForAtopic(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (input.CaseNotesMarkers?.AtopicCondition != true) yield break;
        if (!Regex.IsMatch(input.LetterText, @"\ballerg", RegexOptions.IgnoreCase))
            yield return new LintFinding(rule.Id, rule.Severity,
                "Allergy status (positive or negative) must be included for atopic conditions.");
    }

    private static IEnumerable<LintFinding> DetectBodyLength(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        // Per Writing Module Technical Specification v1.0 (Dr. Ahmed Hesham),
        // the platform must NOT evaluate response length. R03.8 remains in the
        // rulebook as candidate guidance text only and emits zero findings.
        _ = rule; _ = input; _ = s;
        yield break;
    }

    private static IEnumerable<LintFinding> DetectParagraphCount(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var min = 2; var max = 4;
        if (rule.Params.HasValue && rule.Params.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (rule.Params.Value.TryGetProperty("min", out var mn) && mn.TryGetInt32(out var m)) min = m;
            if (rule.Params.Value.TryGetProperty("max", out var mx) && mx.TryGetInt32(out var x)) max = x;
        }
        var n = s.BodyParagraphs.Count;
        if (n < min) yield return new LintFinding(rule.Id, rule.Severity, $"Body has {n} paragraph(s). Minimum is {min}.");
        if (n > max) yield return new LintFinding(rule.Id, rule.Severity, $"Body has {n} paragraphs. Maximum is {max}.");
    }

    private static IEnumerable<LintFinding> DetectMinBodyParagraphs(OetRule rule, WritingLintInput input, LetterStructure s)
        => DetectParagraphCount(rule, input, s).Where(f => f.Message.Contains("Minimum"));

    private static IEnumerable<LintFinding> DetectStructureOrder(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var missing = new List<string>();
        if (s.DateIndex is null) missing.Add("Date");
        if (s.SalutationIndex is null) missing.Add("Salutation (Dear ...)");
        if (s.ReLineIndex is null) missing.Add("Re: line");
        if (s.YoursIndex is null) missing.Add("Yours sincerely/faithfully");
        if (missing.Count > 0)
        {
            yield return new LintFinding(rule.Id, rule.Severity, $"Letter structure is missing: {string.Join(", ", missing)}.");
            yield break;
        }
        int[] ord = { s.DateIndex!.Value, s.SalutationIndex!.Value, s.ReLineIndex!.Value, s.YoursIndex!.Value };
        for (int i = 1; i < ord.Length; i++)
            if (ord[i] <= ord[i - 1])
            {
                yield return new LintFinding(rule.Id, rule.Severity,
                    "Letter structure order is wrong. Expected: Address → Date → Salutation → Re: line → Body → Yours sincerely/faithfully.");
                yield break;
            }
    }

    private static IEnumerable<LintFinding> DetectSalutationReAdjacency(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.SalutationIndex is null || s.ReLineIndex is null) yield break;
        if (s.ReLineIndex!.Value - s.SalutationIndex!.Value != 1)
            yield return new LintFinding(rule.Id, rule.Severity,
                "No blank line allowed between 'Dear ...' and 'Re:'. They must be on consecutive lines.");
    }

    private static IEnumerable<LintFinding> DetectBlankBetweenParagraphs(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.Body.Length > 0 && s.BodyParagraphs.Count == 1 && s.Body.Contains('\n'))
            yield return new LintFinding(rule.Id, rule.Severity, "Separate body paragraphs with exactly one blank line.");
    }

    private static IEnumerable<LintFinding> DetectNoDatePrefix(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var m = Regex.Match(input.LetterText, @"^\s*Date\s*:", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (m.Success)
            yield return new LintFinding(rule.Id, rule.Severity,
                "Do not write 'Date:' before the date. The date stands on its own line.",
                Quote: m.Value, Start: m.Index, End: m.Index + m.Length);
    }

    private static IEnumerable<LintFinding> DetectDateBlankSandwich(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.DateIndex is null) yield break;
        var above = s.DateIndex.Value - 1 >= 0 ? s.Lines[s.DateIndex.Value - 1] : null;
        var below = s.DateIndex.Value + 1 < s.Lines.Length ? s.Lines[s.DateIndex.Value + 1] : null;
        if (above is not null && above.Trim().Length != 0)
            yield return new LintFinding(rule.Id, rule.Severity, "The date must have exactly one blank line above it.");
        if (below is not null && below.Trim().Length != 0)
            yield return new LintFinding(rule.Id, rule.Severity, "The date must have exactly one blank line below it.");
    }

    private static IEnumerable<LintFinding> DetectSalutationLastName(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.SalutationIndex is null) yield break;
        var line = s.Lines[s.SalutationIndex.Value];
        if (Regex.IsMatch(line, @"^Dear\s+(Dr\.?|Mr\.?|Ms\.?|Mrs\.?|Miss)\s+\w+\s+\w+", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(line, @"Sir/?Madam", RegexOptions.IgnoreCase))
        {
            yield return new LintFinding(rule.Id, rule.Severity,
                "Salutation should use LAST name only (e.g. 'Dear Dr Smith,'), not first+last.", Quote: line.Trim());
        }
    }

    private static IEnumerable<LintFinding> DetectThePatient(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var m = Regex.Match(s.Body, @"\bthe patient\b", RegexOptions.IgnoreCase);
        if (m.Success)
            yield return new LintFinding(rule.Id, rule.Severity,
                "Do not use 'the patient' in the body. Use title + last name or a pronoun.",
                Quote: m.Value, Start: m.Index, End: m.Index + m.Length);
    }

    private static IEnumerable<LintFinding> DetectMinorNaming(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.PatientIsMinor || s.ReLineIndex is null) yield break;
        var line = s.Lines[s.ReLineIndex.Value];
        if (Regex.IsMatch(line, @"^\s*Re\s*:\s*(Mr|Ms|Miss|Mrs|Dr|Master)\s", RegexOptions.IgnoreCase))
            yield return new LintFinding(rule.Id, rule.Severity,
                "For minors (under 18), do NOT use a title in the Re: line. Write the full name only.",
                Quote: line.Trim());
    }

    private static IEnumerable<LintFinding> DetectSincerelyVsFaithfully(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.SalutationIndex is null || s.YoursIndex is null) yield break;
        var salutation = s.Lines[s.SalutationIndex.Value];
        var yours = s.Lines[s.YoursIndex.Value];
        var isAnonymous = Regex.IsMatch(salutation, @"Sir/?Madam|Dear Doctor\b", RegexOptions.IgnoreCase)
                          && !Regex.IsMatch(salutation, @"Dr\s+\w+", RegexOptions.IgnoreCase);
        var usesSincerely = Regex.IsMatch(yours, @"sincerely", RegexOptions.IgnoreCase);
        if (isAnonymous && usesSincerely)
            yield return new LintFinding(rule.Id, rule.Severity,
                "When the salutation is 'Dear Sir/Madam' or 'Dear Doctor', use 'Yours faithfully', not 'Yours sincerely'.",
                Quote: yours.Trim());
        if (!isAnonymous && !usesSincerely)
            yield return new LintFinding(rule.Id, rule.Severity,
                "When the recipient is named, use 'Yours sincerely', not 'Yours faithfully'.",
                Quote: yours.Trim());
    }

    private static IEnumerable<LintFinding> DetectIntroPurpose(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.BodyParagraphs.Count == 0) yield break;
        var intro = s.BodyParagraphs[0];
        var markers = new Regex(@"\b(I am writing to|I am referring|I would like to refer|I am requesting|requesting|refer|update you|regarding|for (your|specialist|further) (assessment|management|review))\b",
            RegexOptions.IgnoreCase);
        if (!markers.IsMatch(intro))
            yield return new LintFinding(rule.Id, rule.Severity,
                "Introduction does not state the purpose/request. Always include why you are writing.");
    }

    private static IEnumerable<LintFinding> DetectUrgentIntro(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!string.Equals(input.LetterType, "urgent_referral", StringComparison.OrdinalIgnoreCase)) yield break;
        var intro = s.BodyParagraphs.Count > 0 ? s.BodyParagraphs[0] : "";
        if (!Regex.IsMatch(intro, @"\burgent", RegexOptions.IgnoreCase))
            yield return new LintFinding(rule.Id, rule.Severity,
                "Urgent referral introduction MUST contain the word 'urgent'.");
    }

    private static IEnumerable<LintFinding> DetectDischargeNoIdentity(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!string.Equals(input.LetterType, "discharge", StringComparison.OrdinalIgnoreCase)) yield break;
        var intro = s.BodyParagraphs.Count > 0 ? s.BodyParagraphs[0] : "";
        if (Regex.IsMatch(intro, @"\b\d+-year-old\b|\boccupation\b|\baged\s+\d+\b", RegexOptions.IgnoreCase))
            yield return new LintFinding(rule.Id, rule.Severity,
                "Discharge intro must not describe patient identity (age, occupation). The GP already knows the patient.");
    }

    private static Detector DetectForbidden(string pattern, string message) => (rule, input, s) =>
    {
        var m = Regex.Match(s.Body.Length > 0 ? s.Body : input.LetterText, pattern, RegexOptions.IgnoreCase);
        if (!m.Success) return Array.Empty<LintFinding>();
        return new[]
        {
            new LintFinding(rule.Id, rule.Severity, message, Quote: m.Value, Start: m.Index, End: m.Index + m.Length),
        };
    };

    private static IEnumerable<LintFinding> DetectTodaysDateInBody(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var m = Regex.Match(s.Body, @"\b\d{1,2}[\/\-\s](\d{1,2}|[A-Za-z]+)[\/\-\s]\d{2,4}\b");
        if (m.Success)
            yield return new LintFinding(rule.Id, rule.Severity,
                "Never write today's date in the body. Use 'On today's visit' or 'On today's presentation'.",
                Quote: m.Value, Start: m.Index, End: m.Index + m.Length);
    }

    private static IEnumerable<LintFinding> DetectUrgentClosure(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!string.Equals(input.LetterType, "urgent_referral", StringComparison.OrdinalIgnoreCase)) yield break;
        if (!Regex.IsMatch(input.LetterText, @"at your earliest convenience", RegexOptions.IgnoreCase))
            yield return new LintFinding(rule.Id, rule.Severity,
                "Urgent closure must include 'at your earliest convenience.'");
    }

    private static IEnumerable<LintFinding> DetectUrgentTokenNotRepeated(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!string.Equals(input.LetterType, "urgent_referral", StringComparison.OrdinalIgnoreCase)) yield break;
        var bodyLessIntro = string.Join("\n\n", s.BodyParagraphs.Skip(1));
        var count = Regex.Matches(bodyLessIntro, @"\burgent", RegexOptions.IgnoreCase).Count;
        if (count > 0)
            yield return new LintFinding(rule.Id, rule.Severity,
                "Use 'urgent' only in the introduction. Use 'at your earliest convenience' in the closure.");
    }

    private static IEnumerable<LintFinding> DetectUrgentBodyStartsToday(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!string.Equals(input.LetterType, "urgent_referral", StringComparison.OrdinalIgnoreCase)) yield break;
        var firstVisit = s.BodyParagraphs.Count > 1 ? s.BodyParagraphs[1] : "";
        if (!Regex.IsMatch(firstVisit, @"\b(today|on today'?s|presented today|on this visit)", RegexOptions.IgnoreCase))
            yield return new LintFinding(rule.Id, rule.Severity,
                "Urgent referrals must start the body with today's visit before background.");
    }

    private static IEnumerable<LintFinding> DetectReviewMention(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var fu = input.CaseNotesMarkers?.FollowUpDate;
        if (string.IsNullOrWhiteSpace(fu)) yield break;
        if (!Regex.IsMatch(input.LetterText, @"\b(review|follow[- ]up|see you (in|again)|appointment)\b", RegexOptions.IgnoreCase))
            yield return new LintFinding(rule.Id, rule.Severity,
                $"Case notes mention a follow-up ({fu}) — closure must reference the review.");
    }

    private static IEnumerable<LintFinding> DetectEnclosureResults(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (input.CaseNotesMarkers?.ResultsEnclosed != true) yield break;
        if (!Regex.IsMatch(input.LetterText, @"please find enclosed", RegexOptions.IgnoreCase))
            yield return new LintFinding(rule.Id, rule.Severity,
                "Results/imaging marked as enclosed — include 'Please find enclosed a copy of the pathology results.'");
    }

    private static IEnumerable<LintFinding> DetectLatinAbbreviations(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!rule.Params.HasValue || !rule.Params.Value.TryGetProperty("map", out var mapEl)) yield break;
        foreach (var prop in mapEl.EnumerateObject())
        {
            var re = new Regex($@"\b{Regex.Escape(prop.Name)}\b", RegexOptions.IgnoreCase);
            var m = re.Match(s.Body);
            if (m.Success)
                yield return new LintFinding(rule.Id, rule.Severity,
                    $"Translate Latin abbreviation \"{prop.Name}\" to plain English (\"{prop.Value.GetString()}\").",
                    Quote: m.Value, Start: m.Index, End: m.Index + m.Length,
                    FixSuggestion: prop.Value.GetString());
        }
    }

    private static IEnumerable<LintFinding> DetectContractions(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var re = new Regex(@"\b(?:don't|can't|won't|isn't|aren't|doesn't|didn't|wasn't|weren't|hasn't|haven't|hadn't|I'm|I've|I'll|she's|he's|it's|we're|they're|you're|you'd|we'd|they'd|I'd|we've|they've|you've|couldn't|wouldn't|shouldn't)\b",
            RegexOptions.IgnoreCase);
        var count = 0;
        foreach (Match m in re.Matches(s.Body))
        {
            yield return new LintFinding(rule.Id, rule.Severity,
                $"Contraction \"{m.Value}\" is not allowed in OET letters. Expand it.",
                Quote: m.Value, Start: m.Index, End: m.Index + m.Length);
            if (++count >= 5) yield break;
        }
    }

    private static IEnumerable<LintFinding> DetectConditionsLowercase(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        string[] forbidden = { "Hypertension", "Asthma", "Diabetes Mellitus", "Gastroenteritis", "Bronchial Asthma", "Myocardial Infarction" };
        foreach (var term in forbidden)
        {
            var re = new Regex($@"(?<![A-Za-z]){Regex.Escape(term)}(?![a-z])");
            var m = re.Match(s.Body);
            if (m.Success)
                yield return new LintFinding(rule.Id, rule.Severity,
                    $"Medical condition '{m.Value}' should be lowercase in running text.",
                    Quote: m.Value, Start: m.Index, End: m.Index + m.Length,
                    FixSuggestion: m.Value.ToLowerInvariant());
        }
    }

    private static IEnumerable<LintFinding> DetectHoweverPunctuation(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var re = new Regex(@"([^\n;]{5,})\bhowever\b", RegexOptions.IgnoreCase);
        foreach (Match m in re.Matches(s.Body))
        {
            if (!m.Groups[1].Value.TrimEnd().EndsWith(';'))
                yield return new LintFinding(rule.Id, rule.Severity,
                    "Precede 'however' with a semicolon: '[clause]; however, [clause].'",
                    Quote: m.Value.Trim(), Start: m.Index, End: m.Index + m.Length);
        }
    }

    private static IEnumerable<LintFinding> DetectDischargeIntroTemplate(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!string.Equals(input.LetterType, "discharge", StringComparison.OrdinalIgnoreCase)) yield break;
        var intro = s.BodyParagraphs.Count > 0 ? s.BodyParagraphs[0] : "";
        if (!Regex.IsMatch(intro, @"I am writing to update you regarding", RegexOptions.IgnoreCase))
            yield return new LintFinding(rule.Id, rule.Severity,
                "Discharge intro must start 'I am writing to update you regarding...' — do not use routine-referral phrasing.");
    }

    private static IEnumerable<LintFinding> DetectDischargeOmits(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!string.Equals(input.LetterType, "discharge", StringComparison.OrdinalIgnoreCase)) yield break;
        var text = input.LetterText;
        if (Regex.IsMatch(text, @"\bfamily history\b", RegexOptions.IgnoreCase))
            yield return new LintFinding(rule.Id, rule.Severity, "Discharge must not include family history.");
        if (Regex.IsMatch(text, @"\bsocial history\b", RegexOptions.IgnoreCase))
            yield return new LintFinding(rule.Id, rule.Severity, "Discharge must not include social history.");
        if (Regex.IsMatch(text, @"\bpast medical history\b", RegexOptions.IgnoreCase))
            yield return new LintFinding(rule.Id, rule.Severity, "Discharge must not include past medical history.");
        if (Regex.IsMatch(text, @"\bsmok(ing|es|er)\b", RegexOptions.IgnoreCase))
            yield return new LintFinding(rule.Id, rule.Severity, "Discharge must not include smoking status.");
    }

    private static IEnumerable<LintFinding> DetectDischargeAdmittedWith(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!string.Equals(input.LetterType, "discharge", StringComparison.OrdinalIgnoreCase)) yield break;
        var m = Regex.Match(input.LetterText, @"\bwas presented\b", RegexOptions.IgnoreCase);
        if (m.Success)
            yield return new LintFinding(rule.Id, rule.Severity,
                "'Was presented' is incorrect. Use 'was admitted to [hospital] with [condition]'.",
                Quote: m.Value, Start: m.Index, End: m.Index + m.Length);
    }

    private static IEnumerable<LintFinding> DetectDischargePlanPresent(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!string.Equals(input.LetterType, "discharge", StringComparison.OrdinalIgnoreCase)) yield break;
        var hasMeds = Regex.IsMatch(s.Body, @"\b(mg|mcg|tablet|capsule|ml|prescribed|discharged? with|dose)\b", RegexOptions.IgnoreCase);
        var hasInstr = Regex.IsMatch(s.Body, @"\b(follow[- ]up|review|advised|should|must|recommend)", RegexOptions.IgnoreCase);
        if (!hasMeds || !hasInstr)
            yield return new LintFinding(rule.Id, rule.Severity,
                "Discharge plan paragraph must contain medications with doses AND post-discharge instructions.");
    }

    private static IEnumerable<LintFinding> DetectNonMedicalJargon(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!string.Equals(input.LetterType, "non_medical_referral", StringComparison.OrdinalIgnoreCase)) yield break;
        var re = new Regex(@"\b(hypertension|hypoglycaemia|hyperglycaemia|myocardial infarction|tachycardia|bradycardia|BP|ECG|MRI|CT scan|paediatric|gynaecologic|endocrine)\b",
            RegexOptions.IgnoreCase);
        var c = 0;
        foreach (Match m in re.Matches(input.LetterText))
        {
            yield return new LintFinding(rule.Id, rule.Severity,
                $"Non-medical referral must avoid jargon '{m.Value}'. Use plain English.",
                Quote: m.Value, Start: m.Index, End: m.Index + m.Length);
            if (++c >= 3) yield break;
        }
    }

    private static IEnumerable<LintFinding> DetectSentenceLength(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var max = 30;
        if (rule.Params.HasValue && rule.Params.Value.TryGetProperty("maxWords", out var mx) && mx.TryGetInt32(out var m)) max = m;
        var c = 0;
        foreach (var sentence in Regex.Split(s.Body, @"(?<=[.!?])\s+"))
        {
            var wc = Regex.Matches(sentence, @"\b[\w'-]+\b").Count;
            if (wc > max)
            {
                yield return new LintFinding(rule.Id, rule.Severity,
                    $"Sentence is {wc} words — aim for 15–25. Split to improve clarity.",
                    Quote: sentence.Length > 80 ? sentence[..80] + "…" : sentence);
                if (++c >= 3) yield break;
            }
        }
    }

    private static IEnumerable<LintFinding> DetectLinkerDensity(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var max = 2;
        if (rule.Params.HasValue && rule.Params.Value.TryGetProperty("maxPerParagraph", out var mp) && mp.TryGetInt32(out var m)) max = m;
        var linkerRe = new Regex(@"\b(however|therefore|thus|in addition|moreover|furthermore|consequently|nonetheless|nevertheless|subsequently)\b",
            RegexOptions.IgnoreCase);
        foreach (var p in s.BodyParagraphs)
        {
            var count = linkerRe.Matches(p).Count;
            if (count > max)
                yield return new LintFinding(rule.Id, rule.Severity,
                    $"Paragraph uses {count} linkers. Max {max} per paragraph.");
        }
    }

    // Forbidden patterns baked into the JSON (not tied to a specific checkId)
    private static IEnumerable<LintFinding> RunForbiddenPatterns(OetRule rule, string text)
    {
        if (rule.ForbiddenPatterns is null) yield break;
        foreach (var pat in rule.ForbiddenPatterns)
        {
            Regex re;
            try { re = new Regex(pat, RegexOptions.IgnoreCase); }
            catch { continue; }
            var m = re.Match(text);
            if (m.Success)
                yield return new LintFinding(rule.Id, rule.Severity,
                    $"Rule {rule.Id}: \"{m.Value}\" violates the pattern.",
                    Quote: m.Value, Start: m.Index, End: m.Index + m.Length);
        }
    }
}
