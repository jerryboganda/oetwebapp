namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Machine-rule provenance tags (Writing Master Specification "Ultimate
/// Final", 13 Sep 2026, §15.1). Every candidate-facing finding must state
/// WHICH authority layer produced the rule and whether the deviation is
/// score-bearing for a candidate or coaching-only house style — the
/// official-vs-house firewall (§15.3): a Dr Hesham Model-Answer house rule
/// may hard-fail a Model Answer, but a candidate is only impacted when an
/// independent OET criterion is genuinely affected.
/// </summary>
public static class WritingProvenanceTags
{
    public const string SourceFactTask = "SOURCE_FACT_TASK";
    public const string OetOfficial = "OET_OFFICIAL";
    public const string GeneralEnglishValidated = "GENERAL_ENGLISH_VALIDATED";
    public const string OwnerModelAnswerCanonical = "OWNER_MODEL_ANSWER_CANONICAL";
    public const string ProfessionResource = "PROFESSION_RESOURCE";
    public const string PreferredStyle = "PREFERRED_STYLE";
}

/// <summary>
/// How a rule behaves in CANDIDATE grading (Ultimate Final §15.1 /
/// Appendix E "candidate_behavior"):
/// <list type="bullet">
/// <item>SCORE_BEARING — a genuine OET criterion impact; the finding may
/// lower the candidate's criterion score.</item>
/// <item>COACHING_ONLY — a house-style / preferred-form deviation; report
/// for learning, never as an automatic OET error.</item>
/// <item>ACCEPT_ALTERNATIVE — a valid professional alternative the grader
/// must accept; the deterministic detector either does not run for
/// candidates or runs in a semantic, equivalence-accepting form.</item>
/// <item>N_A — Model Answer only; no candidate meaning.</item>
/// </list>
/// </summary>
public static class WritingCandidateBehaviors
{
    public const string ScoreBearing = "SCORE_BEARING";
    public const string CoachingOnly = "COACHING_ONLY";
    public const string AcceptAlternative = "ACCEPT_ALTERNATIVE";
    public const string NotApplicable = "N_A";
}

/// <summary>
/// Per-check-id provenance + candidate behaviour for the entire
/// deterministic battery. Ultimate Final §15.1: "If the system cannot
/// explain the rule provenance and criterion impact, the finding is not
/// safe for automatic candidate scoring." The regression suite fails when a
/// new <see cref="WritingRuleEngine.SupportedCheckIds"/> entry is missing
/// here, the same completeness contract CheckIdCriteria already enforces.
/// </summary>
public static class WritingRuleProvenance
{
    public const string Version = "owner-clarifications-3-2026-09-16";

    public sealed record Provenance(string Tag, string CandidateBehavior);

    internal static readonly IReadOnlyDictionary<string, Provenance> ByCheckId =
        new Dictionary<string, Provenance>(StringComparer.OrdinalIgnoreCase)
        {
            // --- Layout & identification ---------------------------------
            // House spacing forms: template-only for candidates (§9 layout
            // rows — "assess presentation in context, not exact house
            // spacing as universal template").
            ["address_punctuation"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.ScoreBearing),
            ["blank_line_after_re_line"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["blank_line_between_paragraphs"] = new(WritingProvenanceTags.OetOfficial, WritingCandidateBehaviors.ScoreBearing),
            ["blank_before_closing_phrase"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["date_blank_line_sandwich"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["letter_paragraph_count"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["letter_structure_order"] = new(WritingProvenanceTags.OetOfficial, WritingCandidateBehaviors.ScoreBearing),
            ["min_body_paragraphs"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["model_answer_layout"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.NotApplicable),
            ["salutation_re_adjacent"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["signoff_designation_present"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.ScoreBearing),
            ["signoff_no_invented_name"] = new(WritingProvenanceTags.SourceFactTask, WritingCandidateBehaviors.ScoreBearing),
            ["yours_sincerely_capitalisation"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["yours_sincerely_vs_faithfully"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),

            // --- Dates, DOB, age ------------------------------------------
            ["date_format_consistent"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["year_not_abbreviated"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["no_date_prefix"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["dob_colon_format"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["dob_age_forbidden_phrase"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.ScoreBearing),
            ["re_line_age_dob"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["re_line_full_name"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.ScoreBearing),
            ["age_not_duplicated_in_intro"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["salutation_last_name_only"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),

            // --- Patient naming (§8.4: never automatic template penalties) --
            ["body_forbidden_phrase_the_patient"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["body_uses_last_name_only"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["minor_naming_convention"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.ScoreBearing),
            ["paragraph_start_patient_name"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["relationship_label_patient_reference"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),

            // --- Purpose & letter-type function ----------------------------
            ["cancer_suspected_flagged_urgent"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["intro_contains_purpose"] = new(WritingProvenanceTags.OetOfficial, WritingCandidateBehaviors.ScoreBearing),
            ["intro_opens_i_am_writing_to"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.AcceptAlternative),
            ["intro_sentence_count"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["urgent_intro_contains_urgent"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.ScoreBearing),
            ["urgent_body_starts_today"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.ScoreBearing),
            ["urgent_closure_phrase"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.ScoreBearing),
            ["urgent_token_not_repeated"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["discharge_intro_no_identity"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.ScoreBearing),
            ["discharge_intro_template"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["discharge_all_investigations_listed"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["discharge_omits_knownto_gp"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.ScoreBearing),
            ["discharge_plan_present"] = new(WritingProvenanceTags.OetOfficial, WritingCandidateBehaviors.ScoreBearing),
            ["discharge_admitted_with_past_simple"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["discharge_language_unsupported"] = new(WritingProvenanceTags.SourceFactTask, WritingCandidateBehaviors.ScoreBearing),

            // --- Owner Clarifications Addendum (14 Sep 2026), OA-01..OA-15 ---
            // OA-08/OA-09 grammar gates are genuine English failures in any
            // letter; the canonical-house-form rows are Model-Answer-only or
            // accept-alternative so candidates are never over-penalised (§6).
            ["intro_purpose_vague"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.AcceptAlternative),
            ["closure_request_paragraph"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["treatment_change_grammar"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["results_comma_splice"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["diabetes_type_words"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.AcceptAlternative),
            ["respiratory_rate_unit_style"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.AcceptAlternative),
            ["illogical_quantity_range"] = new(WritingProvenanceTags.SourceFactTask, WritingCandidateBehaviors.ScoreBearing),
            ["vague_clinical_object"] = new(WritingProvenanceTags.SourceFactTask, WritingCandidateBehaviors.ScoreBearing),
            ["letter_date_unsupported"] = new(WritingProvenanceTags.SourceFactTask, WritingCandidateBehaviors.ScoreBearing),
            ["recipient_name_mismatch"] = new(WritingProvenanceTags.SourceFactTask, WritingCandidateBehaviors.ScoreBearing),
            ["semicolon_overuse"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),

            // --- Owner Clarifications Round 3 (15 Sep 2026, OA3-01..OA3-05) ---
            ["patient_name_spelling"] = new(WritingProvenanceTags.SourceFactTask, WritingCandidateBehaviors.ScoreBearing),
            ["re_line_dob_priority"] = new(WritingProvenanceTags.SourceFactTask, WritingCandidateBehaviors.ScoreBearing),
            ["result_at_wording"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.AcceptAlternative),
            ["dangling_treatment_modifier"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["address_slash_separator"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["salutation_re_same_line"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.ScoreBearing),
            ["intro_adverbial_comma"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.AcceptAlternative),
            ["patient_title_mismatch"] = new(WritingProvenanceTags.SourceFactTask, WritingCandidateBehaviors.ScoreBearing),

            // --- Owner Clarifications Addendum TWO (14 Sep 2026), OA2-01..OA2-20 ---
            // OA2-20 is the firewall: the strict canonical requirements in this
            // block (background paragraph position, the exact contact template,
            // the house medication separator) are Model-Answer house style and
            // must never become automatic candidate penalties. Only genuine
            // source-fidelity and English failures stay score-bearing.
            ["discharge_function_missed"] = new(WritingProvenanceTags.SourceFactTask, WritingCandidateBehaviors.AcceptAlternative),
            ["result_noun_fragment"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["supine_position_wording"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["result_head_noun"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.AcceptAlternative),
            ["background_paragraph_placement"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["vital_sign_interpretation_unsupported"] = new(WritingProvenanceTags.SourceFactTask, WritingCandidateBehaviors.ScoreBearing),
            ["role_salutation_matches_task"] = new(WritingProvenanceTags.SourceFactTask, WritingCandidateBehaviors.ScoreBearing),
            ["canonical_contact_template"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.AcceptAlternative),
            ["re_line_identity_unsupported"] = new(WritingProvenanceTags.SourceFactTask, WritingCandidateBehaviors.ScoreBearing),
            ["brand_generic_duplication"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),

            // --- Content selection & source fidelity ------------------------
            ["content_requires_smoking_drinking"] = new(WritingProvenanceTags.ProfessionResource, WritingCandidateBehaviors.CoachingOnly),
            ["content_requires_allergy_for_atopic"] = new(WritingProvenanceTags.ProfessionResource, WritingCandidateBehaviors.CoachingOnly),
            ["closure_mentions_consent_if_flagged"] = new(WritingProvenanceTags.SourceFactTask, WritingCandidateBehaviors.ScoreBearing),
            ["closure_mentions_patient_request_if_flagged"] = new(WritingProvenanceTags.SourceFactTask, WritingCandidateBehaviors.ScoreBearing),
            ["closure_mentions_review_if_required"] = new(WritingProvenanceTags.SourceFactTask, WritingCandidateBehaviors.ScoreBearing),
            ["enclosure_results_phrase"] = new(WritingProvenanceTags.SourceFactTask, WritingCandidateBehaviors.ScoreBearing),
            ["lifestyle_frequency_precision"] = new(WritingProvenanceTags.SourceFactTask, WritingCandidateBehaviors.ScoreBearing),

            // --- Conciseness & closure -------------------------------------
            ["letter_body_length"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["no_duplicated_request"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.ScoreBearing),
            ["closure_contact_offer"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["closure_contains_management"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.ScoreBearing),
            ["non_medical_no_jargon"] = new(WritingProvenanceTags.OetOfficial, WritingCandidateBehaviors.ScoreBearing),
            ["sentence_length_guard"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["linker_density"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),

            // --- Register & tone --------------------------------------------
            ["emotional_wording"] = new(WritingProvenanceTags.OetOfficial, WritingCandidateBehaviors.ScoreBearing),
            ["judgmental_labels"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.ScoreBearing),
            ["register_colloquial"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.ScoreBearing),
            ["no_asap_in_letter"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.ScoreBearing),
            ["no_contractions"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),

            // --- Numbers, units, medication ---------------------------------
            ["number_style_words_vs_digits"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["numerical_values_have_units"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["value_unit_spacing"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["latin_abbreviations_translated"] = new(WritingProvenanceTags.ProfessionResource, WritingCandidateBehaviors.ScoreBearing),
            ["medication_list_punctuation"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["medication_passive_grammar"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),

            // --- Grammar & tense ---------------------------------------------
            ["ago_requires_past_simple"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["conditions_lowercase"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["for_duration_requires_present_perfect"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["since_requires_present_perfect"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["surgery_past_simple"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["treatment_for_not_from"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["visit_content_tense_basic_check"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["visit_paragraphization_check"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.ScoreBearing),
            ["incomplete_clinical_construction"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),

            // --- Linkers ------------------------------------------------------
            ["linker_avoid_words"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["linker_comma_and_case"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["linker_however_punctuation"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["linker_in_addition_punctuation"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),
            ["linker_therefore_punctuation"] = new(WritingProvenanceTags.GeneralEnglishValidated, WritingCandidateBehaviors.ScoreBearing),

            // --- Body phrasing house rules ------------------------------------
            ["body_forbidden_phrase_next_visit"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["body_forbidden_phrase_yesterday"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["body_no_todays_date"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
            ["no_brackets_in_letter"] = new(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly),
        };

    /// <summary>
    /// Provenance for a check id (or a BUILTIN.* / OWN-W-* rule id, resolved
    /// through the same check-id mapping the criterion classifier uses).
    /// Unknown ids fall back to a conservative, explicitly-tagged default
    /// rather than silently scoring: OWNER house style, coaching-only.
    /// </summary>
    public static Provenance For(string? checkId)
        => ByCheckId.TryGetValue(Normalize(checkId), out var p)
            ? p
            : new Provenance(WritingProvenanceTags.OwnerModelAnswerCanonical, WritingCandidateBehaviors.CoachingOnly);

    private static string Normalize(string? checkId)
    {
        var id = (checkId ?? string.Empty).Trim();
        if (id.StartsWith("BUILTIN.", StringComparison.OrdinalIgnoreCase)) id = id["BUILTIN.".Length..];
        return id;
    }
}
