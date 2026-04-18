using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

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

        var now = DateTimeOffset.UtcNow;
        var periodKey = $"month:{now:yyyy-MM}";

        // ── 1. Monthly renewals ─────────────────────────────────────────────
        // Join active subscriptions → billing plan → AI quota plan.
        var subs = await db.Subscriptions.AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active)
            .ToListAsync(ct);

        int renewedCount = 0;
        foreach (var sub in subs)
        {
            var billingPlan = await db.BillingPlans.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == sub.PlanId, ct);
            if (billingPlan is null) continue;

            var aiPlan = await db.AiQuotaPlans.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Code == billingPlan.Code && p.IsActive, ct);
            if (aiPlan is null || aiPlan.MonthlyTokenCap <= 0) continue;

            var referenceId = $"renewal:{sub.UserId}:{periodKey}";
            var alreadyGranted = await db.AiCreditLedger.AsNoTracking()
                .AnyAsync(x => x.ReferenceId == referenceId, ct);
            if (alreadyGranted) continue;

            DateTimeOffset? expiresAt = aiPlan.RolloverPolicy switch
            {
                AiQuotaRolloverPolicy.Expire => EndOfMonth(now),
                AiQuotaRolloverPolicy.RolloverCapped => EndOfMonth(now).AddMonths(1),
                AiQuotaRolloverPolicy.RolloverFull => null,
                _ => EndOfMonth(now),
            };

            await credits.GrantAsync(
                userId: sub.UserId,
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

        // ── 2. Expiration sweep ─────────────────────────────────────────────
        var expiredCount = await credits.SweepExpiredAsync(now, ct);

        if (renewedCount > 0 || expiredCount > 0)
        {
            logger.LogInformation("AI credit worker: renewed={Renewed} expired={Expired}",
                renewedCount, expiredCount);
        }

        return (renewedCount, expiredCount);
    }

    private static DateTimeOffset EndOfMonth(DateTimeOffset from)
    {
        var nextMonth = new DateTimeOffset(from.Year, from.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
        return nextMonth.AddTicks(-1);
    }
}
