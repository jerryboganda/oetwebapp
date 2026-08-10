using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

public sealed class WritingAssessmentReportBuilderTests
{
    [Fact]
    public void Builds_all_six_criteria_and_never_enables_candidate_score_without_release()
    {
        var scenarioId = Guid.NewGuid();
        var preflight = new WritingAssessmentPreflightResult(
            true,
            WritingAssessmentV11Status.AwaitingPreflight,
            [],
            [],
            ["medicine:routine_referral:v1"],
            "medicine",
            "routine_referral",
            "v1",
            "Write to Dr Green requesting review.",
            "Diagnosis: asthma.");
        var criteria = WritingAssessmentReportBuilder.DefaultCriteria(3, 6, 5, 6, 5, 4);

        var report = WritingAssessmentReportBuilder.Build(new WritingAssessmentReportBuildInput(
            scenarioId,
            "hash",
            "The candidate letter",
            preflight,
            [new WritingAssessmentRuleFinding("R12.9", "punctuation", "major", "Fix however punctuation", "however", null, 1, 8, "language")],
            WritingFactMapService.Build("Diagnosis: asthma.", "The patient has asthma.", "gp"),
            criteria,
            380,
            "writing.score.v11",
            "calibration-v1"));

        Assert.Equal(6, report.Criteria.Count);
        Assert.False(report.CandidateNumericScoreEnabled);
        Assert.Equal(WritingAssessmentV11Status.RestrictedCalibration, report.Status);
        Assert.Equal("language", report.Errors.Single().PrimaryCriterionCode);
        Assert.NotNull(report.TopPrioritiesJson);
        Assert.Equal(WritingAssessmentModelAnswerStatus.HeldForReview, report.ModelAnswer!.Status);
    }
}
