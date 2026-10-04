using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Speaking;
using static OetLearner.Api.Services.OetScoring;

namespace OetLearner.Api.Tests.Speaking;

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
        // The stored practice projection stays exact; candidate reporting is quantized separately.
        Assert.Equal(375, OetScoring.SpeakingProjectedScaledFromPercentage(75));
        Assert.Equal(380, OetScoring.OetReportedScaledScore(375));
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

    [Fact]
    public void TutorDivergence_UsesSignedCriterionDeltas_AndAbsoluteBanding()
    {
        var ai = new SpeakingAiAssessment
        {
            Intelligibility = 5,
            Fluency = 4,
            Appropriateness = 4,
            GrammarExpression = 4,
            RelationshipBuilding = 3,
            PatientPerspective = 2,
            Structure = 2,
            InformationGathering = 2,
            InformationGiving = 2,
            EstimatedScaledScore = 380,
        };
        var tutor = new SpeakingTutorAssessment
        {
            Intelligibility = 3,
            Fluency = 6,
            Appropriateness = 4,
            GrammarExpression = 4,
            RelationshipBuilding = 2,
            PatientPerspective = 2,
            Structure = 2,
            InformationGathering = 2,
            InformationGiving = 2,
            EstimatedScaledScore = 360,
        };

        var divergence = TutorAssessmentService.ComputeDivergence(ai, tutor);

        Assert.Equal(-2, divergence.PerCriterion["intelligibility"]);
        Assert.Equal(2, divergence.PerCriterion["fluency"]);
        Assert.Equal(-20, divergence.ScaledDelta);
        Assert.Equal("moderate", divergence.AgreementBand);
    }

    // ── The reported score: the ONE candidate-facing number (owner spec 4 Oct 2026) ──

    [Fact]
    public void ReportedTable_HasOneEntryPerRawTotal_IsMonotone_AndEveryEntryIsAMultipleOfTen()
    {
        var table = OetScoring.SpeakingRawToReported;
        Assert.Equal(OetScoring.SpeakingRubricMax + 1, table.Count);
        Assert.Equal(0, table[0]);
        Assert.Equal(500, table[^1]);
        for (var raw = 0; raw < table.Count; raw++)
        {
            Assert.Equal(0, table[raw] % 10);
            if (raw > 0) Assert.True(table[raw] >= table[raw - 1], $"the table decreases at raw {raw}");
        }
    }

    [Fact]
    public void ReportedTable_V0_IsExactlyTheFormerHeuristicRoundedToTen()
    {
        // v0 must not move any number a learner has already seen: it is the heuristic
        // (linear 500 x raw / 39) rounded to 10. A calibration fit changes the literal AND the version.
        for (var raw = 0; raw <= OetScoring.SpeakingRubricMax; raw++)
        {
            var heuristic = OetScoring.SpeakingProjectedScaledFromPercentage(raw * 100.0 / OetScoring.SpeakingRubricMax);
            Assert.Equal(OetScoring.OetReportedScaledScore(heuristic), OetScoring.SpeakingRawToReported[raw]);
        }
        Assert.Equal("speaking-map.v0-heuristic", OetScoring.SpeakingMappingVersion);
    }

    [Theory]
    [InlineData(4, 4, 4, 4, 2, 2, 1, 2, 1, 310)] // 16/24 + 8/15 = 24/39: production showed 308
    [InlineData(4, 3, 3, 3, 1, 1, 1, 1, 1, 230)] // 13/24 + 5/15 = 18/39: production showed 231
    [InlineData(0, 0, 0, 0, 0, 0, 0, 0, 0, 0)]
    [InlineData(6, 6, 6, 6, 3, 3, 3, 3, 3, 500)]
    [InlineData(99, 99, 99, 99, 99, 99, 99, 99, 99, 500)] // out-of-range input is clamped, never trusted
    public void ReportedScaled_IsAlwaysTheTenPointNumber(
        int intelligibility, int fluency, int appropriateness, int grammar,
        int relationship, int perspective, int structure, int gathering, int giving, int expected)
    {
        var scores = new SpeakingCriterionScores(
            intelligibility, fluency, appropriateness, grammar, relationship, perspective, structure, gathering, giving);

        Assert.Equal(expected, OetScoring.SpeakingReportedScaled(scores));
        Assert.Equal(0, OetScoring.SpeakingReportedScaled(scores) % 10);
    }

    [Fact]
    public void ReportedScore_DrivesGradeReadinessAndPass_SoTheyCanNeverDisagree()
    {
        // Raw 27/39 = 346 on the unrounded heuristic. The learner is shown 350 (Grade B), so the
        // readiness band and the pass line must say "at the pass line" too, not "Borderline".
        var scores = new SpeakingCriterionScores(6, 6, 6, 0, 3, 3, 3, 0, 0);
        Assert.Equal(27, OetScoring.SpeakingRawTotal(scores));
        var reported = OetScoring.SpeakingReportedScaled(scores);
        Assert.Equal(350, reported);
        Assert.Equal("B", OetScoring.OetGradeLetterFromScaled(reported));
        Assert.Equal(SpeakingReadinessBand.ExamReady, OetScoring.SpeakingReadinessBandFromScaled(reported));
        Assert.True(OetScoring.IsSpeakingPass(reported));

        // One criterion point lower is 330: Grade C+, Borderline, not a pass.
        var below = new SpeakingCriterionScores(5, 6, 6, 0, 3, 3, 3, 0, 0);
        var belowReported = OetScoring.SpeakingReportedScaled(below);
        Assert.Equal(330, belowReported);
        Assert.Equal("C+", OetScoring.OetGradeLetterFromScaled(belowReported));
        Assert.Equal(SpeakingReadinessBand.Borderline, OetScoring.SpeakingReadinessBandFromScaled(belowReported));
        Assert.False(OetScoring.IsSpeakingPass(belowReported));
    }

    [Theory]
    [InlineData(500, "A")]
    [InlineData(450, "A")]
    [InlineData(440, "B")]
    [InlineData(430, "B")]
    [InlineData(400, "B")]
    [InlineData(350, "B")]
    [InlineData(340, "C+")]
    [InlineData(300, "C+")]
    [InlineData(290, "C")]
    [InlineData(200, "C")]
    [InlineData(190, "D")]
    [InlineData(100, "D")]
    [InlineData(90, "E")]
    [InlineData(0, "E")]
    public void GradeLetter_FollowsTheOetReportingTable_AndThereIsNoBPlus(int reported, string grade)
    {
        Assert.Equal(grade, OetScoring.OetGradeLetterFromScaled(reported));
        Assert.NotEqual("B+", OetScoring.OetGradeLetterFromScaled(reported));
    }

    [Fact]
    public void EveryReportedScoreMapsToOneOfTheSixOfficialLetters()
    {
        var letters = new HashSet<string>(StringComparer.Ordinal) { "A", "B", "C+", "C", "D", "E" };
        foreach (var reported in OetScoring.SpeakingRawToReported)
            Assert.Contains(OetScoring.OetGradeLetterFromScaled(reported), letters);
    }

    [Fact]
    public void ScoreLabel_IsProvisional_UntilTheGraderVersionHasPassedCalibration()
    {
        Assert.False(OetScoring.IsSpeakingGraderCalibrated(null, null));
        Assert.False(OetScoring.IsSpeakingGraderCalibrated("speaking.score.v3|speaking-map.v0-heuristic|none", "claude-opus-5-5"));
        Assert.Equal(OetScoring.SpeakingScoreLabelProvisional, OetScoring.SpeakingScoreLabel(null, "claude-opus-5-5"));
        Assert.Equal("provisional", OetScoring.SpeakingScoreLabelProvisional);
        Assert.Equal("ai_practice_estimate", OetScoring.SpeakingScoreLabelPracticeEstimate);
    }
}
