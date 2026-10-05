using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>
/// Service-plane read and admin operations on JOBS and the fleet-wide stats/status views (OET-RWP/1 section 7.1). Lists carry no
/// content, no storage key and no secret: ids, states, counters, failure codes and resource ids only. State changes follow the
/// job state machine (T2 requeue, T10 force-local, T11 cancel) and each writes one audit row attributed to the fleet credential.
/// </summary>
public sealed class RemoteFleetJobsService(
    LearnerDbContext db,
    IRemoteJobFlags flags,
    RemoteJobsSettings settings,
    TimeProvider timeProvider)
{
    public async Task<List<Dictionary<string, object?>>> ListAsync(
        string? state,
        string? kind,
        string? nodeId,
        int limit,
        CancellationToken ct)
    {
        IQueryable<RemoteJob> query = db.RemoteJobs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(j => j.State == state);
        if (!string.IsNullOrWhiteSpace(kind)) query = query.Where(j => j.Kind == kind);
        if (!string.IsNullOrWhiteSpace(nodeId)) query = query.Where(j => j.LeaseOwner == nodeId || j.SettledBy == nodeId || j.TargetNodeId == nodeId);

        var rows = await query
            .OrderByDescending(j => j.UpdatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(j => new
            {
                j.Id, j.Kind, j.Purpose, j.State, j.Attempt, j.FenceToken, j.LeaseOwner, j.CreatedAt, j.UpdatedAt,
                j.FailureCode, j.ResourceType, j.ResourceId,
            })
            .ToListAsync(ct);

        return rows.Select(row => Row(row.Id, row.Kind, row.Purpose, row.State, row.Attempt, row.FenceToken, row.LeaseOwner,
            row.CreatedAt, row.UpdatedAt, row.FailureCode, row.ResourceType, row.ResourceId)).ToList();
    }

    public async Task<Dictionary<string, object?>?> GetAsync(string jobId, CancellationToken ct)
    {
        var job = await db.RemoteJobs.AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => new
            {
                j.Id, j.Kind, j.Purpose, j.State, j.Attempt, j.FenceToken, j.LeaseOwner, j.CreatedAt, j.UpdatedAt,
                j.FailureCode, j.ResourceType, j.ResourceId, j.ResultSummaryJson, j.MetricsJson, j.ApplyOutcome,
            })
            .FirstOrDefaultAsync(ct);
        if (job is null) return null;

        var view = Row(job.Id, job.Kind, job.Purpose, job.State, job.Attempt, job.FenceToken, job.LeaseOwner,
            job.CreatedAt, job.UpdatedAt, job.FailureCode, job.ResourceType, job.ResourceId);
        view["applyOutcome"] = job.ApplyOutcome;
        view["resultSummary"] = ParseElement(job.ResultSummaryJson);
        view["metrics"] = ParseElement(job.MetricsJson);
        return view;
    }

    private static Dictionary<string, object?> Row(
        string id, string kind, string purpose, string state, int attempt, long fence, string? leaseOwner,
        DateTimeOffset createdAt, DateTimeOffset updatedAt, string? failureCode, string resourceType, string resourceId)
        => new()
        {
            ["id"] = id,
            ["kind"] = kind,
            ["purpose"] = purpose,
            ["state"] = state,
            ["attempt"] = attempt,
            ["fence"] = fence,
            ["leaseOwner"] = leaseOwner,
            ["createdAt"] = RemoteIds.FormatTime(createdAt),
            ["updatedAt"] = RemoteIds.FormatTime(updatedAt),
            ["failureCode"] = failureCode,
            ["resourceType"] = resourceType,
            ["resourceId"] = resourceId,
        };

    // ── state changes ────────────────────────────────────────────────────────

    /// <summary>T2: <c>Failed</c>/<c>Quarantined</c>/<c>FallbackLocal</c>/<c>Cancelled</c> back to <c>Queued</c>; the fence is preserved.</summary>
    public async Task<(string? State, RemoteProblemResult? Error)> RequeueAsync(string jobId, string actorId, string actorName, CancellationToken ct)
    {
        var options = settings.Current;
        var state = await RemoteDb.QueryFirstAsync(
            db,
            """
            UPDATE "RemoteJobs" SET
                "State" = 'Queued', "Attempt" = 0, "ReleaseCount" = 0,
                "NextAttemptAt" = clock_timestamp(),
                "FallbackAfter" = CASE WHEN "Purpose" = 'apply' THEN clock_timestamp() + make_interval(mins => @fallbackMinutes) ELSE NULL END,
                "ResultSha256" = NULL, "ResultSummaryJson" = NULL, "ResultJson" = NULL, "ApplyOutcome" = NULL, "CompletedAt" = NULL,
                "SettledFence" = NULL, "SettledBy" = NULL, "SettledCode" = NULL,
                "FailureCode" = NULL, "FailureMessage" = NULL,
                "LeaseOwner" = NULL, "LeaseExpiresAt" = NULL, "DeadlineAt" = NULL, "ClaimNonce" = NULL,
                "UpdatedAt" = clock_timestamp()
            WHERE "Id" = @id AND "State" IN ('Failed', 'Quarantined', 'FallbackLocal', 'Cancelled')
            RETURNING "State";
            """,
            parameters =>
            {
                parameters.AddWithValue("fallbackMinutes", options.FallbackAfterMinutes);
                parameters.AddWithValue("id", jobId);
            },
            reader => RemoteDb.Str(reader, "State"),
            ct);

        return await FinishAsync(state, jobId, "RemoteJob.Requeue", actorId, actorName, ct);
    }

    /// <summary>T10: <c>Queued</c>/<c>Quarantined</c>/<c>Failed</c> handed to the local path (<c>FallbackLocal</c>).</summary>
    public async Task<(string? State, RemoteProblemResult? Error)> ForceLocalAsync(string jobId, string actorId, string actorName, CancellationToken ct)
    {
        var state = await RemoteDb.QueryFirstAsync(
            db,
            """
            UPDATE "RemoteJobs" SET "State" = 'FallbackLocal', "FailureCode" = 'force_local', "UpdatedAt" = clock_timestamp()
            WHERE "Id" = @id AND "State" IN ('Queued', 'Quarantined', 'Failed')
            RETURNING "State";
            """,
            parameters => parameters.AddWithValue("id", jobId),
            reader => RemoteDb.Str(reader, "State"),
            ct);

        return await FinishAsync(state, jobId, "RemoteJob.ForceLocal", actorId, actorName, ct);
    }

    /// <summary>T11: a <c>Queued</c> job is withdrawn.</summary>
    public async Task<(string? State, RemoteProblemResult? Error)> CancelAsync(string jobId, string actorId, string actorName, CancellationToken ct)
    {
        var state = await RemoteDb.QueryFirstAsync(
            db,
            """
            UPDATE "RemoteJobs" SET "State" = 'Cancelled', "FailureCode" = 'admin_cancel', "CompletedAt" = clock_timestamp(), "UpdatedAt" = clock_timestamp()
            WHERE "Id" = @id AND "State" = 'Queued'
            RETURNING "State";
            """,
            parameters => parameters.AddWithValue("id", jobId),
            reader => RemoteDb.Str(reader, "State"),
            ct);

        return await FinishAsync(state, jobId, "RemoteJob.Cancel", actorId, actorName, ct);
    }

    private async Task<(string? State, RemoteProblemResult? Error)> FinishAsync(
        string? state,
        string jobId,
        string action,
        string actorId,
        string actorName,
        CancellationToken ct)
    {
        if (state is not null)
        {
            await RemoteAudit.WriteAsync(db, actorId, actorName, action, RemoteAudit.ResourceJob, jobId, new { to = state }, timeProvider.GetUtcNow(), ct);
            return (state, null);
        }

        // Nothing changed: either there is no such job or its state does not allow the transition.
        var current = await RemoteDb.QueryFirstAsync(
            db,
            """SELECT "State" FROM "RemoteJobs" WHERE "Id" = @id;""",
            parameters => parameters.AddWithValue("id", jobId),
            reader => RemoteDb.Str(reader, "State"),
            ct);

        if (current is null) return (null, RemoteProblems.JobNotFound());
        return (null, RemoteProblems.Conflict("invalid_transition", "The job cannot make this transition.", current));
    }

    // ── stats / status ───────────────────────────────────────────────────────

    public async Task<Dictionary<string, object?>> StatsAsync(CancellationToken ct)
    {
        var options = settings.Current;
        var now = timeProvider.GetUtcNow();

        var counts = await RemoteDb.QueryAsync(
            db,
            """SELECT "Kind", "State", COUNT(*)::int AS "Count" FROM "RemoteJobs" GROUP BY "Kind", "State";""",
            null,
            reader => (Kind: RemoteDb.Str(reader, "Kind"), State: RemoteDb.Str(reader, "State"), Count: RemoteDb.Int(reader, "Count")),
            ct);

        var oldest = await RemoteDb.ScalarAsync(
            db,
            """SELECT EXTRACT(EPOCH FROM (clock_timestamp() - MIN("NextAttemptAt")))::bigint FROM "RemoteJobs" WHERE "State" = 'Queued';""",
            null,
            ct);

        var leased = await RemoteDb.QueryAsync(
            db,
            """
            SELECT "LeaseOwner", COUNT(*)::int AS "Count", COALESCE(SUM("Weight"), 0)::int AS "Weight"
            FROM "RemoteJobs" WHERE "State" = 'Leased' GROUP BY "LeaseOwner";
            """,
            null,
            reader => (Node: RemoteDb.Str(reader, "LeaseOwner"), Count: RemoteDb.Int(reader, "Count"), Weight: RemoteDb.Int(reader, "Weight")),
            ct);

        var nodes = await db.RemoteWorkers.AsNoTracking()
            .Select(w => new { w.Status, w.LastHeartbeatAt })
            .ToListAsync(ct);

        var byKind = counts
            .GroupBy(row => row.Kind, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (object?)group.ToDictionary(row => row.State, row => row.Count, StringComparer.Ordinal),
                StringComparer.Ordinal);

        return new Dictionary<string, object?>
        {
            ["queue"] = byKind,
            ["oldestQueuedAgeSeconds"] = oldest is null ? null : (object)Convert.ToInt64(oldest, System.Globalization.CultureInfo.InvariantCulture),
            ["leasedCount"] = leased.Sum(row => row.Count),
            ["leasedWeightByNode"] = leased.ToDictionary(row => row.Node, row => row.Weight, StringComparer.Ordinal),
            ["nodes"] = new Dictionary<string, object?>
            {
                ["byStatus"] = nodes.GroupBy(n => n.Status).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal),
                ["byHealth"] = nodes.GroupBy(n => RemoteNodeView.Health(n.LastHeartbeatAt, now, options))
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal),
            },
            ["serverTime"] = RemoteIds.FormatTime(now),
        };
    }

    /// <summary>Effective flags (read-only), options, protocol numbers and the kind registry: what the manager validates agents against.</summary>
    public async Task<Dictionary<string, object?>> StatusAsync(CancellationToken ct)
    {
        var options = settings.Current;
        var snapshot = await flags.GetAsync(ct);

        return new Dictionary<string, object?>
        {
            ["protocol"] = new Dictionary<string, object?>
            {
                ["current"] = options.CurrentProtocol,
                ["min"] = options.MinProtocol,
            },
            ["flags"] = RemoteJobFlagKeys.All.ToDictionary(key => key, key => (object?)snapshot.IsOn(key), StringComparer.Ordinal),
            ["options"] = new Dictionary<string, object?>
            {
                ["leaseSeconds"] = options.LeaseSeconds,
                ["heartbeatEverySeconds"] = options.HeartbeatEverySeconds,
                ["nodeHeartbeatSeconds"] = options.NodeHeartbeatSeconds,
                ["nodeStaleAfterSeconds"] = options.NodeStaleAfterSeconds,
                ["maxAttempts"] = options.MaxAttempts,
                ["releaseLimit"] = options.ReleaseLimit,
                ["fallbackAfterMinutes"] = options.FallbackAfterMinutes,
                ["fallbackHardAfterMinutes"] = options.FallbackHardAfterMinutes,
                ["integrityStrikeLimit"] = options.IntegrityStrikeLimit,
                ["fairShareGate"] = options.FairShareGate,
                ["verifySampleRate"] = options.VerifySampleRate,
                ["tokenTtlDays"] = options.TokenTtlDays,
            },
            ["kinds"] = RemoteJobKinds.All.Select(spec => new Dictionary<string, object?>
            {
                ["kind"] = spec.Kind,
                ["schemaVersions"] = new[] { spec.SchemaVersion },
                ["engineVersion"] = RemoteJobKinds.EngineVersion(spec.Kind, options),
                ["enabled"] = snapshot.KindEnabled(spec.Kind, RemoteJobPurpose.Apply),
                ["limits"] = new Dictionary<string, object?>
                {
                    ["weight"] = spec.Limits.Weight,
                    ["cpuMilli"] = spec.Limits.CpuMilli,
                    ["memMiB"] = spec.Limits.MemMiB,
                    ["tmpMiB"] = spec.Limits.TmpMiB,
                    ["timeoutSeconds"] = spec.Limits.TimeoutSeconds,
                    ["deadlineSeconds"] = spec.Limits.DeadlineSeconds,
                    ["maxInputBytes"] = spec.Limits.MaxInputBytes,
                    ["maxResultBytes"] = spec.Limits.MaxResultBytes,
                    ["maxOutputBytes"] = spec.Limits.MaxOutputBytes,
                    ["maxOutputs"] = spec.Limits.MaxOutputs,
                },
            }).ToList(),
        };
    }

    private static object? ParseElement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
