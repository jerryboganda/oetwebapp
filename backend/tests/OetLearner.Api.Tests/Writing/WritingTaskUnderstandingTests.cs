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
}
