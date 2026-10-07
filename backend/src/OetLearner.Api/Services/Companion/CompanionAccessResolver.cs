using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services.Companion;

/// <summary>
/// THE AI Learning Companion access gate (owner directive: companion access is a
/// deliberate commercial/admin act, never an accident of an old plan). One
/// implementation for every surface that has to answer "may this learner chat to
/// Sami, and why": <c>GET /v1/companion/session</c>, the per-user operator read on
/// <c>/v1/admin/companion/access/users/{userId}</c>, and the chat turn itself
/// (<see cref="AiAssistant.AiAssistantOrchestrator"/>) — so the hub can never
/// serve a turn the session endpoint would deny.
/// </summary>
public interface ICompanionAccessResolver
{
    /// <summary>
    /// Resolves the decision for one learner. The platform master flag
    /// (<c>ai_learning_companion</c>) is deliberately NOT part of this decision:
    /// callers apply it, exactly as the session endpoint always has, so turning the
    /// flag off cannot be confused with an entitlement denial.
    /// </summary>
    Task<CompanionAccessDecision> ResolveAsync(string userId, CancellationToken ct);
}

/// <summary>
/// One resolved companion decision.
///
/// <para>
/// <see cref="Reason"/> is the coarse block/allow code the learner surface already
/// consumes (<c>package_required</c>, <c>kill_switch</c>, <c>ok</c>, …) — unchanged
/// for every pre-existing path, plus <c>manually_disabled</c> and <c>expired</c> for
/// the two per-user denials. <see cref="Source"/> is the provenance the SAMI §9
/// per-user layer adds (see <see cref="CompanionAccessSources"/>); it answers "why
/// this learner is in this state" even when <see cref="Reason"/> reports a platform
/// block such as a kill switch. It is null only when the caller never evaluated the
/// gate (the master flag is off).
/// </para>
/// </summary>
public sealed record CompanionAccessDecision(
    bool CanChat,
    string Reason,
    string? Source,
    string? PlanCode,
    string? PlanName);

/// <summary>
/// Resolves companion access from, in order: the per-USER override (this layer),
/// the per-PLAN override, the plan's catalog module list, then the AI quota policy
/// (account disable, kill switch, plan feature allow-list). Mirrors what
/// <c>AiQuotaService.TryReserveAsync</c> would decide without reserving anything —
/// it is a display/gating query and must not consume allowance. Token caps are
/// deliberately not part of it (SAMI §1.2/§9: Sami chat is included, not metered).
/// </summary>
public sealed class CompanionAccessResolver(
    LearnerDbContext db,
    IEffectiveEntitlementResolver entitlements,
    IAiQuotaService quota,
    ILogger<CompanionAccessResolver> logger) : ICompanionAccessResolver
{
    /// <summary>
    /// Feature codes whose written rows count as companion memory, and which the
    /// quota plan's allow-list must contain for the companion to be usable. A note
    /// the learner wrote themselves, or one authored by a different AI feature (the
    /// Writing coach, say), is not the companion's.
    /// </summary>
    internal static readonly string[] CompanionFeatureCodes =
    [
        AiFeatureCodes.AiAssistantLearner,
        AiFeatureCodes.CompanionChat,
        AiFeatureCodes.CompanionAction,
    ];

    public async Task<CompanionAccessDecision> ResolveAsync(string userId, CancellationToken ct)
    {
        // ── Plan-wide layer (unchanged) ──────────────────────────────────────
        // AiCompanion is an opt-in module, so a plan that never granted it grants
        // nothing. Grants come from two durable sources: the plan's catalog module
        // list (snapshot) and admin-written PlanModuleOverride rows. Overrides are
        // consulted here — the only AiCompanion enforcement point — and NOT baked
        // into plan rows, because the catalog seeder rewrites
        // DashboardModulesJson from the manifest on every boot and would silently
        // wipe UI-made grants.
        var snapshot = await entitlements.ResolveAsync(userId, ct);
        var planOverride = await ResolveCompanionPlanOverrideAsync(db, snapshot.PlanCode, ct);
        var packageGrants = planOverride is not false
            && (planOverride is true || snapshot.IsModuleEnabled(ModuleKeys.AiCompanion));

        // ── Per-user layer (SAMI §9) ─────────────────────────────────────────
        var userOverride = await TryReadUserOverrideAsync(userId, ct);

        // An operator's DISABLE wins over every grant below it, including a
        // plan-wide override and the catalog manifest. Checked first for the same
        // reason the per-user module deny-set is checked first in the snapshot
        // resolver: it is the most specific act, and it must not be swallowed by a
        // broader source that happens to grant the module.
        if (userOverride is { Enabled: false })
        {
            return new CompanionAccessDecision(
                false,
                CompanionAccessReasons.ManuallyDisabled,
                CompanionAccessSources.ManuallyDisabled,
                snapshot.PlanCode,
                null);
        }

        // A grant kind is decided by the row, never by the caller: an expired
        // promotional grant stops granting, but it never revokes access the package
        // grants on its own (see the source branches below).
        var enabledRow = userOverride is { Enabled: true } ? userOverride : null;
        var now = DateTimeOffset.UtcNow;
        var overrideLapsed = enabledRow is not null
            && enabledRow.ExpiresAt is { } expiresAt
            && expiresAt <= now;
        var overrideGrants = enabledRow is not null && !overrideLapsed;

        string source;
        if (overrideGrants)
        {
            source = CompanionAccessSources.NormalizeGrantSource(enabledRow!.Source);
        }
        else if (packageGrants)
        {
            // Covers two cases with one honest label: no per-user row at all, and a
            // lapsed per-user grant — expiry can only stop granting, it never revokes
            // what the package grants. From the learner's point of view the package
            // includes Sami; the operator surface still shows the raw row and its
            // expiry, so nothing is hidden from the person who set it.
            source = CompanionAccessSources.PackageIncluded;
        }
        else
        {
            source = overrideLapsed ? CompanionAccessSources.Expired : CompanionAccessSources.None;
        }

        if (!packageGrants && !overrideGrants)
        {
            // "expired" is a distinct answer from "nothing ever granted it": the
            // first is a lapsed comp/trial the operator can renew, the second means
            // the learner needs a package that includes the companion.
            var reason = overrideLapsed
                ? CompanionAccessReasons.Expired
                : CompanionAccessReasons.PackageRequired;
            return new CompanionAccessDecision(false, reason, source, snapshot.PlanCode, null);
        }

        // ── Policy layer (unchanged, and never bypassed by an admin grant) ────
        // An admin grant replaces ONLY the package/module step above. The account
        // disable, the kill switch and the quota plan's feature allow-list below all
        // still apply, in the same order as before.
        AiUserPolicySnapshot policy;
        try
        {
            policy = await quota.GetUserPolicyAsync(userId, ct);
        }
        catch (Exception)
        {
            // Fail closed on an unreadable policy: better a paywall the learner
            // can question than a chat box that errors on every message.
            return new CompanionAccessDecision(false, CompanionAccessReasons.PolicyUnavailable, source, null, null);
        }

        if (policy.AiDisabled)
        {
            return new CompanionAccessDecision(false, CompanionAccessReasons.AiDisabled, source, policy.PlanCode, policy.PlanName);
        }

        // Either scope blocks the companion: its feature codes are in
        // AiCredentialResolver.PlatformOnlyFeatures, so it is never BYOK-funded
        // and PlatformKeysOnly stops it just as surely as AllCalls.
        if (policy.KillSwitchActive)
        {
            return new CompanionAccessDecision(false, CompanionAccessReasons.KillSwitch, source, policy.PlanCode, policy.PlanName);
        }

        // An empty allow-list means "every feature"; a populated one is exhaustive.
        var allowedCsv = await db.AiQuotaPlans
            .AsNoTracking()
            .Where(p => p.Code == policy.PlanCode && p.IsActive)
            .Select(p => p.AllowedFeaturesCsv)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrWhiteSpace(allowedCsv))
        {
            var allowed = allowedCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!CompanionFeatureCodes.Any(allowed.Contains))
            {
                return new CompanionAccessDecision(false, CompanionAccessReasons.PlanExcludesCompanion, source, policy.PlanCode, policy.PlanName);
            }
        }

        // SAMI §1.2 / §9 (commit 7988b7596): Sami chat is included with an eligible
        // package, NOT metered. Launch must not depend on a candidate-facing
        // AI-credit or multi-tier wallet, so the monthly/daily token caps do not
        // block or stagger this path, and AiAssistantGateway skips the matching
        // reserve for these feature codes. Reporting a cap here would show a paywall
        // the learner is no longer actually stopped by — the live UAT defect this
        // replaced ("Daily AI credits exhausted on plan pro" after four ordinary
        // turns on an eligible account).
        //
        // Cost control is unchanged and stays invisible: the per-feature kill list
        // and the global emergency kill switch are checked above, per-user rate
        // limiting still applies to the endpoints and the hub, and every call still
        // writes its AiUsageRecord. CompanionAccessReasons.MonthlyCapReached /
        // DailyCapReached stay defined (other callers and the client union reference
        // them) but are deliberately not produced on this path.

        return new CompanionAccessDecision(true, CompanionAccessReasons.Ok, source, policy.PlanCode, policy.PlanName);
    }

    /// <summary>
    /// Admin-written per-plan companion grant for the snapshot's plan code:
    /// true = granted, false = explicitly revoked, null = no override row.
    /// Matched case-insensitively (snapshot codes are normalized lowercase).
    /// The whole table is read because it stays tiny (one row per plan at
    /// most) — no translation-sensitive predicate, works on every provider.
    /// </summary>
    private static async Task<bool?> ResolveCompanionPlanOverrideAsync(
        LearnerDbContext db,
        string? snapshotPlanCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(snapshotPlanCode)) return null;
        var rows = await db.PlanModuleOverrides
            .AsNoTracking()
            .Where(o => o.ModuleKey == ModuleKeys.AiCompanion)
            .Select(o => new { o.PlanCode, o.Enabled })
            .ToListAsync(ct);
        return rows
            .FirstOrDefault(o => string.Equals(o.PlanCode, snapshotPlanCode, StringComparison.OrdinalIgnoreCase))
            ?.Enabled;
    }

    /// <summary>
    /// The learner's per-user override row, or null when there is none.
    ///
    /// <para>
    /// FAIL-SAFE DIRECTION ON AN UNREADABLE OVERRIDE — deliberate, and the reason
    /// this read is isolated in its own try/catch instead of being allowed to take
    /// the whole gate down: a technical failure here is treated as <b>"no per-user
    /// override"</b>, so the decision falls back to the package rule.
    /// </para>
    ///
    /// <list type="bullet">
    ///   <item>It cannot WRONGLY REMOVE access: an unreadable row is never read as
    ///   <c>Enabled = false</c>, so a learner whose package already grants the
    ///   companion keeps it. Reading it as a disable would revoke paid access for
    ///   every learner whose row we merely failed to read — and the realistic cause
    ///   of this failure (the table not existing yet during the rolling deploy that
    ///   introduces it, or a transient read error) has nothing to do with that
    ///   learner's entitlement.</item>
    ///   <item>It does not WRONGLY GRANT: an unreadable row is never read as
    ///   <c>Enabled = true</c> either, so the per-user layer can never be the thing
    ///   that lets somebody in on data we could not actually read. Access granted
    ///   here is access the package rule granted on its own.</item>
    /// </list>
    ///
    /// <para>
    /// Residual, accepted risk (stated plainly rather than hidden): while the table
    /// is unreadable, a real <c>Enabled = false</c> row is not honoured for the
    /// duration of the fault. That is the narrower harm of the two, because the
    /// alternative fails in the direction that removes access from paying learners;
    /// the fault is logged at Warning so it is visible, and every other control —
    /// account AI disable, kill switch, quota plan feature allow-list, the master
    /// feature flag — is untouched and still fails closed.
    /// </para>
    /// </summary>
    private async Task<UserOverrideRow?> TryReadUserOverrideAsync(string userId, CancellationToken ct)
    {
        try
        {
            var row = await db.CompanionUserAccesses
                .AsNoTracking()
                .Where(o => o.UserId == userId && o.ModuleKey == ModuleKeys.AiCompanion)
                .Select(o => new { o.Enabled, o.Source, o.ExpiresAt })
                .FirstOrDefaultAsync(ct);

            return row is null
                ? null
                : new UserOverrideRow(row.Enabled, row.Source, row.ExpiresAt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Per-user companion access override could not be read for {UserId}; falling back to the package rule (the override is ignored in BOTH directions for this request).",
                userId);
            return null;
        }
    }

    /// <summary>Projection of the override row the gate needs — never the whole entity.</summary>
    private sealed record UserOverrideRow(bool Enabled, string? Source, DateTimeOffset? ExpiresAt);
}
