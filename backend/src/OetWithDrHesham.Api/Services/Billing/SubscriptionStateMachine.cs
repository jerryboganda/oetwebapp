using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Billing;

/// <summary>
/// Centralised legal-transition table for <see cref="SubscriptionStatus"/>.
/// Slice D (May 2026 billing hardening) — the canonical authority for any
/// code path that mutates <see cref="Subscription.Status"/>. Existing direct
/// assignments should migrate to <see cref="Transition"/> opportunistically.
/// </summary>
public static class SubscriptionStateMachine
{
    /// <summary>
    /// Adjacency list of legal transitions. A transition is legal if and only
    /// if it appears here. Self-transitions (no-op writes) are permitted to
    /// keep idempotent webhook replays safe.
    /// </summary>
    private static readonly IReadOnlyDictionary<SubscriptionStatus, IReadOnlySet<SubscriptionStatus>> Allowed
        = new Dictionary<SubscriptionStatus, IReadOnlySet<SubscriptionStatus>>
        {
            [SubscriptionStatus.Trial] = new HashSet<SubscriptionStatus>
            {
                SubscriptionStatus.Trial,
                SubscriptionStatus.Active,
                SubscriptionStatus.Suspended, // admin suspend (reversible via Suspended -> Active)
                SubscriptionStatus.Cancelled,
                SubscriptionStatus.Expired,
            },
            [SubscriptionStatus.Pending] = new HashSet<SubscriptionStatus>
            {
                SubscriptionStatus.Pending,
                SubscriptionStatus.Active,
                SubscriptionStatus.Trial,
                SubscriptionStatus.Suspended,
                SubscriptionStatus.Cancelled,
                SubscriptionStatus.Expired,
            },
            [SubscriptionStatus.Active] = new HashSet<SubscriptionStatus>
            {
                SubscriptionStatus.Active,
                SubscriptionStatus.PastDue,
                SubscriptionStatus.Suspended, // dispute-pending equivalent
                SubscriptionStatus.Paused,    // Phase 6 voluntary pause
                SubscriptionStatus.FreezeRequested,
                SubscriptionStatus.Frozen,
                SubscriptionStatus.Cancelled,
                SubscriptionStatus.Expired,
            },
            [SubscriptionStatus.FreezeRequested] = new HashSet<SubscriptionStatus>
            {
                SubscriptionStatus.FreezeRequested,
                SubscriptionStatus.Active,
                SubscriptionStatus.Frozen,
                SubscriptionStatus.Suspended,
                SubscriptionStatus.Cancelled,
                SubscriptionStatus.Expired,
            },
            [SubscriptionStatus.Frozen] = new HashSet<SubscriptionStatus>
            {
                SubscriptionStatus.Frozen,
                SubscriptionStatus.Active,
                SubscriptionStatus.Suspended,
                SubscriptionStatus.Cancelled,
            },
            [SubscriptionStatus.Paused] = new HashSet<SubscriptionStatus>
            {
                SubscriptionStatus.Paused,
                SubscriptionStatus.Active,    // Phase 6 resume
                SubscriptionStatus.Suspended,
                SubscriptionStatus.Cancelled,
                SubscriptionStatus.Expired,
            },
            [SubscriptionStatus.PastDue] = new HashSet<SubscriptionStatus>
            {
                SubscriptionStatus.PastDue,
                SubscriptionStatus.Active,    // dunning recovered
                SubscriptionStatus.Suspended, // dispute opened
                SubscriptionStatus.Cancelled,
                SubscriptionStatus.Expired,
            },
            [SubscriptionStatus.Suspended] = new HashSet<SubscriptionStatus>
            {
                SubscriptionStatus.Suspended,
                SubscriptionStatus.Active,    // dispute resolved in our favour
                SubscriptionStatus.Cancelled,
                SubscriptionStatus.Expired,
            },
            [SubscriptionStatus.Cancelled] = new HashSet<SubscriptionStatus>
            {
                SubscriptionStatus.Cancelled,
                SubscriptionStatus.Active,    // admin restore / ReactivateCancelled
                SubscriptionStatus.Expired,
            },
            [SubscriptionStatus.Expired] = new HashSet<SubscriptionStatus>
            {
                SubscriptionStatus.Expired,
            },
        };

    public static bool IsLegal(SubscriptionStatus from, SubscriptionStatus to)
        => Allowed.TryGetValue(from, out var set) && set.Contains(to);

    public static IReadOnlySet<SubscriptionStatus> AllowedFrom(SubscriptionStatus from)
        => Allowed.TryGetValue(from, out var set) ? set : new HashSet<SubscriptionStatus>();

    /// <summary>
    /// Apply a state change, throwing a structured <see cref="ApiException"/>
    /// if the transition is illegal. Returns true if the status actually
    /// changed (false on a no-op self-transition).
    /// </summary>
    public static bool Transition(Subscription subscription, SubscriptionStatus target, string reason)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A non-empty reason is required for subscription state transitions.", nameof(reason));
        }

        var current = subscription.Status;
        if (!IsLegal(current, target))
        {
            throw ApiException.Conflict(
                "subscription_illegal_transition",
                $"Subscription transition {current} -> {target} is not allowed (reason: {reason}).");
        }

        if (current == target)
        {
            return false;
        }

        subscription.Status = target;
        subscription.ChangedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>Phase 6 — pause an active subscription until <paramref name="until"/>.</summary>
    public static void Pause(Subscription subscription, DateTimeOffset? until, string reason)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A non-empty reason is required for subscription state transitions.", nameof(reason));
        }
        if (!IsLegal(subscription.Status, SubscriptionStatus.Paused))
        {
            throw ApiException.Conflict(
                "subscription_illegal_transition",
                $"Subscription transition {subscription.Status} -> Paused is not allowed (reason: {reason}).");
        }
        subscription.Status = SubscriptionStatus.Paused;
        subscription.PausedUntil = until;
        subscription.ChangedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Phase 6 — resume a paused subscription back to Active.</summary>
    public static void Resume(Subscription subscription, string reason)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A non-empty reason is required for subscription state transitions.", nameof(reason));
        }
        if (subscription.Status != SubscriptionStatus.Paused)
        {
            throw ApiException.Conflict(
                "subscription_resume_invalid_state",
                "Only paused subscriptions can be resumed.");
        }
        subscription.Status = SubscriptionStatus.Active;
        subscription.PausedUntil = null;
        subscription.ChangedAt = DateTimeOffset.UtcNow;
    }

    public static void ReactivateCancelled(Subscription subscription, string reason, DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A non-empty reason is required for subscription state transitions.", nameof(reason));
        }

        if (subscription.Status != SubscriptionStatus.Cancelled)
        {
            throw ApiException.Conflict(
                "subscription_reactivation_invalid_state",
                "Only cancelled subscriptions can be reactivated.");
        }

        subscription.Status = SubscriptionStatus.Active;
        subscription.ChangedAt = changedAt;
    }
}
