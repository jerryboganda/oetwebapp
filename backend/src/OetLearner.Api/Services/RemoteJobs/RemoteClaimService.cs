using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>A leased job with the server-computed remaining lease time.</summary>
public sealed record RemoteLeasedJob(RemoteJobRow Job, long LeaseRemainingMs);

/// <summary>
/// What a claim answered: a leased job, a 204 with a machine reason, or an error. <see cref="Node"/> carries the
/// node row as read under the node lock (used for the desired state and the revision header).
/// </summary>
public sealed record RemoteClaimOutcome(
    RemoteLeasedJob? Leased,
    string? NoContentReason,
    RemoteProblemResult? Error,
    RemoteWorker? Node)
{
    public static RemoteClaimOutcome Job(RemoteLeasedJob leased, RemoteWorker node) => new(leased, null, null, node);

    public static RemoteClaimOutcome NoContent(string reason, RemoteWorker? node) => new(null, reason, null, node);

    public static RemoteClaimOutcome Fail(RemoteProblemResult error, RemoteWorker? node = null) => new(null, null, error, node);
}

/// <summary>A peer node considered by the fair-share gate.</summary>
public sealed record FairSharePeer(string NodeId, int FreeSlots, double Norm);

/// <summary>Fair-share placement gate (OET-RWP/1 section 3.8). Pure; off unless <c>RemoteJobs:FairShareGate</c>.</summary>
public static class RemoteFairShare
{
    public const double Margin = 0.34;

    /// <summary>Leased weight over the node's effective concurrency (at least 1).</summary>
    public static double Norm(int leasedWeight, int maxConcurrency, int effectiveConcurrency)
        => leasedWeight / (double)Math.Max(1, Math.Min(maxConcurrency, effectiveConcurrency));

    /// <summary>
    /// True when a claim by this node should be answered <c>204 fair_share</c>: another eligible node with a free
    /// slot is at least <see cref="Margin"/> emptier, the emptier peers' free slots cover every due eligible job,
    /// and this node was not already declined twice in a row (nobody starves).
    /// </summary>
    public static bool ShouldDecline(
        double selfNorm,
        IReadOnlyList<FairSharePeer> peers,
        int dueEligibleQueuedJobs,
        int declinedInARow)
    {
        if (declinedInARow >= 2) return false;
        var emptier = peers.Where(peer => peer.FreeSlots > 0 && peer.Norm + Margin <= selfNorm).ToList();
        if (emptier.Count == 0) return false;
        return emptier.Sum(peer => peer.FreeSlots) >= dueEligibleQueuedJobs;
    }
}

/// <summary>
/// <c>POST claim</c> (OET-RWP/1 section 4.1): leases at most ONE startable job for a node, in one transaction of at most
/// four statements (lock the node and read its leased weight; idempotent-replay check; pick-and-lease with
/// <c>FOR UPDATE SKIP LOCKED</c>; update the node's last-seen columns). The reaper is the ONLY code that turns an
/// expired lease back into work, so this statement never selects a <c>Leased</c> row.
/// </summary>
public sealed class RemoteClaimService(
    LearnerDbContext db,
    IRemoteJobFlags flags,
    RemoteJobsSettings settings,
    TimeProvider timeProvider)
{
    private const string LockNodeSql = """
        SELECT w."Id", w."Status", w."Paused", w."AllowedKinds", w."MaxConcurrency", w."KindLimitsJson",
               w."CpuBudgetMilli", w."MemBudgetMiB", w."TmpBudgetMiB", w."PressureJson", w."PollJson",
               w."DesiredAgentJson", w."PolicyRevision", w."CurrentInstanceId", w."DeclinedInARow",
               COALESCE((SELECT SUM(j."Weight") FROM "RemoteJobs" j
                         WHERE j."State" = 'Leased' AND j."LeaseOwner" = w."Id"), 0)::int AS "LeasedWeight",
               COALESCE((SELECT jsonb_object_agg(t."Kind", t."Weight")
                         FROM (SELECT j."Kind", SUM(j."Weight")::int AS "Weight" FROM "RemoteJobs" j
                               WHERE j."State" = 'Leased' AND j."LeaseOwner" = w."Id" GROUP BY j."Kind") t),
                        '{}'::jsonb) AS "LeasedByKind",
               (w."LastHeartbeatAt" IS NOT NULL
                AND w."LastHeartbeatAt" >= clock_timestamp() - make_interval(secs => @stale)) AS "HeartbeatFresh",
               (w."CurrentInstanceId" IS NOT NULL AND w."CurrentInstanceId" <> @instance
                AND w."LastHeartbeatAt" IS NOT NULL
                AND w."LastHeartbeatAt" >= clock_timestamp() - make_interval(secs => 60)) AS "OtherInstanceRecent"
        FROM "RemoteWorkers" w
        WHERE w."Id" = @node
        FOR UPDATE OF w;
        """;

    private static readonly string ReplaySql =
        "SELECT " + RemoteJobRow.Columns("j")
        + ", (extract(epoch from (j.\"LeaseExpiresAt\" - clock_timestamp())) * 1000)::bigint AS \"LeaseRemainingMs\""
        + " FROM \"RemoteJobs\" j"
        + " WHERE j.\"State\" = 'Leased' AND j.\"LeaseOwner\" = @node AND j.\"ClaimNonce\" = @claim"
        + " AND j.\"LeaseExpiresAt\" > clock_timestamp();";

    private static readonly string PickSql = """
        WITH picked AS (
            SELECT j."Id"
            FROM "RemoteJobs" j
            WHERE j."State" = 'Queued'
              AND j."NextAttemptAt" <= clock_timestamp()
              AND j."Attempt" < j."MaxAttempts"
              AND (j."TargetNodeId" IS NULL OR j."TargetNodeId" = @node)
              AND (j."Purpose" <> 'canary' OR j."TargetNodeId" = @node)
              AND (j."Kind" || '|' || j."Purpose") = ANY(@kindPurposes)
              AND EXISTS (SELECT 1 FROM unnest(@kinds, @schemas, @engines, @weights) AS a(k, s, e, w)
                          WHERE a.k = j."Kind" AND a.s = j."SchemaVersion" AND a.e = j."EngineVersion"
                            AND j."Weight" <= a.w)
              AND COALESCE((j."LimitsJson"->>'memMiB')::int, 0) + COALESCE((j."LimitsJson"->>'tmpMiB')::int, 0) <= @memFree
              AND COALESCE((j."LimitsJson"->>'cpuMilli')::int, 0) <= @cpuFree
              AND COALESCE((j."LimitsJson"->>'tmpMiB')::int, 0) <= @tmpFree
            ORDER BY j."Priority" DESC, j."NextAttemptAt", j."CreatedAt"
            LIMIT 1
            FOR UPDATE SKIP LOCKED)
        UPDATE "RemoteJobs" r SET
            "State"          = 'Leased',
            "LeaseOwner"     = @node,
            "ClaimNonce"     = @claim,
            "Attempt"        = r."Attempt" + 1,
            "FenceToken"     = r."FenceToken" + 1,
            "LeasedAt"       = clock_timestamp(),
            "DeadlineAt"     = clock_timestamp() + make_interval(secs => COALESCE((r."LimitsJson"->>'deadlineSeconds')::int, 300)),
            "LeaseExpiresAt" = LEAST(clock_timestamp() + make_interval(secs => @leaseSeconds),
                                     clock_timestamp() + make_interval(secs => COALESCE((r."LimitsJson"->>'deadlineSeconds')::int, 300))),
            "UpdatedAt"      = clock_timestamp()
        FROM picked
        WHERE r."Id" = picked."Id"
        RETURNING
        """
        + " " + RemoteJobRow.Columns("r")
        + ", (extract(epoch from (r.\"LeaseExpiresAt\" - clock_timestamp())) * 1000)::bigint AS \"LeaseRemainingMs\";";

    private const string UpdateNodeSql = """
        UPDATE "RemoteWorkers" SET
            "LastSeenAt"        = clock_timestamp(),
            "LastClaimAt"       = CASE WHEN @picked THEN clock_timestamp() ELSE "LastClaimAt" END,
            "CurrentInstanceId" = @instance,
            "AgentVersion"      = @agentVersion,
            "AgentImageDigest"  = @digest,
            "ProtocolVersion"   = @protocol,
            "KindsJson"         = @kinds,
            "AppliedRevision"   = @applied,
            "DeclinedInARow"    = @declined,
            "UpdatedAt"         = clock_timestamp()
        WHERE "Id" = @node;
        """;

    private sealed record LockedNode(
        RemoteWorker Node,
        int LeasedWeight,
        IReadOnlyDictionary<string, int> LeasedByKind,
        bool HeartbeatFresh,
        bool OtherInstanceRecent);

    public async Task<RemoteClaimOutcome> ClaimAsync(
        RemoteWorker authenticatedNode,
        RemoteClaimRequestDto request,
        CancellationToken ct)
    {
        PgScope.RequireNpgsql(db);
        var options = settings.Current;
        var flagSnapshot = await flags.GetAsync(ct);
        var instanceId = request.InstanceId!.Value.ToString("D");
        var claimId = request.ClaimId!.Value.ToString("D");

        // Cheap gates from the authentication snapshot (no transaction). The same gates run again under the
        // node lock below, which is authoritative.
        var pre = EvaluateGate(authenticatedNode.Status, authenticatedNode.Paused, flagSnapshot.Master,
            heartbeatFresh: IsFreshApp(authenticatedNode.LastHeartbeatAt, options.NodeStaleAfterSeconds),
            otherInstanceRecent: authenticatedNode.CurrentInstanceId is not null
                && authenticatedNode.CurrentInstanceId != instanceId
                && IsFreshApp(authenticatedNode.LastHeartbeatAt, 60));
        if (pre.Error is not null) return RemoteClaimOutcome.Fail(pre.Error);
        if (pre.NoContentReason is not null) return RemoteClaimOutcome.NoContent(pre.NoContentReason, authenticatedNode);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var locked = await LockNodeAsync(authenticatedNode.Id, instanceId, options, ct);
        if (locked is null) return RemoteClaimOutcome.Fail(RemoteProblems.Unauthorized());
        var node = locked.Node;

        var gate = EvaluateGate(node.Status, node.Paused, flagSnapshot.Master, locked.HeartbeatFresh, locked.OtherInstanceRecent);
        if (gate.Error is not null) return RemoteClaimOutcome.Fail(gate.Error, node);
        if (gate.NoContentReason is not null) return RemoteClaimOutcome.NoContent(gate.NoContentReason, node);

        // Idempotent replay: a lost response must not leak a lease for a full lease period.
        var replay = await RemoteDb.QueryFirstAsync(
            db,
            ReplaySql,
            parameters =>
            {
                parameters.AddWithValue("node", node.Id);
                parameters.AddWithValue("claim", claimId);
            },
            ReadLeased,
            ct);
        if (replay is not null)
        {
            await UpdateNodeAsync(node.Id, instanceId, request, picked: true, declined: 0, ct);
            await transaction.CommitAsync(ct);
            return RemoteClaimOutcome.Job(replay, node);
        }

        var plan = RemoteClaimPlanner.Plan(
            RemoteNodePolicyView.From(node), request.Kinds!, request.Capacity!, flagSnapshot, options,
            locked.LeasedWeight, locked.LeasedByKind);

        if (plan.IsEmpty)
        {
            await UpdateNodeAsync(node.Id, instanceId, request, picked: false, declined: node.DeclinedInARow, ct);
            await transaction.CommitAsync(ct);
            return node.Status == RemoteNodeStatus.Quarantined
                ? RemoteClaimOutcome.Fail(RemoteProblems.Forbidden("node_quarantined", "This node is quarantined."), node)
                : RemoteClaimOutcome.NoContent(plan.EmptyReason ?? "no_capacity", node);
        }

        var capacity = request.Capacity!;
        if (options.FairShareGate && node.Status == RemoteNodeStatus.Active
            && await ShouldDeclineForFairShareAsync(node, locked, plan, capacity, options, ct))
        {
            await UpdateNodeAsync(node.Id, instanceId, request, picked: false, declined: node.DeclinedInARow + 1, ct);
            await transaction.CommitAsync(ct);
            return RemoteClaimOutcome.NoContent("fair_share", node);
        }

        var memFree = (int)Math.Min(Math.Min(capacity.MemBudgetFreeMiB ?? 0, node.MemBudgetMiB), int.MaxValue);
        var cpuFree = (int)Math.Min(Math.Min(capacity.CpuBudgetFreeMilli ?? 0, node.CpuBudgetMilli), int.MaxValue);
        var tmpFree = (int)Math.Min(Math.Min(capacity.TmpFreeMiB ?? 0, node.TmpBudgetMiB), int.MaxValue);

        var leased = await RemoteDb.QueryFirstAsync(
            db,
            PickSql,
            parameters =>
            {
                parameters.AddWithValue("node", node.Id);
                parameters.AddWithValue("claim", claimId);
                parameters.AddWithValue("kindPurposes", plan.Entries
                    .SelectMany(entry => entry.Purposes.Select(purpose => entry.Kind + "|" + purpose)).Distinct().ToArray());
                parameters.AddWithValue("kinds", plan.Entries.Select(entry => entry.Kind).ToArray());
                parameters.AddWithValue("schemas", plan.Entries.Select(entry => entry.SchemaVersion).ToArray());
                parameters.AddWithValue("engines", plan.Entries.Select(entry => entry.EngineVersion).ToArray());
                parameters.AddWithValue("weights", plan.Entries.Select(entry => entry.MaxWeight).ToArray());
                parameters.AddWithValue("memFree", memFree);
                parameters.AddWithValue("cpuFree", cpuFree);
                parameters.AddWithValue("tmpFree", tmpFree);
                parameters.AddWithValue("leaseSeconds", options.LeaseSeconds);
            },
            ReadLeased,
            ct);

        await UpdateNodeAsync(node.Id, instanceId, request, picked: leased is not null, declined: 0, ct);
        await transaction.CommitAsync(ct);

        if (leased is not null) return RemoteClaimOutcome.Job(leased, node);

        return node.Status == RemoteNodeStatus.Quarantined
            ? RemoteClaimOutcome.Fail(RemoteProblems.Forbidden("node_quarantined", "This node is quarantined."), node)
            : RemoteClaimOutcome.NoContent("no_work", node);
    }

    /// <summary>
    /// The status table of OET-RWP/1 4.0 for <c>claim</c>, the instance rule and the staleness/drain/pause gates, in
    /// the specified order. Returns neither error nor reason when the claim may proceed.
    /// </summary>
    internal static (RemoteProblemResult? Error, string? NoContentReason) EvaluateGate(
        string status,
        bool paused,
        bool masterEnabled,
        bool heartbeatFresh,
        bool otherInstanceRecent)
    {
        switch (status)
        {
            case RemoteNodeStatus.Revoked:
                return (RemoteProblems.Unauthorized(), null);
            case RemoteNodeStatus.Pending:
                return (RemoteProblems.Forbidden("node_not_active", "This node has not been activated."), null);
            case RemoteNodeStatus.Disabled:
                return (RemoteProblems.Forbidden("node_disabled", "This node is disabled."), null);
        }

        if (otherInstanceRecent)
        {
            return (RemoteProblems.Conflict("instance_superseded", "Another instance of this node is active."), null);
        }

        if (!masterEnabled) return (null, "disabled");
        if (!heartbeatFresh) return (null, "heartbeat_stale");
        if (status == RemoteNodeStatus.Draining) return (null, "draining");
        if (paused) return (null, "paused");
        return (null, null);
    }

    private bool IsFreshApp(DateTimeOffset? lastHeartbeat, int seconds)
        => lastHeartbeat is { } at && timeProvider.GetUtcNow() - at <= TimeSpan.FromSeconds(seconds);

    private async Task<LockedNode?> LockNodeAsync(string nodeId, string instanceId, RemoteJobsOptions options, CancellationToken ct)
        => await RemoteDb.QueryFirstAsync(
            db,
            LockNodeSql,
            parameters =>
            {
                parameters.AddWithValue("node", nodeId);
                parameters.AddWithValue("instance", instanceId);
                parameters.AddWithValue("stale", options.NodeStaleAfterSeconds);
            },
            reader =>
            {
                var leasedByKind = new Dictionary<string, int>(StringComparer.Ordinal);
                var leasedJson = RemoteDb.Str(reader, "LeasedByKind");
                try
                {
                    using var document = JsonDocument.Parse(leasedJson);
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (property.Value.TryGetInt32(out var weight)) leasedByKind[property.Name] = weight;
                    }
                }
                catch (JsonException)
                {
                    // An unreadable aggregate cannot happen (it is built by jsonb_object_agg); treat as nothing leased.
                }

                var node = new RemoteWorker
                {
                    Id = RemoteDb.Str(reader, "Id"),
                    Status = RemoteDb.Str(reader, "Status"),
                    Paused = RemoteDb.Bool(reader, "Paused"),
                    AllowedKinds = reader.GetFieldValue<string[]>(reader.GetOrdinal("AllowedKinds")),
                    MaxConcurrency = RemoteDb.Int(reader, "MaxConcurrency"),
                    KindLimitsJson = RemoteDb.Str(reader, "KindLimitsJson"),
                    CpuBudgetMilli = RemoteDb.Int(reader, "CpuBudgetMilli"),
                    MemBudgetMiB = RemoteDb.Int(reader, "MemBudgetMiB"),
                    TmpBudgetMiB = RemoteDb.Int(reader, "TmpBudgetMiB"),
                    PressureJson = RemoteDb.Str(reader, "PressureJson"),
                    PollJson = RemoteDb.Str(reader, "PollJson"),
                    DesiredAgentJson = RemoteDb.Str(reader, "DesiredAgentJson"),
                    PolicyRevision = RemoteDb.Long(reader, "PolicyRevision"),
                    CurrentInstanceId = RemoteDb.StrN(reader, "CurrentInstanceId"),
                    DeclinedInARow = RemoteDb.Int(reader, "DeclinedInARow"),
                };

                return new LockedNode(
                    node,
                    RemoteDb.Int(reader, "LeasedWeight"),
                    leasedByKind,
                    RemoteDb.Bool(reader, "HeartbeatFresh"),
                    RemoteDb.Bool(reader, "OtherInstanceRecent"));
            },
            ct);

    private static RemoteLeasedJob ReadLeased(NpgsqlDataReader reader)
        => new(RemoteJobRow.Read(reader), RemoteDb.Long(reader, "LeaseRemainingMs"));

    private Task<int> UpdateNodeAsync(
        string nodeId,
        string instanceId,
        RemoteClaimRequestDto request,
        bool picked,
        int declined,
        CancellationToken ct)
        => RemoteDb.ExecuteAsync(
            db,
            UpdateNodeSql,
            parameters =>
            {
                parameters.AddWithValue("picked", picked);
                parameters.AddWithValue("instance", instanceId);
                parameters.AddWithValue("agentVersion", request.Agent!.Version!);
                parameters.AddWithValue("digest", request.Agent.ImageDigest!);
                parameters.AddWithValue("protocol", request.Agent.Protocol!.Value);
                parameters.Add(new NpgsqlParameter("kinds", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(request.Kinds!.Select(offer => new
                {
                    kind = offer.Kind,
                    schemaVersions = offer.SchemaVersions,
                    engineVersion = offer.EngineVersion,
                }), RemoteJson.Response) });
                parameters.AddWithValue("applied", request.AppliedRevision!.Value);
                parameters.AddWithValue("declined", declined);
                parameters.AddWithValue("node", nodeId);
            },
            ct);

    private async Task<bool> ShouldDeclineForFairShareAsync(
        RemoteWorker node,
        LockedNode locked,
        ClaimPlan plan,
        RemoteCapacityDto capacity,
        RemoteJobsOptions options,
        CancellationToken ct)
    {
        var effective = (int)Math.Min(capacity.EffectiveConcurrency ?? 0, int.MaxValue);
        var selfNorm = RemoteFairShare.Norm(locked.LeasedWeight, node.MaxConcurrency, effective);
        var eligibleKinds = plan.Entries.Select(entry => entry.Kind).ToArray();
        var kindPurposes = plan.Entries
            .SelectMany(entry => entry.Purposes.Select(purpose => entry.Kind + "|" + purpose)).Distinct().ToArray();

        var peers = await RemoteDb.QueryAsync(
            db,
            """
            SELECT w."Id", w."MaxConcurrency", w."LastCapacityJson", COALESCE(l."Weight", 0) AS "Leased"
            FROM "RemoteWorkers" w
            LEFT JOIN (SELECT "LeaseOwner", SUM("Weight")::int AS "Weight"
                       FROM "RemoteJobs" WHERE "State" = 'Leased' GROUP BY "LeaseOwner") l ON l."LeaseOwner" = w."Id"
            WHERE w."Status" = 'Active' AND w."Paused" = false AND w."Id" <> @node
              AND w."LastHeartbeatAt" >= clock_timestamp() - make_interval(secs => @stale)
              AND w."AllowedKinds" && @kinds;
            """,
            parameters =>
            {
                parameters.AddWithValue("node", node.Id);
                parameters.AddWithValue("stale", options.NodeStaleAfterSeconds);
                parameters.AddWithValue("kinds", eligibleKinds);
            },
            reader =>
            {
                var max = RemoteDb.Int(reader, "MaxConcurrency");
                var leasedWeight = RemoteDb.Int(reader, "Leased");
                var peerEffective = max;
                var capacityJson = RemoteDb.StrN(reader, "LastCapacityJson");
                if (capacityJson is not null)
                {
                    try
                    {
                        using var document = JsonDocument.Parse(capacityJson);
                        if (document.RootElement.TryGetProperty("effectiveConcurrency", out var value) && value.TryGetInt32(out var parsed))
                        {
                            peerEffective = parsed;
                        }
                    }
                    catch (JsonException)
                    {
                        // Unreadable capacity: assume the policy maximum.
                    }
                }

                var slots = Math.Min(max, peerEffective);
                return new FairSharePeer(
                    RemoteDb.Str(reader, "Id"),
                    Math.Max(0, slots - leasedWeight),
                    RemoteFairShare.Norm(leasedWeight, max, peerEffective));
            },
            ct);

        if (peers.Count == 0) return false;

        var due = await RemoteDb.ScalarAsync(
            db,
            """
            SELECT COUNT(*)::int FROM "RemoteJobs" j
            WHERE j."State" = 'Queued' AND j."NextAttemptAt" <= clock_timestamp()
              AND (j."TargetNodeId" IS NULL OR j."TargetNodeId" = @node)
              AND (j."Kind" || '|' || j."Purpose") = ANY(@kindPurposes);
            """,
            parameters =>
            {
                parameters.AddWithValue("node", node.Id);
                parameters.AddWithValue("kindPurposes", kindPurposes);
            },
            ct);

        var dueCount = due is null ? 0 : Convert.ToInt32(due, System.Globalization.CultureInfo.InvariantCulture);
        return RemoteFairShare.ShouldDecline(selfNorm, peers, dueCount, node.DeclinedInARow);
    }
}
