using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

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
                SubscriptionStatus.Cancelled,
                SubscriptionStatus.Expired,
            },
            [SubscriptionStatus.Pending] = new HashSet<SubscriptionStatus>
            {
                SubscriptionStatus.Pending,
                SubscriptionStatus.Active,
                SubscriptionStatus.Trial,
                SubscriptionStatus.Cancelled,
                SubscriptionStatus.Expired,
            },
            [SubscriptionStatus.Active] = new HashSet<SubscriptionStatus>
            {
                SubscriptionStatus.Active,
                SubscriptionStatus.PastDue,
                SubscriptionStatus.Suspended, // dispute-pending equivalent
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
                SubscriptionStatus.Active,    // self-serve reactivation
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
}
