using System.Text.RegularExpressions;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingAssessmentRuleFinding(
    string RuleId,
    string Category,
    string Severity,
    string Message,
    string? Quote,
    string? FixSuggestion,
    int? StartOffset,
    int? EndOffset,
    string PrimaryCriterionCode,
    string SecondaryCriterionCodesJson = "[]");

/// <summary>
/// v1.1 adapter around the deterministic Writing Rulebook engine: assigns
/// exactly one primary OET criterion (and a display category) to each
/// <see cref="WritingRuleEngine"/> finding. It adds NO rules of its own, so
/// the candidate grader, the Model Answer generator and the Model Answer
/// validator share one rule set (Writing Rule Enforcement Addendum Rev8 §7).
/// The former house-style battery (semicolon-only R12.9-R12.11, a comma
/// before "for which"/causal "as", R06.10/R06.11 Re:-line naming) was
/// removed: it flagged the owner-correct sentence-initial "However, ..." and
/// the Rev8 Re: form "Mr David Taylor, aged 55". Linker and patient-naming
/// rules are enforced by the engine's Rev8 detectors (linker_comma_and_case,
/// linker_*_punctuation, paragraph_start_patient_name,
/// body_uses_last_name_only, minor_naming_convention).
/// </summary>
public sealed class WritingAssessmentV11RuleEngine(WritingRuleEngine ruleEngine)
{
    public IReadOnlyList<WritingAssessmentRuleFinding> Evaluate(WritingLintInput input)
        => ruleEngine.Lint(input).Select(ToFinding).ToList();

    /// <summary>
    /// Kept for callers that still pass the case-note snapshot. Patient naming
    /// is now enforced inside <see cref="WritingRuleEngine"/> from the letter
    /// itself, so the snapshot is no longer needed.
    /// </summary>
    public IReadOnlyList<WritingAssessmentRuleFinding> Evaluate(
        WritingLintInput input,
        string? caseNotesSnapshot)
        => Evaluate(input);

    public static string PrimaryCriterionFor(string? ruleId) => Classify(ruleId).Criterion;

    private static readonly (string Criterion, string Category) Purpose = ("purpose", "purpose");
    private static readonly (string Criterion, string Category) Content = ("content", "content");
    private static readonly (string Criterion, string Category) Excess = ("conciseness_clarity", "irrelevant_excess");
    private static readonly (string Criterion, string Category) Register = ("genre_style", "register_jargon");
    private static readonly (string Criterion, string Category) Layout = ("organisation_layout", "layout_format");
    private static readonly (string Criterion, string Category) Language = ("language", "language");
    private static readonly (string Criterion, string Category) Grammar = ("language", "grammar");
    private static readonly (string Criterion, string Category) Punctuation = ("language", "punctuation");

    /// <summary>
    /// Explicit criterion + category for every <see cref="WritingRuleEngine.SupportedCheckIds"/>
    /// entry (the BUILTIN.* rule ids). Replaces the old substring heuristic,
    /// which sent no_contractions, year_not_abbreviated and others to
    /// "content" by accident. A new detector MUST be added here — the
    /// regression test fails otherwise.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, (string Criterion, string Category)> CheckIdCriteria =
        new Dictionary<string, (string Criterion, string Category)>(StringComparer.OrdinalIgnoreCase)
        {
            ["address_punctuation"] = Layout,
            ["age_not_duplicated_in_intro"] = Excess,
            ["ago_requires_past_simple"] = Grammar,
            ["blank_before_closing_phrase"] = Layout,
            ["blank_line_after_re_line"] = Layout,
            ["blank_line_between_paragraphs"] = Layout,
            ["body_forbidden_phrase_next_visit"] = Language,
            ["body_forbidden_phrase_the_patient"] = Register,
            ["body_forbidden_phrase_yesterday"] = Language,
            ["body_no_todays_date"] = Language,
            ["body_uses_last_name_only"] = Register,
            ["cancer_suspected_flagged_urgent"] = Purpose,
            ["closure_contact_offer"] = Layout,
            ["closure_contains_management"] = Layout,
            ["closure_mentions_consent_if_flagged"] = Content,
            ["closure_mentions_patient_request_if_flagged"] = Content,
            ["closure_mentions_review_if_required"] = Purpose,
            ["conditions_lowercase"] = Language,
            ["content_requires_allergy_for_atopic"] = Content,
            ["content_requires_smoking_drinking"] = Content,
            ["date_blank_line_sandwich"] = Layout,
            ["date_format_consistent"] = Layout,
            ["dob_age_forbidden_phrase"] = Layout,
            ["dob_colon_format"] = Layout,
            ["discharge_admitted_with_past_simple"] = Grammar,
            ["discharge_intro_no_identity"] = Excess,
            ["discharge_intro_template"] = Purpose,
            ["discharge_all_investigations_listed"] = Excess,
            ["discharge_omits_knownto_gp"] = Content,
            ["discharge_plan_present"] = Content,
            ["emotional_wording"] = Register,
            ["enclosure_results_phrase"] = Content,
            ["for_duration_requires_present_perfect"] = Grammar,
            ["intro_contains_purpose"] = Purpose,
            ["intro_opens_i_am_writing_to"] = Purpose,
            ["intro_sentence_count"] = Excess,
            ["judgmental_labels"] = Register,
            ["latin_abbreviations_translated"] = Language,
            ["letter_body_length"] = Excess,
            ["letter_paragraph_count"] = Layout,
            ["letter_structure_order"] = Layout,
            ["linker_avoid_words"] = Language,
            ["linker_comma_and_case"] = Punctuation,
            ["linker_density"] = Excess,
            ["linker_however_punctuation"] = Punctuation,
            ["linker_in_addition_punctuation"] = Punctuation,
            ["linker_therefore_punctuation"] = Punctuation,
            ["medication_list_punctuation"] = Punctuation,
            ["min_body_paragraphs"] = Layout,
            ["minor_naming_convention"] = Register,
            ["model_answer_layout"] = Layout,
            ["no_asap_in_letter"] = Register,
            ["no_contractions"] = Register,
            ["no_date_prefix"] = Layout,
            ["no_brackets_in_letter"] = Layout,
            ["no_duplicated_request"] = Excess,
            ["non_medical_no_jargon"] = Register,
            ["number_style_words_vs_digits"] = Language,
            ["numerical_values_have_units"] = Language,
            ["paragraph_start_patient_name"] = Register,
            ["re_line_age_dob"] = Layout,
            ["register_colloquial"] = Register,
            ["relationship_label_patient_reference"] = Register,
            ["salutation_last_name_only"] = Layout,
            ["salutation_re_adjacent"] = Layout,
            ["sentence_length_guard"] = ("conciseness_clarity", "language"),
            ["signoff_designation_present"] = Layout,
            ["signoff_no_invented_name"] = Layout,
            ["since_requires_present_perfect"] = Grammar,
            ["surgery_past_simple"] = Grammar,
            ["treatment_for_not_from"] = Grammar,
            ["urgent_body_starts_today"] = Layout,
            ["urgent_closure_phrase"] = Purpose,
            ["urgent_intro_contains_urgent"] = Purpose,
            ["urgent_token_not_repeated"] = Excess,
            ["value_unit_spacing"] = Punctuation,
            ["visit_content_tense_basic_check"] = Grammar,
            ["visit_paragraphization_check"] = Layout,
            ["year_not_abbreviated"] = Layout,
            ["yours_sincerely_capitalisation"] = Layout,
            ["yours_sincerely_vs_faithfully"] = Layout,
        };

    /// <summary>
    /// Owner registry rows (docs/writing-rev8/owner-rules-rev8.json) that carry
    /// check_ids, mapped to their first check_id so they share its criterion.
    /// Rows without a check_id (OWN-W-019/031/035/037/038) fall back to content.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> OwnerRuleCheckIds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OWN-W-001"] = "blank_line_after_re_line",
            ["OWN-W-002"] = "salutation_re_adjacent",
            ["OWN-W-003"] = "date_format_consistent",
            ["OWN-W-004"] = "dob_colon_format",
            ["OWN-W-005"] = "no_brackets_in_letter",
            ["OWN-W-006"] = "number_style_words_vs_digits",
            ["OWN-W-007"] = "emotional_wording",
            ["OWN-W-008"] = "judgmental_labels",
            ["OWN-W-009"] = "body_forbidden_phrase_the_patient",
            ["OWN-W-010"] = "paragraph_start_patient_name",
            ["OWN-W-011"] = "body_uses_last_name_only",
            ["OWN-W-012"] = "relationship_label_patient_reference",
            ["OWN-W-013"] = "intro_opens_i_am_writing_to",
            ["OWN-W-014"] = "closure_contact_offer",
            ["OWN-W-015"] = "urgent_closure_phrase",
            ["OWN-W-016"] = "urgent_token_not_repeated",
            ["OWN-W-017"] = "urgent_body_starts_today",
            ["OWN-W-018"] = "urgent_intro_contains_urgent",
            ["OWN-W-020"] = "no_duplicated_request",
            ["OWN-W-021"] = "linker_avoid_words",
            ["OWN-W-022"] = "linker_comma_and_case",
            ["OWN-W-023"] = "medication_list_punctuation",
            ["OWN-W-024"] = "value_unit_spacing",
            ["OWN-W-025"] = "age_not_duplicated_in_intro",
            ["OWN-W-026"] = "signoff_no_invented_name",
            ["OWN-W-027"] = "letter_body_length",
            ["OWN-W-028"] = "min_body_paragraphs",
            ["OWN-W-029"] = "body_no_todays_date",
            ["OWN-W-030"] = "no_contractions",
            ["OWN-W-032"] = "discharge_intro_template",
            ["OWN-W-033"] = "register_colloquial",
            ["OWN-W-034"] = "closure_contains_management",
            ["OWN-W-036"] = "model_answer_layout",
        };

    private static readonly Regex LegacySectionRe = new(@"^R(\d{1,2})\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static (string Criterion, string Category) Classify(string? ruleId)
    {
        var id = (ruleId ?? string.Empty).Trim();
        if (id.StartsWith("BUILTIN.", StringComparison.OrdinalIgnoreCase)) id = id["BUILTIN.".Length..];
        if (OwnerRuleCheckIds.TryGetValue(id, out var ownerCheckId)) id = ownerCheckId;
        if (CheckIdCriteria.TryGetValue(id, out var mapped)) return mapped;

        // Legacy profession books (R03..R16) report the rule id, not the
        // check id: map by rulebook section.
        var section = LegacySectionRe.Match(id);
        if (!section.Success) return Content;
        return int.Parse(section.Groups[1].Value) switch
        {
            3 or 14 or 16 => Content,          // content selection, discharge, assessment criteria
            4 or 5 or 6 or 8 or 9 => Layout,   // layout, address/date, salutation/Re:, body, closure
            7 or 13 => Purpose,                // introduction, urgent referral
            10 => Grammar,                     // tenses
            11 or 12 => Language,              // medications, grammar/vocabulary/linkers
            15 => Register,                    // non-medical recipients
            _ => Content,
        };
    }

    private static WritingAssessmentRuleFinding ToFinding(LintFinding finding)
    {
        var (criterion, category) = Classify(finding.RuleId);
        return new WritingAssessmentRuleFinding(
            finding.RuleId,
            category,
            finding.Severity.ToString().ToLowerInvariant(),
            finding.Message,
            finding.Quote,
            finding.FixSuggestion,
            finding.Start,
            finding.End,
            criterion);
    }
}
