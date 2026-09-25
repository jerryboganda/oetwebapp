using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Services;

/// <summary>Owner directive (25 Sep 2026): Speaking and Writing grading run
/// Claude Sonnet 5 with maximum reasoning and enough max_tokens for thinking
/// plus the full scored JSON (the old 4096 cap truncated it).</summary>
public sealed class AiGatewayGradingReasoningTests
{
    [Theory]
    [InlineData(AiFeatureCodes.SpeakingGrade, true)]
    [InlineData(AiFeatureCodes.WritingGrade, true)]
    [InlineData(AiFeatureCodes.Unclassified, false)]
    public void OnlyLearnerGradingGetsMaxReasoning(string featureCode, bool expected)
        => Assert.Equal(expected, AiGatewayService.GradingMaxReasoning(featureCode));

    [Fact]
    public void GradingHasRoomForThinkingAndTheScoredJson()
        => Assert.True(AiGatewayService.GradingMaxTokens >= 32_000);
}
