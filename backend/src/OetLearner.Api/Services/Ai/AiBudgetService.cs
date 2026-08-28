using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// W3 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// the platform-wide dollar spending ceiling, enforced with genuine DB-level
/// atomicity so N concurrent calls can never collectively bypass it.
///
/// <para>
/// <b>Why this exists.</b> <see cref="AiManagement.AiQuotaService"/>'s global
/// budget branch used to be a plain read of <see cref="AiGlobalPolicy.CurrentSpendUsd"/>
/// followed by a separate, later, lock-free increment in
/// <c>CommitAsync</c> — a classic TOCTOU: many concurrent requests could all
/// read "under the limit" before any of them committed spend. This service
/// replaces that check with a single atomic
/// <c>UPDATE ... WHERE ReservedUsd + CommittedUsd + @amount &lt;= @limit</c>
/// (via EF Core's <c>ExecuteUpdateAsync</c>) against <see cref="AiBudgetPeriod"/>
/// — the check and the increment are the SAME SQL statement, so there is no
/// window in which two callers can both believe they are under budget.
/// </para>
///
/// <para>
/// <b>Reserve → commit/release, exactly like <see cref="AiCreditReservation"/>
/// is designed to.</b> <see cref="ReserveAsync"/> holds a conservative estimate
/// BEFORE any provider call; <see cref="CommitAsync"/> trues it up to the real
/// cost once known; <see cref="ReleaseAsync"/> gives the held amount back when
/// the call never happened (denied, duplicate, failed before send). A
/// reservation that is never committed or released simply over-counts
/// <see cref="AiBudgetPeriod.ReservedUsd"/> until the next month's period rolls
/// over — acceptable because both call sites that create a reservation
/// (<see cref="Rulebook.AiGatewayService"/> and <see cref="DirectAiCallRecorder"/>)
/// always reach a commit-or-release on every exit path, mirroring how they
/// already guarantee an <see cref="AiOperation"/> always reaches a terminal
/// state.
/// </para>
///
/// <para>
/// <b>Fail-closed on "no budget configured".</b> Before this wave, an unset or
/// zero <see cref="AiGlobalPolicy.MonthlyBudgetUsd"/> meant "unlimited" — the
/// opposite of the owner's conservative-by-default launch posture. This
/// service treats a non-positive effective limit as "budget exhausted": zero
/// provider calls, not zero enforcement. See
/// <see cref="AiManagement.AiQuotaService.ConservativeDefaultMonthlyBudgetUsd"/>
/// for the seeded default that keeps a fresh database out of that state.
/// </para>
///
/// <para>
/// <b>Scope.</b> Only <c>"global"</c> is used today (one platform-wide monthly
/// ceiling). <see cref="AiBudgetPeriod.Scope"/> is a free-form string so a
/// later wave can add per-user/per-tenant/per-feature scopes without a schema
/// change — see the class doc comment on <see cref="AiBudgetPeriod"/>.
/// </para>
/// </summary>
public interface IAiBudgetService
{
    /// <summary>
    /// Atomically reserves <paramref name="estimatedUsd"/> against the current
    /// monthly spend ceiling for <paramref name="scope"/>. The caller MUST make
    /// zero provider calls when <see cref="AiBudgetReservation.Granted"/> is
    /// false, and MUST eventually call exactly one of
    /// <see cref="CommitAsync"/>/<see cref="ReleaseAsync"/> on every granted
    /// reservation.
    /// </summary>
    Task<AiBudgetReservation> ReserveAsync(string scope, decimal estimatedUsd, CancellationToken ct);

    /// <summary>Converts a granted reservation into real, durable spend. Safe to
    /// call with <paramref name="actualUsd"/> == 0. A no-op for an unmetered or
    /// denied reservation.</summary>
    Task CommitAsync(AiBudgetReservation reservation, decimal actualUsd, CancellationToken ct);

    /// <summary>Gives back a granted reservation that never turned into real
    /// spend (denied downstream, duplicate, or the call never happened). A
    /// no-op for an unmetered or denied reservation. Idempotent.</summary>
    Task ReleaseAsync(AiBudgetReservation reservation, CancellationToken ct);
}

/// <summary>
/// Outcome of <see cref="IAiBudgetService.ReserveAsync"/>. Carries only the
/// identifiers <see cref="CommitAsync"/>/<see cref="ReleaseAsync"/> need — never
/// a live DB handle — so it can be passed across the async gap between
/// reserving and knowing the outcome of the provider call.
/// </summary>
public sealed record AiBudgetReservation(
    bool Granted,
    string? DenyReason,
    string? PeriodId,
    string? Scope,
    decimal ReservedUsd)
{
    /// <summary>No platform budget applies to this call (BYOK, or the service is
    /// not wired). Always safe to pass to Commit/Release as a no-op.</summary>
    public static readonly AiBudgetReservation Unmetered = new(true, null, null, null, 0m);

    public static AiBudgetReservation Denied(string reason) => new(false, reason, null, null, 0m);
}

/// <summary>Thrown by the gateway when the platform-wide AI budget ceiling has
/// been reached. Mirrors <see cref="AiManagement.AiQuotaDeniedException"/> —
/// feature code should map this to a 402/429-style response. Raised strictly
/// BEFORE any provider call, so zero cost was incurred for the refused
/// request.</summary>
public sealed class AiBudgetExhaustedException(string reason)
    : InvalidOperationException("Platform AI budget has been reached. Try again after the next budget cycle.")
{
    public string Reason { get; } = reason;
}

public sealed class AiBudgetService(
    IServiceScopeFactory scopeFactory,
    ILogger<AiBudgetService> logger) : IAiBudgetService
{
    /// <summary>
    /// Conservative flat estimate reserved per call before the real cost is
    /// known. Sized comfortably above a typical single Claude call at the
    /// seeded rate card (CoreAiProviderSeeder: $0.003/1k prompt + $0.015/1k
    /// completion — a generous 4k-completion-token call is ≈$0.06) so
    /// legitimate concurrent load is not falsely denied while still bounding
    /// worst-case in-flight over-reservation to N-concurrent × this amount.
    /// <see cref="CommitAsync"/> trues this up to the real cost immediately
    /// after the call, so the ceiling is never under-enforced for more than the
    /// handful of calls genuinely in flight at once.
    /// </summary>
    public const decimal DefaultReservationEstimateUsd = 0.10m;

    public async Task<AiBudgetReservation> ReserveAsync(string scope, decimal estimatedUsd, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        var amount = Math.Max(0m, estimatedUsd);

        try
        {
            await using var dbScope = scopeFactory.CreateAsyncScope();
            var db = dbScope.ServiceProvider.GetRequiredService<LearnerDbContext>();

            var global = await db.AiGlobalPolicies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == "global", ct);
            var limitUsd = ResolveEffectiveLimit(global);
            if (limitUsd <= 0m)
            {
                logger.LogWarning(
                    "AI platform budget is unconfigured or zero for scope {Scope}; refusing the call (zero provider calls made). Set AiGlobalPolicy.MonthlyBudgetUsd on /admin/ai-usage.",
                    scope);
                return AiBudgetReservation.Denied("global_budget_not_configured");
            }

            var periodKey = $"month:{DateTimeOffset.UtcNow:yyyy-MM}";
            var periodId = $"{scope}:{periodKey}";

            // Two attempts: the first caller creates the period row with
            // INSERT … ON CONFLICT DO NOTHING; losers of that race used to
            // poison their EF context via SaveChanges (aborted Postgres
            // transaction) and then deny as budget_store_unavailable — which
            // under-granted a $100 ceiling in CI. Raw UPSERT + one retry of
            // the atomic UPDATE never denies a call that still has headroom.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await EnsurePeriodRowExistsAsync(db, periodId, scope, periodKey, limitUsd, ct);

                var affected = await db.AiBudgetPeriods
                    .Where(p => p.Id == periodId && p.ReservedUsd + p.CommittedUsd + amount <= limitUsd)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(p => p.ReservedUsd, p => p.ReservedUsd + amount)
                        // Re-synced on every reserve so an admin raising the budget
                        // mid-month takes effect immediately, not just for periods
                        // created after the change.
                        .SetProperty(p => p.LimitUsd, limitUsd)
                        .SetProperty(p => p.UpdatedAt, DateTimeOffset.UtcNow), ct);

                if (affected > 0)
                {
                    return new AiBudgetReservation(true, null, periodId, scope, amount);
                }

                var rowExists = await db.AiBudgetPeriods.AsNoTracking()
                    .AnyAsync(p => p.Id == periodId, ct);
                if (rowExists)
                {
                    break;
                }
            }

            logger.LogWarning(
                "AI platform budget exhausted for scope {Scope} period {PeriodId} (limit {Limit}); refusing the call, zero provider calls made.",
                scope, periodId, limitUsd);
            return AiBudgetReservation.Denied("global_budget_exhausted");
        }
        catch (Exception ex)
        {
            // A control-plane failure BEFORE the provider call must never
            // degrade into "send anyway" — the same fail-closed rule
            // DirectAiCallRecorder.BeginOperationAsync applies to the operation
            // ledger applies here to the budget ledger.
            logger.LogError(ex, "AiBudgetService.ReserveAsync failed for scope {Scope}; refusing the call.", scope);
            return AiBudgetReservation.Denied("budget_store_unavailable");
        }
    }

    public async Task CommitAsync(AiBudgetReservation reservation, decimal actualUsd, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        if (!reservation.Granted || reservation.PeriodId is null) return;

        var actual = Math.Max(0m, actualUsd);
        var reserved = reservation.ReservedUsd;

        try
        {
            await using var dbScope = scopeFactory.CreateAsyncScope();
            var db = dbScope.ServiceProvider.GetRequiredService<LearnerDbContext>();

            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE "AiBudgetPeriods"
                SET "ReservedUsd" = GREATEST(0, "ReservedUsd" - {0}),
                    "CommittedUsd" = "CommittedUsd" + {1},
                    "UpdatedAt" = NOW()
                WHERE "Id" = {2}
                """,
                reserved, actual, reservation.PeriodId);

            // Best-effort sync onto AiGlobalPolicy.CurrentSpendUsd for the
            // existing admin dashboard. AiBudgetPeriod remains authoritative for
            // enforcement; a failure here never affects the caller or the
            // already-completed, already-billed provider call.
            if (string.Equals(reservation.Scope, "global", StringComparison.Ordinal) && actual > 0m)
            {
                await db.AiGlobalPolicies
                    .Where(p => p.Id == "global")
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(p => p.CurrentSpendUsd, p => p.CurrentSpendUsd + actual)
                        .SetProperty(p => p.UpdatedAt, DateTimeOffset.UtcNow), ct);
            }
        }
        catch (Exception ex)
        {
            // Fail-soft on commit: the provider call already happened and is
            // already durably recorded in AiUsageRecord, the true source of
            // truth for spend. A missed ledger commit must never surface to the
            // caller or retroactively "undo" a completed, billed call.
            logger.LogError(ex,
                "AiBudgetService.CommitAsync failed for period {PeriodId}; AiUsageRecord remains the authoritative spend record.",
                reservation.PeriodId);
        }
    }

    public async Task ReleaseAsync(AiBudgetReservation reservation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        if (!reservation.Granted || reservation.PeriodId is null) return;

        var reserved = reservation.ReservedUsd;

        try
        {
            await using var dbScope = scopeFactory.CreateAsyncScope();
            var db = dbScope.ServiceProvider.GetRequiredService<LearnerDbContext>();

            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE "AiBudgetPeriods"
                SET "ReservedUsd" = GREATEST(0, "ReservedUsd" - {0}),
                    "UpdatedAt" = NOW()
                WHERE "Id" = {1}
                """,
                reserved, reservation.PeriodId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "AiBudgetService.ReleaseAsync failed for period {PeriodId}; the held reservation may over-count budget headroom until the next period rollover.",
                reservation.PeriodId);
        }
    }

    private static decimal ResolveEffectiveLimit(AiGlobalPolicy? global)
    {
        if (global is null) return 0m;
        var pct = Math.Clamp(global.HardKillPct, 0, 150);
        return Math.Max(0m, global.MonthlyBudgetUsd) * pct / 100m;
    }

    /// <summary>
    /// Idempotent create via <c>INSERT … ON CONFLICT DO NOTHING</c>. The
    /// previous EF <c>Add</c>+<c>SaveChanges</c> path aborted the Postgres
    /// transaction for every loser of the create race, so the follow-up
    /// atomic UPDATE threw and was mis-classified as
    /// <c>budget_store_unavailable</c>.
    /// </summary>
    private static async Task EnsurePeriodRowExistsAsync(
        LearnerDbContext db, string id, string scope, string periodKey, decimal limitUsd, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "AiBudgetPeriods"
                ("Id", "Scope", "PeriodKey", "LimitUsd", "ReservedUsd", "CommittedUsd", "CreatedAt", "UpdatedAt")
            VALUES
                ({0}, {1}, {2}, {3}, 0, 0, NOW(), NOW())
            ON CONFLICT ("Id") DO NOTHING
            """,
            id, scope, periodKey, limitUsd);
    }
}
