using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Billing;
using Xunit;

namespace OetWithDrHesham.Api.Tests.Billing;

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

    // ─────────────────────────────────────────────────────────────────────────
    // OET 2026 entitlement conformance — ApplyPlanEntitlements /
    // ApplyAddOnEntitlements set ONLY the non-AI bundled fields. AI credits stay
    // the single source of truth granted via the AI-credit ledger at fulfillment,
    // so these helpers must never mutate AiCreditsRemaining (double-grant guard).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyPlanEntitlements_FromPlan_SetsNonAiBundleAndExpiry_WithoutTouchingAiCredits()
    {
        var now = new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero);
        var plan = new BillingPlan
        {
            Code = "full-condensed-medicine",
            Name = "Full Condensed (Medicine)",
            BundledWritingAssessments = 5,
            BundledSpeakingSessions = 1,
            BundledAiCredits = 7, // must be ignored by this method
            BundledTutorBook = false,
            BundledBasicEnglish = false,
            AccessDurationDays = 180,
        };
        // Seed a pre-existing AI balance to prove the method leaves it untouched.
        var sub = new Subscription { Id = "sub_test", UserId = "user_test", PlanId = plan.Code, AiCreditsRemaining = 42 };

        SubscriptionBundleInitializer.ApplyPlanEntitlements(sub, plan, now);

        Assert.Equal(5, sub.WritingAssessmentsRemaining);
        Assert.Equal(1, sub.SpeakingSessionsRemaining);
        Assert.False(sub.TutorBookUnlocked);
        Assert.False(sub.BasicEnglishUnlocked);
        Assert.Equal(now.AddDays(180), sub.ExpiresAt);
        Assert.Equal(42, sub.AiCreditsRemaining); // untouched — NOT 42 + 7
    }

    [Fact]
    public void ApplyPlanEntitlements_FromVersion_PermanentTutorBook_SetsNullExpiryAndUnlock_WithoutAiCredits()
    {
        var now = DateTimeOffset.UtcNow;
        var version = new BillingPlanVersion
        {
            Id = "plan-version-tutor-book",
            PlanId = "tutor-book",
            Code = "tutor-book",
            BundledTutorBook = true,
            BundledAiCredits = 3, // must be ignored
            AccessDurationDays = 9999, // permanent → no expiry
        };
        var sub = new Subscription { Id = "s1", UserId = "u1", PlanId = "tutor-book", AiCreditsRemaining = 5 };

        SubscriptionBundleInitializer.ApplyPlanEntitlements(sub, version, now);

        Assert.True(sub.TutorBookUnlocked);
        Assert.Null(sub.ExpiresAt); // 9999 = permanent entitlement
        Assert.Equal(5, sub.AiCreditsRemaining); // untouched
    }

    [Fact]
    public void ApplyPlanEntitlements_FromVersion_NonPositiveDuration_SetsNullExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var version = new BillingPlanVersion
        {
            Id = "plan-version-free",
            PlanId = "free-tier",
            Code = "free-tier",
            AccessDurationDays = 0, // <= 0 → permanent / no expiry
        };
        var sub = new Subscription { Id = "s1", UserId = "u1", PlanId = "free-tier" };

        SubscriptionBundleInitializer.ApplyPlanEntitlements(sub, version, now);

        Assert.Null(sub.ExpiresAt);
    }

    [Fact]
    public void ApplyPlanEntitlements_FromVersion_BasicEnglishBundle_SetsFlag_WithoutAiCredits()
    {
        var now = DateTimeOffset.UtcNow;
        var version = new BillingPlanVersion
        {
            Id = "plan-version-nursing-premium",
            PlanId = "full-nursing-premium",
            Code = "full-nursing-premium",
            BundledWritingAssessments = 5,
            BundledAiCredits = 5, // must be ignored
            BundledBasicEnglish = true,
            AccessDurationDays = 180,
        };
        var sub = new Subscription { Id = "s1", UserId = "u1", PlanId = version.Code, AiCreditsRemaining = 0 };

        SubscriptionBundleInitializer.ApplyPlanEntitlements(sub, version, now);

        Assert.Equal(5, sub.WritingAssessmentsRemaining);
        Assert.True(sub.BasicEnglishUnlocked);
        Assert.Equal(0, sub.AiCreditsRemaining); // untouched — NOT 5
    }

    [Fact]
    public void ApplyAddOnEntitlements_FromAddOn_IncrementsLettersAndSessions_WithoutAiCredits()
    {
        var addon = new BillingAddOn
        {
            Code = "addon-5-letters",
            AddonKind = "writing_assessments",
            LettersGranted = 5,
            SessionsGranted = 0,
            GrantCredits = 9, // must be ignored by this method
        };
        var sub = new Subscription
        {
            Id = "s1",
            UserId = "u1",
            PlanId = "writing-crash",
            WritingAssessmentsRemaining = 3,
            SpeakingSessionsRemaining = 2,
            AiCreditsRemaining = 11,
        };

        SubscriptionBundleInitializer.ApplyAddOnEntitlements(sub, addon);

        Assert.Equal(8, sub.WritingAssessmentsRemaining); // 3 + 5
        Assert.Equal(2, sub.SpeakingSessionsRemaining); // unchanged
        Assert.False(sub.TutorBookUnlocked);
        Assert.Equal(11, sub.AiCreditsRemaining); // untouched — NOT 11 + 9
    }

    [Fact]
    public void ApplyAddOnEntitlements_FromVersion_IncrementsSessions_WithoutAiCredits()
    {
        var version = new BillingAddOnVersion
        {
            Code = "speaking-2sessions",
            AddonKind = "speaking_sessions",
            SessionsGranted = 2,
            LettersGranted = 0,
            GrantCredits = 4, // must be ignored
        };
        var sub = new Subscription
        {
            Id = "s1",
            UserId = "u1",
            PlanId = "mega-special",
            SpeakingSessionsRemaining = 1,
            AiCreditsRemaining = 6,
        };

        SubscriptionBundleInitializer.ApplyAddOnEntitlements(sub, version);

        Assert.Equal(3, sub.SpeakingSessionsRemaining); // 1 + 2
        Assert.Equal(0, sub.WritingAssessmentsRemaining); // unchanged
        Assert.Equal(6, sub.AiCreditsRemaining); // untouched
    }

    [Fact]
    public void ApplyAddOnEntitlements_FromAddOn_TutorBookKind_FlipsUnlocked_WithoutAiCredits()
    {
        var addon = new BillingAddOn { Code = "tutor-book-addon", AddonKind = "tutor_book" };
        var sub = new Subscription { Id = "s1", UserId = "u1", PlanId = "writing-crash", TutorBookUnlocked = false, AiCreditsRemaining = 2 };

        SubscriptionBundleInitializer.ApplyAddOnEntitlements(sub, addon);

        Assert.True(sub.TutorBookUnlocked);
        Assert.Equal(2, sub.AiCreditsRemaining); // untouched
    }

    [Fact]
    public void ApplyAddOnEntitlements_FromVersion_TutorBookKind_FlipsUnlocked()
    {
        var version = new BillingAddOnVersion { Code = "tutor-book-addon", AddonKind = "tutor_book" };
        var sub = new Subscription { Id = "s1", UserId = "u1", PlanId = "writing-crash", TutorBookUnlocked = false };

        SubscriptionBundleInitializer.ApplyAddOnEntitlements(sub, version);

        Assert.True(sub.TutorBookUnlocked);
    }
}
