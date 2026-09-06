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
    private static readonly HashSet<string> SupportedCheckIdSet = new(StringComparer.Ordinal)
    {
        "address_punctuation",
        "ago_requires_past_simple",
        "blank_before_closing_phrase",
        "blank_line_between_paragraphs",
        "body_forbidden_phrase_next_visit",
        "body_forbidden_phrase_the_patient",
        "body_forbidden_phrase_yesterday",
        "body_no_todays_date",
        "body_uses_last_name_only",
        "cancer_suspected_flagged_urgent",
        "closure_mentions_consent_if_flagged",
        "closure_mentions_patient_request_if_flagged",
        "closure_mentions_review_if_required",
        "conditions_lowercase",
        "content_requires_allergy_for_atopic",
        "content_requires_smoking_drinking",
        "date_blank_line_sandwich",
        "date_format_consistent",
        "dob_age_forbidden_phrase",
        "discharge_admitted_with_past_simple",
        "discharge_intro_no_identity",
        "discharge_intro_template",
        "discharge_all_investigations_listed",
        "discharge_omits_knownto_gp",
        "discharge_plan_present",
        "enclosure_results_phrase",
        "for_duration_requires_present_perfect",
        "intro_contains_purpose",
        "intro_sentence_count",
        "latin_abbreviations_translated",
        "letter_body_length",
        "letter_paragraph_count",
        "letter_structure_order",
        "linker_density",
        "linker_however_punctuation",
        "linker_in_addition_punctuation",
        "linker_therefore_punctuation",
        "min_body_paragraphs",
        "minor_naming_convention",
        "no_asap_in_letter",
        "no_contractions",
        "no_date_prefix",
        "no_brackets_in_letter",
        "non_medical_no_jargon",
        "numerical_values_have_units",
        "re_line_age_dob",
        "salutation_last_name_only",
        "salutation_re_adjacent",
        "sentence_length_guard",
        "signoff_no_invented_name",
        "since_requires_present_perfect",
        "surgery_past_simple",
        "treatment_for_not_from",
        "urgent_body_starts_today",
        "urgent_closure_phrase",
        "urgent_intro_contains_urgent",
        "urgent_token_not_repeated",
        "visit_content_tense_basic_check",
        "visit_paragraphization_check",
        "year_not_abbreviated",
        "yours_sincerely_capitalisation",
        "yours_sincerely_vs_faithfully",
    };

    // Severity defaults for the always-on builtin battery below. Values are
    // taken verbatim from the original (pre-canonical-switch) profession
    // rulebooks, which carried these CheckIds with real severities — kept as
    // the historical default so restoring the fallback (see Lint() below)
    // does not change any existing detector's blocking behaviour. The three
    // new global formatting/sign-off CheckIds (owner addendum, 2026-09-06)
    // are absolute layout rules and are always Critical.
    private static readonly Dictionary<string, RuleSeverity> BuiltInSeverityByCheckId = new(StringComparer.Ordinal)
    {
        ["cancer_suspected_flagged_urgent"] = RuleSeverity.Critical,
        ["content_requires_smoking_drinking"] = RuleSeverity.Critical,
        ["content_requires_allergy_for_atopic"] = RuleSeverity.Critical,
        ["letter_body_length"] = RuleSeverity.Major,
        ["letter_paragraph_count"] = RuleSeverity.Major,
        ["letter_structure_order"] = RuleSeverity.Major,
        ["salutation_re_adjacent"] = RuleSeverity.Critical,
        ["blank_line_between_paragraphs"] = RuleSeverity.Major,
        ["address_punctuation"] = RuleSeverity.Critical,
        ["date_format_consistent"] = RuleSeverity.Major,
        ["year_not_abbreviated"] = RuleSeverity.Major,
        ["no_date_prefix"] = RuleSeverity.Critical,
        ["date_blank_line_sandwich"] = RuleSeverity.Major,
        ["salutation_last_name_only"] = RuleSeverity.Critical,
        ["body_uses_last_name_only"] = RuleSeverity.Critical,
        ["re_line_age_dob"] = RuleSeverity.Major,
        ["minor_naming_convention"] = RuleSeverity.Critical,
        ["yours_sincerely_vs_faithfully"] = RuleSeverity.Critical,
        ["yours_sincerely_capitalisation"] = RuleSeverity.Minor,
        ["intro_sentence_count"] = RuleSeverity.Major,
        ["intro_contains_purpose"] = RuleSeverity.Critical,
        ["urgent_intro_contains_urgent"] = RuleSeverity.Critical,
        ["discharge_intro_no_identity"] = RuleSeverity.Critical,
        ["min_body_paragraphs"] = RuleSeverity.Critical,
        ["visit_paragraphization_check"] = RuleSeverity.Critical,
        ["urgent_body_starts_today"] = RuleSeverity.Critical,
        ["body_forbidden_phrase_next_visit"] = RuleSeverity.Critical,
        ["body_no_todays_date"] = RuleSeverity.Critical,
        ["body_forbidden_phrase_yesterday"] = RuleSeverity.Major,
        ["body_forbidden_phrase_the_patient"] = RuleSeverity.Critical,
        ["urgent_closure_phrase"] = RuleSeverity.Critical,
        ["urgent_token_not_repeated"] = RuleSeverity.Major,
        ["closure_mentions_review_if_required"] = RuleSeverity.Critical,
        ["enclosure_results_phrase"] = RuleSeverity.Major,
        ["closure_mentions_patient_request_if_flagged"] = RuleSeverity.Major,
        ["closure_mentions_consent_if_flagged"] = RuleSeverity.Major,
        ["blank_before_closing_phrase"] = RuleSeverity.Major,
        ["visit_content_tense_basic_check"] = RuleSeverity.Critical,
        ["since_requires_present_perfect"] = RuleSeverity.Critical,
        ["for_duration_requires_present_perfect"] = RuleSeverity.Critical,
        ["surgery_past_simple"] = RuleSeverity.Critical,
        ["ago_requires_past_simple"] = RuleSeverity.Critical,
        ["latin_abbreviations_translated"] = RuleSeverity.Critical,
        ["numerical_values_have_units"] = RuleSeverity.Critical,
        ["no_contractions"] = RuleSeverity.Critical,
        ["conditions_lowercase"] = RuleSeverity.Critical,
        ["linker_however_punctuation"] = RuleSeverity.Critical,
        ["linker_therefore_punctuation"] = RuleSeverity.Major,
        ["linker_in_addition_punctuation"] = RuleSeverity.Major,
        ["sentence_length_guard"] = RuleSeverity.Major,
        ["linker_density"] = RuleSeverity.Major,
        ["no_asap_in_letter"] = RuleSeverity.Critical,
        ["discharge_intro_template"] = RuleSeverity.Critical,
        ["discharge_omits_knownto_gp"] = RuleSeverity.Critical,
        ["discharge_admitted_with_past_simple"] = RuleSeverity.Critical,
        ["discharge_plan_present"] = RuleSeverity.Critical,
        ["discharge_all_investigations_listed"] = RuleSeverity.Critical,
        ["treatment_for_not_from"] = RuleSeverity.Critical,
        ["non_medical_no_jargon"] = RuleSeverity.Critical,
        ["no_brackets_in_letter"] = RuleSeverity.Critical,
        ["dob_age_forbidden_phrase"] = RuleSeverity.Critical,
        ["signoff_no_invented_name"] = RuleSeverity.Critical,
    };

    public static IReadOnlySet<string> SupportedCheckIds => SupportedCheckIdSet;

    public static bool IsSupportedCheckId(string? checkId)
        => !string.IsNullOrWhiteSpace(checkId) && SupportedCheckIdSet.Contains(checkId);

    public IReadOnlyList<LintFinding> Lint(WritingLintInput input)
    {
        var book = loader.Load(RuleKind.Writing, input.Profession);
        var structure = ParseLetter(input.LetterText);
        var applicable = RulesApplicableTo(book, input.LetterType);

        var findings = new List<LintFinding>();
        var handledCheckIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in applicable)
        {
            if (!string.IsNullOrWhiteSpace(rule.CheckId))
            {
                handledCheckIds.Add(rule.CheckId!);
                var det = DetectorFor(rule.CheckId!);
                if (det is not null) findings.AddRange(det(rule, input, structure));
            }
            if (rule.ForbiddenPatterns is { Count: > 0 })
            {
                findings.AddRange(RunForbiddenPatterns(rule, input.LetterText));
            }
        }

        // Always-on baseline: any supported detector whose CheckId is not
        // already wired via a rulebook-JSON rule still runs, using the
        // BuiltInSeverityByCheckId default. Root-cause fix — the vendored
        // canonical rule records (docs/canonical-rules/README.md) carry no
        // CheckId/ForbiddenPatterns by design, which otherwise silently
        // drops this entire deterministic detector battery for every
        // canonical profession. Legacy professions are unaffected: their
        // rulebook-JSON rules already populate handledCheckIds above, so
        // this loop only fills the gap, never double-fires.
        foreach (var checkId in SupportedCheckIdSet)
        {
            if (handledCheckIds.Contains(checkId)) continue;
            var det = DetectorFor(checkId);
            if (det is null) continue;
            var builtIn = new OetRule
            {
                Id = $"BUILTIN.{checkId}",
                Title = checkId,
                CheckId = checkId,
                Enforcement = RuleEnforcement.Deterministic,
                Severity = BuiltInSeverityByCheckId.GetValueOrDefault(checkId, RuleSeverity.Major),
            };
            findings.AddRange(det(builtIn, input, structure));
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
        "body_forbidden_phrase_next_visit" => DetectNoOp,
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
        "address_punctuation" => DetectAddressPunctuation,
        "re_line_age_dob" => DetectReLineAgeDob,
        "yours_sincerely_capitalisation" => DetectYoursSincerelyCapitalisation,
        "intro_sentence_count" => DetectIntroSentenceCount,
        "closure_mentions_patient_request_if_flagged" => DetectClosurePatientRequest,
        "closure_mentions_consent_if_flagged" => DetectClosureConsent,
        "blank_before_closing_phrase" => DetectBlankBeforeClosing,
        "since_requires_present_perfect" => DetectSinceRequiresPresentPerfect,
        "for_duration_requires_present_perfect" => DetectForDurationRequiresPresentPerfect,
        "surgery_past_simple" => DetectSurgeryPastSimple,
        "ago_requires_past_simple" => DetectAgoRequiresPastSimple,
        "linker_therefore_punctuation" => DetectThereforePunctuation,
        "linker_in_addition_punctuation" => DetectInAdditionPunctuation,
        "date_format_consistent" => DetectDateFormatConsistent,
        "year_not_abbreviated" => DetectYearNotAbbreviated,
        "discharge_intro_template" => DetectDischargeIntroTemplate,
        "discharge_omits_knownto_gp" => DetectDischargeOmits,
        "discharge_admitted_with_past_simple" => DetectDischargeAdmittedWith,
        "discharge_plan_present" => DetectDischargePlanPresent,
        "treatment_for_not_from" => DetectForbidden(@"\b(treated|admitted|referred|managed)\s+from\b", "Use 'treatment/admission/referral for ...', not 'from ...'."),
        "non_medical_no_jargon" => DetectNonMedicalJargon,
        "sentence_length_guard" => DetectSentenceLength,
        "linker_density" => DetectLinkerDensity,
        "cancer_suspected_flagged_urgent" => DetectCancerSuspectedUrgent,
        "visit_paragraphization_check" => DetectMarkerDependentNoop,
        "visit_content_tense_basic_check" => DetectVisitContentTense,
        "numerical_values_have_units" => DetectNumericalValuesHaveUnits,
        "discharge_all_investigations_listed" => DetectMarkerDependentNoop,
        "no_brackets_in_letter" => DetectNoBrackets,
        "dob_age_forbidden_phrase" => DetectDobAgeForbiddenPhrase,
        "signoff_no_invented_name" => DetectSignoffNoInventedName,
        _ => null,
    };

    // ---------------------------------------------------------------------
    // Detectors
    // ---------------------------------------------------------------------

    // FINAL MASTER Writing Rulebook v1.0 (31 Aug 2026), §8 provenance audit:
    // R03.4 is OVERRIDDEN_OR_CORRECTED — "Smoking/alcohol are not universal
    // always-include facts; relevance and reader needs control content."
    // A deterministic detector cannot judge relevance, so it must not
    // deduct. Intentionally inert no-op; the corrected rule body (now in the
    // rulebook JSON) guides the AI assessor, which sees the case notes.
    private static IEnumerable<LintFinding> DetectSmokingDrinking(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        yield break;
    }

    // FINAL MASTER Writing Rulebook v1.0 (31 Aug 2026), §8 provenance audit:
    // R03.6 is OVERRIDDEN_OR_CORRECTED — "Allergy inclusion is
    // relevance/safety driven; the atopic-condition rule is a heuristic, not
    // an official absolute." Intentionally inert no-op; relevance is judged
    // by the AI assessor from the case notes, never by a blanket detector.
    private static IEnumerable<LintFinding> DetectAllergyForAtopic(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        yield break;
    }

    // Mirrors lib/rulebook/writing-rules.ts letter_body_length.
    //
    // Per owner directive 2026-05-07 the prior "platform must NOT evaluate
    // response length" stance (Tech Spec v1.0) is overridden. We now emit a
    // single advisory finding when the body word count is meaningfully
    // outside the configured 180–200 target window.
    //
    // The advisory is deliberately downgraded to RuleSeverity.Minor regardless
    // of rule.Severity (which is "major" in the rulebook JSON) so it never
    // blocks submission and never gets sorted ahead of substantive findings.
    private static IEnumerable<LintFinding> DetectBodyLength(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var min = 180;
        var max = 200;
        if (rule.Params.HasValue && rule.Params.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (rule.Params.Value.TryGetProperty("min", out var mn) && mn.TryGetInt32(out var mv)) min = mv;
            if (rule.Params.Value.TryGetProperty("max", out var mx) && mx.TryGetInt32(out var xv)) max = xv;
        }

        // Prefer the parsed body paragraphs; fall back to the whole letter
        // text when no salutation+closure has been detected yet (early draft).
        string source = s.BodyParagraphs.Count > 0
            ? string.Join(" ", s.BodyParagraphs)
            : input.LetterText;
        var count = Regex.Matches(source, @"\S+").Count;

        // Suppress noise during brainstorming / very early drafts.
        if (count < 80) yield break;
        if (count >= min && count <= max) yield break;

        var direction = count < min ? "short" : "long";
        var hint = direction == "short"
            ? "you may be missing relevant data"
            : "you may be including semi-relevant data";
        yield return new LintFinding(
            rule.Id,
            RuleSeverity.Minor,
            $"Letter body is {count} word(s) (target {min}–{max}). Too {direction} — {hint}. Advisory only; submission is not blocked.");
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
            && !Regex.IsMatch(line, @"Sir/?Madam", RegexOptions.IgnoreCase)
            // "Dear Mr and Mrs Murray," addresses a couple by one shared
            // surname, not a first+last name — the second \w+ that matched
            // is the joining word "and", not a first name.
            && !Regex.IsMatch(line, @"^Dear\s+(Dr\.?|Mr\.?|Ms\.?|Mrs\.?|Miss)\s+and\s+(Dr\.?|Mr\.?|Ms\.?|Mrs\.?|Miss)\s+\w+", RegexOptions.IgnoreCase))
        {
            yield return new LintFinding(rule.Id, rule.Severity,
                "Salutation should use LAST name only (e.g. 'Dear Dr Smith,'), not first+last.", Quote: line.Trim());
        }
    }

    // Rulebook update (31 Aug 2026, G-W-112): "'Patient' is not a forbidden word ... this
    // explicitly overrides the legacy blanket prohibition." The new PDF's own "correct"
    // example letter itself uses "The patient was discharged with community follow-up".
    // Intentionally inert no-op — kept (rather than removed) because it is still wired
    // from the registry above for both "body_forbidden_phrase_the_patient" and
    // "body_uses_last_name_only".
    private static IEnumerable<LintFinding> DetectThePatient(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        yield break;
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

    // Rulebook update (31 Aug 2026): "next visit" is standard English and is no longer
    // forbidden (the old requirement to write "on the following visit" instead is dropped).
    // Intentionally inert no-op, wired for "body_forbidden_phrase_next_visit" above.
    private static IEnumerable<LintFinding> DetectNoOp(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        yield break;
    }

    // Root-cause fix (2026-09-06): this used to match ANY date-like substring
    // anywhere in the body, flagging every letter that cites a past visit,
    // admission, or investigation date — which is essential, expected
    // content in a clinical letter, not a violation. The rule's own message
    // ("never write TODAY'S date") means: don't redundantly repeat the
    // letter's own header date inside the body — narrate it as "today" or
    // "on today's visit" instead. Now compares body dates against the
    // header date specifically, rather than flagging any date at all.
    private static IEnumerable<LintFinding> DetectTodaysDateInBody(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.DateIndex is null) yield break;
        var headerLine = s.Lines[s.DateIndex.Value];
        var headerMatch = Regex.Match(headerLine, @"\d{1,2}[\/\.\-\s](\d{1,2}|[A-Za-z]+)[\/\.\-\s]\d{2,4}");
        if (!headerMatch.Success) yield break;
        var headerDateText = headerMatch.Value.Trim();

        var m = Regex.Match(s.Body, Regex.Escape(headerDateText), RegexOptions.IgnoreCase);
        if (m.Success)
            yield return new LintFinding(rule.Id, rule.Severity,
                "Never repeat today's date (the letter's own header date) in the body. Use 'today' or 'on today's visit/presentation' instead.",
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

    // R11.1 — translating Latin abbreviations is still recommended (rulebook
    // G-W-105, FINAL MASTER 2026-08-31), "unless the task/recipient convention
    // clearly supports [keeping it]" — a condition the engine cannot evaluate
    // deterministically. So this stays advisory-only: downgraded to
    // RuleSeverity.Minor regardless of rule.Severity, matching the established
    // DetectBodyLength advisory pattern above.
    private static IEnumerable<LintFinding> DetectLatinAbbreviations(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!rule.Params.HasValue || !rule.Params.Value.TryGetProperty("map", out var mapEl)) yield break;
        foreach (var prop in mapEl.EnumerateObject())
        {
            var re = new Regex($@"\b{Regex.Escape(prop.Name)}\b", RegexOptions.IgnoreCase);
            var m = re.Match(s.Body);
            if (m.Success)
                yield return new LintFinding(rule.Id, RuleSeverity.Minor,
                    $"Consider translating Latin abbreviation \"{prop.Name}\" to plain English (\"{prop.Value.GetString()}\") unless you are confident the recipient's convention supports it.",
                    Quote: m.Value, Start: m.Index, End: m.Index + m.Length,
                    FixSuggestion: prop.Value.GetString());
        }
    }

    // R12.1 — an isolated contraction is a Genre/Style note, not a catastrophic
    // grammar failure (rulebook DH-W-044 / G-W-117, FINAL MASTER 2026-08-31).
    // Still worth flagging, but downgraded to RuleSeverity.Minor regardless of
    // rule.Severity, matching the established DetectBodyLength advisory pattern
    // above — non-blocking. Cap of 5 findings unchanged.
    private static IEnumerable<LintFinding> DetectContractions(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var re = new Regex(@"\b(?:don't|can't|won't|isn't|aren't|doesn't|didn't|wasn't|weren't|hasn't|haven't|hadn't|I'm|I've|I'll|she's|he's|it's|we're|they're|you're|you'd|we'd|they'd|I'd|we've|they've|you've|couldn't|wouldn't|shouldn't)\b",
            RegexOptions.IgnoreCase);
        var count = 0;
        foreach (Match m in re.Matches(s.Body))
        {
            yield return new LintFinding(rule.Id, RuleSeverity.Minor,
                $"Contraction \"{m.Value}\" is a Genre/Style note — OET letters conventionally avoid it, but an isolated instance is not an automatic failure.",
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

    // Conjunctive adverbs (however / therefore / thus / in addition) are correct either
    // preceded by a semicolon within one sentence, or starting a brand-new sentence after
    // a full stop (e.g. "... clause. However, ..."). Only a genuine run-on — the linker
    // mid-clause with neither a semicolon nor a preceding sentence boundary — is an error.
    private static bool PrecededBySemicolonOrSentenceBoundary(string clauseBeforeLinker)
    {
        var trimmed = clauseBeforeLinker.TrimEnd();
        if (trimmed.Length == 0) return true;
        var lastChar = trimmed[^1];
        return lastChar == ';' || lastChar == '.' || lastChar == '!' || lastChar == '?';
    }

    private static IEnumerable<LintFinding> DetectHoweverPunctuation(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var re = new Regex(@"([^\n;]{5,})\bhowever\b", RegexOptions.IgnoreCase);
        foreach (Match m in re.Matches(s.Body))
        {
            if (!PrecededBySemicolonOrSentenceBoundary(m.Groups[1].Value))
                yield return new LintFinding(rule.Id, rule.Severity,
                    "Precede 'however' with either a semicolon, or start a new sentence: '...clause; however, ...' or '...clause. However, ...'.",
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

    // FINAL MASTER Writing Rulebook v1.0 (31 Aug 2026), §8 provenance audit:
    // R14.4 is OVERRIDDEN_OR_CORRECTED — "Social/family/past history can be
    // relevant to discharge if it changed or affects ongoing care; do not
    // exclude categorically." A deterministic detector cannot judge
    // relevance, so it must not deduct. Intentionally inert no-op.
    private static IEnumerable<LintFinding> DetectDischargeOmits(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        yield break;
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

    // FINAL MASTER Writing Rulebook v1.0 (31 Aug 2026), §8 provenance audit:
    // R15.2 is OVERRIDDEN_OR_CORRECTED — "Technicality should match the
    // recipient. Allied-health professionals may understand clinical
    // terminology; explain/simplify for the actual reader." A fixed word
    // list cannot judge the actual reader, so it must not deduct.
    // Intentionally inert no-op; register is judged by the AI assessor,
    // which sees the task recipient.
    private static IEnumerable<LintFinding> DetectNonMedicalJargon(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        yield break;
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

    // ---------------------------------------------------------------------
    // Newly ported detectors (mirror lib/rulebook/writing-rules.ts)
    // ---------------------------------------------------------------------

    // R05.2 — address must not have trailing commas / full stops on any line
    private static IEnumerable<LintFinding> DetectAddressPunctuation(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var boundary = s.DateIndex ?? s.SalutationIndex ?? s.Lines.Length;
        for (int i = 0; i < boundary && i < s.Lines.Length; i++)
        {
            var trimmed = s.Lines[i].Trim();
            if (trimmed.Length == 0) continue;
            if (Regex.IsMatch(trimmed, @"[,.]$"))
                yield return new LintFinding(rule.Id, rule.Severity,
                    $"Address line contains punctuation: \"{trimmed}\". No commas or full stops in the address.",
                    Quote: trimmed);
        }
    }

    // R06.8 — Re: line must not contain "Age:" (capitalised). 'aged 40' lowercase is OK.
    private static IEnumerable<LintFinding> DetectReLineAgeDob(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.ReLineIndex is null) yield break;
        var reLine = s.Lines[s.ReLineIndex.Value];
        if (Regex.IsMatch(reLine, @"\bAge\s*:", RegexOptions.IgnoreCase))
            yield return new LintFinding(rule.Id, rule.Severity,
                "Use 'aged 40' (lowercase, no colon) not 'Age: 40' in the Re: line.",
                Quote: reLine.Trim());
    }

    // R06.12 — Yours capitalisation + spelling
    private static IEnumerable<LintFinding> DetectYoursSincerelyCapitalisation(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.YoursIndex is null) yield break;
        var yours = s.Lines[s.YoursIndex.Value].Trim();
        // Lowercase 'yours' is wrong (port of TS check)
        if (Regex.IsMatch(yours, @"^yours\s+(sincerely|faithfully)", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(yours, @"^Yours\s+(sincerely|faithfully)"))
        {
            yield return new LintFinding(rule.Id, rule.Severity,
                "Capitalise 'Yours' (capital Y, lowercase s in 'sincerely').",
                Quote: yours);
            yield break;
        }
        // Misspellings of 'sincerely' / 'faithfully'
        if (Regex.IsMatch(yours, @"sincerly|sincerley|sincrely|faithfuly", RegexOptions.IgnoreCase))
            yield return new LintFinding(rule.Id, rule.Severity,
                "Check spelling of the closing phrase (S-I-N-C-E-R-E-L-Y).",
                Quote: yours);
    }

    // R07.1 — intro paragraph must not exceed maxSentences (default 3)
    private static IEnumerable<LintFinding> DetectIntroSentenceCount(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.SalutationIndex is null || s.BodyParagraphs.Count == 0) yield break;
        var max = 3;
        if (rule.Params.HasValue && rule.Params.Value.ValueKind == System.Text.Json.JsonValueKind.Object
            && rule.Params.Value.TryGetProperty("maxSentences", out var ms) && ms.TryGetInt32(out var mv))
            max = mv;
        var intro = s.BodyParagraphs[0];
        var sentences = Regex.Split(intro, @"(?<=[.!?])\s+")
            .Where(x => x.Trim().Length > 0)
            .Count();
        if (sentences > max)
            yield return new LintFinding(rule.Id, rule.Severity,
                $"Introduction has {sentences} sentences. Keep it to {max} maximum.");
    }

    // R09.7 — patient-initiated referral phrase
    private static IEnumerable<LintFinding> DetectClosurePatientRequest(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (input.CaseNotesMarkers?.PatientInitiatedReferral != true) yield break;
        if (!Regex.IsMatch(input.LetterText,
            @"\bat (his|her|mr|ms|mrs|miss|dr) .*?(\brequest|\bown request)\b|upon (his|her) request",
            RegexOptions.IgnoreCase))
            yield return new LintFinding(rule.Id, rule.Severity,
                "Patient-initiated referral — include 'upon his/her request' or 'at [Name]'s request'.");
    }

    // R09.8 — consent statement
    private static IEnumerable<LintFinding> DetectClosureConsent(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (input.CaseNotesMarkers?.ConsentDocumented != true) yield break;
        if (!Regex.IsMatch(input.LetterText,
            @"\b(fully informed|has consented|has been informed|aware of (his|her) (diagnosis|management))\b",
            RegexOptions.IgnoreCase))
            yield return new LintFinding(rule.Id, rule.Severity,
                "Consent was documented in case notes — include the consent statement in closure.");
    }

    // R09.9 — blank line before "Yours sincerely,"
    private static IEnumerable<LintFinding> DetectBlankBeforeClosing(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.YoursIndex is null || s.YoursIndex.Value == 0) yield break;
        var prev = s.Lines[s.YoursIndex.Value - 1];
        if (prev.Trim().Length > 0)
            yield return new LintFinding(rule.Id, rule.Severity,
                "Leave one blank line between the last body paragraph and 'Yours sincerely,'.");
    }

    // R10.5 — "since" requires present perfect
    private static IEnumerable<LintFinding> DetectSinceRequiresPresentPerfect(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var re = new Regex(
            @"\b(?:she|he|they|the patient|[A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)\s+(had|has)\s+([a-z]+)\s+since\b",
            RegexOptions.IgnoreCase);
        foreach (Match m in re.Matches(s.Body))
        {
            if (string.Equals(m.Groups[1].Value, "had", StringComparison.OrdinalIgnoreCase))
                yield return new LintFinding(rule.Id, rule.Severity,
                    "With 'since', use present perfect ('has had X since ...'), not past simple.",
                    Quote: m.Value, Start: m.Index, End: m.Index + m.Length);
        }
    }

    // R10.6 — "for [duration]" requires present perfect
    private static IEnumerable<LintFinding> DetectForDurationRequiresPresentPerfect(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var re = new Regex(
            @"\b(she|he|they)\s+had\s+([a-z ]+?)\s+for\s+(\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+(years?|months?|weeks?|days?)\b",
            RegexOptions.IgnoreCase);
        foreach (Match m in re.Matches(s.Body))
        {
            yield return new LintFinding(rule.Id, rule.Severity,
                "With 'for [duration]', use present perfect ('has had X for Y').",
                Quote: m.Value, Start: m.Index, End: m.Index + m.Length);
        }
    }

    // R10.8 — present perfect for surgery is valid when no finished-time marker is
    // stated (rulebook G-W-021, FINAL MASTER 2026-08-31): "He has undergone cataract
    // surgery and is recovering well" is correct present-perfect usage when the
    // result still has current/ongoing relevance. Past simple is required only when
    // a specific finished-time expression accompanies the mention (a year, "ago",
    // "in <Month>", "on <date>", "last <year/month/week>") — pairing present
    // perfect with a stated finished time is the actual error, not present-perfect
    // phrasing on its own.
    private static readonly Regex SurgeryFinishedTimeRegex = new(
        @"\b((19|20)\d{2}|ago|in\s+(January|February|March|April|May|June|July|August|September|October|November|December)|on\s+(the\s+)?\d{1,2}(st|nd|rd|th)?|last\s+(year|month|week))\b",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectSurgeryPastSimple(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var re = new Regex(
            @"\bhas\s+had\s+(a\s+|an\s+)?([a-z]+(?:ectomy|otomy|ostomy|plasty)|surgery|operation)\b",
            RegexOptions.IgnoreCase);
        var m = re.Match(s.Body);
        if (!m.Success) yield break;

        // Only flag when a finished-time marker appears in a short window right
        // after the matched phrase — that combination is the genuine error.
        var windowStart = m.Index + m.Length;
        var windowEnd = Math.Min(s.Body.Length, windowStart + 40);
        var window = s.Body[windowStart..windowEnd];
        if (!SurgeryFinishedTimeRegex.IsMatch(window)) yield break;

        yield return new LintFinding(rule.Id, rule.Severity,
            "Present perfect combined with a finished-time marker (a year, 'ago', a specific date) is inconsistent: use past simple, e.g. 'had a cholecystectomy in 2018'. Present perfect alone ('has had surgery') is fine when no finished time is stated.",
            Quote: m.Value, Start: m.Index, End: m.Index + m.Length);
    }

    // R10.10 — "X ago" requires past simple
    private static IEnumerable<LintFinding> DetectAgoRequiresPastSimple(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var re = new Regex(
            @"\bhas\s+(\w+ed)\s+(\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+(years?|months?|weeks?|days?)\s+ago\b",
            RegexOptions.IgnoreCase);
        var m = re.Match(s.Body);
        if (m.Success)
            yield return new LintFinding(rule.Id, rule.Severity,
                "'X ago' always takes past simple, not present perfect. Write 'She presented 3 weeks ago.'",
                Quote: m.Value, Start: m.Index, End: m.Index + m.Length);
    }

    // R12.10 — 'therefore' / 'thus' must be preceded by ';' OR start a new sentence after a full stop
    private static IEnumerable<LintFinding> DetectThereforePunctuation(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var re = new Regex(@"([^\n;]{5,})\b(therefore|thus)\b", RegexOptions.IgnoreCase);
        foreach (Match m in re.Matches(s.Body))
        {
            if (!PrecededBySemicolonOrSentenceBoundary(m.Groups[1].Value))
                yield return new LintFinding(rule.Id, rule.Severity,
                    "Precede 'therefore'/'thus' with either a semicolon, or start a new sentence: '...clause; therefore, ...' or '...clause. Therefore, ...'.",
                    Quote: m.Value.Trim(), Start: m.Index, End: m.Index + m.Length);
        }
    }

    // R12.11 — 'in addition' (clause joiner; not 'in addition to/with') must be preceded by ';' OR start a new sentence after a full stop
    private static IEnumerable<LintFinding> DetectInAdditionPunctuation(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var re = new Regex(@"([^\n;]{5,})\bin addition\b(?!\s+(to|with)\b)", RegexOptions.IgnoreCase);
        foreach (Match m in re.Matches(s.Body))
        {
            if (!PrecededBySemicolonOrSentenceBoundary(m.Groups[1].Value))
                yield return new LintFinding(rule.Id, rule.Severity,
                    "Precede 'in addition' (as a clause joiner) with either a semicolon, or start a new sentence: '...clause; in addition, ...' or '...clause. In addition, ...'.",
                    Quote: m.Value.Trim(), Start: m.Index, End: m.Index + m.Length);
        }
    }

    // R05.5 — date format consistency. Distinguishes DD/MM/YYYY (slash-numeric),
    // DD.MM.YYYY (dot-numeric), "1 January 2024" (day-first long), and
    // "January 1, 2024" (month-first long). Global Model Answer Formatting &
    // Sign-Off addendum (owner, 2026-09-06), §2: one consistent style per
    // letter; date/address ordering is unaffected. If two or more distinct
    // styles appear in the same letter, flag major.
    private static IEnumerable<LintFinding> DetectDateFormatConsistent(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var re = new Regex(
            @"\b(\d{1,2}\/\d{1,2}\/\d{4}|\d{1,2}\.\d{1,2}\.\d{4}|\d{1,2}(?:st|nd|rd|th)?\s+[A-Za-z]+\s+\d{4}|[A-Za-z]+\s+\d{1,2},?\s+\d{4})\b");
        var hasSlash = false;
        var hasDot = false;
        var hasDayFirstLong = false;
        var hasMonthFirstLong = false;
        foreach (Match m in re.Matches(input.LetterText))
        {
            if (m.Value.Contains('/')) hasSlash = true;
            else if (m.Value.Contains('.')) hasDot = true;
            else if (char.IsDigit(m.Value[0])) hasDayFirstLong = true;
            else hasMonthFirstLong = true;
        }
        var styles = (hasSlash ? 1 : 0) + (hasDot ? 1 : 0) + (hasDayFirstLong ? 1 : 0) + (hasMonthFirstLong ? 1 : 0);
        if (styles > 1)
            yield return new LintFinding(rule.Id, RuleSeverity.Major,
                "Date format must be consistent throughout the letter. Pick ONE style (fully written, slash, or dot) and use it for every date.");
    }

    // ---------------------------------------------------------------------
    // Global Model Answer Formatting & Sign-Off Rules (owner addendum,
    // 2026-09-06). Mandatory, system-wide; supersedes any conflicting
    // generated output or earlier implementation behaviour. Applied via the
    // always-on builtin battery in Lint() above, so these three checks run
    // for every profession/letter-type without any rulebook-JSON change.
    // ---------------------------------------------------------------------

    // §1 — no round/square brackets or placeholder brackets anywhere in the
    // final letter (e.g. "[Name]", "(Medical Practitioner)"). Zero tolerance.
    private static IEnumerable<LintFinding> DetectNoBrackets(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var count = 0;
        foreach (Match m in Regex.Matches(input.LetterText, @"[()\[\]]"))
        {
            var start = Math.Max(0, m.Index - 20);
            var end = Math.Min(input.LetterText.Length, m.Index + 20);
            yield return new LintFinding(rule.Id, rule.Severity,
                "No brackets, parentheses, or placeholder brackets are allowed anywhere in the final letter. Rewrite the bracketed content naturally.",
                Quote: input.LetterText[start..end].Trim(), Start: m.Index, End: m.Index + m.Length);
            if (++count >= 5) yield break;
        }
    }

    // §3 — never write "DOB not provided"/"age not given" etc. Omit entirely
    // if unavailable; never present it as a hedge phrase.
    private static IEnumerable<LintFinding> DetectDobAgeForbiddenPhrase(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var m = Regex.Match(input.LetterText,
            @"\b(DOB|date of birth|age)\s*(is\s+)?(not\s+(provided|given|available|stated|known)|unknown)\b",
            RegexOptions.IgnoreCase);
        if (m.Success)
            yield return new LintFinding(rule.Id, rule.Severity,
                "Do not write hedge phrases like 'DOB not provided'. Omit DOB entirely if unavailable, or state the age naturally without brackets.",
                Quote: m.Value, Start: m.Index, End: m.Index + m.Length);
    }

    private static readonly Regex SignoffTitleNamePattern = new(@"\b(Dr|Mr|Mrs|Ms|Miss)\.?\s+[A-Z][a-zA-Z'-]+");
    private static readonly HashSet<string> SignoffRoleWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "doctor", "nurse", "charge", "staff", "registered", "pharmacist", "physiotherapist", "dentist",
        "radiographer", "podiatrist", "dietitian", "occupational", "therapist", "optometrist", "speech",
        "pathologist", "language", "veterinarian", "veterinary", "surgeon", "practitioner", "general",
        "medical", "midwife", "clinician", "consultant", "specialist", "paramedic", "technician", "officer",
        "assistant", "associate", "senior", "junior",
    };
    private static readonly Regex SignoffOrgOrContactPattern = new(
        @"\b(hospital|clinic|centre|center|unit|department|ward|surgery|health\s?care|nhs|street|road|avenue|lane|drive)\b" +
        @"|[\w.+-]+@[\w-]+\.[\w.-]+|\b\d{3,}[\s-]?\d{3,}\b",
        RegexOptions.IgnoreCase);

    // §4 — the sign-off after "Yours sincerely/faithfully," must be the
    // professional designation ONLY: no invented writer name, no fabricated
    // surname, no hospital/department/address/phone/email beneath it.
    private static IEnumerable<LintFinding> DetectSignoffNoInventedName(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (s.YoursIndex is null) yield break;
        var sigLines = s.Lines.Skip(s.YoursIndex.Value + 1)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        if (sigLines.Count == 0) yield break;

        var sigBlock = string.Join(" | ", sigLines);
        var titleName = SignoffTitleNamePattern.Match(sigBlock);
        if (titleName.Success)
        {
            yield return new LintFinding(rule.Id, rule.Severity,
                "Sign-off must not include an invented writer name or title (e.g. 'Dr Reynolds'). Use the professional designation only (e.g. 'Doctor'), unless the case notes explicitly give the writer's real name.",
                Quote: titleName.Value);
            yield break;
        }

        foreach (var line in sigLines)
        {
            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var looksLikeBareName = words.Length is 2 or 3
                && words.All(w => Regex.IsMatch(w, @"^[A-Z][a-zA-Z'-]*$"))
                && !words.Any(w => SignoffRoleWords.Contains(w));
            if (looksLikeBareName)
            {
                yield return new LintFinding(rule.Id, rule.Severity,
                    "Sign-off looks like an invented personal name. Use the professional designation only (e.g. 'Doctor', 'Charge Nurse'), not a name.",
                    Quote: line);
                yield break;
            }
        }

        foreach (var line in sigLines)
        {
            if (SignoffOrgOrContactPattern.IsMatch(line))
            {
                yield return new LintFinding(rule.Id, rule.Severity,
                    "Do not add the hospital, clinic, department, unit, organisation, address, phone number, or email beneath the sign-off. The default signature line is the professional designation only.",
                    Quote: line);
                yield break;
            }
        }
    }

    // R05.6 — year not abbreviated (e.g. 01/01/'24)
    private static IEnumerable<LintFinding> DetectYearNotAbbreviated(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var m = Regex.Match(input.LetterText, @"\b\d{1,2}\/\d{1,2}\/'?\d{2}\b");
        if (m.Success && !m.Value.Contains("19") && !m.Value.Contains("20"))
            yield return new LintFinding(rule.Id, rule.Severity,
                "Do not abbreviate the year (write 2024, not '24).",
                Quote: m.Value, Start: m.Index, End: m.Index + m.Length);
    }

    // FINAL MASTER Writing Rulebook v1.0 (31 Aug 2026), §8 provenance audit:
    // R01.5 is OVERRIDDEN_OR_CORRECTED — "Suspected cancer does not
    // automatically define task type independently of the task/context;
    // preserve urgency exactly as supported." Urgency follows the writing
    // task, which a letter-only detector cannot see. Intentionally inert
    // no-op; urgency is judged by the AI assessor from task + case notes.
    private static IEnumerable<LintFinding> DetectCancerSuspectedUrgent(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        yield break;
    }

    private static IEnumerable<LintFinding> DetectVisitContentTense(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (string.IsNullOrWhiteSpace(s.Body)) yield break;
        var verbs = "presented|examined|prescribed|admitted|referred|advised|counselled|reviewed|commenced|attended";
        var re = new Regex(@"\b(has|have|had)\s+(?:been\s+)?(" + verbs + @")\b", RegexOptions.IgnoreCase);
        var hits = 0;
        foreach (Match m in re.Matches(s.Body))
        {
            yield return new LintFinding(rule.Id, rule.Severity,
                $"Visit content should use past simple. \"{m.Value}\" looks like present perfect.",
                Quote: m.Value, Start: m.Index, End: m.Index + m.Length);
            hits++;
            if (hits >= 3) yield break;
        }
    }

    private static IEnumerable<LintFinding> DetectNumericalValuesHaveUnits(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        var keywords = new[]
        {
            "glucose",
            "hba1c",
            "temperature",
            "blood pressure",
            "pulse",
            "weight",
            "height",
            "BMI",
            "haemoglobin",
            "sodium",
            "potassium",
            "creatinine"
        };
        // /min (pulse/respiratory rate) and a bare X/Y ratio (blood pressure,
        // conventionally never needs "mmHg" spelled out) each already carry
        // their own implicit unit \u2014 added 2026-09-06 after this flagged
        // "pulse 66/min" and "blood pressure 120/60" as missing a unit.
        var unitRe = new Regex(@"(mmol\/l|mg\/dl|kg|g|cm|mm|mmHg|bpm|\/min|\u00b0c|celsius|mmol|%|\d+\s*\/\s*\d+)", RegexOptions.IgnoreCase);
        var findings = 0;
        foreach (var keyword in keywords)
        {
            var re = new Regex(@"\b" + Regex.Escape(keyword) + @"\b[^.\n]{0,30}", RegexOptions.IgnoreCase);
            foreach (Match m in re.Matches(input.LetterText))
            {
                var snippet = m.Value;
                if (!Regex.IsMatch(snippet, @"\d") || unitRe.IsMatch(snippet))
                {
                    continue;
                }

                yield return new LintFinding(rule.Id, rule.Severity,
                    $"Investigation value missing its unit: \"{snippet.Trim()}\".",
                    Quote: snippet, Start: m.Index, End: m.Index + snippet.Length);
                findings++;
                if (findings >= 3) yield break;
            }
        }
    }

    private static IEnumerable<LintFinding> DetectMarkerDependentNoop(OetRule rule, WritingLintInput input, LetterStructure s)
        => [];

    // Rulebook update (31 Aug 2026): the rulebook JSON's forbiddenPatterns arrays for
    // "next visit" (R08.7/R10.14) and "the patient" (R08.14/R12.2/G-W-112) still contain
    // the legacy regexes (rulebook JSON is out of scope for this change). Those two
    // checkIds' own Detector functions above are already inert no-ops for the same
    // rulebook update, so this generic JSON-driven pattern runner is told to skip the
    // same checkIds — otherwise it would independently re-flag the identical phrase.
    private static readonly HashSet<string> ForbiddenPatternCheckIdsNoLongerEnforced = new(StringComparer.Ordinal)
    {
        "body_forbidden_phrase_next_visit",
        "body_forbidden_phrase_the_patient",
        "body_uses_last_name_only",
    };

    // Forbidden patterns baked into the JSON (not tied to a specific checkId)
    private static IEnumerable<LintFinding> RunForbiddenPatterns(OetRule rule, string text)
    {
        if (rule.ForbiddenPatterns is null) yield break;
        if (!string.IsNullOrWhiteSpace(rule.CheckId) && ForbiddenPatternCheckIdsNoLongerEnforced.Contains(rule.CheckId))
            yield break;
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
