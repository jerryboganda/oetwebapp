using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>Body of <c>POST /v1/internal/fleet/nodes</c> (OET-RWP/1 section 7.1.1).</summary>
public sealed class RemoteRegisterRequestDto
{
    public string? NodeRef { get; set; }
    public string? DisplayName { get; set; }
    public string? Region { get; set; }
    public string? Provider { get; set; }
    public RemotePolicyUpdateDto? Policy { get; set; }
    public int? TokenTtlDays { get; set; }
}

/// <summary>Body of <c>POST /nodes/{id}/tokens/rotate</c>.</summary>
public sealed class RemoteRotateRequestDto
{
    public int? GraceSeconds { get; set; }
    public int? TtlDays { get; set; }
}

/// <summary>Optional body of a node status action: <c>{ "reason": "..." }</c>.</summary>
public sealed class RemoteReasonRequestDto
{
    public string? Reason { get; set; }
}

/// <summary>Optional body of <c>POST /v1/admin/remote-workers/fleet-credential</c>.</summary>
public sealed class RemoteFleetCredentialRequestDto
{
    public int? GraceSeconds { get; set; }
    public int? TtlDays { get; set; }
}

/// <summary>A freshly issued credential, shown to the caller exactly once.</summary>
public sealed record RemoteIssuedToken(string Id, string Value, DateTimeOffset ExpiresAt);

public sealed record RemoteNodeResult(
    Dictionary<string, object?>? Node,
    RemoteIssuedToken? Token,
    bool Created,
    RemoteProblemResult? Error)
{
    public static RemoteNodeResult Fail(RemoteProblemResult error) => new(null, null, false, error);
}

/// <summary>What <c>workers/heartbeat</c> answered: the response body, or an error.</summary>
public sealed record RemoteNodeHeartbeatOutcome(
    Dictionary<string, object?>? Body,
    long DesiredRevision,
    RemoteProblemResult? Error);

/// <summary>
/// Everything about NODES: registration (idempotent on <c>nodeRef</c>), the status state machine, desired-state policy,
/// token issuance and rotation, the known-answer canary, and the node-level heartbeat that doubles as the capacity
/// report and the desired-state channel (OET-RWP/1 sections 3.9, 4.7 and 7). Every state change bumps the node's
/// <c>PolicyRevision</c>, writes one audit row with no secret in it, and requeues leases with a refund when a node
/// is quarantined or revoked.
/// </summary>
public sealed partial class RemoteWorkerService(
    LearnerDbContext db,
    RemoteJobsSettings settings,
    IRemoteJobFlags flags,
    IRemoteJobQueue queue,
    RemoteOrphanTracker orphans,
    RemoteAuthCache authCache,
    TimeProvider timeProvider,
    ILogger<RemoteWorkerService> logger)
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{2,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex NodeRefPattern();

    private const int MaxActiveCredentialsPerNode = 3;
    private const int CanaryPriority = 10;

    // ── registration ─────────────────────────────────────────────────────────

    public async Task<RemoteNodeResult> RegisterAsync(
        RemoteRegisterRequestDto request,
        string actorId,
        string actorName,
        CancellationToken ct)
    {
        var options = settings.Current;
        if (request is null || request.NodeRef is null || !NodeRefPattern().IsMatch(request.NodeRef))
        {
            return RemoteNodeResult.Fail(RemoteProblems.BadRequest("nodeRef must match ^[a-z0-9][a-z0-9-]{2,62}$."));
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 128)
        {
            return RemoteNodeResult.Fail(RemoteProblems.BadRequest("displayName is required (at most 128 characters)."));
        }

        if (request.Region is { Length: > 32 } || request.Provider is { Length: > 64 })
        {
            return RemoteNodeResult.Fail(RemoteProblems.BadRequest("region (32) or provider (64) is too long."));
        }

        var ttlDays = request.TokenTtlDays ?? options.TokenTtlDays;
        if (ttlDays is < 1 or > 365) return RemoteNodeResult.Fail(RemoteProblems.BadRequest("tokenTtlDays must be 1..365."));

        var policy = request.Policy ?? new RemotePolicyUpdateDto();
        var policyError = RemotePolicyValidation.Validate(policy, 2, 5120, 3072);
        if (policyError is not null) return RemoteNodeResult.Fail(RemoteProblems.Unprocessable("policy_invalid", policyError));

        var existing = await db.RemoteWorkers.AsNoTracking().FirstOrDefaultAsync(w => w.NodeRef == request.NodeRef, ct);
        if (existing is not null) return await ExistingAsync(existing, ct);

        var now = timeProvider.GetUtcNow();
        var node = new RemoteWorker
        {
            Id = RemoteIds.NewNodeId(now),
            NodeRef = request.NodeRef,
            DisplayName = request.DisplayName.Trim(),
            Status = RemoteNodeStatus.Pending,
            StatusReason = "registered",
            StatusChangedAt = now,
            Region = string.IsNullOrWhiteSpace(request.Region) ? null : request.Region.Trim(),
            Provider = string.IsNullOrWhiteSpace(request.Provider) ? null : request.Provider.Trim(),
            AllowedKinds = (policy.AllowedKinds ?? new List<string>()).Distinct(StringComparer.Ordinal).ToArray(),
            MaxConcurrency = policy.MaxConcurrency ?? 2,
            KindLimitsJson = JsonSerializer.Serialize(policy.PerKind ?? new Dictionary<string, int>()),
            CpuBudgetMilli = policy.Budgets?.CpuMilli ?? 3000,
            MemBudgetMiB = policy.Budgets?.MemMiB ?? 5120,
            TmpBudgetMiB = policy.Budgets?.TmpMiB ?? 3072,
            PolicyRevision = 1,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = actorId.Length > 64 ? actorId[..64] : actorId,
        };

        var token = RemoteTokenFormat.Generate(RemoteTokenFormat.NodeKind);
        var expiresAt = now.AddDays(ttlDays);

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.RemoteWorkers.Add(node);
            db.RemoteCredentials.Add(new RemoteCredential
            {
                TokenId = token.TokenId,
                Kind = RemoteTokenFormat.NodeKind,
                NodeId = node.Id,
                SecretHash = token.SecretHashHex,
                CreatedAt = now,
                ExpiresAt = expiresAt,
                CreatedBy = node.CreatedBy,
            });
            RemoteAudit.Add(db, actorId, actorName, "RemoteWorker.Register", RemoteAudit.ResourceNode, node.Id,
                new { nodeRef = node.NodeRef, region = node.Region, provider = node.Provider, tokenId = token.TokenId }, now);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A concurrent registration of the same nodeRef won: converge on it (the token is never returned twice).
            db.ChangeTracker.Clear();
            var winner = await db.RemoteWorkers.AsNoTracking().FirstOrDefaultAsync(w => w.NodeRef == request.NodeRef, ct);
            if (winner is null) throw;
            return await ExistingAsync(winner, ct);
        }

        var view = await BuildViewAsync(node, ct);
        return new RemoteNodeResult(view, new RemoteIssuedToken(token.TokenId, token.Token, expiresAt), true, null);
    }

    private async Task<RemoteNodeResult> ExistingAsync(RemoteWorker existing, CancellationToken ct)
        => new(await BuildViewAsync(existing, ct), null, false, null);

    // ── reads ────────────────────────────────────────────────────────────────

    public async Task<Dictionary<string, object?>?> GetViewAsync(string nodeId, CancellationToken ct)
    {
        var node = await db.RemoteWorkers.AsNoTracking().FirstOrDefaultAsync(w => w.Id == nodeId, ct);
        return node is null ? null : await BuildViewAsync(node, ct);
    }

    public async Task<List<Dictionary<string, object?>>> ListViewsAsync(string? status, int limit, CancellationToken ct)
    {
        IQueryable<RemoteWorker> query = db.RemoteWorkers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(w => w.Status == status);
        var nodes = await query.OrderBy(w => w.NodeRef).Take(Math.Clamp(limit, 1, 200)).ToListAsync(ct);

        var aggregates = await LoadAggregatesAsync(nodes.Select(n => n.Id).ToArray(), ct);
        var now = timeProvider.GetUtcNow();
        var options = settings.Current;
        return nodes
            .Select(node => RemoteNodeView.Build(
                node, aggregates.TryGetValue(node.Id, out var value) ? value : RemoteNodeAggregates.None, now, options))
            .ToList();
    }

    private async Task<Dictionary<string, object?>> BuildViewAsync(RemoteWorker node, CancellationToken ct)
    {
        var aggregates = await LoadAggregatesAsync([node.Id], ct);
        return RemoteNodeView.Build(
            node,
            aggregates.TryGetValue(node.Id, out var value) ? value : RemoteNodeAggregates.None,
            timeProvider.GetUtcNow(),
            settings.Current);
    }

    private async Task<Dictionary<string, RemoteNodeAggregates>> LoadAggregatesAsync(string[] nodeIds, CancellationToken ct)
    {
        var result = new Dictionary<string, RemoteNodeAggregates>(StringComparer.Ordinal);
        if (nodeIds.Length == 0) return result;

        var now = timeProvider.GetUtcNow();
        var tokens = await db.RemoteCredentials.AsNoTracking()
            .Where(c => c.NodeId != null && nodeIds.Contains(c.NodeId) && c.RevokedAt == null && c.ExpiresAt > now)
            .Select(c => new { c.NodeId, c.ExpiresAt })
            .ToListAsync(ct);

        var leased = db.Database.IsNpgsql()
            ? await RemoteDb.QueryAsync(
                db,
                """
                SELECT "LeaseOwner", COUNT(*)::int AS "Count", COALESCE(SUM("Weight"), 0)::int AS "Weight"
                FROM "RemoteJobs" WHERE "State" = 'Leased' AND "LeaseOwner" = ANY(@ids) GROUP BY "LeaseOwner";
                """,
                parameters => parameters.AddWithValue("ids", nodeIds),
                reader => (Node: RemoteDb.Str(reader, "LeaseOwner"), Count: RemoteDb.Int(reader, "Count"), Weight: RemoteDb.Int(reader, "Weight")),
                ct)
            : new List<(string Node, int Count, int Weight)>();

        foreach (var nodeId in nodeIds)
        {
            var nodeTokens = tokens.Where(t => t.NodeId == nodeId).ToList();
            var lease = leased.FirstOrDefault(l => l.Node == nodeId);
            result[nodeId] = new RemoteNodeAggregates(
                lease.Node is null ? 0 : lease.Count,
                lease.Node is null ? 0 : lease.Weight,
                nodeTokens.Count,
                nodeTokens.Count == 0 ? null : nodeTokens.Min(t => t.ExpiresAt));
        }

        return result;
    }

    // ── policy ───────────────────────────────────────────────────────────────

    public async Task<(long? Revision, RemoteProblemResult? Error)> UpdatePolicyAsync(
        string nodeId,
        RemotePolicyUpdateDto policy,
        string actorId,
        string actorName,
        CancellationToken ct)
    {
        if (policy is null || policy.ExpectedRevision is null or < 1)
        {
            return (null, RemoteProblems.BadRequest("expectedRevision is required."));
        }

        var node = await db.RemoteWorkers.AsNoTracking().FirstOrDefaultAsync(w => w.Id == nodeId, ct);
        if (node is null) return (null, RemoteProblems.NodeNotFound());
        if (node.PolicyRevision != policy.ExpectedRevision)
        {
            return (null, RemoteProblems.Conflict("policy_revision_conflict", "The policy changed since it was read.", node.PolicyRevision.ToString()));
        }

        var invalid = RemotePolicyValidation.Validate(policy, node.MaxConcurrency, node.MemBudgetMiB, node.TmpBudgetMiB);
        if (invalid is not null) return (null, RemoteProblems.Unprocessable("policy_invalid", invalid));

        var sets = new List<string>();
        var parameterActions = new List<Action<NpgsqlParameterCollection>>();

        if (policy.AllowedKinds is not null)
        {
            sets.Add("\"AllowedKinds\" = @allowedKinds");
            var kinds = policy.AllowedKinds.Distinct(StringComparer.Ordinal).ToArray();
            parameterActions.Add(p => p.AddWithValue("allowedKinds", kinds));
        }

        if (policy.MaxConcurrency is not null)
        {
            sets.Add("\"MaxConcurrency\" = @maxConcurrency");
            parameterActions.Add(p => p.AddWithValue("maxConcurrency", policy.MaxConcurrency.Value));
        }

        if (policy.PerKind is not null)
        {
            sets.Add("\"KindLimitsJson\" = @perKind");
            var json = JsonSerializer.Serialize(policy.PerKind);
            parameterActions.Add(p => p.Add(new NpgsqlParameter("perKind", NpgsqlDbType.Jsonb) { Value = json }));
        }

        if (policy.Budgets is { } budgets)
        {
            if (budgets.CpuMilli is not null)
            {
                sets.Add("\"CpuBudgetMilli\" = @cpu");
                parameterActions.Add(p => p.AddWithValue("cpu", budgets.CpuMilli.Value));
            }

            if (budgets.MemMiB is not null)
            {
                sets.Add("\"MemBudgetMiB\" = @mem");
                parameterActions.Add(p => p.AddWithValue("mem", budgets.MemMiB.Value));
            }

            if (budgets.TmpMiB is not null)
            {
                sets.Add("\"TmpBudgetMiB\" = @tmp");
                parameterActions.Add(p => p.AddWithValue("tmp", budgets.TmpMiB.Value));
            }
        }

        if (policy.Pressure is { } pressure)
        {
            sets.Add("\"PressureJson\" = @pressure");
            var json = JsonSerializer.Serialize(new Dictionary<string, int>
            {
                ["reduceCpuPct"] = pressure.ReduceCpuPct!.Value,
                ["reduceMemFreePct"] = pressure.ReduceMemFreePct!.Value,
                ["restoreCpuPct"] = pressure.RestoreCpuPct!.Value,
                ["restoreMemFreePct"] = pressure.RestoreMemFreePct!.Value,
                ["restoreAfterSeconds"] = pressure.RestoreAfterSeconds!.Value,
            });
            parameterActions.Add(p => p.Add(new NpgsqlParameter("pressure", NpgsqlDbType.Jsonb) { Value = json }));
        }

        if (policy.PollSeconds is { } poll)
        {
            sets.Add("\"PollJson\" = @poll");
            var json = JsonSerializer.Serialize(new Dictionary<string, int>
            {
                ["idle"] = poll.Idle!.Value,
                ["min"] = poll.Min!.Value,
                ["max"] = poll.Max!.Value,
            });
            parameterActions.Add(p => p.Add(new NpgsqlParameter("poll", NpgsqlDbType.Jsonb) { Value = json }));
        }

        if (policy.AgentImage is { } image)
        {
            sets.Add("\"DesiredAgentJson\" = @agentImage");
            var json = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["approvedDigests"] = (image.ApprovedDigests ?? new List<string>()).Distinct(StringComparer.Ordinal).ToArray(),
                ["target"] = image.Target,
                ["minVersion"] = image.MinVersion ?? "0.0.0",
            });
            parameterActions.Add(p => p.Add(new NpgsqlParameter("agentImage", NpgsqlDbType.Jsonb) { Value = json }));
        }

        if (policy.Paused is not null)
        {
            sets.Add("\"Paused\" = @paused");
            parameterActions.Add(p => p.AddWithValue("paused", policy.Paused.Value));
        }

        var sql = "UPDATE \"RemoteWorkers\" SET " + string.Join(", ", sets.Append("\"PolicyRevision\" = \"PolicyRevision\" + 1").Append("\"UpdatedAt\" = clock_timestamp()"))
            + " WHERE \"Id\" = @id AND \"PolicyRevision\" = @expected RETURNING \"PolicyRevision\";";

        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var scalar = await RemoteDb.ScalarAsync(
            db,
            sql,
            parameters =>
            {
                foreach (var action in parameterActions) action(parameters);
                parameters.AddWithValue("id", nodeId);
                parameters.AddWithValue("expected", policy.ExpectedRevision.Value);
            },
            ct);

        if (scalar is null)
        {
            await transaction.RollbackAsync(ct);
            return (null, RemoteProblems.Conflict("policy_revision_conflict", "The policy changed since it was read."));
        }

        var revision = Convert.ToInt64(scalar, System.Globalization.CultureInfo.InvariantCulture);
        RemoteAudit.Add(db, actorId, actorName, "RemoteWorker.Policy", RemoteAudit.ResourceNode, nodeId,
            new { revision, changed = sets.Select(s => s.Split('"')[1]).ToArray() }, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        authCache.Clear();
        return (revision, null);
    }

    // ── status state machine ─────────────────────────────────────────────────

    public static readonly IReadOnlySet<string> Actions = new HashSet<string>(StringComparer.Ordinal)
    {
        "enable", "drain", "disable", "quarantine", "revoke",
    };

    /// <summary>
    /// Applies one status action under a row lock (OET-RWP/1 section 3.9). An illegal transition is
    /// <c>409 invalid_transition</c> with the current status as the reason; <c>enable</c> from Probation or Quarantined
    /// needs a passing canary newer than the last status change (<c>409 canary_required</c>). Quarantine and revoke
    /// requeue the node's leases with a refund in the same transaction; revoke also revokes every credential.
    /// </summary>
    public async Task<RemoteNodeResult> TransitionAsync(
        string nodeId,
        string action,
        string? reason,
        string actorId,
        string actorName,
        CancellationToken ct)
    {
        var options = settings.Current;
        var now = timeProvider.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var current = await LockNodeAsync(nodeId, ct);
        if (current is null) return RemoteNodeResult.Fail(RemoteProblems.NodeNotFound());

        var fromStatus = current.Status;
        string? to = action switch
        {
            "enable" when fromStatus is RemoteNodeStatus.Probation or RemoteNodeStatus.Quarantined or RemoteNodeStatus.Disabled or RemoteNodeStatus.Draining
                => RemoteNodeStatus.Active,
            "drain" when fromStatus == RemoteNodeStatus.Active => RemoteNodeStatus.Draining,
            "disable" when fromStatus is RemoteNodeStatus.Active or RemoteNodeStatus.Draining => RemoteNodeStatus.Disabled,
            "quarantine" when fromStatus is not (RemoteNodeStatus.Quarantined or RemoteNodeStatus.Revoked) => RemoteNodeStatus.Quarantined,
            "revoke" when fromStatus != RemoteNodeStatus.Revoked => RemoteNodeStatus.Revoked,
            _ => null,
        };

        // A repeated revoke converges (the manager may retry); every other illegal transition is a 409.
        if (action == "revoke" && fromStatus == RemoteNodeStatus.Revoked)
        {
            await transaction.RollbackAsync(ct);
            return new RemoteNodeResult(await BuildViewAsync(current, ct), null, false, null);
        }

        if (to is null)
        {
            return RemoteNodeResult.Fail(RemoteProblems.Conflict("invalid_transition", "The node cannot make this transition.", fromStatus));
        }

        if (action == "enable" && fromStatus is RemoteNodeStatus.Probation or RemoteNodeStatus.Quarantined)
        {
            var canaryFresh = current.LastCanaryOk == true
                && current.LastCanaryAt is { } canaryAt
                && canaryAt > current.StatusChangedAt;
            if (!canaryFresh)
            {
                return RemoteNodeResult.Fail(RemoteProblems.Conflict(
                    "canary_required", "A passing known-answer canary newer than the last status change is required.", fromStatus));
            }
        }

        if (!await RemoteNodeOps.TryChangeStatusAsync(db, nodeId, fromStatus, to, reason ?? action, ct))
        {
            return RemoteNodeResult.Fail(RemoteProblems.Conflict("invalid_transition", "The node changed concurrently.", fromStatus));
        }

        var releasedLeases = 0;
        if (to is RemoteNodeStatus.Quarantined or RemoteNodeStatus.Revoked)
        {
            releasedLeases = (await RemoteNodeOps.ReleaseLeasesAsync(
                db, nodeId, to == RemoteNodeStatus.Revoked ? "node_revoked" : "node_quarantined", options.ReleaseLimit, null, ct)).Count;
        }

        if (to == RemoteNodeStatus.Revoked) await RemoteNodeOps.RevokeCredentialsAsync(db, nodeId, ct);

        var auditAction = action switch
        {
            "enable" => "RemoteWorker.Enable",
            "drain" => "RemoteWorker.Drain",
            "disable" => "RemoteWorker.Disable",
            "quarantine" => "RemoteWorker.Quarantine",
            _ => "RemoteWorker.Revoke",
        };
        RemoteAudit.Add(db, actorId, actorName, auditAction, RemoteAudit.ResourceNode, nodeId,
            new { fromStatus, toStatus = to, reason, releasedLeases }, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        authCache.Clear();
        orphans.Forget(nodeId);

        var updated = await db.RemoteWorkers.AsNoTracking().FirstAsync(w => w.Id == nodeId, ct);
        return new RemoteNodeResult(await BuildViewAsync(updated, ct), null, false, null);
    }

    // ── tokens ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Mints a new node token. Every OTHER active credential of the node expires at most <c>graceSeconds</c> from now
    /// (0 revokes immediately); at most three credentials may be active at once.
    /// </summary>
    public async Task<(RemoteIssuedToken? Token, RemoteProblemResult? Error)> RotateTokenAsync(
        string nodeId,
        RemoteRotateRequestDto? request,
        string actorId,
        string actorName,
        CancellationToken ct)
    {
        var options = settings.Current;
        var grace = request?.GraceSeconds ?? options.TokenRotationGraceSeconds;
        var ttlDays = request?.TtlDays ?? options.TokenTtlDays;
        if (grace is < 0 or > 86400) return (null, RemoteProblems.BadRequest("graceSeconds must be 0..86400."));
        if (ttlDays is < 1 or > 365) return (null, RemoteProblems.BadRequest("ttlDays must be 1..365."));

        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var node = await LockNodeAsync(nodeId, ct);
        if (node is null) return (null, RemoteProblems.NodeNotFound());
        if (node.Status == RemoteNodeStatus.Revoked)
        {
            return (null, RemoteProblems.Conflict("invalid_transition", "A revoked node cannot receive a token.", node.Status));
        }

        var active = await db.RemoteCredentials
            .Where(c => c.NodeId == nodeId && c.RevokedAt == null && c.ExpiresAt > now)
            .ToListAsync(ct);
        if (active.Count >= MaxActiveCredentialsPerNode)
        {
            return (null, RemoteProblems.Conflict("too_many_credentials", "The node already has the maximum number of active credentials."));
        }

        var graceUntil = now.AddSeconds(grace);
        foreach (var credential in active)
        {
            if (grace == 0) credential.RevokedAt = now;
            else if (credential.ExpiresAt > graceUntil) credential.ExpiresAt = graceUntil;
        }

        var token = RemoteTokenFormat.Generate(RemoteTokenFormat.NodeKind);
        var expiresAt = now.AddDays(ttlDays);
        db.RemoteCredentials.Add(new RemoteCredential
        {
            TokenId = token.TokenId,
            Kind = RemoteTokenFormat.NodeKind,
            NodeId = nodeId,
            SecretHash = token.SecretHashHex,
            CreatedAt = now,
            ExpiresAt = expiresAt,
            CreatedBy = actorId.Length > 64 ? actorId[..64] : actorId,
        });
        RemoteAudit.Add(db, actorId, actorName, "RemoteWorker.TokenRotate", RemoteAudit.ResourceNode, nodeId,
            new { tokenId = token.TokenId, graceSeconds = grace, retiredCredentials = active.Count }, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        authCache.Clear();

        return (new RemoteIssuedToken(token.TokenId, token.Token, expiresAt), null);
    }

    // ── canary ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Enqueues the known-answer canary targeted at one node (<c>201 { jobId }</c>). At most one open canary per node
    /// (<c>409 canary_in_progress</c>). The input is the embedded fixture, never storage.
    /// </summary>
    public async Task<(string? JobId, RemoteProblemResult? Error)> EnqueueCanaryAsync(
        string nodeId,
        string actorId,
        string actorName,
        CancellationToken ct)
    {
        var options = settings.Current;
        var node = await db.RemoteWorkers.AsNoTracking().FirstOrDefaultAsync(w => w.Id == nodeId, ct);
        if (node is null) return (null, RemoteProblems.NodeNotFound());
        if (node.Status == RemoteNodeStatus.Revoked)
        {
            return (null, RemoteProblems.Conflict("invalid_transition", "A revoked node cannot be canaried.", node.Status));
        }

        var engine = RemoteJobKinds.EngineVersion(RemoteJobKinds.PdfExtract, options)
            ?? throw new InvalidOperationException("The pdf.extract engine version is not defined.");

        var open = await RemoteDb.ScalarAsync(
            db,
            """
            SELECT COUNT(*)::int FROM "RemoteJobs"
            WHERE "Purpose" = 'canary' AND "TargetNodeId" = @node AND "State" IN ('Queued', 'Leased');
            """,
            parameters => parameters.AddWithValue("node", nodeId),
            ct);
        if (open is not null && Convert.ToInt32(open, System.Globalization.CultureInfo.InvariantCulture) > 0)
        {
            return (null, RemoteProblems.Conflict("canary_in_progress", "A canary is already open for this node."));
        }

        var spec = RemoteJobKinds.Find(RemoteJobKinds.PdfExtract)!;
        var now = timeProvider.GetUtcNow();
        var run = RemoteIds.NewUlid(now);

        // The run id keeps every canary a distinct unit of work (the idempotency key would otherwise collide).
        var settingsHash = RemoteJobKeys.SettingsHash(
        [
            new KeyValuePair<string, string>("minTextLength", RemoteCanary.MinTextLength.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("mode", "flat"),
            new KeyValuePair<string, string>("provider", "pdfpig"),
            new KeyValuePair<string, string>("run", run),
            new KeyValuePair<string, string>("target", nodeId),
        ]);

        var paramsJson = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["mode"] = "flat",
            ["minTextLength"] = RemoteCanary.MinTextLength,
            ["provider"] = "pdfpig",
            ["includePages"] = false,
            ["replaceExisting"] = false,
            ["purpose"] = RemoteJobPurpose.Canary,
        });
        var inputsJson = JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, object>
            {
                ["name"] = "pdf",
                ["sizeBytes"] = RemoteCanary.PdfSize,
                ["sha256"] = RemoteCanary.PdfSha256,
                ["contentType"] = "application/pdf",
                ["storageKey"] = RemoteCanary.EmbeddedKey,
            },
        });

        var result = await queue.EnqueueAsync(
            new RemoteEnqueueRequest(
                RemoteJobKinds.PdfExtract, spec.SchemaVersion, RemoteJobPurpose.Canary, RemoteCanary.ResourceType, RemoteCanary.ResourceId,
                RemoteCanary.PdfSha256, engine, settingsHash, paramsJson, inputsJson,
                RemoteJobKinds.LimitsFor(spec, RemoteJobPurpose.Canary), RemoteAudit.FleetActor("manager"),
                Priority: CanaryPriority, TargetNodeId: nodeId, WithFallback: false),
            force: false,
            ct);

        RemoteAudit.Add(db, actorId, actorName, "RemoteWorker.Canary", RemoteAudit.ResourceNode, nodeId, new { jobId = result.JobId }, now);
        await db.SaveChangesAsync(ct);
        return (result.JobId, null);
    }

    // ── node heartbeat ───────────────────────────────────────────────────────

    private const string HeartbeatSql = """
        UPDATE "RemoteWorkers" w SET
            "LastSeenAt"      = clock_timestamp(),
            "LastHeartbeatAt" = clock_timestamp(),
            "AgentVersion"    = @agentVersion,
            "AgentImageDigest" = @digest,
            "ProtocolVersion" = @protocol,
            "KindsJson"       = @kinds,
            "LastCapacityJson" = @capacity,
            "LastLoadJson"    = COALESCE(@load, w."LastLoadJson"),
            "AppliedRevision" = @applied,
            "CurrentInstanceId" = @instance,
            "CurrentInstanceStartedAt" = CASE WHEN w."CurrentInstanceId" IS NOT DISTINCT FROM @instance
                                              THEN COALESCE(@startedAt, w."CurrentInstanceStartedAt") ELSE @startedAt END,
            "Status"          = CASE WHEN w."Status" = 'Pending' THEN 'Probation' ELSE w."Status" END,
            "StatusReason"    = CASE WHEN w."Status" = 'Pending' THEN 'first heartbeat' ELSE w."StatusReason" END,
            "StatusChangedAt" = CASE WHEN w."Status" = 'Pending' THEN clock_timestamp() ELSE w."StatusChangedAt" END,
            "PolicyRevision"  = CASE WHEN w."Status" = 'Pending' THEN w."PolicyRevision" + 1 ELSE w."PolicyRevision" END,
            "UpdatedAt"       = clock_timestamp()
        WHERE w."Id" = @id AND w."Status" <> 'Revoked'
          AND (w."CurrentInstanceId" IS NULL
               OR w."CurrentInstanceId" = @instance
               OR w."LastHeartbeatAt" IS NULL
               OR w."LastHeartbeatAt" < clock_timestamp() - make_interval(secs => 60)
               OR (@startedAt IS NOT NULL AND (w."CurrentInstanceStartedAt" IS NULL OR @startedAt > w."CurrentInstanceStartedAt")))
        RETURNING w."Id";
        """;

    /// <summary>
    /// <c>POST workers/heartbeat</c> (OET-RWP/1 section 4.7): one upsert of the node's reported state, the lease audit, the
    /// orphan reconciliation, and the desired-state response. The NEWER of two agent instances sharing a token wins
    /// (by reported start time, or because the other went silent for 60 s); the older gets <c>409 instance_superseded</c>.
    /// Never refused for status reasons (only a revoked node is turned away, with 401) so the manager keeps seeing
    /// health even while claims are off.
    /// </summary>
    public async Task<RemoteNodeHeartbeatOutcome> HeartbeatAsync(
        RemoteWorker authenticatedNode,
        RemoteNodeHeartbeatRequestDto request,
        CancellationToken ct)
    {
        PgScope.RequireNpgsql(db);
        var options = settings.Current;
        var instanceId = request.InstanceId!.Value.ToString("D");
        var startedAt = request.Agent!.StartedAt?.ToUniversalTime();
        var nodeId = authenticatedNode.Id;

        var accepted = await RemoteDb.ScalarAsync(
            db,
            HeartbeatSql,
            parameters =>
            {
                parameters.AddWithValue("agentVersion", request.Agent.Version!);
                parameters.AddWithValue("digest", request.Agent.ImageDigest!);
                parameters.AddWithValue("protocol", request.Agent.Protocol!.Value);
                parameters.Add(new NpgsqlParameter("kinds", NpgsqlDbType.Jsonb)
                {
                    Value = JsonSerializer.Serialize(request.Kinds!.Select(offer => new
                    {
                        kind = offer.Kind,
                        schemaVersions = offer.SchemaVersions,
                        engineVersion = offer.EngineVersion,
                    }), RemoteJson.Response),
                });
                parameters.Add(new NpgsqlParameter("capacity", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(request.Capacity, RemoteJson.Response) });
                parameters.Add(new NpgsqlParameter("load", NpgsqlDbType.Jsonb)
                {
                    Value = request.Load is null ? DBNull.Value : (object)JsonSerializer.Serialize(request.Load, RemoteJson.Response),
                });
                parameters.AddWithValue("applied", request.AppliedRevision!.Value);
                parameters.AddWithValue("instance", instanceId);
                parameters.Add(new NpgsqlParameter("startedAt", NpgsqlDbType.TimestampTz) { Value = (object?)startedAt ?? DBNull.Value });
                parameters.AddWithValue("id", nodeId);
            },
            ct);

        if (accepted is null)
        {
            var status = await RemoteDb.ScalarAsync(
                db,
                """SELECT "Status" FROM "RemoteWorkers" WHERE "Id" = @id;""",
                parameters => parameters.AddWithValue("id", nodeId),
                ct) as string;

            if (status is null || status == RemoteNodeStatus.Revoked)
            {
                return new RemoteNodeHeartbeatOutcome(null, 0, RemoteProblems.Unauthorized());
            }

            return new RemoteNodeHeartbeatOutcome(
                null, 0, RemoteProblems.Conflict("instance_superseded", "A newer instance of this node is active."));
        }

        var node = await db.RemoteWorkers.AsNoTracking().FirstAsync(w => w.Id == nodeId, ct);

        var leaseAudit = await AuditLeasesAsync(nodeId, request.Leases!, ct);
        await ReconcileOrphansAsync(nodeId, request.Leases!, options, ct);

        var snapshot = await flags.GetAsync(ct);
        var kindFlags = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var spec in RemoteJobKinds.All)
        {
            kindFlags[spec.Kind] = snapshot.KindEnabled(spec.Kind, RemoteJobPurpose.Apply);
        }

        kindFlags["pdf.extract.shadow"] = snapshot.KindEnabled(RemoteJobKinds.PdfExtract, RemoteJobPurpose.Shadow);

        var body = new Dictionary<string, object?>
        {
            ["serverTime"] = RemoteIds.FormatTime(timeProvider.GetUtcNow()),
            ["node"] = new Dictionary<string, object?> { ["id"] = node.Id, ["status"] = node.Status },
            ["features"] = new Dictionary<string, object?> { ["enabled"] = snapshot.Master, ["kinds"] = kindFlags },
            ["desired"] = RemoteDesiredState.Build(node),
            ["leaseAudit"] = leaseAudit,
            ["nextHeartbeatSeconds"] = options.NodeHeartbeatSeconds,
        };

        return new RemoteNodeHeartbeatOutcome(body, node.PolicyRevision, null);
    }

    private async Task<List<Dictionary<string, object?>>> AuditLeasesAsync(
        string nodeId,
        List<RemoteLeaseReportDto> reported,
        CancellationToken ct)
    {
        var audit = new List<Dictionary<string, object?>>();
        if (reported.Count == 0) return audit;

        var live = await RemoteDb.QueryAsync(
            db,
            """
            SELECT j."Id", j."FenceToken",
                   (extract(epoch from (j."LeaseExpiresAt" - clock_timestamp())) * 1000)::bigint AS "Remaining"
            FROM "RemoteJobs" j
            WHERE j."Id" = ANY(@ids) AND j."State" = 'Leased' AND j."LeaseOwner" = @node
              AND j."LeaseExpiresAt" > clock_timestamp();
            """,
            parameters =>
            {
                parameters.AddWithValue("ids", reported.Select(r => r.JobId!).ToArray());
                parameters.AddWithValue("node", nodeId);
            },
            reader => (Id: RemoteDb.Str(reader, "Id"), Fence: RemoteDb.Long(reader, "FenceToken"), Remaining: RemoteDb.Long(reader, "Remaining")),
            ct);

        foreach (var lease in reported)
        {
            var match = live.FirstOrDefault(l => l.Id == lease.JobId && l.Fence == lease.Fence);
            audit.Add(new Dictionary<string, object?>
            {
                ["jobId"] = lease.JobId,
                ["fence"] = lease.Fence,
                ["valid"] = match.Id is not null,
                ["leaseRemainingMs"] = match.Id is not null ? match.Remaining : 0L,
            });
        }

        return audit;
    }

    /// <summary>
    /// Leases the database holds for this node (older than 20 s) that the agent did not report in TWO consecutive
    /// heartbeats are released with a refund: crash recovery in about 30 s instead of a full lease period.
    /// </summary>
    private async Task ReconcileOrphansAsync(
        string nodeId,
        List<RemoteLeaseReportDto> reported,
        RemoteJobsOptions options,
        CancellationToken ct)
    {
        var missing = await RemoteDb.QueryAsync(
            db,
            """
            SELECT "Id" FROM "RemoteJobs"
            WHERE "State" = 'Leased' AND "LeaseOwner" = @node
              AND "LeasedAt" < clock_timestamp() - make_interval(secs => 20)
              AND NOT ("Id" = ANY(@reported));
            """,
            parameters =>
            {
                parameters.AddWithValue("node", nodeId);
                parameters.AddWithValue("reported", reported.Select(r => r.JobId!).ToArray());
            },
            reader => RemoteDb.Str(reader, "Id"),
            ct);

        var confirmed = orphans.ConfirmMissing(nodeId, missing);
        if (confirmed.Count == 0) return;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var released = await RemoteNodeOps.ReleaseLeasesAsync(db, nodeId, "orphan_reconciled", options.ReleaseLimit, confirmed, ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Released {Count} orphaned lease(s) of node {NodeId}.", released.Count, nodeId);
    }

    /// <summary>
    /// Takes the row lock of a node for the rest of the surrounding transaction and returns its current state, or null when it
    /// does not exist. Two statements (lock, then a plain read) keep the locking clause out of any EF-composed query.
    /// </summary>
    private async Task<RemoteWorker?> LockNodeAsync(string nodeId, CancellationToken ct)
    {
        var locked = await RemoteDb.ScalarAsync(
            db,
            """SELECT 1 FROM "RemoteWorkers" WHERE "Id" = @id FOR UPDATE;""",
            parameters => parameters.AddWithValue("id", nodeId),
            ct);
        return locked is null
            ? null
            : await db.RemoteWorkers.AsNoTracking().FirstOrDefaultAsync(w => w.Id == nodeId, ct);
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
