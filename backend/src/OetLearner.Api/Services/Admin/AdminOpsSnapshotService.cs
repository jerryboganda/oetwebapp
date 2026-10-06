using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Services.Admin;

/// <summary>One row of the <c>pg_stat_activity</c> roll-up (an unmapped raw-SQL result type).</summary>
internal sealed class PgActivityRow
{
    public string ApplicationName { get; set; } = string.Empty;
    public string? State { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// The read-only load snapshot behind <c>GET /v1/admin/ops/snapshot</c> (owner programme 5 Oct 2026): background
/// job queue depth by type, database connections by <c>application_name</c>, the live-session admission gate and the
/// remote worker node count. Five short aggregate queries, no payloads, no learner data; each block
/// fails soft (a block that cannot be read is reported empty, never an error for the whole snapshot).
/// </summary>
public sealed class AdminOpsSnapshotService(
    LearnerDbContext db,
    SpeakingLiveAdmissionService admission,
    TimeProvider? clock = null,
    ILogger<AdminOpsSnapshotService>? logger = null)
{
    /// <summary>Processing for longer than this is "stuck" (the threshold <c>/health/ready</c> already uses).</summary>
    private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(10);

    private readonly TimeProvider time = clock ?? TimeProvider.System;

    public async Task<AdminOpsSnapshot> GetAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var jobs = await ReadJobsAsync(now, ct);
        var connections = await ReadConnectionsAsync(ct);

        SpeakingLiveAdmissionCounts? counts = null;
        try
        {
            counts = await admission.GetCountsAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Ops snapshot: could not read the live Speaking admission counts.");
        }

        return new AdminOpsSnapshot(
            GeneratedAt: now,
            Jobs: jobs,
            Connections: connections,
            Speaking: new AdminOpsSpeakingSnapshot(counts),
            RemoteWorkers: await ReadRemoteWorkersAsync(ct));
    }

    private async Task<AdminOpsRemoteWorkersSnapshot> ReadRemoteWorkersAsync(CancellationToken ct)
    {
        if (!db.Database.IsNpgsql())
        {
            return new AdminOpsRemoteWorkersSnapshot(
                Deployed: false,
                Nodes: 0,
                Note: "Remote workers are not deployed on this platform yet; this block is reserved for the fleet summary.");
        }

        try
        {
            // One indexed count (IX_RemoteWorkers_Status); a revoked node is gone for good, so it is not fleet capacity.
            var nodes = await db.RemoteWorkers.AsNoTracking()
                .CountAsync(w => w.Status != RemoteNodeStatus.Revoked, ct);
            return new AdminOpsRemoteWorkersSnapshot(
                Deployed: nodes > 0,
                Nodes: nodes,
                Note: nodes > 0
                    ? "Registered remote worker nodes that are not revoked, whatever their status."
                    : "No remote worker node is registered.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Ops snapshot: could not read the remote worker table.");
            return new AdminOpsRemoteWorkersSnapshot(false, 0, "The remote worker table could not be read.");
        }
    }

    private async Task<AdminOpsJobsSnapshot> ReadJobsAsync(DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            var stuckCutoff = now - StuckAfter;

            // Only jobs that matter to a queue: due and waiting, or being processed. Completed/failed history
            // (the bulk of the table) is never touched; (State, AvailableAt) is indexed.
            var groups = await db.BackgroundJobs.AsNoTracking()
                .Where(j => (j.State == AsyncState.Queued && j.AvailableAt <= now) || j.State == AsyncState.Processing)
                .GroupBy(j => new { j.Type, j.State })
                .Select(g => new
                {
                    g.Key.Type,
                    g.Key.State,
                    Count = g.Count(),
                    Oldest = g.Min(j => j.AvailableAt),
                    Stuck = g.Count(j => j.LastTransitionAt < stuckCutoff),
                })
                .ToListAsync(ct);

            var scheduled = await db.BackgroundJobs.AsNoTracking()
                .CountAsync(j => j.State == AsyncState.Queued && j.AvailableAt > now, ct);

            var rows = groups
                .GroupBy(g => g.Type)
                .Select(byType =>
                {
                    var queued = byType.FirstOrDefault(x => x.State == AsyncState.Queued);
                    var processing = byType.FirstOrDefault(x => x.State == AsyncState.Processing);
                    return new AdminOpsJobTypeRow(
                        Type: byType.Key.ToString(),
                        Queued: queued?.Count ?? 0,
                        Processing: processing?.Count ?? 0,
                        StuckProcessing: processing?.Stuck ?? 0,
                        OldestQueuedAgeSeconds: queued is null ? null : AgeSeconds(now, queued.Oldest));
                })
                .OrderByDescending(r => r.Queued)
                .ThenByDescending(r => r.Processing)
                .ThenBy(r => r.Type, StringComparer.Ordinal)
                .ToList();

            return new AdminOpsJobsSnapshot(
                TotalQueued: rows.Sum(r => r.Queued),
                TotalProcessing: rows.Sum(r => r.Processing),
                Scheduled: scheduled,
                OldestQueuedAgeSeconds: rows.Select(r => r.OldestQueuedAgeSeconds).Max(),
                ByType: rows);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Ops snapshot: could not read the background job queue.");
            return new AdminOpsJobsSnapshot(0, 0, 0, null, []);
        }
    }

    private async Task<AdminOpsConnectionsSnapshot> ReadConnectionsAsync(CancellationToken ct)
    {
        if (!db.Database.IsNpgsql())
        {
            return new AdminOpsConnectionsSnapshot(false, null, null, [], "Connection counts are read from Postgres only.");
        }

        try
        {
            var activity = await db.Database.SqlQueryRaw<PgActivityRow>(
                "SELECT COALESCE(NULLIF(application_name, ''), '(unset)') AS \"ApplicationName\", " +
                "state AS \"State\", COUNT(*)::int AS \"Count\" " +
                "FROM pg_stat_activity WHERE datname = current_database() GROUP BY 1, 2")
                .ToListAsync(ct);
            var max = await db.Database.SqlQueryRaw<int>(
                "SELECT setting::int AS \"Value\" FROM pg_settings WHERE name = 'max_connections'")
                .ToListAsync(ct);

            var rows = activity
                .OrderByDescending(r => r.Count)
                .ThenBy(r => r.ApplicationName, StringComparer.Ordinal)
                .ThenBy(r => r.State, StringComparer.Ordinal)
                .Select(r => new AdminOpsConnectionRow(r.ApplicationName, r.State, r.Count))
                .ToList();
            return new AdminOpsConnectionsSnapshot(
                Available: true,
                Total: rows.Sum(r => r.Count),
                MaxConnections: max.Count > 0 ? max[0] : null,
                ByApplicationName: rows,
                Note: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Ops snapshot: could not read pg_stat_activity.");
            return new AdminOpsConnectionsSnapshot(false, null, null, [], "pg_stat_activity could not be read.");
        }
    }

    private static int AgeSeconds(DateTimeOffset now, DateTimeOffset since)
        => (int)Math.Max(0, (now - since).TotalSeconds);
}
