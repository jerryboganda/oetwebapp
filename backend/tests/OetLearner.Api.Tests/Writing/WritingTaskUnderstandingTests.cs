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
}

