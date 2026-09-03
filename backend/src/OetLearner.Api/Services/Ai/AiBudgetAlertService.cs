using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Services.Ai;

public interface IAiBudgetAlertService
{
    Task EvaluateAfterCommitAsync(
        string scope,
        string periodKey,
        decimal reservedUsd,
        decimal committedUsd,
        decimal limitUsd,
        CancellationToken ct);
}

public sealed class AiBudgetAlertService(
    IServiceScopeFactory scopeFactory,
    ILogger<AiBudgetAlertService> logger) : IAiBudgetAlertService
{
    public static readonly int[] Ladder = [50, 75, 90, 100];

    public async Task EvaluateAfterCommitAsync(
        string scope,
        string periodKey,
        decimal reservedUsd,
        decimal committedUsd,
        decimal limitUsd,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(periodKey)) return;

        try
        {
            var occupancy = reservedUsd + committedUsd;
            var pct = limitUsd <= 0m
                ? 100m
                : decimal.Round(occupancy / limitUsd * 100m, 2, MidpointRounding.AwayFromZero);

            await using var dbScope = scopeFactory.CreateAsyncScope();
            var db = dbScope.ServiceProvider.GetRequiredService<LearnerDbContext>();

            foreach (var threshold in Ladder)
            {
                if (pct < threshold) continue;

                var inserted = await InsertAlertIfNewAsync(db, scope, periodKey, threshold, ct);
                if (inserted)
                {
                    logger.LogWarning(
                        "AI budget threshold {ThresholdPct}% fired for scope {Scope} period {PeriodKey} (occupancy {Occupancy}/{Limit} = {Pct}%).",
                        threshold, scope, periodKey, occupancy, limitUsd, pct);

                    var notifications = dbScope.ServiceProvider.GetService<OetLearner.Api.Services.NotificationService>();
                    if (notifications is not null)
                    {
                        // Separate AI-budget signal from billing/invoice failures:
                        // previously this reused AdminBillingFailureAlert
                        // ("Billing failures need attention"), so an AI-budget
                        // threshold fired the billing-failure alarm even when
                        // Failed Invoices = 0 in Billing Ops.
                        await notifications.CreateForAdminsAsync(
                            NotificationEventKey.AdminAiBudgetAlert,
                            "ai_budget",
                            scope,
                            periodKey,
                            new Dictionary<string, object?>
                            {
                                ["scope"] = scope,
                                ["periodKey"] = periodKey,
                                ["thresholdPct"] = threshold,
                                ["occupancyUsd"] = occupancy,
                                ["limitUsd"] = limitUsd,
                            },
                            ct);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "AiBudgetAlertService.EvaluateAfterCommitAsync failed for scope {Scope} period {PeriodKey}; alerts are best-effort.",
                scope, periodKey);
        }
    }

    private static async Task<bool> InsertAlertIfNewAsync(
        LearnerDbContext db, string scope, string periodKey, int thresholdPct, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        var affected = await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "AiBudgetAlerts"
                ("Id", "Scope", "PeriodKey", "ThresholdPct", "FiredAt")
            VALUES
                ({0}, {1}, {2}, {3}, NOW())
            ON CONFLICT ("Scope", "PeriodKey", "ThresholdPct") DO NOTHING
            """,
            id, scope, periodKey, thresholdPct);

        // ExecuteSqlRawAsync returns -1 on some providers when row counts are
        // unavailable; treat a unique-index miss as "already fired".
        if (affected > 0) return true;
        if (affected == 0) return false;

        var exists = await db.AiBudgetAlerts.AsNoTracking()
            .AnyAsync(a => a.Scope == scope && a.PeriodKey == periodKey && a.ThresholdPct == thresholdPct, ct);
        return !exists;
    }
}
