using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services.Pronunciation;

/// <summary>
/// Pronunciation entitlement gating. Business rules (v1):
///
///   - Unauthenticated: blocked entirely at the middleware layer.
///   - Authenticated free tier: capped at <c>FreeTierWeeklyAttemptLimit</c>
///     submitted attempts per rolling <c>FreeTierWindowDays</c>-day window.
///   - Authenticated resolver-granted access (Active, Trial, sponsor seat,
///     add-on, or current freeze): unlimited.
///
/// Evaluated on every <c>POST /v1/pronunciation/drills/{id}/attempt/upload/init</c>
/// so learners see the paywall before they record, not after.
/// </summary>
public interface IPronunciationEntitlementService
{
    Task<PronunciationEntitlement> CheckAsync(string? userId, CancellationToken ct);
}

public sealed record PronunciationEntitlement(
    bool Allowed,
    string Tier,
    int Remaining,
    int LimitPerWindow,
    int WindowDays,
    DateTimeOffset? ResetAt,
    string Reason);

public sealed class PronunciationEntitlementService(
    LearnerDbContext db,
    ILearnerEntitlementResolver entitlementResolver,
    IOptions<PronunciationOptions> options) : IPronunciationEntitlementService
{
    public async Task<PronunciationEntitlement> CheckAsync(string? userId, CancellationToken ct)
    {
        var opts = options.Value;
        var windowDays = opts.FreeTierWindowDays <= 0 ? 7 : opts.FreeTierWindowDays;

        if (string.IsNullOrWhiteSpace(userId))
        {
            return new PronunciationEntitlement(
                Allowed: false,
                Tier: "anonymous",
                Remaining: 0,
                LimitPerWindow: 0,
                WindowDays: windowDays,
                ResetAt: null,
                Reason: "Sign in to practise pronunciation.");
        }

        var now = DateTimeOffset.UtcNow;

        var resolvedEntitlement = await entitlementResolver.ResolveAsync(userId, LearnerEntitlementResources.Pronunciation, ct);
        if (HasUnlimitedAccess(resolvedEntitlement))
        {
            return new PronunciationEntitlement(
                Allowed: true,
                Tier: ResolveUnlimitedTier(resolvedEntitlement),
                Remaining: int.MaxValue,
                LimitPerWindow: int.MaxValue,
                WindowDays: windowDays,
                ResetAt: null,
                Reason: ResolveUnlimitedReason(resolvedEntitlement));
        }

        if (opts.FreeTierWeeklyAttemptLimit < 0)
        {
            return new PronunciationEntitlement(
                Allowed: true,
                Tier: "free",
                Remaining: int.MaxValue,
                LimitPerWindow: int.MaxValue,
                WindowDays: windowDays,
                ResetAt: null,
                Reason: "Pronunciation free tier is currently unlimited.");
        }

        var windowStart = now - TimeSpan.FromDays(windowDays);
        var attemptsInWindowRows = await db.PronunciationAttempts
            .Where(a => a.UserId == userId)
            .ToListAsync(ct);

        attemptsInWindowRows = attemptsInWindowRows
            .Where(a => a.CreatedAt >= windowStart)
            .ToList();
        var attemptsInWindow = attemptsInWindowRows.Count(a => a.Status != "refused");

        var remaining = Math.Max(0, opts.FreeTierWeeklyAttemptLimit - attemptsInWindow);
        var earliest = attemptsInWindowRows
            .OrderBy(a => a.CreatedAt)
            .Select(a => (DateTimeOffset?)a.CreatedAt)
            .FirstOrDefault();
        var resetAt = earliest.HasValue ? earliest.Value + TimeSpan.FromDays(windowDays) : (DateTimeOffset?)null;

        if (remaining <= 0)
        {
            return new PronunciationEntitlement(
                Allowed: false,
                Tier: "free",
                Remaining: 0,
                LimitPerWindow: opts.FreeTierWeeklyAttemptLimit,
                WindowDays: windowDays,
                ResetAt: resetAt,
                Reason: $"Free tier allows {opts.FreeTierWeeklyAttemptLimit} pronunciation attempts every {windowDays} days. Upgrade for unlimited practice.");
        }

        return new PronunciationEntitlement(
            Allowed: true,
            Tier: "free",
            Remaining: remaining,
            LimitPerWindow: opts.FreeTierWeeklyAttemptLimit,
            WindowDays: windowDays,
            ResetAt: resetAt,
            Reason: $"{remaining} of {opts.FreeTierWeeklyAttemptLimit} free pronunciation attempts remaining this week.");
    }

    private static bool HasUnlimitedAccess(LearnerEntitlementResolution entitlement)
        => entitlement.HasPaidOrSponsoredAccess || entitlement.HasActiveAddOn || entitlement.HasCurrentFreeze;

    private static string ResolveUnlimitedTier(LearnerEntitlementResolution entitlement)
        => entitlement.HasDirectTrialSubscription && !entitlement.HasDirectActiveSubscription ? "trial" : "paid";

    private static string ResolveUnlimitedReason(LearnerEntitlementResolution entitlement)
    {
        if (entitlement.HasDirectActiveSubscription || entitlement.HasDirectTrialSubscription)
        {
            return "Active subscription — unlimited pronunciation practice.";
        }

        if (entitlement.HasSponsorSeat)
        {
            return "Sponsor seat — unlimited pronunciation practice.";
        }

        if (entitlement.HasActiveAddOn)
        {
            return "Active add-on — unlimited pronunciation practice.";
        }

        return "Account freeze — unlimited pronunciation practice.";
    }
}
