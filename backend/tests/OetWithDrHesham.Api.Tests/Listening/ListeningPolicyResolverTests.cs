using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Listening;

namespace OetWithDrHesham.Api.Tests.Listening;

public class ListeningPolicyResolverTests
{
    // Known constants from ListeningPolicyDefaults (internal)
    private const int DefaultPreviewMsA1 = 30_000;
    private const int DefaultPreviewMsA2 = 30_000;
    private const int DefaultPreviewMsC1 = 90_000;
    private const int DefaultPreviewMsC2 = 60_000;
    private const int DefaultReviewMsA1 = 75_000;
    private const int DefaultReviewMsA2 = 75_000;
    private const int DefaultReviewMsC1 = 30_000;
    private const int DefaultReviewMsC2FinalCbt = 120_000;
    private const int DefaultReviewMsC2FinalPaper = 120_000;
    private const int DefaultBetweenSectionTransitionMs = 40_000;
    private const int DefaultPartBQuestionWindowMs = 15_000;
    private const int DefaultConfirmTokenTtlMs = 30_000;
    private const int DefaultTechReadinessTtlMs = 900_000;
    private const int DefaultFinalReviewAllPartsMsPaper = 120_000;

    [Fact]
    public void Resolve_NullPolicyAndNullOverride_ReturnsAllDefaults()
    {
        // Act
        var result = ListeningPolicyResolver.Resolve(null, null);

        // Assert — spot-check key defaults from ListeningPolicyDefaults
        Assert.Equal(DefaultPreviewMsA1, result.PreviewMsA1);
        Assert.Equal(DefaultPreviewMsA2, result.PreviewMsA2);
        Assert.Equal(DefaultPreviewMsC1, result.PreviewMsC1);
        Assert.Equal(DefaultPreviewMsC2, result.PreviewMsC2);
        Assert.Equal(DefaultReviewMsA1, result.ReviewMsA1);
        Assert.Equal(DefaultReviewMsA2, result.ReviewMsA2);
        Assert.Equal(DefaultReviewMsC1, result.ReviewMsC1);
        Assert.Equal(DefaultReviewMsC2FinalCbt, result.ReviewMsC2FinalCbt);
        Assert.Equal(DefaultReviewMsC2FinalPaper, result.ReviewMsC2FinalPaper);
        Assert.Equal(DefaultBetweenSectionTransitionMs, result.BetweenSectionTransitionMs);
        Assert.Equal(DefaultPartBQuestionWindowMs, result.PartBQuestionWindowMs);
        Assert.Equal(DefaultConfirmTokenTtlMs, result.ConfirmTokenTtlMs);
        Assert.Equal(DefaultTechReadinessTtlMs, result.TechReadinessTtlMs);
        Assert.Equal(DefaultFinalReviewAllPartsMsPaper, result.FinalReviewAllPartsMsPaper);
        Assert.True(result.OneWayLocksEnabled);
        Assert.Equal(0, result.ExtraTimePct);
        Assert.False(result.AccessibilityModeEnabled);
    }

    [Fact]
    public void Resolve_PolicyValues_OverrideDefaults()
    {
        // Arrange
        var policy = new ListeningPolicy
        {
            PreviewWindowMsA1 = 45_000,
            ReviewWindowMsC1 = 60_000,
            OneWayLocksEnabled = false,
            DefaultExtraTimePct = 25,
        };

        // Act
        var result = ListeningPolicyResolver.Resolve(policy, null);

        // Assert — overridden values
        Assert.Equal(45_000, result.PreviewMsA1);
        Assert.Equal(60_000, result.ReviewMsC1);
        Assert.False(result.OneWayLocksEnabled);
        Assert.Equal(25, result.ExtraTimePct);

        // Non-overridden values should still be defaults
        Assert.Equal(DefaultPreviewMsA2, result.PreviewMsA2);
        Assert.Equal(DefaultReviewMsA1, result.ReviewMsA1);
    }

    [Fact]
    public void Resolve_UserOverrideExtraTimePct_TakesPriorityOverPolicy()
    {
        // Arrange
        var policy = new ListeningPolicy
        {
            DefaultExtraTimePct = 10,
        };
        var userOverride = new ListeningUserPolicyOverride
        {
            UserId = "user-1",
            ExtraTimeEntitlementPct = 50,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        // Act
        var result = ListeningPolicyResolver.Resolve(policy, userOverride);

        // Assert — user override wins
        Assert.Equal(50, result.ExtraTimePct);
    }

    [Fact]
    public void Resolve_UserOverrideAccessibilityModeEnabled_IsHonored()
    {
        // Arrange
        var userOverride = new ListeningUserPolicyOverride
        {
            UserId = "user-1",
            AccessibilityModeEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        // Act
        var result = ListeningPolicyResolver.Resolve(null, userOverride);

        // Assert
        Assert.True(result.AccessibilityModeEnabled);
    }

    [Fact]
    public void Resolve_UserOverrideAccessibilityModeFalse_DefaultsToFalse()
    {
        // Arrange — no override or override with false
        var userOverride = new ListeningUserPolicyOverride
        {
            UserId = "user-2",
            AccessibilityModeEnabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        // Act
        var result = ListeningPolicyResolver.Resolve(null, userOverride);

        // Assert
        Assert.False(result.AccessibilityModeEnabled);
    }
}
