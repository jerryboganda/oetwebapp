using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services.AiManagement;

// ═════════════════════════════════════════════════════════════════════════════
// AI Quota Service — enforces docs/AI-USAGE-POLICY.md §3, §4, §7
//
// The gateway calls TryReserveAsync() BEFORE handing the request to the
// provider and CommitAsync() after a successful response. Failures roll the
// reservation back by recording a zero-token commit.
//
// This service is the ONLY place that reads AiQuotaPlan / AiUserQuotaOverride
// / AiGlobalPolicy. Feature code never touches those tables directly.
// ═════════════════════════════════════════════════════════════════════════════

public interface IAiQuotaService
{
    /// <summary>
    /// Evaluate whether the user may make this call. Returns a
    /// <see cref="AiQuotaDecision"/> that the gateway uses to either proceed
    /// or refuse with a specific error code.
    /// </summary>
    Task<AiQuotaDecision> TryReserveAsync(
        string? userId,
        string featureCode,
        AiKeySource prospectiveKeySource,
        CancellationToken ct);

    /// <summary>
    /// After a successful provider call, commit the actual tokens used. Safe
    /// to call with 0 tokens (no-op) on failure paths.
    /// </summary>
    Task CommitAsync(
        string? userId,
        string featureCode,
        int promptTokens,
        int completionTokens,
        decimal costEstimateUsd,
        CancellationToken ct);

    /// <summary>Read the resolved policy for the user, for display on
    /// dashboards. Cached per request.</summary>
    Task<AiUserPolicySnapshot> GetUserPolicyAsync(string userId, CancellationToken ct);

    /// <summary>Read-through accessor for the singleton global policy row.</summary>
    Task<AiGlobalPolicy> GetGlobalPolicyAsync(CancellationToken ct);
}

/// <summary>
/// Outcome of a quota check. Composite so the gateway can record a precise
/// <c>errorCode</c> and <c>policyTrace</c> on refusal.
/// </summary>
public sealed record AiQuotaDecision(
    bool Allowed,
    string? ErrorCode,
    string? ErrorMessage,
    string PolicyTrace,
    AiGlobalPolicy GlobalPolicy,
    AiQuotaPlan? Plan,
    AiUserQuotaOverride? Override,
    int TokensUsedThisPeriod,
    int TokensCapThisPeriod);

/// <summary>
/// Snapshot for `/v1/me/ai/usage`. Learner-facing numbers derived through
/// the quota service so the same logic powers UI and enforcement.
/// </summary>
public sealed record AiUserPolicySnapshot(
    string PlanCode,
    string PlanName,
    int MonthlyTokenCap,
    int DailyTokenCap,
    int TokensUsedThisMonth,
    int TokensUsedToday,
    int RequestsThisMonth,
    decimal CostEstimateUsdThisMonth,
    AiOveragePolicy OveragePolicy,
    bool AiDisabled,
    bool KillSwitchActive,
    AiKillSwitchScope KillSwitchScope);

public sealed class AiQuotaService(
    LearnerDbContext db,
    IMemoryCache cache,
    ILogger<AiQuotaService> logger,
    ILearnerEntitlementResolver entitlementResolver)
    : IAiQuotaService
{
    private const string DefaultPlanCode = "free";
    private static readonly TimeSpan GlobalPolicyCacheTtl = TimeSpan.FromSeconds(15);

    public async Task<AiQuotaDecision> TryReserveAsync(
        string? userId,
        string featureCode,
        AiKeySource prospectiveKeySource,
        CancellationToken ct)
    {
        var global = await GetGlobalPolicyAsync(ct);

        // ── Per-feature kill list ───────────────────────────────────────────
        // Admins can disable specific feature codes without tripping the full
        // kill-switch. Evaluated FIRST so a disabled feature refuses before we
        // ever touch quota, plans or BYOK paths. Applies uniformly to BYOK
        // and platform keys because some features (e.g. scoring-critical) must
        // be turned off wholesale when a rulebook regression is detected.
        if (!string.IsNullOrWhiteSpace(global.DisabledFeaturesCsv)
            && IsFeatureCodeInCsv(global.DisabledFeaturesCsv, featureCode))
        {
            return new AiQuotaDecision(
                Allowed: false,
                ErrorCode: "feature_disabled",
                ErrorMessage: "This AI feature is temporarily disabled by an administrator.",
                PolicyTrace: $"feature_disabled.{featureCode}",
                GlobalPolicy: global, Plan: null, Override: null,
                TokensUsedThisPeriod: 0, TokensCapThisPeriod: 0);
        }

        // ── Global kill-switch ───────────────────────────────────────────────
        if (global.KillSwitchEnabled)
        {
            var scopeBlocksThisCall =
                global.KillSwitchScope == AiKillSwitchScope.AllCalls
                || (global.KillSwitchScope == AiKillSwitchScope.PlatformKeysOnly
                    && prospectiveKeySource != AiKeySource.Byok);

            if (scopeBlocksThisCall)
            {
                return new AiQuotaDecision(
                    Allowed: false,
                    ErrorCode: "kill_switch",
                    ErrorMessage: global.KillSwitchReason ?? "AI is temporarily disabled by an administrator.",
                    PolicyTrace: $"kill_switch.{global.KillSwitchScope}",
                    GlobalPolicy: global, Plan: null, Override: null,
                    TokensUsedThisPeriod: 0, TokensCapThisPeriod: 0);
            }
        }

        // ── BYOK bypasses quota — user pays with their own key ───────────────
        if (prospectiveKeySource == AiKeySource.Byok)
        {
            return new AiQuotaDecision(
                Allowed: true,
                ErrorCode: null, ErrorMessage: null,
                PolicyTrace: "byok.unmetered",
                GlobalPolicy: global, Plan: null, Override: null,
                TokensUsedThisPeriod: 0, TokensCapThisPeriod: int.MaxValue);
        }

        // Anonymous / system calls (no user) bypass quota. Platform-wide kill
        // switch and global budget still apply.
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new AiQuotaDecision(
                Allowed: true,
                ErrorCode: null, ErrorMessage: null,
                PolicyTrace: "anonymous.unmetered",
                GlobalPolicy: global, Plan: null, Override: null,
                TokensUsedThisPeriod: 0, TokensCapThisPeriod: int.MaxValue);
        }

        // ── Global budget ────────────────────────────────────────────────────
        if (global.MonthlyBudgetUsd > 0
            && global.HardKillPct > 0
            && global.CurrentSpendUsd >= global.MonthlyBudgetUsd * (global.HardKillPct / 100m))
        {
            return new AiQuotaDecision(
                Allowed: false,
                ErrorCode: "global_budget_exhausted",
                ErrorMessage: "Platform AI budget has been reached. Try again after the next budget cycle.",
                PolicyTrace: "global_budget.hard_kill",
                GlobalPolicy: global, Plan: null, Override: null,
                TokensUsedThisPeriod: 0, TokensCapThisPeriod: 0);
        }

        // ── Per-user override: admin-disabled ────────────────────────────────
        var userOverride = await db.AiUserQuotaOverrides.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);

        if (userOverride is { AiDisabled: true })
        {
            return new AiQuotaDecision(
                Allowed: false,
                ErrorCode: "user_disabled",
                ErrorMessage: userOverride.Reason ?? "AI access has been disabled for this account.",
                PolicyTrace: "override.user_disabled",
                GlobalPolicy: global, Plan: null, Override: userOverride,
                TokensUsedThisPeriod: 0, TokensCapThisPeriod: 0);
        }

        // ── Resolve plan ─────────────────────────────────────────────────────
        var plan = await ResolvePlanAsync(userId, userOverride, ct);
        if (plan is null)
        {
            // No plan configured means quota cannot be enforced. Fail closed.
            return new AiQuotaDecision(
                Allowed: false,
                ErrorCode: "no_plan",
                ErrorMessage: "No AI quota plan is configured for this account.",
                PolicyTrace: "plan.missing",
                GlobalPolicy: global, Plan: null, Override: userOverride,
                TokensUsedThisPeriod: 0, TokensCapThisPeriod: 0);
        }

        // ── Feature allow-list ───────────────────────────────────────────────
        if (!FeatureAllowedByPlan(plan, featureCode))
        {
            return new AiQuotaDecision(
                Allowed: false,
                ErrorCode: "feature_not_in_plan",
                ErrorMessage: "Your plan does not include this AI feature.",
                PolicyTrace: $"plan.feature_gate.{featureCode}",
                GlobalPolicy: global, Plan: plan, Override: userOverride,
                TokensUsedThisPeriod: 0, TokensCapThisPeriod: 0);
        }

        // ── Period counter check ─────────────────────────────────────────────
        var now = DateTimeOffset.UtcNow;
        var monthKey = $"month:{now:yyyy-MM}";
        var dayKey = $"day:{now:yyyy-MM-dd}";

        var monthCounter = await db.AiQuotaCounters.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.PeriodKey == monthKey, ct);
        var dayCounter = await db.AiQuotaCounters.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.PeriodKey == dayKey, ct);

        var monthlyCap = userOverride?.MonthlyTokenCapOverride ?? plan.MonthlyTokenCap;
        var dailyCap = userOverride?.DailyTokenCapOverride ?? plan.DailyTokenCap;

        var monthlyUsed = monthCounter?.TokensUsed ?? 0;
        var dailyUsed = dayCounter?.TokensUsed ?? 0;

        // Monthly cap (primary)
        if (monthlyCap > 0 && monthlyUsed >= monthlyCap)
        {
            return ApplyOveragePolicy(plan, global, userOverride,
                scope: "monthly", used: monthlyUsed, cap: monthlyCap);
        }

        // Daily safety cap
        if (dailyCap > 0 && dailyUsed >= dailyCap)
        {
            return ApplyOveragePolicy(plan, global, userOverride,
                scope: "daily", used: dailyUsed, cap: dailyCap);
        }

        return new AiQuotaDecision(
            Allowed: true,
            ErrorCode: null, ErrorMessage: null,
            PolicyTrace: $"plan.{plan.Code}.ok",
            GlobalPolicy: global, Plan: plan, Override: userOverride,
            TokensUsedThisPeriod: monthlyUsed,
            TokensCapThisPeriod: monthlyCap);
    }

    public async Task CommitAsync(
        string? userId,
        string featureCode,
        int promptTokens,
        int completionTokens,
        decimal costEstimateUsd,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        var total = promptTokens + completionTokens;
        if (total <= 0 && costEstimateUsd <= 0) return;
        var now = DateTimeOffset.UtcNow;

        // Upsert month + day counters. Retries on concurrency exceptions with
        // a small bounded loop — expected contention is one call per user per
        // ~second, so this keeps simple semantics without a dedicated queue.
        foreach (var periodKey in new[] { $"month:{now:yyyy-MM}", $"day:{now:yyyy-MM-dd}" })
        {
            const int maxAttempts = 4;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var existing = await db.AiQuotaCounters
                        .FirstOrDefaultAsync(x => x.UserId == userId && x.PeriodKey == periodKey, ct);
                    if (existing is null)
                    {
                        db.AiQuotaCounters.Add(new AiQuotaCounter
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            UserId = userId,
                            PeriodKey = periodKey,
                            TokensUsed = total,
                            RequestsCount = 1,
                            CostAccumulatedUsd = costEstimateUsd,
                            LastUpdatedAt = now,
                            RowVersion = 1,
                        });
                    }
                    else
                    {
                        existing.TokensUsed += total;
                        existing.RequestsCount += 1;
                        existing.CostAccumulatedUsd += costEstimateUsd;
                        existing.LastUpdatedAt = now;
                        existing.RowVersion += 1;
                    }
                    await db.SaveChangesAsync(ct);
                    break;
                }
                catch (DbUpdateConcurrencyException) when (attempt < maxAttempts)
                {
                    // Another concurrent call beat us — detach and retry.
                    foreach (var entry in db.ChangeTracker.Entries<AiQuotaCounter>().ToList())
                    {
                        entry.State = EntityState.Detached;
                    }
                    await Task.Delay(20 * attempt, ct);
                }
                catch (DbUpdateException) when (attempt < maxAttempts)
                {
                    // Unique-index collision from a concurrent insert — retry.
                    foreach (var entry in db.ChangeTracker.Entries<AiQuotaCounter>().ToList())
                    {
                        entry.State = EntityState.Detached;
                    }
                    await Task.Delay(20 * attempt, ct);
                }
            }
        }

        // Global spend accumulation is best-effort and lock-free. Slight
        // under-count under contention is acceptable; the hard-kill
        // threshold is policy-driven, not cent-precise.
        try
        {
            var globalTracked = await db.AiGlobalPolicies.FirstOrDefaultAsync(ct);
            if (globalTracked is not null && costEstimateUsd > 0)
            {
                globalTracked.CurrentSpendUsd += costEstimateUsd;
                globalTracked.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
                cache.Remove(GlobalPolicyCacheKey);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Global spend update failed; counter still accurate.");
        }
    }

    public async Task<AiUserPolicySnapshot> GetUserPolicyAsync(string userId, CancellationToken ct)
    {
        var global = await GetGlobalPolicyAsync(ct);
        var userOverride = await db.AiUserQuotaOverrides.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);
        var plan = await ResolvePlanAsync(userId, userOverride, ct)
            ?? new AiQuotaPlan { Code = DefaultPlanCode, Name = "Default", MonthlyTokenCap = 0 };

        var now = DateTimeOffset.UtcNow;
        var monthKey = $"month:{now:yyyy-MM}";
        var dayKey = $"day:{now:yyyy-MM-dd}";
        var month = await db.AiQuotaCounters.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.PeriodKey == monthKey, ct);
        var day = await db.AiQuotaCounters.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.PeriodKey == dayKey, ct);

        return new AiUserPolicySnapshot(
            PlanCode: plan.Code,
            PlanName: plan.Name,
            MonthlyTokenCap: userOverride?.MonthlyTokenCapOverride ?? plan.MonthlyTokenCap,
            DailyTokenCap: userOverride?.DailyTokenCapOverride ?? plan.DailyTokenCap,
            TokensUsedThisMonth: month?.TokensUsed ?? 0,
            TokensUsedToday: day?.TokensUsed ?? 0,
            RequestsThisMonth: month?.RequestsCount ?? 0,
            CostEstimateUsdThisMonth: month?.CostAccumulatedUsd ?? 0m,
            OveragePolicy: plan.OveragePolicy,
            AiDisabled: userOverride?.AiDisabled ?? false,
            KillSwitchActive: global.KillSwitchEnabled,
            KillSwitchScope: global.KillSwitchScope);
    }

    private const string GlobalPolicyCacheKey = "ai:global-policy";

    public async Task<AiGlobalPolicy> GetGlobalPolicyAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(GlobalPolicyCacheKey, out AiGlobalPolicy? cached) && cached is not null)
        {
            return cached;
        }

        var row = await db.AiGlobalPolicies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == "global", ct);
        if (row is null)
        {
            // Bootstrap a fresh default row. Safe: it's all-zeros defaults
            // with kill switch off.
            row = new AiGlobalPolicy { Id = "global", UpdatedAt = DateTimeOffset.UtcNow };
            db.AiGlobalPolicies.Add(row);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Another startup raced us. Re-fetch.
                row = await db.AiGlobalPolicies.AsNoTracking().FirstAsync(x => x.Id == "global", ct);
            }
        }

        cache.Set(GlobalPolicyCacheKey, row, GlobalPolicyCacheTtl);
        return row;
    }

    private async Task<AiQuotaPlan?> ResolvePlanAsync(
        string userId,
        AiUserQuotaOverride? userOverride,
        CancellationToken ct)
    {
        // Precedence: admin force → eligible direct subscription → default
        if (!string.IsNullOrWhiteSpace(userOverride?.ForcePlanCode))
        {
            return await db.AiQuotaPlans.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Code == userOverride.ForcePlanCode && p.IsActive, ct);
        }

        // Derive from the user's current direct subscription entitlement if present.
        // The billing plan code is expected to match the AI plan code by convention.
        var entitlement = await entitlementResolver.ResolveAsync(userId, LearnerEntitlementResources.AiQuota, ct);
        var activeSub = entitlement.DirectSubscription;
        if (activeSub is not null && !string.IsNullOrWhiteSpace(activeSub.PlanId))
        {
            var billingPlan = await db.BillingPlans.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == activeSub.PlanId, ct);
            if (billingPlan is not null && !string.IsNullOrWhiteSpace(billingPlan.Code))
            {
                var match = await db.AiQuotaPlans.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Code == billingPlan.Code && p.IsActive, ct);
                if (match is not null) return match;
            }
        }

        return await db.AiQuotaPlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == DefaultPlanCode && p.IsActive, ct);
    }

    private static bool FeatureAllowedByPlan(AiQuotaPlan plan, string featureCode)
    {
        if (string.IsNullOrWhiteSpace(plan.AllowedFeaturesCsv)) return true;
        var allowed = plan.AllowedFeaturesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return allowed.Any(a => string.Equals(a, featureCode, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFeatureCodeInCsv(string csv, string featureCode)
    {
        if (string.IsNullOrWhiteSpace(csv)) return false;
        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Any(p => string.Equals(p, featureCode, StringComparison.OrdinalIgnoreCase));
    }

    private static AiQuotaDecision ApplyOveragePolicy(
        AiQuotaPlan plan,
        AiGlobalPolicy global,
        AiUserQuotaOverride? userOverride,
        string scope,
        int used,
        int cap)
    {
        switch (plan.OveragePolicy)
        {
            case AiOveragePolicy.Deny:
                return new AiQuotaDecision(
                    Allowed: false,
                    ErrorCode: "quota_exhausted",
                    ErrorMessage: $"{(scope == "daily" ? "Daily" : "Monthly")} AI credits exhausted on plan {plan.Code}.",
                    PolicyTrace: $"plan.{plan.Code}.{scope}.deny",
                    GlobalPolicy: global, Plan: plan, Override: userOverride,
                    TokensUsedThisPeriod: used, TokensCapThisPeriod: cap);

            case AiOveragePolicy.AllowWithCharge:
                // Pre-consent wiring lands in Slice 6. Until then this
                // degrades to deny with a distinct error code so the UI can
                // surface the upsell path separately from hard denial.
                return new AiQuotaDecision(
                    Allowed: false,
                    ErrorCode: "overage_consent_required",
                    ErrorMessage: "Quota exceeded. Upgrade or top up to continue.",
                    PolicyTrace: $"plan.{plan.Code}.{scope}.allow_with_charge.not_yet_consented",
                    GlobalPolicy: global, Plan: plan, Override: userOverride,
                    TokensUsedThisPeriod: used, TokensCapThisPeriod: cap);

            case AiOveragePolicy.AutoUpgrade:
                // Auto-upgrade workflow requires billing webhook hand-off
                // (Slice 6). Until then, same safe fallback.
                return new AiQuotaDecision(
                    Allowed: false,
                    ErrorCode: "auto_upgrade_pending",
                    ErrorMessage: "Quota exceeded. Auto-upgrade is pending — try again shortly.",
                    PolicyTrace: $"plan.{plan.Code}.{scope}.auto_upgrade.pending",
                    GlobalPolicy: global, Plan: plan, Override: userOverride,
                    TokensUsedThisPeriod: used, TokensCapThisPeriod: cap);

            case AiOveragePolicy.DegradeToSmallerModel:
                // Degrade path is a gateway-level decision carried in the
                // policy trace; the caller switches model when it sees this.
                return new AiQuotaDecision(
                    Allowed: true,
                    ErrorCode: null, ErrorMessage: null,
                    PolicyTrace: $"plan.{plan.Code}.{scope}.degrade.{plan.DegradeModel ?? "(unset)"}",
                    GlobalPolicy: global, Plan: plan, Override: userOverride,
                    TokensUsedThisPeriod: used, TokensCapThisPeriod: cap);

            default:
                return new AiQuotaDecision(
                    Allowed: false,
                    ErrorCode: "quota_exhausted",
                    ErrorMessage: "Quota exceeded.",
                    PolicyTrace: $"plan.{plan.Code}.{scope}.unknown_overage",
                    GlobalPolicy: global, Plan: plan, Override: userOverride,
                    TokensUsedThisPeriod: used, TokensCapThisPeriod: cap);
        }
    }
}

/// <summary>
/// Raised by the gateway when quota enforcement denies a call. Feature code
/// should map this to a user-friendly response (402 Payment Required / 429
/// Too Many Requests depending on the error code).
/// </summary>
public sealed class AiQuotaDeniedException(string errorCode, string message) : InvalidOperationException(message)
{
    public string ErrorCode { get; } = errorCode;
}
