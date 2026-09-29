using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Services;

/// <summary>Owner directive (25 Sep 2026, amended 2026-09-29): Speaking grading runs
/// Claude with maximum reasoning and enough max_tokens for thinking plus the full
/// scored JSON. Writing grading runs claude-opus-5-5 at effort "high" — effort
/// "max" on Claude 5 models previously consumed the whole token budget and
/// returned empty grades, so Writing is excluded from the max-reasoning set.</summary>
public sealed class AiGatewayGradingReasoningTests
{
    [Theory]
    [InlineData(AiFeatureCodes.SpeakingGrade, true)]
    [InlineData(AiFeatureCodes.WritingGrade, false)]
    [InlineData(AiFeatureCodes.Unclassified, false)]
    public void OnlyLearnerGradingGetsMaxReasoning(string featureCode, bool expected)
        => Assert.Equal(expected, AiGatewayService.GradingMaxReasoning(featureCode));

    [Fact]
    public void GradingHasRoomForThinkingAndTheScoredJson()
        => Assert.True(AiGatewayService.GradingMaxTokens >= 32_000);
}
