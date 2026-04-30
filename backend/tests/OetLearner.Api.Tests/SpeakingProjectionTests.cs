using OetLearner.Api.Services;
using static OetLearner.Api.Services.OetScoring;

namespace OetLearner.Api.Tests;

/// <summary>
/// Wave 1 — Speaking projection and readiness band tests.
/// Mirrors lib/__tests__/scoring-speaking.test.ts on the .NET side.
/// </summary>
public class SpeakingProjectionTests
{
    private static SpeakingCriterionScores Zero => new(0, 0, 0, 0, 0, 0, 0, 0, 0);
    private static SpeakingCriterionScores Full => new(6, 6, 6, 6, 3, 3, 3, 3, 3);

    [Fact]
    public void RubricMax_Is_39()
    {
        Assert.Equal(39, OetScoring.SpeakingRubricMax);
    }

    [Theory]
    [InlineData(0,   0)]
    [InlineData(50,  250)]
    [InlineData(70,  350)] // canonical B-pass anchor
    [InlineData(80,  400)]
    [InlineData(90,  450)]
    [InlineData(100, 500)]
    public void ProjectedScaledFromPercentage_HitsAnchors(double pct, int scaled)
    {
        Assert.Equal(scaled, OetScoring.SpeakingProjectedScaledFromPercentage(pct));
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(150, 500)]
    [InlineData(double.NaN, 0)]
    public void ProjectedScaledFromPercentage_ClampsOutOfRange(double pct, int expected)
    {
        Assert.Equal(expected, OetScoring.SpeakingProjectedScaledFromPercentage(pct));
    }

    [Fact]
    public void ProjectedScaledFromPercentage_InterpolatesLinearly()
    {
        // 75% → midway between 350 and 400 = 375
        Assert.Equal(375, OetScoring.SpeakingProjectedScaledFromPercentage(75));
    }

    [Fact]
    public void ProjectedScaled_Zero_Maps_To_Zero()
    {
        Assert.Equal(0, OetScoring.SpeakingProjectedScaled(Zero));
    }

    [Fact]
    public void ProjectedScaled_Full_Maps_To_500()
    {
        Assert.Equal(500, OetScoring.SpeakingProjectedScaled(Full));
    }

    [Fact]
    public void ProjectedScaled_Clamps_OutOfRange_Inputs()
    {
        var overrange = new SpeakingCriterionScores(99, 99, 99, 99, 99, 99, 99, 99, 99);
        Assert.Equal(500, OetScoring.SpeakingProjectedScaled(overrange));
    }

    [Fact]
    public void ProjectedScaled_70_Anchor_Boundary_Behaviour()
    {
        // 27 of 39 ≈ 69.23% → < 350
        var justBelow = new SpeakingCriterionScores(
            Intelligibility: 6, Fluency: 6, Appropriateness: 6, GrammarExpression: 0,
            RelationshipBuilding: 3, PatientPerspective: 3, Structure: 3,
            InformationGathering: 0, InformationGiving: 0);
        Assert.True(OetScoring.SpeakingProjectedScaled(justBelow) < 350);

        // 28 of 39 ≈ 71.79% → > 350
        var justAbove = new SpeakingCriterionScores(
            Intelligibility: 6, Fluency: 6, Appropriateness: 6, GrammarExpression: 0,
            RelationshipBuilding: 3, PatientPerspective: 3, Structure: 3,
            InformationGathering: 1, InformationGiving: 0);
        Assert.True(OetScoring.SpeakingProjectedScaled(justAbove) > 350);
    }

    [Fact]
    public void ProjectedScaled_Matches_Percentage_Helper()
    {
        var mid = new SpeakingCriterionScores(
            Intelligibility: 3, Fluency: 3, Appropriateness: 3, GrammarExpression: 3,
            RelationshipBuilding: 1, PatientPerspective: 2, Structure: 2,
            InformationGathering: 2, InformationGiving: 1);
        // 12 + 8 = 20 of 39 ≈ 51.28%
        var direct = OetScoring.SpeakingProjectedScaled(mid);
        var viaPct = OetScoring.SpeakingProjectedScaledFromPercentage(20 * 100.0 / 39);
        Assert.Equal(viaPct, direct);
    }

    [Fact]
    public void ProjectedBand_FullScores_Pass()
    {
        var r = OetScoring.SpeakingProjectedBand(Full);
        Assert.True(r.Passed);
        Assert.Equal(500, r.ScaledScore);
        Assert.Equal("speaking", r.Subtest);
    }

    [Fact]
    public void ProjectedBand_Zero_DoesNotPass()
    {
        Assert.False(OetScoring.SpeakingProjectedBand(Zero).Passed);
    }

    [Theory]
    [InlineData(0,   SpeakingReadinessBand.NotReady)]
    [InlineData(249, SpeakingReadinessBand.NotReady)]
    [InlineData(250, SpeakingReadinessBand.Developing)]
    [InlineData(299, SpeakingReadinessBand.Developing)]
    [InlineData(300, SpeakingReadinessBand.Borderline)]
    [InlineData(349, SpeakingReadinessBand.Borderline)]
    [InlineData(350, SpeakingReadinessBand.ExamReady)]
    [InlineData(419, SpeakingReadinessBand.ExamReady)]
    [InlineData(420, SpeakingReadinessBand.Strong)]
    [InlineData(500, SpeakingReadinessBand.Strong)]
    [InlineData(-100, SpeakingReadinessBand.NotReady)]
    [InlineData(9999, SpeakingReadinessBand.Strong)]
    public void ReadinessBand_Bucketing(int scaled, SpeakingReadinessBand expected)
    {
        Assert.Equal(expected, OetScoring.SpeakingReadinessBandFromScaled(scaled));
    }

    [Theory]
    [InlineData(SpeakingReadinessBand.NotReady,   "not_ready")]
    [InlineData(SpeakingReadinessBand.Developing, "developing")]
    [InlineData(SpeakingReadinessBand.Borderline, "borderline")]
    [InlineData(SpeakingReadinessBand.ExamReady,  "exam_ready")]
    [InlineData(SpeakingReadinessBand.Strong,     "strong")]
    public void ReadinessBandCode_StableWireFormat(SpeakingReadinessBand band, string expected)
    {
        Assert.Equal(expected, OetScoring.SpeakingReadinessBandCode(band));
    }
}
