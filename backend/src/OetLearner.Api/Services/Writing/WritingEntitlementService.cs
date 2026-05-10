using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Writing entitlement gating. Mirrors
/// <see cref="OetLearner.Api.Services.Grammar.GrammarEntitlementService"/>
/// but reads its limit / window / enabled flag from the runtime-mutable
/// <see cref="WritingOptions"/> singleton (managed via the admin
/// endpoint <c>PUT /v1/admin/writing/options</c>).
///
/// Default behaviour (out of the box): <c>FreeTierEnabled=false</c> →
/// Writing is premium-only. Admins flip the master flag and set the
/// limit to expose Writing to free-tier learners.
public sealed class WritingEntitlementService(
    LearnerDbContext db,
    IEffectiveEntitlementResolver entitlementResolver,
    IWritingOptionsProvider optionsProvider) : IWritingEntitlementService
{
    public async Task<WritingEntitlement> CheckAsync(string? userId, CancellationToken ct)
    {
        var opts = await optionsProvider.GetAsync(ct);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return new WritingEntitlement(
                Allowed: false,
                Tier: "anonymous",
                Remaining: 0,
                LimitPerWindow: 0,
                WindowDays: opts.FreeTierWindowDays,
                ResetAt: null,
                Reason: "Sign in to practise writing.");
        }

        var entitlement = await entitlementResolver.ResolveAsync(userId, ct);
        if (entitlement.HasEligibleSubscription)
        {
            return new WritingEntitlement(
                Allowed: true,
                Tier: entitlement.IsTrial ? "trial" : "paid",
                Remaining: int.MaxValue,
                LimitPerWindow: int.MaxValue,
                WindowDays: opts.FreeTierWindowDays,
                ResetAt: null,
                Reason: "Active subscription — unlimited writing attempts.");
        }

        if (!opts.FreeTierEnabled)
        {
            return new WritingEntitlement(
                Allowed: false,
                Tier: "free",
                Remaining: 0,
                LimitPerWindow: 0,
                WindowDays: opts.FreeTierWindowDays,
                ResetAt: null,
                Reason: "premium_required");
        }

        var now = DateTimeOffset.UtcNow;
        var windowStart = now - TimeSpan.FromDays(opts.FreeTierWindowDays);

        // Predicate matches GrammarEntitlementService — `!= null && >=`
        // is server-translatable in both Postgres and (current) SQLite EF
        // because it does not coalesce against a DateTimeOffset constant.
        var completionsInWindow = await db.Attempts
            .Where(a => a.UserId == userId
                && a.SubtestCode == "writing"
                && a.State == AttemptState.Completed
                && a.CompletedAt != null
                && a.CompletedAt >= windowStart)
            .CountAsync(ct);

        var remaining = Math.Max(0, opts.FreeTierLimit - completionsInWindow);

        var earliestCompletion = await db.Attempts
            .Where(a => a.UserId == userId
                && a.SubtestCode == "writing"
                && a.State == AttemptState.Completed
                && a.CompletedAt != null
                && a.CompletedAt >= windowStart)
            .OrderBy(a => a.CompletedAt)
            .Select(a => a.CompletedAt)
            .FirstOrDefaultAsync(ct);

        var resetAt = earliestCompletion.HasValue
            ? earliestCompletion.Value + TimeSpan.FromDays(opts.FreeTierWindowDays)
            : (DateTimeOffset?)null;

        if (remaining <= 0)
        {
            return new WritingEntitlement(
                Allowed: false,
                Tier: "free",
                Remaining: 0,
                LimitPerWindow: opts.FreeTierLimit,
                WindowDays: opts.FreeTierWindowDays,
                ResetAt: resetAt,
                Reason: "quota_exceeded");
        }

        return new WritingEntitlement(
            Allowed: true,
            Tier: "free",
            Remaining: remaining,
            LimitPerWindow: opts.FreeTierLimit,
            WindowDays: opts.FreeTierWindowDays,
            ResetAt: resetAt,
            Reason: $"{remaining} of {opts.FreeTierLimit} free writing attempts remaining this window.");
    }
}
