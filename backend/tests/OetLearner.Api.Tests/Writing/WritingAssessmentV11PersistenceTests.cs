using OetLearner.Api.Domain;

namespace OetLearner.Api.Tests.Writing;

public sealed class WritingAssessmentV11PersistenceTests
{
    [Fact]
    public void Report_keeps_immutable_submission_and_source_snapshots()
    {
        var submissionId = Guid.NewGuid();
        var report = new WritingAssessmentReportV11
        {
            SubmissionId = submissionId,
            OriginalLetterSnapshot = "Dear Dr Green,\nPlease review...",
            TaskSnapshot = "Please write to Dr Green requesting a review.",
            CaseNotesSnapshot = "Page 1: asthma; allergy status negative.",
            Status = WritingAssessmentV11Status.AwaitingPreflight,
        };

        Assert.Equal(submissionId, report.SubmissionId);
        Assert.Equal("Dear Dr Green,\nPlease review...", report.OriginalLetterSnapshot);
        Assert.Equal("Page 1: asthma; allergy status negative.", report.CaseNotesSnapshot);
    }

    [Fact]
    public void Release_gate_defaults_to_blocked_and_never_enables_candidate_scores()
    {
        var gate = new WritingAssessmentReleaseGate();

        Assert.Equal(WritingAssessmentReleaseStatus.Blocked, gate.Status);
        Assert.False(gate.CandidateNumericScoreEnabled);
    }

    [Fact]
    public void Error_requires_one_primary_criterion_and_preserves_secondary_teaching_references()
    {
        var error = new WritingAssessmentError
        {
            Category = "register_jargon",
            PrimaryCriterionCode = "genre_style",
            SecondaryCriterionCodesJson = "[\"language\"]",
        };

        Assert.True(WritingAssessmentV11Invariants.IsPrimaryCriterion(error.PrimaryCriterionCode));
        Assert.Equal("[\"language\"]", error.SecondaryCriterionCodesJson);
    }

    [Fact]
    public void Model_answer_is_held_until_grounding_is_verified()
    {
        var answer = new WritingAssessmentModelAnswer();

        Assert.Equal(WritingAssessmentModelAnswerStatus.HeldForReview, answer.Status);
        Assert.False(answer.IsCandidateVisible);
    }
}
