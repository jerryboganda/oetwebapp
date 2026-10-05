using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fleet.Core.Policy;
using Fleet.Manager.Api;

namespace Fleet.Manager.Tests.Infrastructure;

public sealed class FakeApiToken
{
    public string Id { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public bool Revoked { get; set; }
}

public sealed record FakeCanary(DateTimeOffset At, bool Ok);

public sealed class FakeApiNode
{
    public string Id { get; init; } = string.Empty;

    public string NodeRef { get; init; } = string.Empty;

    public string Status { get; set; } = "Pending";

    public int PolicyRevision { get; set; } = 1;

    public NodePolicy? Policy { get; set; }

    public RegisterPolicyDto? InitialPolicy { get; init; }

    public List<FakeApiToken> Tokens { get; } = new();

    public string? AgentDigest { get; set; }

    public int AgentProtocol { get; set; } = 1;

    public bool AutoHeartbeat { get; set; }

    public DateTimeOffset? LastHeartbeatAt { get; set; }

    public int LeaseCount { get; set; }

    public DateTimeOffset? CanaryRequestedAt { get; set; }

    public FakeCanary? LastCanary { get; set; }

    public DateTimeOffset StatusChangedAt { get; set; }

    public int AppliedRevision { get; set; }

    /// <summary>The load the node's last heartbeat reported (the pressure governor reads it).</summary>
    public double CpuPct { get; set; } = 20;

    public double MemFreePct { get; set; } = 80;
}

/// <summary>
/// The OET API service plane as the manager sees it (OET-RWP/1 section 7.1), reduced to the node state machine,
/// token issuance/rotation/revocation, optimistic policy revisions and the canary. It also plays the agent's side of
/// the job plane: <see cref="AgentHeartbeat"/> is what a started agent does with its node token.
/// </summary>
public sealed class FakeFleetApi : IFleetApi
{
    public const string Engine = "pdfpig:1.7.0-custom-5/oet-text:1";

    private readonly object _gate = new();
    private readonly ManualTimeProvider _time;
    private readonly Dictionary<string, FakeApiNode> _nodes = new(StringComparer.Ordinal);
    private int _nodeCounter;
    private int _tokenCounter;

    public FakeFleetApi(ManualTimeProvider time)
    {
        _time = time;
    }

    // ---- controls ----
    public bool Reachable { get; set; } = true;

    public bool AutoCompleteCanary { get; set; } = true;

    public bool CanaryResultOk { get; set; } = true;

    /// <summary>The next register call creates the node and the token, then the response is lost (the caller sees "unreachable").</summary>
    public bool DropNextRegisterResponse { get; set; }

    /// <summary>The next register call creates the node and the token, then the whole manager process is killed.</summary>
    public CancellationTokenSource? CrashAfterRegister { get; set; }

    /// <summary>Every read of a draining node finishes this many leases, so a "wait for the leases" step sees them drain over several polls.</summary>
    public int LeasesFinishPerPoll { get; set; }

    /// <summary>The next policy write finds the revision bumped by somebody else (the optimistic-concurrency conflict).</summary>
    public bool BumpRevisionOnNextPut { get; set; }

    /// <summary>Policy writes for these node references are refused by the API (<c>policy_rejected</c>).</summary>
    public HashSet<string> RejectPolicyForNodeRefs { get; } = new(StringComparer.Ordinal);

    public int RegisterCalls { get; private set; }

    public int RotateCalls { get; private set; }

    public int PutPolicyCalls { get; private set; }

    public List<string> Calls { get; } = new();

    public IReadOnlyList<FakeApiNode> Nodes
    {
        get
        {
            lock (_gate)
            {
                return _nodes.Values.ToList();
            }
        }
    }

    public FakeApiNode? NodeByRef(string nodeRef)
    {
        lock (_gate)
        {
            return _nodes.Values.FirstOrDefault(n => n.NodeRef == nodeRef);
        }
    }

    /// <summary>What a started agent does: heartbeat with its node token. Returns false (HTTP 401) for an invalid, expired or revoked token.</summary>
    public bool AgentHeartbeat(string nodeId, string tokenValue, string digest, int protocol = 1)
    {
        lock (_gate)
        {
            if (!_nodes.TryGetValue(nodeId, out var node) || node.Status == "Revoked")
            {
                return false;
            }

            var token = node.Tokens.FirstOrDefault(t => t.Value == tokenValue);
            if (token is null || token.Revoked || token.ExpiresAt <= _time.GetUtcNow())
            {
                return false;
            }

            node.AgentDigest = digest;
            node.AgentProtocol = protocol;
            node.AutoHeartbeat = true;
            node.LastHeartbeatAt = _time.GetUtcNow();
            node.AppliedRevision = node.PolicyRevision;
            if (node.Status == "Pending")
            {
                node.Status = "Probation";
                node.StatusChangedAt = _time.GetUtcNow();
            }

            return true;
        }
    }

    /// <summary>Whether a token (by its full value) would still authenticate.</summary>
    public bool TokenAccepted(string nodeId, string tokenValue)
    {
        lock (_gate)
        {
            return _nodes.TryGetValue(nodeId, out var node)
                && node.Status != "Revoked"
                && node.Tokens.Any(t => t.Value == tokenValue && !t.Revoked && t.ExpiresAt > _time.GetUtcNow());
        }
    }

    // ---- IFleetApi ----

    public Task<RegisterNodeResult> RegisterNodeAsync(RegisterNodeRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Begin("register");
            RegisterCalls++;
            var existing = _nodes.Values.FirstOrDefault(n => n.NodeRef == request.NodeRef);
            if (existing is not null)
            {
                return Task.FromResult(new RegisterNodeResult(Ref(existing), null, false));
            }

            var node = new FakeApiNode
            {
                Id = "rw_" + (++_nodeCounter).ToString("x26"),
                NodeRef = request.NodeRef,
                Status = "Pending",
                InitialPolicy = request.Policy,
                StatusChangedAt = _time.GetUtcNow(),
            };
            var token = NewToken(node, TimeSpan.FromDays(request.TokenTtlDays ?? 30));
            _nodes[node.Id] = node;

            if (CrashAfterRegister is { } crash)
            {
                CrashAfterRegister = null;
                crash.Cancel();
                throw new OperationCanceledException(crash.Token);
            }

            if (DropNextRegisterResponse)
            {
                DropNextRegisterResponse = false;
                throw new FleetApiException(0, FleetApiException.Unreachable, null, true, "response lost");
            }

            return Task.FromResult(new RegisterNodeResult(Ref(node), new ApiToken(token.Id, token.Value, token.ExpiresAt), true));
        }
    }

    public Task<IReadOnlyList<ApiNode>> ListNodesAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Begin("list");
            return Task.FromResult<IReadOnlyList<ApiNode>>(_nodes.Values.Select(Map).ToList());
        }
    }

    public Task<ApiNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Begin("get");
            if (_nodes.TryGetValue(nodeId, out var draining) && draining.Status == "Draining" && LeasesFinishPerPoll > 0 && draining.LeaseCount > 0)
            {
                draining.LeaseCount = Math.Max(0, draining.LeaseCount - LeasesFinishPerPoll);
            }

            return Task.FromResult(_nodes.TryGetValue(nodeId, out var node) ? Map(node) : null);
        }
    }

    public Task<int> PutPolicyAsync(string nodeId, NodePolicy policy, int expectedRevision, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Begin("put-policy");
            PutPolicyCalls++;
            var node = Require(nodeId);
            if (RejectPolicyForNodeRefs.Contains(node.NodeRef))
            {
                throw Error(HttpStatusCode.UnprocessableEntity, "policy_rejected", null);
            }

            if (BumpRevisionOnNextPut)
            {
                BumpRevisionOnNextPut = false;
                node.PolicyRevision++;
            }

            if (PolicyValidator.Validate(policy).Count > 0)
            {
                throw Error(HttpStatusCode.UnprocessableEntity, "policy_invalid", null);
            }

            if (expectedRevision != node.PolicyRevision)
            {
                throw Error(HttpStatusCode.Conflict, "policy_revision_conflict", node.PolicyRevision.ToString());
            }

            node.PolicyRevision++;
            node.Policy = policy;
            if (node.AutoHeartbeat)
            {
                node.AppliedRevision = node.PolicyRevision;
            }

            return Task.FromResult(node.PolicyRevision);
        }
    }

    public Task EnableAsync(string nodeId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Begin("enable");
            var node = Require(nodeId);
            if (node.Status is "Probation" or "Quarantined")
            {
                if (node.LastCanary is not { Ok: true } canary || canary.At < node.StatusChangedAt)
                {
                    throw Error(HttpStatusCode.Conflict, "canary_required", node.Status);
                }
            }
            else if (node.Status is not ("Disabled" or "Draining"))
            {
                throw Error(HttpStatusCode.Conflict, "invalid_transition", node.Status);
            }

            Set(node, "Active");
            return Task.CompletedTask;
        }
    }

    public Task DrainAsync(string nodeId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Begin("drain");
            var node = Require(nodeId);
            if (node.Status != "Active")
            {
                throw Error(HttpStatusCode.Conflict, "invalid_transition", node.Status);
            }

            Set(node, "Draining");
            return Task.CompletedTask;
        }
    }

    public Task DisableAsync(string nodeId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Begin("disable");
            var node = Require(nodeId);
            if (node.Status is not ("Active" or "Draining"))
            {
                throw Error(HttpStatusCode.Conflict, "invalid_transition", node.Status);
            }

            Set(node, "Disabled");
            return Task.CompletedTask;
        }
    }

    public Task QuarantineAsync(string nodeId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Begin("quarantine");
            var node = Require(nodeId);
            if (node.Status == "Revoked")
            {
                throw Error(HttpStatusCode.Conflict, "invalid_transition", node.Status);
            }

            Set(node, "Quarantined");
            return Task.CompletedTask;
        }
    }

    public Task RevokeAsync(string nodeId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Begin("revoke");
            var node = Require(nodeId);
            if (node.Status == "Revoked")
            {
                throw Error(HttpStatusCode.Conflict, "invalid_transition", node.Status);
            }

            foreach (var token in node.Tokens)
            {
                token.Revoked = true;
            }

            node.AutoHeartbeat = false;
            Set(node, "Revoked");
            return Task.CompletedTask;
        }
    }

    public Task<ApiToken> RotateTokenAsync(string nodeId, int graceSeconds, int ttlDays, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Begin("rotate");
            RotateCalls++;
            var node = Require(nodeId);
            var now = _time.GetUtcNow();
            if (node.Tokens.Count(t => !t.Revoked && t.ExpiresAt > now) >= 3)
            {
                throw Error(HttpStatusCode.Conflict, "too_many_credentials", null);
            }

            var token = NewToken(node, TimeSpan.FromDays(ttlDays));
            foreach (var other in node.Tokens.Where(t => t != token && !t.Revoked))
            {
                if (graceSeconds <= 0)
                {
                    other.Revoked = true;
                }
                else
                {
                    var graceEnds = now.AddSeconds(graceSeconds);
                    other.ExpiresAt = other.ExpiresAt < graceEnds ? other.ExpiresAt : graceEnds;
                }
            }

            node.PolicyRevision++;
            return Task.FromResult(new ApiToken(token.Id, token.Value, token.ExpiresAt));
        }
    }

    public Task<string> EnqueueCanaryAsync(string nodeId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Begin("canary");
            var node = Require(nodeId);
            if (node.CanaryRequestedAt is not null)
            {
                throw Error(HttpStatusCode.Conflict, "canary_in_progress", null);
            }

            node.CanaryRequestedAt = _time.GetUtcNow();
            return Task.FromResult("rj_" + (++_tokenCounter).ToString("x26"));
        }
    }

    public Task<ApiStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Begin("status");
            return Task.FromResult(new ApiStatus(new[] { "pdf.extract" }, 1, 1));
        }
    }

    public Task<JsonElement> GetStatsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Begin("stats");
            using var document = JsonDocument.Parse("{\"queued\":0}");
            return Task.FromResult(document.RootElement.Clone());
        }
    }

    // ---- internals ----

    private void Begin(string call)
    {
        Calls.Add(call);
        if (!Reachable)
        {
            throw new FleetApiException(0, FleetApiException.Unreachable, null, true, "the API is unreachable");
        }
    }

    private FakeApiNode Require(string nodeId) =>
        _nodes.TryGetValue(nodeId, out var node) ? node : throw Error(HttpStatusCode.NotFound, "node_not_found", null);

    private static FleetApiException Error(HttpStatusCode status, string code, string? reason) =>
        new(status, code, reason, false, code);

    private void Set(FakeApiNode node, string status)
    {
        node.Status = status;
        node.StatusChangedAt = _time.GetUtcNow();
        node.PolicyRevision++;
        if (node.AutoHeartbeat)
        {
            node.AppliedRevision = node.PolicyRevision;
        }
    }

    private FakeApiToken NewToken(FakeApiNode node, TimeSpan ttl)
    {
        var id = (++_tokenCounter).ToString("x16");
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("secret:" + node.Id + ":" + id)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = new FakeApiToken { Id = id, Value = "orw1_" + id + "_" + secret, ExpiresAt = _time.GetUtcNow() + ttl };
        node.Tokens.Add(token);
        return token;
    }

    private static ApiNodeRef Ref(FakeApiNode node) => new(node.Id, node.NodeRef, node.Status, node.PolicyRevision);

    private ApiNode Map(FakeApiNode node)
    {
        var now = _time.GetUtcNow();
        if (node.AutoHeartbeat && node.Status != "Revoked")
        {
            node.LastHeartbeatAt = now;
            node.AppliedRevision = node.PolicyRevision;
        }

        if (node.CanaryRequestedAt is not null && AutoCompleteCanary && node.AutoHeartbeat && node.Status is "Probation" or "Active")
        {
            node.LastCanary = new FakeCanary(now, CanaryResultOk);
            node.CanaryRequestedAt = null;
        }

        var health = node.LastHeartbeatAt is not { } beat
            ? "Unseen"
            : now - beat <= TimeSpan.FromSeconds(45) ? "Online" : now - beat <= TimeSpan.FromMinutes(10) ? "Stale" : "Offline";
        var activeTokens = node.Tokens.Where(t => !t.Revoked && t.ExpiresAt > now).ToList();
        var policy = node.Policy;
        var initial = node.InitialPolicy;
        var allowed = policy?.AllowedKinds ?? initial?.AllowedKinds ?? new[] { "pdf.extract" };
        var maxConcurrency = policy?.MaxConcurrency ?? initial?.MaxConcurrency ?? 2;

        return new ApiNode(
            node.Id,
            node.NodeRef,
            node.NodeRef,
            node.Status,
            health,
            null,
            null,
            node.LastHeartbeatAt,
            node.LastHeartbeatAt,
            null,
            node.AgentDigest is null
                ? null
                : new ApiAgentDto("1.0.0", node.AgentDigest, node.AgentProtocol, Guid.Empty.ToString(), new[] { new ApiKindDto("pdf.extract", new[] { 1 }, Engine) }),
            new ApiCapacityDto(2000, 3500, 2900, 1, 2),
            new ApiLoadDto(node.CpuPct, node.MemFreePct, "normal"),
            new ApiLeasesDto(node.LeaseCount, node.LeaseCount, 2),
            new ApiNodePolicyDto(node.PolicyRevision, allowed, maxConcurrency),
            node.AppliedRevision,
            0,
            node.LastCanary is { } canary ? new ApiCanaryDto(canary.At, canary.Ok) : null,
            new ApiTokensDto(activeTokens.Count, activeTokens.Count == 0 ? null : activeTokens.Min(t => t.ExpiresAt)));
    }
}
