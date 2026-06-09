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
        subscription.AccessDurationDays = ResolveAccessDurationDays(plan.AccessDurationDays);
        subscription.ExpiresAt = ResolveExpiry(subscription, now, plan.AccessDurationDays);
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
        subscription.AccessDurationDays = ResolveAccessDurationDays(version.AccessDurationDays);
        subscription.ExpiresAt = ResolveExpiry(subscription, now, version.AccessDurationDays);
    }

    /// <summary>
    /// Apply ONLY the non-AI bundled entitlements from a <see cref="BillingPlan"/>
    /// to a Subscription (writing assessments, speaking sessions, Tutor Book,
    /// Basic English, and the access-duration expiry).
    ///
    /// <para>Deliberately does NOT touch <c>AiCreditsRemaining</c>. AI credits are
    /// granted exactly once at fulfillment via the AI-credit ledger
    /// (<c>CreditAiLedgerForPlanPaymentAsync</c>) under its own idempotency guard;
    /// this method is called alongside that flow, so it must leave AI credits as
    /// the single source of truth to avoid double-counting.</para>
    ///
    /// <para>Use this from webhook fulfillment where the plan's AI credits are
    /// handled separately. Use <see cref="ApplyBundle(Subscription, BillingPlan, DateTimeOffset)"/>
    /// instead when you want the complete bundle (including AI credits) applied
    /// in one shot at first activation.</para>
    /// </summary>
    public static void ApplyPlanEntitlements(Subscription subscription, BillingPlan plan, DateTimeOffset now)
    {
        subscription.WritingAssessmentsRemaining = plan.BundledWritingAssessments;
        subscription.SpeakingSessionsRemaining = plan.BundledSpeakingSessions;
        subscription.TutorBookUnlocked = plan.BundledTutorBook;
        subscription.BasicEnglishUnlocked = plan.BundledBasicEnglish;
        subscription.AccessDurationDays = ResolveAccessDurationDays(plan.AccessDurationDays);
        subscription.ExpiresAt = ResolveExpiry(subscription, now, plan.AccessDurationDays);
    }

    /// <summary>Immutable-snapshot flavour of
    /// <see cref="ApplyPlanEntitlements(Subscription, BillingPlan, DateTimeOffset)"/>
    /// — reads the bundled entitlements from a locked <see cref="BillingPlanVersion"/>
    /// so the grant matches the purchased version even if the live plan was later
    /// edited. Does NOT touch <c>AiCreditsRemaining</c> (see remarks on the
    /// <see cref="BillingPlan"/> overload).</summary>
    public static void ApplyPlanEntitlements(Subscription subscription, BillingPlanVersion version, DateTimeOffset now)
    {
        subscription.WritingAssessmentsRemaining = version.BundledWritingAssessments;
        subscription.SpeakingSessionsRemaining = version.BundledSpeakingSessions;
        subscription.TutorBookUnlocked = version.BundledTutorBook;
        subscription.BasicEnglishUnlocked = version.BundledBasicEnglish;
        subscription.AccessDurationDays = ResolveAccessDurationDays(version.AccessDurationDays);
        subscription.ExpiresAt = ResolveExpiry(subscription, now, version.AccessDurationDays);
    }

    /// <summary>
    /// Apply ONLY the non-AI add-on entitlements to a Subscription: increment
    /// writing assessments by <c>LettersGranted</c>, speaking sessions by
    /// <c>SessionsGranted</c>, and unlock the Tutor Book for <c>tutor_book</c>
    /// add-ons.
    ///
    /// <para>Deliberately does NOT touch <c>AiCreditsRemaining</c>. AI-credit
    /// add-ons are granted separately via the AI-credit ledger
    /// (<c>CreditAiLedgerForAddOnPaymentAsync</c>) under its own idempotency
    /// guard; this method runs alongside that flow, so it must leave AI credits
    /// alone to avoid double-counting.</para>
    ///
    /// <para>Idempotent only when the caller wraps it in a once-per-grant window
    /// (e.g. the <c>existingItem is null</c> branch in webhook fulfillment) — this
    /// helper does not inspect history.</para>
    /// </summary>
    public static void ApplyAddOnEntitlements(Subscription subscription, BillingAddOn addon)
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
    }

    /// <summary>Immutable-snapshot flavour of
    /// <see cref="ApplyAddOnEntitlements(Subscription, BillingAddOn)"/> — reads
    /// <c>LettersGranted</c> / <c>SessionsGranted</c> / <c>AddonKind</c> from a
    /// locked <see cref="BillingAddOnVersion"/>. Does NOT touch
    /// <c>AiCreditsRemaining</c>.</summary>
    public static void ApplyAddOnEntitlements(Subscription subscription, BillingAddOnVersion version)
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

    private static int ResolveAccessDurationDays(int accessDurationDays)
        => accessDurationDays <= 0 || accessDurationDays >= 9999 ? 180 : accessDurationDays;

    private static DateTimeOffset? ResolveExpiry(Subscription subscription, DateTimeOffset now, int accessDurationDays)
    {
        if (accessDurationDays <= 0) return null;
        if (accessDurationDays >= 9999) return null; // permanent entitlement (Tutor Book)
        var anchor = subscription.ExpiresAt is { } current && current > now ? current : now;
        return anchor.AddDays(accessDurationDays);
    }
}
