using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services.Grammar;

/// <summary>
/// Grammar entitlement gating. Business rules (v1):
///
///   - Unauthenticated / anonymous: blocked entirely at the middleware layer.
///   - Authenticated with no active subscription: 3 lesson completions per
///     rolling 7-day window.
///   - Authenticated with <see cref="SubscriptionStatus.Active"/> or
///     <see cref="SubscriptionStatus.Trial"/>: unlimited.
///   - Sponsor-seat learners inherit their sponsor's plan.
///
/// The entitlement is evaluated on:
///   1. Learner page render (GET /v1/grammar/entitlement) to show a paywall.
///   2. Server-side submit (already server-authoritative) — the submit
///      endpoint should call <see cref="CheckAsync"/> and refuse (403) when
///      the quota is exhausted.
/// </summary>
public interface IGrammarEntitlementService
{
    Task<GrammarEntitlement> CheckAsync(string? userId, CancellationToken ct);
}

public sealed record GrammarEntitlement(
    bool Allowed,
    string Tier,
    int Remaining,
    int LimitPerWindow,
    int WindowDays,
    DateTimeOffset? ResetAt,
    string Reason);

public sealed class GrammarEntitlementService(LearnerDbContext db, ILearnerEntitlementResolver entitlementResolver) : IGrammarEntitlementService
{
    public const int FreeTierWeeklyLimit = 3;
    public const int WindowDays = 7;

    public async Task<GrammarEntitlement> CheckAsync(string? userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new GrammarEntitlement(
                Allowed: false,
                Tier: "anonymous",
                Remaining: 0,
                LimitPerWindow: 0,
                WindowDays: WindowDays,
                ResetAt: null,
                Reason: "Sign in to practise grammar.");
        }

        var now = DateTimeOffset.UtcNow;

        var resolvedEntitlement = await entitlementResolver.ResolveAsync(userId, LearnerEntitlementResources.Grammar, ct);

        if (resolvedEntitlement.HasPaidOrSponsoredAccess)
        {
            var tier = resolvedEntitlement.HasDirectTrialSubscription
                && !resolvedEntitlement.HasDirectActiveSubscription
                && !resolvedEntitlement.HasSponsorSeat
                    ? "trial"
                    : "paid";

            var reason = resolvedEntitlement.HasSponsorSeat
                && !resolvedEntitlement.HasDirectActiveSubscription
                && !resolvedEntitlement.HasDirectTrialSubscription
                    ? "Sponsor seat — unlimited grammar lessons."
                    : "Active subscription — unlimited grammar lessons.";

            return new GrammarEntitlement(
                Allowed: true,
                Tier: tier,
                Remaining: int.MaxValue,
                LimitPerWindow: int.MaxValue,
                WindowDays: WindowDays,
                ResetAt: null,
                Reason: reason);
        }

        var windowStart = now - TimeSpan.FromDays(WindowDays);
        var completionsInWindow = await db.Set<LearnerGrammarProgress>()
            .Where(p => p.UserId == userId
                && p.Status == "completed"
                && p.CompletedAt != null
                && p.CompletedAt >= windowStart)
            .CountAsync(ct);

        var remaining = Math.Max(0, FreeTierWeeklyLimit - completionsInWindow);
        var earliestCompletion = await db.Set<LearnerGrammarProgress>()
            .Where(p => p.UserId == userId
                && p.Status == "completed"
                && p.CompletedAt != null
                && p.CompletedAt >= windowStart)
            .OrderBy(p => p.CompletedAt)
            .Select(p => p.CompletedAt)
            .FirstOrDefaultAsync(ct);

        var resetAt = earliestCompletion.HasValue
            ? earliestCompletion.Value + TimeSpan.FromDays(WindowDays)
            : (DateTimeOffset?)null;

        if (remaining <= 0)
        {
            return new GrammarEntitlement(
                Allowed: false,
                Tier: "free",
                Remaining: 0,
                LimitPerWindow: FreeTierWeeklyLimit,
                WindowDays: WindowDays,
                ResetAt: resetAt,
                Reason: $"Free tier allows {FreeTierWeeklyLimit} grammar lessons every {WindowDays} days. Upgrade for unlimited practice.");
        }

        return new GrammarEntitlement(
            Allowed: true,
            Tier: "free",
            Remaining: remaining,
            LimitPerWindow: FreeTierWeeklyLimit,
            WindowDays: WindowDays,
            ResetAt: resetAt,
            Reason: $"{remaining} of {FreeTierWeeklyLimit} free grammar lessons remaining this week.");
    }
}
