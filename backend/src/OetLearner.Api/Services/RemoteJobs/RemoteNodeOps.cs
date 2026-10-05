using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>Audit rows for remote-worker activity: ids, kinds, outcomes, counts and hashes, never content or secrets.</summary>
public static class RemoteAudit
{
    public const string ResourceNode = "RemoteWorker";
    public const string ResourceJob = "RemoteJob";
    public const string ResourceFleetCredential = "RemoteFleetCredential";

    public static string NodeActor(string nodeId) => "remote:" + nodeId;
    public static string FleetActor(string credentialId) => "fleet:" + credentialId;
    public const string ReaperActor = "system:remote-job-reaper";

    /// <summary>Stages an <see cref="AuditEvent"/>; the caller's <c>SaveChanges</c> persists it (inside its transaction).</summary>
    public static void Add(
        LearnerDbContext db,
        string actorId,
        string actorName,
        string action,
        string resourceType,
        string? resourceId,
        object? details,
        DateTimeOffset now)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = now,
            ActorId = actorId.Length > 64 ? actorId[..64] : actorId,
            // The FK column to ApplicationUserAccounts stays null: remote/fleet actors are not accounts.
            ActorAuthAccountId = null,
            ActorName = actorName,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details is null ? null : JsonSerializer.Serialize(details),
        });
    }

    public static async Task WriteAsync(
        LearnerDbContext db,
        string actorId,
        string actorName,
        string action,
        string resourceType,
        string? resourceId,
        object? details,
        DateTimeOffset now,
        CancellationToken ct)
    {
        Add(db, actorId, actorName, action, resourceType, resourceId, details, now);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Result of recording an integrity strike against a node.</summary>
public sealed record RemoteStrikeResult(int Strikes, bool Quarantined);

/// <summary>
/// Postgres statements on nodes and on the leases a node holds, shared by the node service, the
/// completion service, the canary applier and the reaper. Methods run in the caller's transaction when
/// one is open (the shared <c>DbContext</c> hands its transaction to every command).
/// </summary>
public static class RemoteNodeOps
{
    /// <summary>
    /// Releases (requeues, with a refund while <c>ReleaseCount &lt; releaseLimit</c>) every lease held by
    /// <paramref name="nodeId"/>, or only <paramref name="jobIds"/> when given. A job whose attempts are
    /// exhausted without a refund becomes <c>Quarantined</c>. Returns the affected job ids and new states.
    /// </summary>
    public static async Task<List<(string Id, string State)>> ReleaseLeasesAsync(
        LearnerDbContext db,
        string nodeId,
        string failureCode,
        int releaseLimit,
        IReadOnlyCollection<string>? jobIds,
        CancellationToken ct)
    {
        var onlyIds = jobIds is { Count: > 0 };
        var sql = $"""
            UPDATE "RemoteJobs" j SET
                "ReleaseCount"     = j."ReleaseCount" + CASE WHEN j."ReleaseCount" < @releaseLimit THEN 1 ELSE 0 END,
                "Attempt"          = GREATEST(j."Attempt" - CASE WHEN j."ReleaseCount" < @releaseLimit THEN 1 ELSE 0 END, 0),
                "State"            = CASE WHEN GREATEST(j."Attempt" - CASE WHEN j."ReleaseCount" < @releaseLimit THEN 1 ELSE 0 END, 0) >= j."MaxAttempts"
                                          THEN 'Quarantined' ELSE 'Queued' END,
                "NextAttemptAt"    = clock_timestamp(),
                "FailureCode"      = @code,
                "LastFailedNodeId" = j."LeaseOwner",
                "LeaseOwner"       = NULL,
                "LeaseExpiresAt"   = NULL,
                "DeadlineAt"       = NULL,
                "ClaimNonce"       = NULL,
                "UpdatedAt"        = clock_timestamp()
            WHERE j."State" = 'Leased' AND j."LeaseOwner" = @node
              {(onlyIds ? "AND j.\"Id\" = ANY(@ids)" : string.Empty)}
            RETURNING j."Id", j."State";
            """;

        return await RemoteDb.QueryAsync(
            db,
            sql,
            parameters =>
            {
                parameters.AddWithValue("releaseLimit", releaseLimit);
                parameters.AddWithValue("code", failureCode);
                parameters.AddWithValue("node", nodeId);
                if (onlyIds) parameters.AddWithValue("ids", jobIds!.ToArray());
            },
            reader => (RemoteDb.Str(reader, "Id"), RemoteDb.Str(reader, "State")),
            ct);
    }

    /// <summary>Changes a node's status (bumping <c>PolicyRevision</c>) only if it still has the expected status.</summary>
    public static async Task<bool> TryChangeStatusAsync(
        LearnerDbContext db,
        string nodeId,
        string expectedFrom,
        string to,
        string? reason,
        CancellationToken ct)
    {
        var rows = await RemoteDb.ExecuteAsync(
            db,
            """
            UPDATE "RemoteWorkers" SET
                "Status"          = @to,
                "StatusReason"    = @reason,
                "StatusChangedAt" = clock_timestamp(),
                "PolicyRevision"  = "PolicyRevision" + 1,
                "UpdatedAt"       = clock_timestamp()
            WHERE "Id" = @id AND "Status" = @from;
            """,
            parameters =>
            {
                parameters.AddWithValue("to", to);
                parameters.Add(new NpgsqlParameter("reason", NpgsqlDbType.Varchar) { Value = (object?)Truncate(reason, 200) ?? DBNull.Value });
                parameters.AddWithValue("id", nodeId);
                parameters.AddWithValue("from", expectedFrom);
            },
            ct);
        return rows == 1;
    }

    /// <summary>Revokes every active credential of a node (idempotent).</summary>
    public static Task<int> RevokeCredentialsAsync(LearnerDbContext db, string nodeId, CancellationToken ct)
        => RemoteDb.ExecuteAsync(
            db,
            """
            UPDATE "RemoteCredentials" SET "RevokedAt" = clock_timestamp()
            WHERE "NodeId" = @id AND "RevokedAt" IS NULL;
            """,
            parameters => parameters.AddWithValue("id", nodeId),
            ct);

    /// <summary>
    /// Quarantines a node that is not already Quarantined or Revoked: status change, leases requeued with a
    /// refund, one audit row (all in the caller's transaction). Returns false when the node was not eligible.
    /// </summary>
    public static async Task<bool> QuarantineAsync(
        LearnerDbContext db,
        string nodeId,
        string reason,
        string actorId,
        string actorName,
        RemoteJobsOptions options,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var current = await RemoteDb.QueryFirstAsync(
            db,
            """SELECT "Status" FROM "RemoteWorkers" WHERE "Id" = @id FOR UPDATE;""",
            parameters => parameters.AddWithValue("id", nodeId),
            reader => RemoteDb.Str(reader, "Status"),
            ct);

        if (current is null or RemoteNodeStatus.Quarantined or RemoteNodeStatus.Revoked) return false;
        if (!await TryChangeStatusAsync(db, nodeId, current, RemoteNodeStatus.Quarantined, reason, ct)) return false;

        var released = await ReleaseLeasesAsync(db, nodeId, "node_quarantined", options.ReleaseLimit, null, ct);
        RemoteAudit.Add(db, actorId, actorName, "RemoteWorker.Quarantine", RemoteAudit.ResourceNode, nodeId,
            new { fromStatus = current, reason, releasedLeases = released.Count }, now);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Records one integrity strike (OET-RWP/1 section 3.6). At the limit within the window the node is
    /// quarantined in the same transaction. Opens its own transaction when none is active.
    /// </summary>
    public static async Task<RemoteStrikeResult> AddStrikeAsync(
        LearnerDbContext db,
        string nodeId,
        string reason,
        RemoteJobsOptions options,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var ownTransaction = db.Database.CurrentTransaction is null;
        var transaction = ownTransaction ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var scalar = await RemoteDb.ScalarAsync(
                db,
                """
                UPDATE "RemoteWorkers" SET
                    "IntegrityStrikes" = CASE
                        WHEN "StrikeWindowStartedAt" IS NULL
                          OR "StrikeWindowStartedAt" < clock_timestamp() - make_interval(mins => @window) THEN 1
                        ELSE "IntegrityStrikes" + 1 END,
                    "StrikeWindowStartedAt" = CASE
                        WHEN "StrikeWindowStartedAt" IS NULL
                          OR "StrikeWindowStartedAt" < clock_timestamp() - make_interval(mins => @window) THEN clock_timestamp()
                        ELSE "StrikeWindowStartedAt" END,
                    "UpdatedAt" = clock_timestamp()
                WHERE "Id" = @id
                RETURNING "IntegrityStrikes";
                """,
                parameters =>
                {
                    parameters.AddWithValue("window", options.StrikeWindowMinutes);
                    parameters.AddWithValue("id", nodeId);
                },
                ct);

            var count = scalar is null ? 0 : Convert.ToInt32(scalar, System.Globalization.CultureInfo.InvariantCulture);
            var quarantined = false;
            if (count >= options.IntegrityStrikeLimit)
            {
                quarantined = await QuarantineAsync(
                    db, nodeId, $"integrity strikes ({reason})", RemoteAudit.NodeActor(nodeId), "remote-worker", options, now, ct);
            }

            if (transaction is not null) await transaction.CommitAsync(ct);
            return new RemoteStrikeResult(count, quarantined);
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    private static string? Truncate(string? value, int max)
        => value is null ? null : value.Length <= max ? value : value[..max];
}

/// <summary>
/// Remembers, per node, which leases the database holds that the agent did not report, so a lease absent
/// from two consecutive heartbeats can be released early with a refund (OET-RWP/1 section 4.7.2 step 4).
/// Per-process and best-effort: losing it on a restart only delays reconciliation to the lease expiry.
/// </summary>
public sealed class RemoteOrphanTracker
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _previous = new(StringComparer.Ordinal);

    /// <summary>
    /// Given the leases missing from THIS heartbeat, returns those that were also missing from the previous
    /// one (and forgets them), and remembers the rest for next time.
    /// </summary>
    public IReadOnlyList<string> ConfirmMissing(string nodeId, IReadOnlyCollection<string> missingNow)
    {
        var previous = _previous.TryGetValue(nodeId, out var set) ? set : new HashSet<string>(StringComparer.Ordinal);
        var confirmed = missingNow.Where(previous.Contains).ToList();
        var remaining = new HashSet<string>(missingNow.Where(id => !previous.Contains(id)), StringComparer.Ordinal);
        if (remaining.Count == 0) _previous.TryRemove(nodeId, out _);
        else _previous[nodeId] = remaining;
        return confirmed;
    }

    public void Forget(string nodeId) => _previous.TryRemove(nodeId, out _);
}
