using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>Counts of what one reaper pass did (diagnostics and tests).</summary>
public sealed record RemoteSweepResult(
    int Requeued,
    int Quarantined,
    int FellBackLocal,
    int OrphanOutputSets,
    int Purged,
    int ResultsCleared,
    int CanariesCancelled = 0);

/// <summary>
/// The statements of the reaper (OET-RWP/1 section 3.5). R1 is the ONE code path that turns an expired lease back into work: the
/// claim statement never selects a <c>Leased</c> row, so there is no double-increment of <c>Attempt</c>. Every statement is
/// state-conditional (<c>FOR UPDATE SKIP LOCKED</c> on a bounded batch), so any number of reapers (blue, green, ai-worker) may run
/// at once and an expired lease is requeued exactly once. <c>FenceToken</c> is never touched here; the next claim increments it,
/// which is what fences the previous holder out.
/// </summary>
public sealed class RemoteJobSweeper(
    LearnerDbContext db,
    IRemoteJobFlags flags,
    IFileStorage storage,
    RemoteJobsSettings settings,
    TimeProvider timeProvider,
    ILogger<RemoteJobSweeper> logger)
{
    private const string ExpirySql = """
        WITH expired AS (
            SELECT j."Id", COALESCE(w."Status" IN ('Revoked', 'Quarantined'), false) AS "NodeDown"
            FROM "RemoteJobs" j
            LEFT JOIN "RemoteWorkers" w ON w."Id" = j."LeaseOwner"
            WHERE j."State" = 'Leased' AND j."LeaseExpiresAt" < clock_timestamp()
            ORDER BY j."LeaseExpiresAt"
            LIMIT @batch
            FOR UPDATE OF j SKIP LOCKED)
        UPDATE "RemoteJobs" r SET
            "ReleaseCount" = r."ReleaseCount" + CASE WHEN e."NodeDown" AND r."ReleaseCount" < @releaseLimit THEN 1 ELSE 0 END,
            "Attempt"      = GREATEST(r."Attempt" - CASE WHEN e."NodeDown" AND r."ReleaseCount" < @releaseLimit THEN 1 ELSE 0 END, 0),
            "State"        = CASE WHEN GREATEST(r."Attempt" - CASE WHEN e."NodeDown" AND r."ReleaseCount" < @releaseLimit THEN 1 ELSE 0 END, 0) >= r."MaxAttempts"
                                  THEN 'Quarantined' ELSE 'Queued' END,
            "NextAttemptAt" = clock_timestamp() + make_interval(secs => CASE WHEN e."NodeDown" AND r."ReleaseCount" < @releaseLimit THEN 0
                                  ELSE LEAST(@backoffMax, @backoffBase * power(2, GREATEST(r."Attempt" - 1, 0))) * (1 + random() * @jitter) END),
            "FailureCode"  = 'lease_expired',
            "LastFailedNodeId" = r."LeaseOwner",
            "LeaseOwner"   = NULL,
            "LeaseExpiresAt" = NULL,
            "DeadlineAt"   = NULL,
            "ClaimNonce"   = NULL,
            "UpdatedAt"    = clock_timestamp()
        FROM expired e
        WHERE r."Id" = e."Id"
        RETURNING r."Id", r."State", r."Kind", r."LastFailedNodeId";
        """;

    private const string FallbackSql = """
        WITH due AS (
            SELECT "Id" FROM "RemoteJobs"
            WHERE "State" = 'Queued' AND "Purpose" = 'apply'
              AND (@masterOff OR ("FallbackAfter" IS NOT NULL AND "FallbackAfter" < clock_timestamp()))
            ORDER BY "CreatedAt"
            LIMIT 500
            FOR UPDATE SKIP LOCKED)
        UPDATE "RemoteJobs" r SET
            "State" = 'FallbackLocal',
            "FailureCode" = CASE WHEN @masterOff THEN 'master_off' ELSE 'queue_timeout' END,
            "UpdatedAt" = clock_timestamp()
        FROM due WHERE r."Id" = due."Id";
        """;

    /// <summary>One full pass: R1 expiry, R2 fallback, R3 orphan outputs, R4 retention.</summary>
    public async Task<RemoteSweepResult> SweepAsync(CancellationToken ct)
    {
        PgScope.RequireNpgsql(db);
        var options = settings.Current;

        var (requeued, quarantined) = await ReapExpiredAsync(options, ct);
        var fellBack = await SweepFallbackAsync(ct);
        var canaries = await SweepStaleCanariesAsync(options, ct);
        var orphans = await SweepOrphanOutputsAsync(ct);
        var (purged, cleared) = await PurgeAsync(options, ct);
        return new RemoteSweepResult(requeued, quarantined, fellBack, orphans, purged, cleared, canaries);
    }

    /// <summary>R1: expired leases become <c>Queued</c> (with backoff) or <c>Quarantined</c> (attempts exhausted).</summary>
    public async Task<(int Requeued, int Quarantined)> ReapExpiredAsync(RemoteJobsOptions options, CancellationToken ct)
    {
        var rows = await RemoteDb.QueryAsync(
            db,
            ExpirySql,
            parameters =>
            {
                parameters.AddWithValue("batch", options.ReaperBatch);
                parameters.AddWithValue("releaseLimit", options.ReleaseLimit);
                parameters.AddWithValue("backoffMax", (double)options.BackoffMaxSeconds);
                parameters.AddWithValue("backoffBase", (double)options.BackoffBaseSeconds);
                parameters.AddWithValue("jitter", options.BackoffJitterPercent / 100.0);
            },
            reader => (Id: RemoteDb.Str(reader, "Id"), State: RemoteDb.Str(reader, "State"), Kind: RemoteDb.Str(reader, "Kind"), Node: RemoteDb.StrN(reader, "LastFailedNodeId")),
            ct);

        var quarantined = rows.Where(row => row.State == RemoteJobState.Quarantined).ToList();
        if (quarantined.Count > 0)
        {
            var now = timeProvider.GetUtcNow();
            foreach (var row in quarantined)
            {
                RemoteAudit.Add(db, RemoteAudit.ReaperActor, "remote-job-reaper", "RemoteJob.Quarantined", RemoteAudit.ResourceJob, row.Id,
                    new { row.Kind, code = "lease_expired", node = row.Node }, now);
            }

            await db.SaveChangesAsync(ct);
            logger.LogWarning("Quarantined {Count} remote job(s) whose attempts were exhausted by lease expiry.", quarantined.Count);
        }

        return (rows.Count - quarantined.Count, quarantined.Count);
    }

    /// <summary>
    /// R2: queued work whose wait ran out (or ALL queued apply work when the master flag is off) becomes <c>FallbackLocal</c>, the
    /// signal that the local path owns it. Canary and shadow jobs have no local path and are never swept here.
    /// </summary>
    public async Task<int> SweepFallbackAsync(CancellationToken ct)
    {
        var masterOff = !(await flags.GetAsync(ct)).Master;
        return await RemoteDb.ExecuteAsync(
            db,
            FallbackSql,
            parameters => parameters.AddWithValue("masterOff", masterOff),
            ct);
    }

    /// <summary>
    /// R2b: a canary has no local path, so R2 never touches it. One that stays <c>Queued</c> for <c>FallbackAfterMinutes</c> (its
    /// target never claimed it, or its lease expired and the retry was never claimed) is cancelled <c>canary_timeout</c>: otherwise
    /// every later request would answer <c>409 canary_in_progress</c> and the node could never reach <c>Active</c>. The manager
    /// simply requests a new canary.
    /// </summary>
    public async Task<int> SweepStaleCanariesAsync(RemoteJobsOptions options, CancellationToken ct)
    {
        var cancelled = await RemoteDb.QueryAsync(
            db,
            """
            WITH stale AS (
                SELECT "Id" FROM "RemoteJobs"
                WHERE "State" = 'Queued' AND "Purpose" = 'canary'
                  AND "UpdatedAt" < clock_timestamp() - make_interval(mins => @minutes)
                ORDER BY "UpdatedAt"
                LIMIT 100
                FOR UPDATE SKIP LOCKED)
            UPDATE "RemoteJobs" r SET
                "State" = 'Cancelled',
                "FailureCode" = 'canary_timeout',
                "CompletedAt" = clock_timestamp(),
                "UpdatedAt" = clock_timestamp()
            FROM stale
            WHERE r."Id" = stale."Id"
            RETURNING r."Id", r."TargetNodeId";
            """,
            parameters => parameters.AddWithValue("minutes", options.FallbackAfterMinutes),
            reader => (Id: RemoteDb.Str(reader, "Id"), Node: RemoteDb.StrN(reader, "TargetNodeId")),
            ct);

        if (cancelled.Count > 0)
        {
            var now = timeProvider.GetUtcNow();
            foreach (var row in cancelled)
            {
                RemoteAudit.Add(db, RemoteAudit.ReaperActor, "remote-job-reaper", "RemoteJob.CanaryTimeout", RemoteAudit.ResourceJob, row.Id,
                    new { code = "canary_timeout", node = row.Node }, now);
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation("Cancelled {Count} canary job(s) that no node claimed in time.", cancelled.Count);
        }

        return cancelled.Count;
    }

    /// <summary>R3: outputs of a fence that never settled, more than an hour after its lease ended, are deleted.</summary>
    public async Task<int> SweepOrphanOutputsAsync(CancellationToken ct)
    {
        var orphans = await RemoteDb.QueryAsync(
            db,
            """
            SELECT o."JobId", o."Fence"
            FROM "RemoteJobOutputs" o
            JOIN "RemoteJobs" j ON j."Id" = o."JobId"
            WHERE NOT (j."State" = 'Succeeded' AND j."SettledFence" = o."Fence")
              AND NOT (j."State" = 'Leased' AND j."FenceToken" = o."Fence")
              AND j."UpdatedAt" < clock_timestamp() - interval '1 hour'
            GROUP BY o."JobId", o."Fence"
            LIMIT 50;
            """,
            null,
            reader => (JobId: RemoteDb.Str(reader, "JobId"), Fence: RemoteDb.Long(reader, "Fence")),
            ct);

        foreach (var (jobId, fence) in orphans)
        {
            try
            {
                // The trailing slash matters: fence 2 must never match the keys of fence 20 (object stores match by raw prefix).
                await storage.DeletePrefixAsync(RemoteInputOutputService.OutputKey(jobId, fence, string.Empty), ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not delete orphan outputs of remote job {JobId} fence {Fence}.", jobId, fence);
                continue;
            }

            await RemoteDb.ExecuteAsync(
                db,
                """DELETE FROM "RemoteJobOutputs" WHERE "JobId" = @id AND "Fence" = @fence;""",
                parameters =>
                {
                    parameters.AddWithValue("id", jobId);
                    parameters.AddWithValue("fence", fence);
                },
                ct);
        }

        return orphans.Count;
    }

    /// <summary>R4: terminal rows past retention are deleted in batches; parked results (Deferred) are cleared after their own, shorter window.</summary>
    public async Task<(int Purged, int Cleared)> PurgeAsync(RemoteJobsOptions options, CancellationToken ct)
    {
        var purged = await RemoteDb.ExecuteAsync(
            db,
            """
            DELETE FROM "RemoteJobs" WHERE "Id" IN (
                SELECT "Id" FROM "RemoteJobs"
                WHERE "State" IN ('Succeeded', 'Failed', 'Quarantined', 'FallbackLocal', 'Cancelled')
                  AND "UpdatedAt" < clock_timestamp() - make_interval(days => @days)
                ORDER BY "UpdatedAt" LIMIT 500);
            """,
            parameters => parameters.AddWithValue("days", options.JobRetentionDays),
            ct);

        var cleared = await RemoteDb.ExecuteAsync(
            db,
            """
            UPDATE "RemoteJobs" SET "ResultJson" = NULL, "UpdatedAt" = clock_timestamp()
            WHERE "ResultJson" IS NOT NULL AND "State" = 'Succeeded'
              AND COALESCE("CompletedAt", "UpdatedAt") < clock_timestamp() - make_interval(days => @days);
            """,
            parameters => parameters.AddWithValue("days", options.DeferredResultRetentionDays),
            ct);

        return (purged, cleared);
    }
}

/// <summary>
/// Hosted wrapper of the reaper. It runs in EVERY API process (blue, green and <c>ai-worker</c>): all of its statements are
/// state-conditional and skip locked rows, so concurrent reapers are safe. It never pushes to a hub and never calls an AI provider.
/// Registered only on PostgreSQL (see <c>RemoteJobsServiceCollectionExtensions</c>).
/// </summary>
public sealed class RemoteJobReaper(
    IServiceScopeFactory scopeFactory,
    RemoteJobsSettings settings,
    ILogger<RemoteJobReaper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(5, 20)), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Remote job reaper pass failed."); }

            try { await Task.Delay(TimeSpan.FromSeconds(settings.Current.ReaperIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>One pass in a fresh scope: the <c>DbContext</c> (and its connection) is released between passes.</summary>
    public async Task<RemoteSweepResult?> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        if (!db.Database.IsNpgsql()) return null;

        var sweeper = scope.ServiceProvider.GetRequiredService<RemoteJobSweeper>();
        return await sweeper.SweepAsync(ct);
    }
}
