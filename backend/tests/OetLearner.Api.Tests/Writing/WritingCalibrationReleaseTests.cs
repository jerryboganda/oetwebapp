using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

public sealed class WritingCalibrationReleaseTests
{
    [Fact]
    public void Missing_or_blocked_gate_cannot_enable_candidate_numeric_scores()
    {
        Assert.False(WritingCalibrationReleaseService.IsCandidateReleaseAllowed(null));
        Assert.False(WritingCalibrationReleaseService.IsCandidateReleaseAllowed(new WritingAssessmentReleaseGate
        {
            Status = WritingAssessmentReleaseStatus.Blocked,
        }));
    }

    [Fact]
    public void Approved_gate_requires_two_ratings_and_content_priority_over_language()
    {
        var gate = new WritingAssessmentReleaseGate
        {
            Status = WritingAssessmentReleaseStatus.Approved,
            CandidateNumericScoreEnabled = true,
            OwnerApprovedTolerance = 30,
            QualifiedReviewerCount = 2,
            HumanRatingsPerBenchmark = 2,
            MeanAbsoluteError = 25,
            InventedClaimRate = 0,
            ContentConcisenessCorrelation = 0.81m,
            LanguageCorrelation = 0.41m,
        };

        Assert.True(WritingCalibrationReleaseService.IsCandidateReleaseAllowed(gate));
    }

    [Fact]
    public void Language_dominant_calibration_is_blocked_until_recalibrated()
    {
        var gate = new WritingAssessmentReleaseGate
        {
            Status = WritingAssessmentReleaseStatus.Approved,
            CandidateNumericScoreEnabled = true,
            OwnerApprovedTolerance = 30,
            QualifiedReviewerCount = 2,
            HumanRatingsPerBenchmark = 2,
            MeanAbsoluteError = 25,
            InventedClaimRate = 0,
            ContentConcisenessCorrelation = 0.40m,
            LanguageCorrelation = 0.82m,
        };

        Assert.False(WritingCalibrationReleaseService.IsCandidateReleaseAllowed(gate));
    }
}
