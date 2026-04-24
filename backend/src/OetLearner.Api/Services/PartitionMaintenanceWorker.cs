using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services;

/// <summary>
/// Background sweeper that ensures <em>next-month</em> partitions exist for
/// candidate high-volume, time-ordered tables (<c>AnalyticsEvents</c>,
/// <c>AuditEvents</c>, <c>AiUsageRecords</c>). Uses the pl/pgsql helper
/// <c>public.ensure_monthly_partition</c> installed by migration
/// <c>20260424180000_AddMonthlyPartitionHelper</c>.
///
/// <para>The helper is a no-op if the parent table is not yet partitioned,
/// so this worker is safe to run even before ops convert the tables to a
/// range-partitioned layout (which requires a maintenance window).</para>
///
/// <para>Runs once at startup (+1 minute delay) and then every 24 hours.
/// Creates the current and next <c>LookaheadMonths</c> worth of partitions
/// so a rollover never blocks writes.</para>
/// </summary>
public sealed class PartitionMaintenanceWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PartitionMaintenanceWorker> logger) : BackgroundService
{
    private static readonly string[] Candidates = new[]
    {
        "public.\"AnalyticsEvents\"",
        "public.\"AuditEvents\"",
        "public.\"AiUsageRecords\"",
    };

    /// <summary>Partition columns aligned with <see cref="Candidates"/>.</summary>
    private static readonly string[] PartitionColumns = new[]
    {
        "OccurredAt",
        "OccurredAt",
        "CreatedAt",
    };

    /// <summary>How many months ahead to pre-create partitions.</summary>
    private const int LookaheadMonths = 2;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Partition maintenance sweep failed");
            }
            try { await Task.Delay(TimeSpan.FromHours(24), stoppingToken); }
            catch (TaskCanceledException) { /* shutdown */ }
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        // Only run on Postgres; no-op for SQLite test harnesses.
        if (!db.Database.IsNpgsql())
        {
            return;
        }

        // Guard: helper function might be absent in dev DBs that never ran
        // the 20260424180000 migration. Probe once.
        var hasHelper = await db.Database.SqlQueryRaw<int>(
            "SELECT 1 AS \"Value\" FROM pg_proc WHERE proname = 'ensure_monthly_partition' LIMIT 1")
            .AnyAsync(ct);
        if (!hasHelper)
        {
            return;
        }

        var today = DateTime.UtcNow.Date;
        for (var i = 0; i < Candidates.Length; i++)
        {
            var parent = Candidates[i];
            var col = PartitionColumns[i];
            for (var m = 0; m <= LookaheadMonths; m++)
            {
                var month = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(m);
                try
                {
                    // The helper takes (parent regclass, part_col text, target_month date).
                    // Using parameterised form avoids SQL injection even though inputs are constant.
                    await db.Database.ExecuteSqlRawAsync(
                        "SELECT public.ensure_monthly_partition({0}::regclass, {1}, {2}::date)",
                        new object[] { parent, col, month },
                        ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "ensure_monthly_partition failed for {Parent} {Month:yyyy-MM}", parent, month);
                }
            }
        }
    }
}
