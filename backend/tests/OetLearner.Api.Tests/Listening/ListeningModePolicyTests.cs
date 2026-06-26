using OetLearner.Api.Domain;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

public class ListeningModePolicyTests
{
    private readonly ListeningModePolicyResolver _resolver = new();

    [Fact]
    public void ExamMode_EnforcesStrictCbtConstraints()
    {
        var policy = _resolver.For(ListeningAttemptMode.Exam);

        Assert.True(policy.OneWayLocks);
        Assert.False(policy.AudioPauseAllowed);
        Assert.False(policy.AudioSeekAllowed);
        Assert.False(policy.ReplayAllowed);
        Assert.False(policy.FreeNavigation);
        Assert.True(policy.ConfirmDialogRequired);
        Assert.True(policy.UnansweredWarningRequired);
        Assert.True(policy.RequiresTechReadiness);
        Assert.False(policy.TranscriptVisibleOnReview);
        Assert.False(policy.FullscreenEnforced);
        Assert.Null(policy.FinalReviewAllPartsMs);
    }

    [Fact]
    public void LearningMode_LocksAudioButKeepsTranscriptAndFreeNavigation()
    {
        var policy = _resolver.For(ListeningAttemptMode.Learning);

        // Audio is non-pausable in every mode now (owner directive 2026-06-27):
        // no pause / seek / replay, even in learning/practice.
        Assert.False(policy.AudioPauseAllowed);
        Assert.False(policy.AudioSeekAllowed);
        Assert.False(policy.ReplayAllowed);
        Assert.True(policy.TranscriptVisibleOnReview);
        Assert.True(policy.FreeNavigation);
        Assert.False(policy.OneWayLocks);
        Assert.False(policy.ConfirmDialogRequired);
        Assert.False(policy.UnansweredWarningRequired);
        Assert.False(policy.RequiresTechReadiness);
        Assert.False(policy.FullscreenEnforced);
    }

    [Fact]
    public void PaperMode_AllowsFreeNavigationNoReplay()
    {
        var policy = _resolver.For(ListeningAttemptMode.Paper);

        Assert.True(policy.FreeNavigation);
        Assert.False(policy.OneWayLocks);
        Assert.False(policy.AudioPauseAllowed);
        Assert.False(policy.AudioSeekAllowed);
        Assert.False(policy.ReplayAllowed);
        Assert.False(policy.TranscriptVisibleOnReview);
        Assert.False(policy.ConfirmDialogRequired);
        Assert.True(policy.UnansweredWarningRequired);
        Assert.NotNull(policy.FinalReviewAllPartsMs);
        Assert.Equal(120_000, policy.FinalReviewAllPartsMs);
    }

    [Fact]
    public void HomeMode_EnforcesFullscreen()
    {
        var policy = _resolver.For(ListeningAttemptMode.Home);

        Assert.True(policy.FullscreenEnforced);
        Assert.True(policy.OneWayLocks);
        Assert.False(policy.AudioPauseAllowed);
        Assert.False(policy.AudioSeekAllowed);
        Assert.False(policy.ReplayAllowed);
        Assert.False(policy.FreeNavigation);
        Assert.True(policy.RequiresTechReadiness);
    }

    [Fact]
    public void DiagnosticMode_FreeNavigationNoReplay()
    {
        var policy = _resolver.For(ListeningAttemptMode.Diagnostic);

        Assert.True(policy.FreeNavigation);
        Assert.False(policy.OneWayLocks);
        Assert.False(policy.AudioPauseAllowed);
        Assert.False(policy.AudioSeekAllowed);
        Assert.False(policy.ReplayAllowed);
        Assert.False(policy.TranscriptVisibleOnReview);
        Assert.False(policy.RequiresTechReadiness);
        Assert.False(policy.FullscreenEnforced);
    }

    [Theory]
    [InlineData(ListeningAttemptMode.Drill)]
    [InlineData(ListeningAttemptMode.MiniTest)]
    [InlineData(ListeningAttemptMode.ErrorBank)]
    public void LegacyModes_MapToLearningSemantics(ListeningAttemptMode mode)
    {
        var policy = _resolver.For(mode);
        var learning = _resolver.For(ListeningAttemptMode.Learning);

        // Legacy modes should behave identically to Learning
        Assert.Equal(learning.AudioPauseAllowed, policy.AudioPauseAllowed);
        Assert.Equal(learning.AudioSeekAllowed, policy.AudioSeekAllowed);
        Assert.Equal(learning.ReplayAllowed, policy.ReplayAllowed);
        Assert.Equal(learning.FreeNavigation, policy.FreeNavigation);
        Assert.Equal(learning.OneWayLocks, policy.OneWayLocks);
        Assert.Equal(learning.TranscriptVisibleOnReview, policy.TranscriptVisibleOnReview);
    }
}
