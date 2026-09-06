using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

public sealed class WritingTaskUnderstandingTests
{
    [Fact]
    public void Urgent_task_returns_triggering_evidence_and_recipient()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write an urgent referral to the emergency registrar for acute management.",
            "Today: sudden chest pain.",
            "routine_referral");

        Assert.Equal("urgent_referral", result.PrimaryLetterType);
        Assert.Equal("emergency_registrar", result.RecipientCategory);
        Assert.Contains(result.EvidencePhrases, item => item.Contains("urgent", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("classified", result.Status);
    }

    [Fact]
    public void Conflicting_letter_type_evidence_requires_review_instead_of_forcing_a_type()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a discharge letter for urgent admission to the emergency registrar.",
            "Hospital admission and discharge planning are both mentioned.",
            "routine_referral");

        Assert.Equal("requires_review", result.Status);
        Assert.Null(result.PrimaryLetterType);
        Assert.True(result.ConflictingEvidence);
    }

    [Fact]
    public void Missing_diagnosis_uses_only_the_case_note_plan_section_as_specified()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write to the GP requesting follow-up.",
            "Findings: wheeze.\nPlan: asthma review and inhaler education.",
            "routine_referral");

        Assert.Equal("routine_referral", result.PrimaryLetterType);
        Assert.Contains("asthma review", result.DiagnosisOrPlanEvidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Doris_white_task_returns_community_nurse_recipient_and_classified_status()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Using the information in the case notes, write a letter to Mrs Lucy Walters , a Community Nurse , requesting for wound dressing. Address the letter to Mrs Lucy Walters, Newtown Nurse Clinic, 10 Stillwater St, Newtown.",
            "Plan : discharge\nAttach list of meds",
            "routine_referral");

        Assert.Equal("classified", result.Status);
        Assert.Equal("community_nurse", result.RecipientCategory);
        Assert.Equal("routine_referral", result.PrimaryLetterType);
        Assert.Equal("discharge", result.DiagnosisOrPlanEvidence);
        Assert.Contains("wound dressing", result.PurposeOrRequest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nurse_and_nurse_in_charge_categorized_as_nurse()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a letter to the Nurse-in-Charge at Newtown Hospital.",
            "Plan: wound dressing",
            "routine_referral");

        Assert.Equal("nurse", result.RecipientCategory);
    }

    [Fact]
    public void Specialists_without_dr_prefix_categorized_as_clinician()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Refer the patient to a cardiologist for urgent assessment.",
            "Plan: refer to cardiologist",
            "routine_referral");

        Assert.Equal("named_or_unnamed_clinician", result.RecipientCategory);
    }

    [Fact]
    public void Named_recipient_with_honorific_categorized_as_named_recipient()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Using the information in the case notes, write a letter to Mrs Lucy Walters at Newtown Clinic.",
            "Plan: review",
            "routine_referral");

        Assert.Equal("named_recipient", result.RecipientCategory);
    }

    [Fact]
    public void Case_notes_plan_fallback_detects_recipient_when_task_text_is_generic()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Using the information in the case notes, write a letter regarding the patient's care.",
            "Plan: refer to community nurse for wound management.",
            "routine_referral");

        Assert.Equal("community_nurse", result.RecipientCategory);
    }

    [Fact]
    public void Write_a_letter_to_named_recipient_without_honorific_categorized()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Using the information in the case notes, write a letter to Lucy Walters requesting wound dressing.",
            "Plan: review",
            "routine_referral");

        Assert.Equal("named_recipient", result.RecipientCategory);
    }

    [Fact]
    public void Health_facility_with_periods_and_apostrophes_categorized()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a letter to St. Vincent's Hospital regarding the patient.",
            "Plan: review",
            "routine_referral");

        Assert.Equal("health_facility", result.RecipientCategory);
    }

    [Fact]
    public void Case_notes_multiline_bulleted_plan_fallback_detects_recipient()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Using the information in the case notes, write a letter regarding the patient's care.",
            "Plan:\n- Refer to Community Nurse for wound management\n- Review in 2 weeks",
            "routine_referral");

        Assert.Equal("community_nurse", result.RecipientCategory);
    }

    [Fact]
    public void Case_notes_management_section_fallback_detects_recipient()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Using the information in the case notes, write a letter regarding the patient's care.",
            "Management:\nRefer to physiotherapist for rehabilitation.",
            "routine_referral");

        Assert.Equal("non_medical_professional", result.RecipientCategory);
    }

    [Fact]
    public void Midwife_and_health_visitor_categorized()
    {
        var result1 = WritingTaskUnderstandingService.Understand(
            "Write a letter to the Community Midwife at Newtown Clinic.",
            "Plan: post-natal review",
            "routine_referral");
        Assert.Equal("nurse", result1.RecipientCategory);

        var result2 = WritingTaskUnderstandingService.Understand(
            "Write a letter to the Health Visitor regarding baby check.",
            "Plan: infant feeding review",
            "routine_referral");
        Assert.Equal("nurse", result2.RecipientCategory);
    }

    [Fact]
    public void GP_with_periods_categorized_as_gp()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a letter of discharge to the patient's G.P.",
            "Plan: discharge home",
            "discharge");

        Assert.Equal("gp", result.RecipientCategory);
    }

    [Fact]
    public void Dentist_and_dental_specialties_categorized_as_clinician()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a letter of referral to the patient's dentist for extraction.",
            "Plan: dental assessment",
            "routine_referral");

        Assert.Equal("named_or_unnamed_clinician", result.RecipientCategory);
    }

    [Fact]
    public void Speech_and_language_therapist_categorized_as_non_medical()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a letter of referral to the Speech and Language Therapist.",
            "Plan: dysphagia assessment",
            "routine_referral");

        Assert.Equal("non_medical_professional", result.RecipientCategory);
    }

    [Fact]
    public void Community_health_centre_categorized_as_health_facility_not_named_recipient()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a letter to the Community Health Centre regarding the patient.",
            "Plan: follow up",
            "routine_referral");

        Assert.Equal("health_facility", result.RecipientCategory);
    }

    [Fact]
    public void Lucy_walters_at_clinic_categorized_as_named_recipient_not_health_facility()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a letter to Lucy Walters at Newtown Clinic requesting wound care.",
            "Plan: dressing",
            "routine_referral");

        Assert.Equal("named_recipient", result.RecipientCategory);
    }

    [Fact]
    public void Patients_doctor_categorized_as_clinician_not_named_recipient()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a letter to the patient's doctor requesting immediate review.",
            "Plan: medical review",
            "routine_referral");

        Assert.Equal("named_or_unnamed_clinician", result.RecipientCategory);
    }

    // ── Production catalogue remediation (44 live tasks, all detector gaps) ──

    [Fact]
    public void Referral_letter_task_counts_as_request_evidence()
    {
        // "write a letter of referral to Dr ..." is the OET staple phrasing;
        // "referral" must count as clinical purpose evidence.
        var result = WritingTaskUnderstandingService.Understand(
            "Using the information in the case notes, write a letter of referral to the endodontist, Dr Patrick O'Malley.",
            "She requires further endodontic treatment on tooth 37.",
            "routine_referral");

        Assert.False(string.IsNullOrWhiteSpace(result.DiagnosisOrPlanEvidence));
        Assert.False(result.ConflictingEvidence);
    }

    [Fact]
    public void Inline_diagnosis_in_case_notes_counts_as_evidence()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a referral letter to Dr Green requesting a review.",
            "Mr Amir Akbari is married.\nHis diagnosis is Guillain-Barré Syndrome (GBS).\nHe lives in a rented house.",
            "routine_referral");

        Assert.Contains("Guillain", result.DiagnosisOrPlanEvidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Admission_diagnosis_line_counts_as_evidence()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a referral letter to Ms Jackson regarding the patient.",
            "Admission diagnosis: multiple leg injuries and a closed Colles' fracture.",
            "routine_referral");

        Assert.False(string.IsNullOrWhiteSpace(result.DiagnosisOrPlanEvidence));
    }

    [Fact]
    public void Transfer_letter_matching_configured_type_is_not_conflicting()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Using the information in the case notes, write a transfer letter to the Admissions Officer for immediate treatment.",
            "Reason for transfer: rehabilitation care.",
            "transfer");

        Assert.False(result.ConflictingEvidence);
        Assert.Equal("classified", result.Status);
        Assert.Equal("transfer", result.PrimaryLetterType);
    }

    [Fact]
    public void Transferred_to_phrasing_supports_transfer_classification()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a letter of referral to the Charge Nurse where he will be transferred to for rehabilitation after discharge.",
            "His diagnosis is Guillain-Barré Syndrome.",
            "transfer");

        Assert.False(result.ConflictingEvidence);
        Assert.Equal("transfer", result.PrimaryLetterType);
    }

    [Fact]
    public void Social_worker_referral_matching_configured_type_is_not_conflicting()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a letter to the social worker requesting assessment and help with living arrangements after discharge.",
            "A social worker referral is planned as part of her discharge.",
            "non_medical_referral");

        Assert.False(result.ConflictingEvidence);
        Assert.Equal("non_medical_referral", result.PrimaryLetterType);
    }

    [Fact]
    public void Genuine_cross_type_conflict_still_requires_review()
    {
        // Configured routine with both discharge and urgent signals and no
        // corroboration for either: still ambiguous, still blocked.
        var result = WritingTaskUnderstandingService.Understand(
            "Write a discharge letter for urgent admission to the emergency registrar.",
            "Hospital admission and discharge planning are both mentioned.",
            "routine_referral");

        Assert.True(result.ConflictingEvidence);
        Assert.Equal("requires_review", result.Status);
    }

    [Fact]
    public void Hospital_director_recipient_resolves_as_health_facility()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a letter to the Director of the Repatriation General Hospital and request that the hospital take over the care.",
            "Her diagnosis is left lung resection.",
            "transfer");

        Assert.Equal("health_facility", result.RecipientCategory);
    }

    [Fact]
    public void Hospice_recipient_resolves_as_health_facility()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a letter to the Director of Nursing, Glen Haven Palliative Care Hospice, introducing this patient.",
            "Prognosis: not expected to survive more than 3-4 months.",
            "transfer");

        Assert.Equal("health_facility", result.RecipientCategory);
    }

    [Fact]
    public void Parents_recipient_resolves_as_family()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write to the child's parents with a summary of the injury and care needed.",
            "X-ray showed a spiral fracture to the right tibia.",
            "other");

        Assert.Equal("family", result.RecipientCategory);
    }

    [Fact]
    public void Foundation_section_recipient_resolves_as_health_facility()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a letter to the Community Information Section of the Heart Foundation on the patient's behalf.",
            "Diagnosis: obstructive coronary artery disease.",
            "other");

        Assert.Equal("health_facility", result.RecipientCategory);
    }

    [Fact]
    public void Adr_report_task_counts_as_clinical_purpose()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a letter to the Registrar of the Adverse Drug Reactions Data Bank reporting the suspected ADR.",
            "Mrs Daniels started taking Drug X about two weeks ago and developed a rash.",
            "other");

        Assert.False(string.IsNullOrWhiteSpace(result.DiagnosisOrPlanEvidence));
    }

    [Fact]
    public void Information_request_task_counts_as_purpose()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a letter to Mr Styles, providing information on angina and use of his medication.",
            "While in hospital, he was diagnosed with angina.",
            "other");

        Assert.False(string.IsNullOrWhiteSpace(result.DiagnosisOrPlanEvidence));
    }

    [Fact]
    public void Daughter_carer_letter_counts_as_purpose()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a letter to the daughter outlining her mother's medication regime and adverse effects.",
            "On discharge, medication includes Lipitor 20mg every morning.",
            "other");

        Assert.False(string.IsNullOrWhiteSpace(result.DiagnosisOrPlanEvidence));
    }

    [Fact]
    public void Requested_information_task_counts_as_purpose()
    {
        var result = WritingTaskUnderstandingService.Understand(
            "Write a letter to the Board. Include a response to each item the Board has requested information about.",
            "The pharmacist forgot to check the expiry date on the tablets.",
            "other");

        Assert.False(string.IsNullOrWhiteSpace(result.DiagnosisOrPlanEvidence));
    }
}

