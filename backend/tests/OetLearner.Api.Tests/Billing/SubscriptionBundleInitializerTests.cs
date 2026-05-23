using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using Xunit;

namespace OetLearner.Api.Tests.Billing;

/// <summary>
/// Unit tests for <see cref="SubscriptionBundleInitializer"/>.
/// Verifies that the OET 2026 bundle promises ("Mega Special bundles 5 + 1",
/// "Nursing Premium bundles 5 + 5 AI + Basic English", etc.) translate
/// correctly into Subscription counters at activation.
/// </summary>
public class SubscriptionBundleInitializerTests
{
    [Fact]
    public void ApplyBundle_FromPlan_CopiesAllGrantsAndExpiry()
    {
        var now = new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero);
        var plan = new BillingPlan
        {
            Code = "mega-special",
            Name = "Mega Special Package",
            BundledWritingAssessments = 5,
            BundledSpeakingSessions = 1,
            BundledAiCredits = 0,
            BundledTutorBook = false,
            BundledBasicEnglish = false,
            AccessDurationDays = 180,
        };
        var sub = new Subscription { Id = "sub_test", UserId = "user_test", PlanId = plan.Code };

        SubscriptionBundleInitializer.ApplyBundle(sub, plan, now);

        Assert.Equal(5, sub.WritingAssessmentsRemaining);
        Assert.Equal(1, sub.SpeakingSessionsRemaining);
        Assert.Equal(0, sub.AiCreditsRemaining);
        Assert.False(sub.TutorBookUnlocked);
        Assert.False(sub.BasicEnglishUnlocked);
        Assert.Equal(now.AddDays(180), sub.ExpiresAt);
    }

    [Fact]
    public void ApplyBundle_TutorBookSku_SetsPermanentEntitlement()
    {
        var now = DateTimeOffset.UtcNow;
        var plan = new BillingPlan
        {
            Code = "tutor-book",
            BundledTutorBook = true,
            AccessDurationDays = 9999,
        };
        var sub = new Subscription { Id = "s1", UserId = "u1", PlanId = "tutor-book" };

        SubscriptionBundleInitializer.ApplyBundle(sub, plan, now);

        Assert.True(sub.TutorBookUnlocked);
        Assert.Null(sub.ExpiresAt); // 9999 days = permanent
    }

    [Fact]
    public void ApplyBundle_NursingPremium_SetsBasicEnglishAndAiCredits()
    {
        var plan = new BillingPlan
        {
            Code = "full-nursing-premium",
            BundledWritingAssessments = 5,
            BundledAiCredits = 5,
            BundledBasicEnglish = true,
            AccessDurationDays = 180,
        };
        var sub = new Subscription { Id = "s1", UserId = "u1", PlanId = plan.Code };

        SubscriptionBundleInitializer.ApplyBundle(sub, plan, DateTimeOffset.UtcNow);

        Assert.Equal(5, sub.WritingAssessmentsRemaining);
        Assert.Equal(5, sub.AiCreditsRemaining);
        Assert.True(sub.BasicEnglishUnlocked);
    }

    [Fact]
    public void ApplyAddOnGrant_WritingAssessments_IncrementsCounter()
    {
        var addon = new BillingAddOn { Code = "addon-5-letters", AddonKind = "writing_assessments", LettersGranted = 5 };
        var sub = new Subscription { Id = "s1", UserId = "u1", PlanId = "writing-crash", WritingAssessmentsRemaining = 3 };

        SubscriptionBundleInitializer.ApplyAddOnGrant(sub, addon);

        Assert.Equal(8, sub.WritingAssessmentsRemaining);
    }

    [Fact]
    public void ApplyAddOnGrant_SpeakingSessions_IncrementsCounter()
    {
        var addon = new BillingAddOn { Code = "speaking-2sessions", AddonKind = "speaking_sessions", SessionsGranted = 2 };
        var sub = new Subscription { Id = "s1", UserId = "u1", PlanId = "mega-special", SpeakingSessionsRemaining = 1 };

        SubscriptionBundleInitializer.ApplyAddOnGrant(sub, addon);

        Assert.Equal(3, sub.SpeakingSessionsRemaining);
    }

    [Fact]
    public void ApplyAddOnGrant_TutorBook_FlipsUnlocked()
    {
        var addon = new BillingAddOn { Code = "tutor-book-addon", AddonKind = "tutor_book" };
        var sub = new Subscription { Id = "s1", UserId = "u1", PlanId = "writing-crash", TutorBookUnlocked = false };

        SubscriptionBundleInitializer.ApplyAddOnGrant(sub, addon);

        Assert.True(sub.TutorBookUnlocked);
    }

    [Fact]
    public void ReverseAddOnGrant_ClampsAtZero()
    {
        var version = new BillingAddOnVersion { LettersGranted = 5, SessionsGranted = 2, GrantCredits = 0 };
        var sub = new Subscription { Id = "s1", UserId = "u1", PlanId = "p1", WritingAssessmentsRemaining = 3, SpeakingSessionsRemaining = 1 };

        SubscriptionBundleInitializer.ReverseAddOnGrant(sub, version);

        Assert.Equal(0, sub.WritingAssessmentsRemaining); // clamped
        Assert.Equal(0, sub.SpeakingSessionsRemaining); // clamped
    }
}
