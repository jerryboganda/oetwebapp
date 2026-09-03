using System.Security.Cryptography;
using System.Text;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Assessment;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

public class M2AdversarialScoringAndAiGatewayTests
{
    private readonly RulebookLoader _loader = new();

    // -------------------------------------------------------------------------
    // 1. OBJECTIVE SCORING [0, 42] EXHAUSTIVE DOMAIN & MONOTONICITY ORACLE
    // -------------------------------------------------------------------------

    [Fact]
    public void ObjectiveScoring_Exact_Invariant_Anchors()
    {
        Assert.Equal(0, OetScoring.OetRawToScaled(0));
        Assert.False(OetScoring.IsListeningReadingPassByRaw(0));
        Assert.False(OetScoring.IsListeningReadingPassByScaled(0));
        Assert.Equal("E", OetScoring.OetGradeLetterFromScaled(0));

        Assert.Equal(350, OetScoring.OetRawToScaled(30));
        Assert.True(OetScoring.IsListeningReadingPassByRaw(30));
        Assert.True(OetScoring.IsListeningReadingPassByScaled(350));
        Assert.Equal("B", OetScoring.OetGradeLetterFromScaled(350));

        Assert.Equal(500, OetScoring.OetRawToScaled(42));
        Assert.True(OetScoring.IsListeningReadingPassByRaw(42));
        Assert.True(OetScoring.IsListeningReadingPassByScaled(500));
        Assert.Equal("A", OetScoring.OetGradeLetterFromScaled(500));
    }

    [Fact]
    public void ObjectiveScoring_Exact_Boundary_Transition_Around_Pass_Line()
    {
        // r = 29 -> 338 (< 350, Grade C+, fail)
        var scaled29 = OetScoring.OetRawToScaled(29);
        Assert.Equal(338, scaled29);
        Assert.True(scaled29 < 350);
        Assert.False(OetScoring.IsListeningReadingPassByRaw(29));
        Assert.False(OetScoring.IsListeningReadingPassByScaled(scaled29));
        Assert.Equal("C+", OetScoring.OetGradeLetterFromScaled(scaled29));

        // r = 31 -> 363 (> 350, Grade B, pass)
        var scaled31 = OetScoring.OetRawToScaled(31);
        Assert.Equal(363, scaled31);
        Assert.True(scaled31 > 350);
        Assert.True(OetScoring.IsListeningReadingPassByRaw(31));
        Assert.True(OetScoring.IsListeningReadingPassByScaled(scaled31));
        Assert.Equal("B", OetScoring.OetGradeLetterFromScaled(scaled31));
    }

    [Fact]
    public void ObjectiveScoring_Exhaustive_PiecewiseFormula_And_Monotonicity_Across_0_To_42()
    {
        var prev = -1;
        for (var r = 0; r <= 42; r++)
        {
            var actual = OetScoring.OetRawToScaled(r);
            int expected;
            if (r == 0) expected = 0;
            else if (r == 30) expected = 350;
            else if (r == 42) expected = 500;
            else if (r < 30) expected = (int)Math.Round((double)r * 350 / 30, MidpointRounding.AwayFromZero);
            else expected = (int)Math.Round(350 + (double)(r - 30) * 150 / 12, MidpointRounding.AwayFromZero);

            Assert.Equal(expected, actual);
            Assert.True(actual >= prev, $"Monotonicity violation at r={r}: actual={actual} < prev={prev}");
            prev = actual;

            var result = OetScoring.GradeListeningReading("listening", r);
            Assert.Equal(r, result.RawCorrect);
            Assert.Equal(42, result.RawMax);
            Assert.Equal(actual, result.ScaledScore);
            Assert.Equal(r >= 30, result.Passed);
        }
    }

    [Fact]
    public void ObjectiveScoring_Clamps_OutOfRange_Inputs()
    {
        Assert.Equal(0, OetScoring.OetRawToScaled(-100));
        Assert.Equal(0, OetScoring.OetRawToScaled(-1));
        Assert.Equal(500, OetScoring.OetRawToScaled(43));
        Assert.Equal(500, OetScoring.OetRawToScaled(9999));
    }

    // -------------------------------------------------------------------------
    // 2. WRITING RUBRIC & DESTINATION COUNTRY PERMUTATIONS
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("GB")]
    [InlineData("IE")]
    [InlineData("AU")]
    [InlineData("NZ")]
    [InlineData("CA")]
    [InlineData("gb")]
    [InlineData("uk")]
    [InlineData("UNITED KINGDOM")]
    [InlineData("australia")]
    [InlineData("New Zealand")]
    [InlineData("Canada")]
    [InlineData("Gulf Countries")]
    [InlineData("Other Countries")]
    public void Writing_GradeB_Countries_Require_350_Pass(string country)
    {
        var threshold = OetScoring.GetWritingPassThreshold(country);
        Assert.NotNull(threshold);
        Assert.Equal(350, threshold!.Threshold);
        Assert.Equal("B", threshold.Grade);

        var pass = OetScoring.GradeWriting(350, country);
        Assert.True(pass.Passed);
        Assert.Equal(350, pass.RequiredScaled);
        Assert.Equal("B", pass.RequiredGrade);

        var fail349 = OetScoring.GradeWriting(349, country);
        Assert.False(fail349.Passed);
        Assert.Equal(350, fail349.RequiredScaled);

        var fail300 = OetScoring.GradeWriting(300, country);
        Assert.False(fail300.Passed);
        Assert.Equal("C+", fail300.Grade);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("QA")]
    [InlineData("us")]
    [InlineData("usa")]
    [InlineData("United States")]
    [InlineData("United States of America")]
    [InlineData("America")]
    [InlineData("qatar")]
    public void Writing_GradeCPlus_Countries_Require_300_Pass(string country)
    {
        var threshold = OetScoring.GetWritingPassThreshold(country);
        Assert.NotNull(threshold);
        Assert.Equal(300, threshold!.Threshold);
        Assert.Equal("C+", threshold.Grade);

        var pass300 = OetScoring.GradeWriting(300, country);
        Assert.True(pass300.Passed);
        Assert.Equal(300, pass300.RequiredScaled);
        Assert.Equal("C+", pass300.RequiredGrade);

        var fail299 = OetScoring.GradeWriting(299, country);
        Assert.False(fail299.Passed);
        Assert.Equal(300, fail299.RequiredScaled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Writing_Missing_Country_Returns_NullPassed_CountryRequired(string? country)
    {
        var threshold = OetScoring.GetWritingPassThreshold(country);
        Assert.Null(threshold);

        var result = OetScoring.GradeWriting(400, country);
        Assert.Null(result.Passed);
        Assert.Equal("country_required", result.Reason);
        Assert.Equal("writing", result.Subtest);
    }

    [Theory]
    [InlineData("INVALID")]
    [InlineData("FR")]
    [InlineData("DE")]
    [InlineData("JP")]
    [InlineData("ES")]
    public void Writing_Unsupported_Country_Returns_NullPassed_CountryUnsupported(string country)
    {
        var threshold = OetScoring.GetWritingPassThreshold(country);
        Assert.Null(threshold);

        var result = OetScoring.GradeWriting(400, country);
        Assert.Null(result.Passed);
        Assert.Equal("country_unsupported", result.Reason);
        Assert.Equal("writing", result.Subtest);
    }

    // -------------------------------------------------------------------------
    // 3. SPEAKING RUBRIC & UNIVERSAL PASS POLICY
    // -------------------------------------------------------------------------

    [Fact]
    public void Speaking_Rubric_Exact_Anchors_And_Monotonicity()
    {
        Assert.Equal(39, OetScoring.SpeakingRubricMax);

        Assert.Equal(0, OetScoring.SpeakingProjectedScaledFromPercentage(0));
        Assert.Equal(250, OetScoring.SpeakingProjectedScaledFromPercentage(50));
        Assert.Equal(350, OetScoring.SpeakingProjectedScaledFromPercentage(70)); // 70% = 350 Grade B
        Assert.Equal(400, OetScoring.SpeakingProjectedScaledFromPercentage(80));
        Assert.Equal(450, OetScoring.SpeakingProjectedScaledFromPercentage(90));
        Assert.Equal(500, OetScoring.SpeakingProjectedScaledFromPercentage(100));

        var prev = -1;
        for (var p = 0; p <= 100; p++)
        {
            var s = OetScoring.SpeakingProjectedScaledFromPercentage(p);
            Assert.True(s >= prev, $"Speaking percentage projection monotonicity failure at p={p}");
            prev = s;
        }
    }

    [Fact]
    public void Speaking_Full_Criterion_Scores_And_Readiness()
    {
        var perfectScores = new OetScoring.SpeakingCriterionScores(6, 6, 6, 6, 3, 3, 3, 3, 3);
        Assert.Equal(500, OetScoring.SpeakingProjectedScaled(perfectScores));
        var perfectBand = OetScoring.SpeakingProjectedBand(perfectScores);
        Assert.True(perfectBand.Passed);
        Assert.Equal("A", perfectBand.Grade);

        var zeroScores = new OetScoring.SpeakingCriterionScores(0, 0, 0, 0, 0, 0, 0, 0, 0);
        Assert.Equal(0, OetScoring.SpeakingProjectedScaled(zeroScores));
        var zeroBand = OetScoring.SpeakingProjectedBand(zeroScores);
        Assert.False(zeroBand.Passed);
        Assert.Equal("E", zeroBand.Grade);

        // Universal Speaking pass mark is strictly 350
        Assert.True(OetScoring.IsSpeakingPass(350));
        Assert.False(OetScoring.IsSpeakingPass(349));
        Assert.False(OetScoring.IsSpeakingPass(300));
    }

    // -------------------------------------------------------------------------
    // 4. ASSESSMENT SCORE TABLE VALIDATOR GATING
    // -------------------------------------------------------------------------

    [Fact]
    public void AssessmentScoreTableValidator_Requires_Exact_43_Rows()
    {
        var incomplete = Enumerable.Range(0, 40)
            .Select(r => new AssessmentScoreTableRowInput(r, r * 10, "B", true))
            .ToArray();
        var result = AssessmentScoreTableValidator.Validate("listening", incomplete);
        Assert.False(result.IsValid);
        Assert.Equal("score_table_requires_43_rows", result.ErrorCode);
    }

    [Fact]
    public void AssessmentScoreTableValidator_Requires_Full_0_To_42_Coverage()
    {
        // 43 rows but missing raw score 0 (has duplicate 42)
        var rows = Enumerable.Range(1, 43)
            .Select(r => new AssessmentScoreTableRowInput(r == 43 ? 42 : r, 100, "B", true))
            .ToArray();
        var result = AssessmentScoreTableValidator.Validate("reading", rows);
        Assert.False(result.IsValid);
        Assert.Equal("score_table_duplicate_raw_score", result.ErrorCode);
    }

    [Fact]
    public void AssessmentScoreTableValidator_Rejects_OutOfRange_ConvertedScores()
    {
        var rows = Enumerable.Range(0, 43)
            .Select(r => new AssessmentScoreTableRowInput(r, r == 42 ? 501 : 100, "B", true))
            .ToArray();
        var result = AssessmentScoreTableValidator.Validate("reading", rows);
        Assert.False(result.IsValid);
        Assert.Equal("score_table_converted_score_out_of_range", result.ErrorCode);
    }

    // -------------------------------------------------------------------------
    // 5. AI GATEWAY UNGROUNDED PROMPT & POLICY REFUSALS
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AiGateway_Refuses_Null_Prompt_With_PromptNotGroundedException()
    {
        var gateway = new AiGatewayService(_loader, new[] { new MockAiProvider() });

        var ex = await Assert.ThrowsAsync<PromptNotGroundedException>(() =>
            gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = null,
                FeatureCode = AiFeatureCodes.WritingGrade,
            }));

        Assert.Contains("AiGatewayRequest.Prompt is null", ex.Message);
    }

    [Fact]
    public async Task AiGateway_Refuses_Ungrounded_SystemPrompt_With_PromptNotGroundedException()
    {
        var gateway = new AiGatewayService(_loader, new[] { new MockAiProvider() });

        var ex = await Assert.ThrowsAsync<PromptNotGroundedException>(() =>
            gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = new AiGroundedPrompt
                {
                    SystemPrompt = "You are an ungrounded model.",
                    TaskInstruction = "Grade this text.",
                    Metadata = new AiGroundedPromptMetadata { RulebookVersion = "v1", AppliedRulesCount = 0 },
                },
                FeatureCode = AiFeatureCodes.WritingGrade,
            }));

        Assert.Contains("rulebook grounding header", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AiGateway_Refuses_Retired_MockFullGrade()
    {
        var gateway = new AiGatewayService(_loader, new[] { new MockAiProvider() });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateway.CompleteAsync(new AiGatewayRequest
            {
                FeatureCode = AiFeatureCodes.MockFullGrade,
            }));

        Assert.Contains("mock.full_grade is retired", ex.Message);
    }

    [Fact]
    public async Task AiGateway_Refuses_Banned_Mock_FeatureCodes()
    {
        var gateway = new AiGatewayService(_loader, new[] { new MockAiProvider() });

        await Assert.ThrowsAsync<MockAssessmentForbiddenException>(() =>
            gateway.CompleteAsync(new AiGatewayRequest
            {
                FeatureCode = AiFeatureCodes.ConversationEvaluation,
                AssessmentContext = AiAssessmentContext.Mock,
            }));
    }

    // -------------------------------------------------------------------------
    // 6. REQUEST HASH DETERMINISM AND PRIVACY
    // -------------------------------------------------------------------------

    [Fact]
    public void CoordinatedAiGateway_BuildRequestHash_Is_Deterministic_And_DigestBased()
    {
        var request1 = new AiGatewayRequest
        {
            FeatureCode = AiFeatureCodes.WritingGrade,
            UserId = "user-123",
            TenantId = "tenant-abc",
            Provider = "anthropic",
            Model = "claude-3-7-sonnet",
            Temperature = 0.2,
            MaxTokens = 2000,
            AssessmentContext = AiAssessmentContext.Practice,
            PromptTemplateId = "tmpl-01",
            Prompt = new AiGroundedPrompt
            {
                SystemPrompt = "OET AI — Rulebook-Grounded System Prompt\nRules...",
                TaskInstruction = "Evaluate letter.",
                Metadata = new AiGroundedPromptMetadata { RulebookVersion = "2026.1", AppliedRulesCount = 12 },
            },
            UserInput = "Dear Doctor,\nPatient notes...",
        };

        var request2 = new AiGatewayRequest
        {
            FeatureCode = AiFeatureCodes.WritingGrade,
            UserId = "user-123",
            TenantId = "tenant-abc",
            Provider = "anthropic",
            Model = "claude-3-7-sonnet",
            Temperature = 0.2,
            MaxTokens = 2000,
            AssessmentContext = AiAssessmentContext.Practice,
            PromptTemplateId = "tmpl-01",
            Prompt = new AiGroundedPrompt
            {
                SystemPrompt = "OET AI — Rulebook-Grounded System Prompt\nRules...",
                TaskInstruction = "Evaluate letter.",
                Metadata = new AiGroundedPromptMetadata { RulebookVersion = "2026.1", AppliedRulesCount = 12 },
            },
            UserInput = "Dear Doctor,\nPatient notes...",
        };

        var hash1 = CoordinatedAiGatewayService.BuildRequestHash(request1);
        var hash2 = CoordinatedAiGatewayService.BuildRequestHash(request2);

        Assert.Equal(64, hash1.Length);
        Assert.Equal(hash1, hash2);

        // Perturbation in candidate input produces a different hash
        var request3 = request1 with { UserInput = "Dear Doctor,\nDifferent patient notes..." };
        var hash3 = CoordinatedAiGatewayService.BuildRequestHash(request3);
        Assert.NotEqual(hash1, hash3);
    }
}
