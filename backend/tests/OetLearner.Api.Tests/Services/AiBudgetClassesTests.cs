using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;

namespace OetLearner.Api.Tests.Services;

public sealed class AiBudgetClassesTests
{
    [Fact]
    public void ScopeFor_MapsEachClassToStableScope()
    {
        Assert.Equal("class:ScoringCritical", AiBudgetClasses.ScopeFor(AiOperationClass.ScoringCritical));
        Assert.Equal("class:InteractiveLearning", AiBudgetClasses.ScopeFor(AiOperationClass.InteractiveLearning));
        Assert.Equal("class:AdminBatch", AiBudgetClasses.ScopeFor(AiOperationClass.AdminBatch));
    }

    [Fact]
    public void Limits_MatchOwnerApprovedCeilings()
    {
        Assert.Equal(3.50m, AiBudgetClasses.DailyLimitUsd(AiOperationClass.ScoringCritical));
        Assert.Equal(35.00m, AiBudgetClasses.MonthlyLimitUsd(AiOperationClass.ScoringCritical));
        Assert.Equal(1.00m, AiBudgetClasses.DailyLimitUsd(AiOperationClass.InteractiveLearning));
        Assert.Equal(10.00m, AiBudgetClasses.MonthlyLimitUsd(AiOperationClass.InteractiveLearning));
        Assert.Equal(0.50m, AiBudgetClasses.DailyLimitUsd(AiOperationClass.AdminBatch));
        Assert.Equal(5.00m, AiBudgetClasses.MonthlyLimitUsd(AiOperationClass.AdminBatch));
        Assert.Equal(5.00m, AiBudgetClasses.PlatformDailyCapUsd);
    }

    [Fact]
    public void Scoring_CanBorrowInteractiveThenAdmin_NeverScoring()
    {
        Assert.True(AiBudgetClasses.CanBorrow(AiOperationClass.ScoringCritical, AiOperationClass.InteractiveLearning));
        Assert.True(AiBudgetClasses.CanBorrow(AiOperationClass.ScoringCritical, AiOperationClass.AdminBatch));
        Assert.False(AiBudgetClasses.CanBorrow(AiOperationClass.ScoringCritical, AiOperationClass.ScoringCritical));
        Assert.Equal(
            new[] { AiOperationClass.InteractiveLearning, AiOperationClass.AdminBatch },
            AiBudgetClasses.ScoringBorrowOrder);
    }

    [Fact]
    public void Interactive_CannotBorrowAnything()
    {
        Assert.False(AiBudgetClasses.CanBorrow(AiOperationClass.InteractiveLearning, AiOperationClass.ScoringCritical));
        Assert.False(AiBudgetClasses.CanBorrow(AiOperationClass.InteractiveLearning, AiOperationClass.AdminBatch));
        Assert.False(AiBudgetClasses.CanBorrow(AiOperationClass.InteractiveLearning, AiOperationClass.InteractiveLearning));
    }

    [Fact]
    public void Admin_CannotBorrowScoringOrAnythingElse()
    {
        Assert.False(AiBudgetClasses.CanBorrow(AiOperationClass.AdminBatch, AiOperationClass.ScoringCritical));
        Assert.False(AiBudgetClasses.CanBorrow(AiOperationClass.AdminBatch, AiOperationClass.InteractiveLearning));
        Assert.False(AiBudgetClasses.CanBorrow(AiOperationClass.AdminBatch, AiOperationClass.AdminBatch));
    }

    [Theory]
    [InlineData(AiFeatureCodes.WritingGrade, AiOperationClass.ScoringCritical)]
    [InlineData(AiFeatureCodes.SpeakingGrade, AiOperationClass.ScoringCritical)]
    [InlineData(AiFeatureCodes.ListeningPartAScore, AiOperationClass.ScoringCritical)]
    [InlineData(AiFeatureCodes.PronunciationScore, AiOperationClass.ScoringCritical)]
    [InlineData(AiFeatureCodes.AdminContentGeneration, AiOperationClass.AdminBatch)]
    [InlineData(AiFeatureCodes.AdminWritingDraft, AiOperationClass.AdminBatch)]
    [InlineData(AiFeatureCodes.WritingCoachSuggest, AiOperationClass.InteractiveLearning)]
    [InlineData(AiFeatureCodes.ReadingExplanation, AiOperationClass.InteractiveLearning)]
    public void ClassForFeature_MapsKnownCodes(string featureCode, AiOperationClass expected)
    {
        Assert.Equal(expected, AiBudgetClasses.ClassForFeature(featureCode));
    }
}
