using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Copies the bundled-grant fields from a <see cref="BillingPlan"/> (or
/// matching <see cref="BillingPlanVersion"/>) onto a new
/// <see cref="Subscription"/>.
///
/// <para>Used at subscription activation so the buyer immediately gets the
/// promised assessments / sessions / AI credits / Tutor Book unlock from the
/// purchased SKU (see the OET 2026 catalog — Mega Special bundles 5
/// assessments + 1 session, Nursing Premium bundles 5 + 5 AI + Basic
/// English, etc.).</para>
///
/// <para>Side-effect free — only mutates the passed Subscription. Callers
/// own persisting via <c>SaveChangesAsync</c>.</para>
/// </summary>
public static class SubscriptionBundleInitializer
{
    /// <summary>Apply the bundled grants from a <see cref="BillingPlan"/>
    /// to a freshly-created Subscription. Also stamps <c>ExpiresAt</c>
    /// from <c>AccessDurationDays</c>.</summary>
    public static void ApplyBundle(Subscription subscription, BillingPlan plan, DateTimeOffset now)
    {
        subscription.WritingAssessmentsRemaining = plan.BundledWritingAssessments;
        subscription.SpeakingSessionsRemaining = plan.BundledSpeakingSessions;
        subscription.AiCreditsRemaining = plan.BundledAiCredits;
        subscription.TutorBookUnlocked = plan.BundledTutorBook;
        subscription.BasicEnglishUnlocked = plan.BundledBasicEnglish;
        subscription.ExpiresAt = ResolveExpiry(now, plan.AccessDurationDays);
    }

    /// <summary>Same as <see cref="ApplyBundle(Subscription, BillingPlan, DateTimeOffset)"/>
    /// but reads from an immutable <see cref="BillingPlanVersion"/> snapshot — preferred
    /// at activation time so the bundle is locked to the purchased version even if
    /// the live plan is later edited.</summary>
    public static void ApplyBundle(Subscription subscription, BillingPlanVersion version, DateTimeOffset now)
    {
        subscription.WritingAssessmentsRemaining = version.BundledWritingAssessments;
        subscription.SpeakingSessionsRemaining = version.BundledSpeakingSessions;
        subscription.AiCreditsRemaining = version.BundledAiCredits;
        subscription.TutorBookUnlocked = version.BundledTutorBook;
        subscription.BasicEnglishUnlocked = version.BundledBasicEnglish;
        subscription.ExpiresAt = ResolveExpiry(now, version.AccessDurationDays);
    }

    /// <summary>Apply an add-on grant to an existing Subscription. Increments
    /// the matching counter or flips the matching flag. Idempotent only when
    /// caller wraps in their own idempotency window — this helper does not
    /// inspect history.</summary>
    public static void ApplyAddOnGrant(Subscription subscription, BillingAddOn addon)
    {
        if (addon.LettersGranted > 0)
        {
            subscription.WritingAssessmentsRemaining = checked(subscription.WritingAssessmentsRemaining + addon.LettersGranted);
        }
        if (addon.SessionsGranted > 0)
        {
            subscription.SpeakingSessionsRemaining = checked(subscription.SpeakingSessionsRemaining + addon.SessionsGranted);
        }
        if (string.Equals(addon.AddonKind, "tutor_book", StringComparison.OrdinalIgnoreCase))
        {
            subscription.TutorBookUnlocked = true;
        }
        if (addon.GrantCredits > 0)
        {
            subscription.AiCreditsRemaining = checked(subscription.AiCreditsRemaining + addon.GrantCredits);
        }
    }

    /// <summary>Add-on version flavour for use inside webhook handlers where
    /// the immutable snapshot is preferred over the live add-on row.</summary>
    public static void ApplyAddOnGrant(Subscription subscription, BillingAddOnVersion version)
    {
        if (version.LettersGranted > 0)
        {
            subscription.WritingAssessmentsRemaining = checked(subscription.WritingAssessmentsRemaining + version.LettersGranted);
        }
        if (version.SessionsGranted > 0)
        {
            subscription.SpeakingSessionsRemaining = checked(subscription.SpeakingSessionsRemaining + version.SessionsGranted);
        }
        if (string.Equals(version.AddonKind, "tutor_book", StringComparison.OrdinalIgnoreCase))
        {
            subscription.TutorBookUnlocked = true;
        }
        if (version.GrantCredits > 0)
        {
            subscription.AiCreditsRemaining = checked(subscription.AiCreditsRemaining + version.GrantCredits);
        }
    }

    /// <summary>Reverse a grant (refund / chargeback). Counters clamp at zero.</summary>
    public static void ReverseAddOnGrant(Subscription subscription, BillingAddOnVersion version)
    {
        if (version.LettersGranted > 0)
        {
            subscription.WritingAssessmentsRemaining = Math.Max(0, subscription.WritingAssessmentsRemaining - version.LettersGranted);
        }
        if (version.SessionsGranted > 0)
        {
            subscription.SpeakingSessionsRemaining = Math.Max(0, subscription.SpeakingSessionsRemaining - version.SessionsGranted);
        }
        if (version.GrantCredits > 0)
        {
            subscription.AiCreditsRemaining = Math.Max(0, subscription.AiCreditsRemaining - version.GrantCredits);
        }
        // tutor_book is intentionally not auto-revoked on refund — leave that to admin action.
    }

    private static DateTimeOffset? ResolveExpiry(DateTimeOffset now, int accessDurationDays)
    {
        if (accessDurationDays <= 0) return null;
        if (accessDurationDays >= 9999) return null; // permanent entitlement (Tutor Book)
        return now.AddDays(accessDurationDays);
    }
}
