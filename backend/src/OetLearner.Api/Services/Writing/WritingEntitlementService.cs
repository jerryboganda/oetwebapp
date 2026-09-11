using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
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
    IWritingOptionsProvider optionsProvider,
    IAiPackageCreditService? aiPackageCreditService = null) : IWritingEntitlementService
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
        if (aiPackageCreditService is not null)
        {
            var snapshot = await aiPackageCreditService.GetSnapshotAsync(userId, 0, ct);
            var expired = snapshot.ExpiredBecausePassed
                || (snapshot.ExpiresAt is { } expires && expires <= DateTimeOffset.UtcNow);
            if (!expired)
            {
                if (snapshot.WritingUnlimited)
                {
                    return new WritingEntitlement(
                        Allowed: true,
                        Tier: entitlement.HasEligibleSubscription
                            ? (entitlement.IsTrial ? "trial" : "paid")
                            : "ai_package",
                        Remaining: int.MaxValue,
                        LimitPerWindow: int.MaxValue,
                        WindowDays: opts.FreeTierWindowDays,
                        ResetAt: snapshot.ExpiresAt,
                        Reason: "Catalogue unlimited writing grant.");
                }

                var writingCreditsRemaining = snapshot.AvailableWritingActivities;
                if (writingCreditsRemaining > 0)
                {
                    return new WritingEntitlement(
                        Allowed: true,
                        Tier: entitlement.HasEligibleSubscription
                            ? (entitlement.IsTrial ? "trial" : "paid")
                            : "ai_package",
                        Remaining: writingCreditsRemaining,
                        LimitPerWindow: writingCreditsRemaining,
                        WindowDays: opts.FreeTierWindowDays,
                        ResetAt: snapshot.ExpiresAt,
                        Reason: $"{writingCreditsRemaining} writing grading credit(s) remaining.");
                }
            }
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

    public async Task<WritingStartAuthorization> AuthorizeStartAsync(string? userId, string referenceId, string? taskId, CancellationToken ct)
    {
        var entitlement = await CheckAsync(userId, ct);
        if (!entitlement.Allowed)
        {
            var message = entitlement.Reason switch
            {
                "premium_required" => "Writing practice requires an active subscription.",
                "quota_exceeded" => $"Free tier allows {entitlement.LimitPerWindow} writing attempts every {entitlement.WindowDays} days.",
                _ => entitlement.Reason,
            };
            return new WritingStartAuthorization(false, "none", false, entitlement.Reason, message, null);
        }

        // Unlimited (Remaining == int.MaxValue) and free-tier (Tier == "free")
        // authorise with no deduction — §12.1/§12.3: a zero balance in an
        // unrelated pool (e.g. the AI-package ledger) must not block either.
        if (entitlement.Remaining == int.MaxValue)
        {
            await RecordStartAsync(userId, referenceId, taskId, "unlimited", charged: 0, ct);
            return new WritingStartAuthorization(true, "unlimited", false, null, null, null);
        }

        if (entitlement.Tier == "free")
        {
            await RecordStartAsync(userId, referenceId, taskId, "free_tier", charged: 0, ct);
            return new WritingStartAuthorization(true, "free_tier", false, null, null, null);
        }

        // Only remaining path: a finite AI-package writing-grading credit
        // balance. This is the one case that actually spends a credit — debit
        // atomically, idempotent on referenceId (mirrors ReadingAttemptService
        // Gate 6 / CreditGateExtensions.ObjectivePaperReference): a double-tap
        // or refresh reusing the same referenceId dedupes via
        // AiPackageCreditService's own TransactionExistsAsync check and is
        // never charged twice.
        if (aiPackageCreditService is null)
        {
            await RecordStartAsync(userId, referenceId, taskId, entitlement.Tier, charged: 0, ct);
            return new WritingStartAuthorization(true, entitlement.Tier, false, null, null, null);
        }

        var debit = await aiPackageCreditService.DeductGradingCreditAsync(
            userId!, "writing", referenceId, AiGradingCreditCost.WritingExam, ct);
        if (!debit.Debited)
        {
            return new WritingStartAuthorization(
                false,
                "none",
                false,
                debit.ErrorCode ?? "no_ai_package_credits",
                debit.ErrorMessage ?? "You do not have enough credits to start this activity. Please purchase another package or upgrade your plan.",
                null);
        }

        await RecordStartAsync(userId, referenceId, taskId, "ai_package", debit.CreditsUsed, ct);
        return new WritingStartAuthorization(true, "ai_package", true, null, null, debit.FeedbackMessage);
    }

    /// <summary>
    /// Attempt-level entitlement/billing record persisted at the moment an
    /// attempt is actually authorised to start (§12: "Persist an
    /// attempt-level entitlement/billing record at start: attempt ID,
    /// candidate ID, Task ID, entitlement source, unlimited/finite status,
    /// charged amount, timestamp, idempotency key"). Reuses the existing
    /// AnalyticsEvents audit trail rather than a new table — no schema
    /// migration needed for a lightweight audit record. Uses the reference
    /// id as both the idempotency key and the de-facto attempt/session id,
    /// since neither the scenario (writing-v2) nor legacy task start flow
    /// has a single shared attempt entity at this layer.
    /// </summary>
    private async Task RecordStartAsync(string? userId, string referenceId, string? taskId, string source, int charged, CancellationToken ct)
    {
        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = $"evt-{Guid.NewGuid():N}",
            UserId = userId ?? "",
            EventName = "writing_practice_start_authorized",
            PayloadJson = JsonSupport.Serialize(new
            {
                referenceId,
                taskId,
                entitlementSource = source,
                unlimited = source == "unlimited",
                chargedAmount = charged,
                idempotencyKey = referenceId,
            }),
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}
