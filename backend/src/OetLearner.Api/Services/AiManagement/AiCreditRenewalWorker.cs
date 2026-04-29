using Microsoft.EntityFrameworkCore;
using Npgsql;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services.AiManagement;

/// <summary>
/// Background worker for AI credit lifecycle:
/// <list type="bullet">
///   <item>Monthly renewals: grants <c>plan.MonthlyTokenCap</c> credits to
///   every user whose <see cref="Subscription"/> is active and who has not
///   yet received their renewal this period.</item>
///   <item>Expiration sweep: zeroes out grants past their ExpiresAt.</item>
/// </list>
/// Runs hourly. Idempotent: uses <see cref="AiCreditLedgerEntry.ReferenceId"/>
/// keyed to <c>renewal:{userId}:{periodKey}</c> so re-runs don't double-grant.
/// </summary>
public sealed class AiCreditRenewalWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AiCreditRenewalWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private const string RenewalReferenceIndexName = "UX_AiCreditLedger_PlanRenewal_ReferenceId";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a short random moment after boot so multiple instances don't
        // hammer the DB at the same second.
        await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(5, 30)), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AiCreditRenewalWorker tick failed.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task<(int renewed, int expired)> RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var credits = scope.ServiceProvider.GetRequiredService<IAiCreditService>();
        var entitlementResolver = scope.ServiceProvider.GetRequiredService<IEffectiveEntitlementResolver>();

        var now = DateTimeOffset.UtcNow;
        var periodKey = $"month:{now:yyyy-MM}";

        // ── 1. Monthly renewals ─────────────────────────────────────────────
        var userIds = await db.Subscriptions.AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active)
            .Select(s => s.UserId)
            .Distinct()
            .ToListAsync(ct);

        int renewedCount = 0;
        foreach (var userId in userIds)
        {
            var entitlement = await entitlementResolver.ResolveAsync(userId, ct);
            if (!entitlement.HasEligibleSubscription || entitlement.SubscriptionStatus != SubscriptionStatus.Active)
            {
                continue;
            }

            var aiPlan = await ResolveMappedAiPlanAsync(db, entitlement, ct);
            if (aiPlan is null || aiPlan.MonthlyTokenCap <= 0) continue;

            var referenceId = $"renewal:{userId}:{periodKey}";
            var alreadyGranted = await db.AiCreditLedger.AsNoTracking()
                .AnyAsync(x => x.Source == AiCreditSource.PlanRenewal && x.ReferenceId == referenceId, ct);
            if (alreadyGranted) continue;

            DateTimeOffset? expiresAt = aiPlan.RolloverPolicy switch
            {
                AiQuotaRolloverPolicy.Expire => EndOfMonth(now),
                AiQuotaRolloverPolicy.RolloverCapped => EndOfMonth(now).AddMonths(1),
                AiQuotaRolloverPolicy.RolloverFull => null,
                _ => EndOfMonth(now),
            };

            try
            {
                await credits.GrantAsync(
                    userId: userId,
                    tokens: aiPlan.MonthlyTokenCap,
                    costUsd: 0m,
                    source: AiCreditSource.PlanRenewal,
                    description: $"Monthly renewal for plan {aiPlan.Code}",
                    referenceId: referenceId,
                    expiresAt: expiresAt,
                    adminId: null,
                    ct: ct);
                renewedCount++;
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                db.ChangeTracker.Clear();
                logger.LogInformation(
                    "AI credit renewal {ReferenceId} was already granted by a concurrent worker.",
                    referenceId);
            }
        }

        // ── 2. Expiration sweep ─────────────────────────────────────────────
        var expiredCount = await credits.SweepExpiredAsync(now, ct);

        if (renewedCount > 0 || expiredCount > 0)
        {
            logger.LogInformation("AI credit worker: renewed={Renewed} expired={Expired}",
                renewedCount, expiredCount);
        }

        return (renewedCount, expiredCount);
    }

    private static async Task<AiQuotaPlan?> ResolveMappedAiPlanAsync(
        LearnerDbContext db,
        EffectiveEntitlementSnapshot entitlement,
        CancellationToken ct)
    {
        if (string.Equals(entitlement.AiQuotaPlanCodeSource, "explicit-invalid", StringComparison.OrdinalIgnoreCase))
        {
            return await ResolveDefaultPlanAsync(db, ct);
        }

        if (string.Equals(entitlement.AiQuotaPlanCodeSource, "explicit", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(entitlement.AiQuotaPlanCode))
        {
            var explicitPlan = await ResolveActiveAiPlanAsync(db, entitlement.AiQuotaPlanCode, ct);
            return explicitPlan ?? await ResolveDefaultPlanAsync(db, ct);
        }

        if (!string.IsNullOrWhiteSpace(entitlement.PlanCode))
        {
            var directPlan = await ResolveActiveAiPlanAsync(db, entitlement.PlanCode, ct);
            if (directPlan is not null) return directPlan;
        }

        if (string.Equals(entitlement.AiQuotaPlanCodeSource, "fallback", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(entitlement.AiQuotaPlanCode))
        {
            var fallbackPlan = await ResolveActiveAiPlanAsync(db, entitlement.AiQuotaPlanCode, ct);
            if (fallbackPlan is not null) return fallbackPlan;
        }

        return await ResolveDefaultPlanAsync(db, ct);
    }

    private static Task<AiQuotaPlan?> ResolveActiveAiPlanAsync(
        LearnerDbContext db,
        string planCode,
        CancellationToken ct)
    {
        var normalizedPlanCode = planCode.Trim().ToLowerInvariant();
        return db.AiQuotaPlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code.ToLower() == normalizedPlanCode && p.IsActive, ct);
    }

    private static Task<AiQuotaPlan?> ResolveDefaultPlanAsync(LearnerDbContext db, CancellationToken ct)
        => db.AiQuotaPlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == "free" && p.IsActive, ct);

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: RenewalReferenceIndexName,
            }
            || (exception.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ?? false);

    private static DateTimeOffset EndOfMonth(DateTimeOffset from)
    {
        var nextMonth = new DateTimeOffset(from.Year, from.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
        return nextMonth.AddTicks(-1);
    }
}
