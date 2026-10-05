using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>A job row with the database-clock facts needed to classify a lost lease.</summary>
public sealed record RemoteJobSnapshot(RemoteJobRow Row, bool PastDeadline, bool PastExpiry);

/// <summary>
/// Outcome of the lease guard shared by <c>heartbeat</c>, <c>inputs</c>, <c>outputs</c>, <c>complete</c> and
/// <c>fail</c>: either the live leased row, or the protocol error (404 / 409 lease_lost with its reason).
/// </summary>
public sealed record RemoteLeaseCheck(RemoteJobRow? Row, RemoteProblemResult? Error);

public sealed record RemoteHeartbeatResult(
    DateTimeOffset LeaseExpiresAt,
    long LeaseRemainingMs,
    DateTimeOffset ServerTime,
    RemoteProblemResult? Error);

/// <summary>Agent failure codes (OET-RWP/1 section 4.6): whether they retry and whether the attempt is refunded.</summary>
public static class RemoteFailCodes
{
    private static readonly Dictionary<string, (bool Retryable, bool Refund)> Table = new(StringComparer.Ordinal)
    {
        ["timeout"] = (true, false),
        ["oom"] = (true, false),
        ["internal_error"] = (true, false),
        ["input_unavailable"] = (true, false),
        ["input_hash_mismatch"] = (true, false),
        ["input_too_large"] = (false, false),
        ["extract_exception"] = (false, false),
        ["not_pdf"] = (false, false),
        ["no_audio_stream"] = (false, false),
        ["duration_exceeded"] = (false, false),
        ["transcoder_unavailable"] = (false, false),
        ["limits_exceeded"] = (false, false),
        ["shutdown"] = (true, true),
        ["drain"] = (true, true),
        ["pressure_shed"] = (true, true),
        ["protocol_mismatch"] = (true, true),
    };

    /// <summary>Codes the SERVER may record itself (a rejected result requeues like a retryable failure).</summary>
    public const string ResultInvalid = "result_invalid";
    public const string ContentRejected = "content_rejected";

    public static bool IsKnown(string? code) => code is not null && Table.ContainsKey(code);

    /// <summary>The API decides retryability from the code; the agent's flag is only a request.</summary>
    public static bool IsRetryable(string code) => Table.TryGetValue(code, out var entry) && entry.Retryable;

    /// <summary>Only the allow-listed codes refund the attempt; the agent cannot choose.</summary>
    public static bool IsRefund(string code) => Table.TryGetValue(code, out var entry) && entry.Refund;

    /// <summary>Terminal failure codes the local path may take over deterministically (OET-RWP/1 section 3.7).</summary>
    public static bool AllowsLocalFallback(string? code)
        => code is "not_pdf" or ContentRejected or "transcoder_unavailable";
}

/// <summary>Why a lease is lost, from the row and the database clock (OET-RWP/1 sections 4.2.2 and 4.5.3).</summary>
public static class RemoteLeaseClassifier
{
    public static string LostReason(RemoteJobSnapshot snapshot, string nodeId, long fence)
    {
        var row = snapshot.Row;
        switch (row.State)
        {
            case RemoteJobState.Cancelled:
                return "cancelled";
            case RemoteJobState.Queued:
                return "superseded";
            case RemoteJobState.Leased:
                if (row.LeaseOwner == nodeId && row.FenceToken == fence)
                {
                    return snapshot.PastDeadline ? "deadline" : "expired";
                }

                // Same fence number held by another node: a guess or a bug, never a legitimate holder.
                return row.FenceToken == fence ? "not_owner" : "superseded";
            default:
                return "terminal";
        }
    }
}

/// <summary>
/// Lease-guarded operations on a single job: heartbeat, fail, the guard for input/output access and the
/// stale-input cancel. Every mutating or data-exposing call is guarded by
/// <c>State='Leased' AND LeaseOwner=@node AND FenceToken=@fence AND LeaseExpiresAt &gt; clock_timestamp()</c>:
/// an expired-but-unreaped lease can no longer renew, read inputs or commit. All lease arithmetic uses the
/// database clock, never the application clock.
/// </summary>
public sealed class RemoteJobLifecycleService(
    LearnerDbContext db,
    RemoteJobsSettings settings,
    TimeProvider timeProvider)
{
    private static readonly string SnapshotSql =
        "SELECT " + RemoteJobRow.Columns("j")
        + ", (j.\"DeadlineAt\" IS NOT NULL AND j.\"DeadlineAt\" <= clock_timestamp()) AS \"PastDeadline\""
        + ", (j.\"LeaseExpiresAt\" IS NOT NULL AND j.\"LeaseExpiresAt\" <= clock_timestamp()) AS \"PastExpiry\""
        + " FROM \"RemoteJobs\" j WHERE j.\"Id\" = @id;";

    private static readonly string GuardSql =
        "SELECT " + RemoteJobRow.Columns("j")
        + " FROM \"RemoteJobs\" j WHERE j.\"Id\" = @id AND j.\"State\" = 'Leased' AND j.\"LeaseOwner\" = @node"
        + " AND j.\"FenceToken\" = @fence AND j.\"LeaseExpiresAt\" > clock_timestamp();";

    private const string HeartbeatSql = """
        UPDATE "RemoteJobs" SET
            "LeaseExpiresAt"  = LEAST(clock_timestamp() + make_interval(secs => @lease), "DeadlineAt"),
            "LastHeartbeatAt" = clock_timestamp(),
            "MetricsJson"     = COALESCE(@metrics, "MetricsJson"),
            "UpdatedAt"       = clock_timestamp()
        WHERE "Id" = @id AND "State" = 'Leased' AND "LeaseOwner" = @node
          AND "FenceToken" = @fence AND "LeaseExpiresAt" > clock_timestamp()
        RETURNING "LeaseExpiresAt", clock_timestamp() AS "Now",
                  (extract(epoch from ("LeaseExpiresAt" - clock_timestamp())) * 1000)::bigint AS "LeaseRemainingMs";
        """;

    private const string FailSql = """
        UPDATE "RemoteJobs" j SET
            "ReleaseCount"     = j."ReleaseCount" + @refund,
            "Attempt"          = GREATEST(j."Attempt" - @refund, 0),
            "State"            = CASE WHEN NOT @retryable THEN 'Failed'
                                      WHEN GREATEST(j."Attempt" - @refund, 0) >= j."MaxAttempts" THEN 'Quarantined'
                                      ELSE 'Queued' END,
            "NextAttemptAt"    = clock_timestamp() + make_interval(secs => CASE WHEN @refund = 1 THEN 0
                                     ELSE LEAST(@backoffMax, @backoffBase * power(2, GREATEST(j."Attempt" - 1, 0))) * @jitter END),
            "FailureCode"      = @code,
            "FailureMessage"   = @message,
            "LastFailedNodeId" = @node,
            "SettledFence"     = @fence,
            "SettledBy"        = @node,
            "SettledCode"      = @code,
            "CompletedAt"      = CASE WHEN NOT @retryable THEN clock_timestamp() ELSE j."CompletedAt" END,
            "MetricsJson"      = COALESCE(@metrics, j."MetricsJson"),
            "LeaseOwner"       = NULL,
            "LeaseExpiresAt"   = NULL,
            "DeadlineAt"       = NULL,
            "ClaimNonce"       = NULL,
            "UpdatedAt"        = clock_timestamp()
        WHERE j."Id" = @id AND j."State" = 'Leased' AND j."LeaseOwner" = @node
          AND j."FenceToken" = @fence AND j."LeaseExpiresAt" > clock_timestamp()
        RETURNING j."State";
        """;

    /// <summary>The row with database-clock flags, or null when there is no such job.</summary>
    public Task<RemoteJobSnapshot?> GetSnapshotAsync(string jobId, CancellationToken ct)
        => RemoteDb.QueryFirstAsync(
            db,
            SnapshotSql,
            parameters => parameters.AddWithValue("id", jobId),
            reader => new RemoteJobSnapshot(
                RemoteJobRow.Read(reader),
                RemoteDb.Bool(reader, "PastDeadline"),
                RemoteDb.Bool(reader, "PastExpiry")),
            ct);

    /// <summary>
    /// Applies the lease guard (OET-RWP/1 section 3.3 rule 2). A live lease returns the row; anything else returns
    /// <c>404 job_not_found</c> or <c>409 lease_lost</c> with the reason.
    /// </summary>
    public async Task<RemoteLeaseCheck> GuardAsync(string jobId, string nodeId, long fence, CancellationToken ct)
    {
        var row = await RemoteDb.QueryFirstAsync(
            db,
            GuardSql,
            parameters =>
            {
                parameters.AddWithValue("id", jobId);
                parameters.AddWithValue("node", nodeId);
                parameters.AddWithValue("fence", fence);
            },
            RemoteJobRow.Read,
            ct);
        if (row is not null) return new RemoteLeaseCheck(row, null);

        return new RemoteLeaseCheck(null, await ClassifyLostAsync(jobId, nodeId, fence, ct));
    }

    /// <summary>The error for a guard that failed: 404 when the job does not exist, else 409 lease_lost + reason.</summary>
    public async Task<RemoteProblemResult> ClassifyLostAsync(string jobId, string nodeId, long fence, CancellationToken ct)
    {
        var snapshot = await GetSnapshotAsync(jobId, ct);
        return snapshot is null
            ? RemoteProblems.JobNotFound()
            : RemoteProblems.LeaseLost(RemoteLeaseClassifier.LostReason(snapshot, nodeId, fence));
    }

    /// <summary>Extends the lease (clamped to the deadline) for the current owner and fence while it is live.</summary>
    public async Task<RemoteHeartbeatResult> HeartbeatAsync(
        string jobId,
        string nodeId,
        RemoteJobHeartbeatRequestDto request,
        CancellationToken ct)
    {
        var fence = request.Fence!.Value;
        var metricsJson = BuildMetricsJson(request.Stage, request.Metrics);

        var updated = await RemoteDb.QueryFirstAsync(
            db,
            HeartbeatSql,
            parameters =>
            {
                parameters.AddWithValue("lease", settings.Current.LeaseSeconds);
                parameters.Add(new NpgsqlParameter("metrics", NpgsqlDbType.Jsonb) { Value = (object?)metricsJson ?? DBNull.Value });
                parameters.AddWithValue("id", jobId);
                parameters.AddWithValue("node", nodeId);
                parameters.AddWithValue("fence", fence);
            },
            reader => new RemoteHeartbeatResult(
                RemoteDb.Ts(reader, "LeaseExpiresAt"),
                RemoteDb.Long(reader, "LeaseRemainingMs"),
                RemoteDb.Ts(reader, "Now"),
                null),
            ct);
        if (updated is not null) return updated;

        var error = await ClassifyLostAsync(jobId, nodeId, fence, ct);
        return new RemoteHeartbeatResult(default, 0, timeProvider.GetUtcNow(), error);
    }

    /// <summary>
    /// <c>POST fail</c> (OET-RWP/1 section 4.6): replay first, then one fenced CAS. Retryability and refund come from the
    /// code table, never from the agent. Returns the resulting status (<c>queued</c>, <c>failed</c>,
    /// <c>quarantined</c>) and whether this was a replay, or an error.
    /// </summary>
    public async Task<(string? Status, bool Replayed, RemoteProblemResult? Error)> FailAsync(
        string jobId,
        string nodeId,
        RemoteFailRequestDto request,
        CancellationToken ct)
    {
        var options = settings.Current;
        var fence = request.Fence!.Value;
        var code = request.Code!;

        var snapshot = await GetSnapshotAsync(jobId, ct);
        if (snapshot is null) return (null, false, RemoteProblems.JobNotFound());

        var row = snapshot.Row;
        if (row.SettledFence == fence && row.SettledBy == nodeId && row.SettledCode == code)
        {
            return (StatusOf(row.State), true, null);
        }

        var retryable = RemoteFailCodes.IsRetryable(code);
        var refund = RemoteFailCodes.IsRefund(code) && row.ReleaseCount < options.ReleaseLimit;

        var status = await ApplyFailureAsync(
            jobId, nodeId, fence, code, request.Message, retryable, refund,
            BuildMetricsJson(null, request.Metrics), options, ct);

        if (status is null)
        {
            return (null, false, await ClassifyLostAsync(jobId, nodeId, fence, ct));
        }

        return (StatusOf(status), false, null);
    }

    /// <summary>
    /// Runs the fail CAS (used by <c>fail</c> and by the server itself when a result is rejected). Returns the new
    /// state, or null when the guard no longer holds. A job that lands in <c>Quarantined</c> writes one audit row.
    /// </summary>
    public async Task<string?> ApplyFailureAsync(
        string jobId,
        string nodeId,
        long fence,
        string code,
        string? message,
        bool retryable,
        bool refund,
        string? metricsJson,
        RemoteJobsOptions options,
        CancellationToken ct)
    {
        var jitter = 1.0 + Random.Shared.NextDouble() * (options.BackoffJitterPercent / 100.0);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var state = await RemoteDb.QueryFirstAsync(
            db,
            FailSql,
            parameters =>
            {
                parameters.AddWithValue("refund", refund ? 1 : 0);
                parameters.AddWithValue("retryable", retryable);
                parameters.AddWithValue("backoffMax", (double)options.BackoffMaxSeconds);
                parameters.AddWithValue("backoffBase", (double)options.BackoffBaseSeconds);
                parameters.AddWithValue("jitter", jitter);
                parameters.AddWithValue("code", code);
                parameters.Add(new NpgsqlParameter("message", NpgsqlDbType.Varchar) { Value = (object?)Truncate(message, 256) ?? DBNull.Value });
                parameters.AddWithValue("node", nodeId);
                parameters.AddWithValue("fence", fence);
                parameters.Add(new NpgsqlParameter("metrics", NpgsqlDbType.Jsonb) { Value = (object?)metricsJson ?? DBNull.Value });
                parameters.AddWithValue("id", jobId);
            },
            reader => RemoteDb.Str(reader, "State"),
            ct);

        if (state is null) return null;

        if (state == RemoteJobState.Quarantined)
        {
            RemoteAudit.Add(db, RemoteAudit.NodeActor(nodeId), "remote-worker", "RemoteJob.Quarantined",
                RemoteAudit.ResourceJob, jobId, new { code, fence }, timeProvider.GetUtcNow());
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return state;
    }

    /// <summary>
    /// Cancels a job the node still holds because its input manifest is stale (object missing or length drift). The
    /// producer then re-enqueues with a fresh fingerprint. Returns false when the guard no longer holds.
    /// </summary>
    public async Task<bool> CancelLeasedAsync(string jobId, string nodeId, long fence, string failureCode, CancellationToken ct)
    {
        var rows = await RemoteDb.ExecuteAsync(
            db,
            """
            UPDATE "RemoteJobs" SET
                "State" = 'Cancelled', "FailureCode" = @code,
                "SettledFence" = @fence, "SettledBy" = @node, "SettledCode" = @code,
                "CompletedAt" = clock_timestamp(),
                "LeaseOwner" = NULL, "LeaseExpiresAt" = NULL, "DeadlineAt" = NULL, "ClaimNonce" = NULL,
                "UpdatedAt" = clock_timestamp()
            WHERE "Id" = @id AND "State" = 'Leased' AND "LeaseOwner" = @node
              AND "FenceToken" = @fence AND "LeaseExpiresAt" > clock_timestamp();
            """,
            parameters =>
            {
                parameters.AddWithValue("code", failureCode);
                parameters.AddWithValue("fence", fence);
                parameters.AddWithValue("node", nodeId);
                parameters.AddWithValue("id", jobId);
            },
            ct);
        return rows == 1;
    }

    /// <summary>The wire status of a settled-by-failure row: queued | failed | quarantined.</summary>
    public static string StatusOf(string state) => state switch
    {
        RemoteJobState.Failed => "failed",
        RemoteJobState.Quarantined => "quarantined",
        _ => "queued",
    };

    /// <summary>Stage and numeric metrics as a small JSON object, or null when there is nothing to store.</summary>
    public static string? BuildMetricsJson(string? stage, Dictionary<string, double>? metrics)
    {
        if (stage is null && (metrics is null || metrics.Count == 0)) return null;

        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        if (stage is not null) values["stage"] = stage;
        if (metrics is not null)
        {
            foreach (var (key, value) in metrics)
            {
                if (key.Length is 0 or > 48 || double.IsNaN(value) || double.IsInfinity(value)) continue;
                values[key] = value;
            }
        }

        return values.Count == 0 ? null : JsonSerializer.Serialize(values);
    }

    private static string? Truncate(string? value, int max)
        => value is null ? null : value.Length <= max ? value : value[..max];
}
