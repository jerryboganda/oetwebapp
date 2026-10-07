using System.Net;
using System.Text.Json;
using Fleet.Core.Placement;
using Fleet.Core.Policy;

namespace Fleet.Manager.Api;

// Wire shapes of OET-RWP/1 section 7.1 (service plane, manager -> OET API). Readers ignore unknown
// properties, so additive protocol changes never break the manager.

public sealed record ApiKindDto(string Kind, IReadOnlyList<int>? SchemaVersions, string? EngineVersion);

public sealed record ApiAgentDto(
    string? Version,
    string? ImageDigest,
    int? Protocol,
    string? InstanceId,
    IReadOnlyList<ApiKindDto>? Kinds);

public sealed record ApiCapacityDto(
    int CpuBudgetFreeMilli,
    int MemBudgetFreeMiB,
    int TmpFreeMiB,
    int HeavySlotsFree,
    int EffectiveConcurrency);

public sealed record ApiLoadDto(double CpuPct, double MemFreePct, string? Pressure);

public sealed record ApiLeasesDto(int Count, int Weight, int Max);

public sealed record ApiNodePolicyDto(int Revision, IReadOnlyList<string>? AllowedKinds, int MaxConcurrency);

public sealed record ApiCanaryDto(DateTimeOffset At, bool Ok);

public sealed record ApiTokensDto(int ActiveCount, DateTimeOffset? NextExpiryAt);

/// <summary>The node object (section 7.1.2).</summary>
public sealed record ApiNode(
    string Id,
    string NodeRef,
    string? DisplayName,
    string Status,
    string Health,
    string? Region,
    string? Provider,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? LastHeartbeatAt,
    DateTimeOffset? LastClaimAt,
    ApiAgentDto? Agent,
    ApiCapacityDto? Capacity,
    ApiLoadDto? Load,
    ApiLeasesDto? Leases,
    ApiNodePolicyDto? Policy,
    int AppliedRevision,
    int IntegrityStrikes,
    ApiCanaryDto? LastCanary,
    ApiTokensDto? Tokens)
{
    /// <summary>Folds the API's view and the manager's stored policy into the placement engine's input.</summary>
    public NodeSnapshot ToSnapshot(NodePolicy storedPolicy)
    {
        var kinds = (Agent?.Kinds ?? Array.Empty<ApiKindDto>())
            .Select(k => new AgentKind(k.Kind, k.SchemaVersions ?? Array.Empty<int>(), k.EngineVersion ?? string.Empty))
            .ToList();
        var capacity = Capacity is null
            ? new NodeCapacity(0, 0, 0, 0, 0)
            : new NodeCapacity(Capacity.CpuBudgetFreeMilli, Capacity.MemBudgetFreeMiB, Capacity.TmpFreeMiB, Capacity.HeavySlotsFree, Capacity.EffectiveConcurrency);
        return new NodeSnapshot(
            Id,
            NodeRef,
            Status,
            Health,
            Paused: false,
            storedPolicy,
            kinds,
            capacity,
            Leases?.Weight ?? 0);
    }
}

/// <summary>A one-time secret. <see cref="ToString"/> never prints the value, so a stray log line cannot leak it.</summary>
public sealed record ApiToken(string Id, string Value, DateTimeOffset ExpiresAt)
{
    public override string ToString() => "ApiToken { Id = " + Id + ", Value = [redacted] }";
}

public sealed record ApiNodeRef(string Id, string NodeRef, string Status, int PolicyRevision);

public sealed record RegisterPolicyDto(
    IReadOnlyList<string> AllowedKinds,
    int MaxConcurrency,
    IReadOnlyDictionary<string, int> PerKind,
    Budgets Budgets);

public sealed record RegisterNodeRequest(
    string NodeRef,
    string DisplayName,
    string? Region,
    string? Provider,
    RegisterPolicyDto Policy,
    int? TokenTtlDays);

/// <summary><see cref="Token"/> is null when the node already existed (the token is never returned twice).</summary>
public sealed record RegisterNodeResult(ApiNodeRef Node, ApiToken? Token, bool Created);

public sealed record ApiStatus(IReadOnlyList<string> Kinds, int? ProtocolCurrent, int? ProtocolMinimum);

public sealed class FleetApiException : Exception
{
    public FleetApiException(HttpStatusCode status, string code, string? reason, bool retryable, string message)
        : base(message)
    {
        Status = status;
        Code = code;
        Reason = reason;
        Retryable = retryable;
    }

    public HttpStatusCode Status { get; }

    /// <summary>The API's stable error code (<c>canary_in_progress</c>, <c>policy_revision_conflict</c>, ...) or a local one (<c>unreachable</c>, <c>credential_missing</c>).</summary>
    public string Code { get; }

    public string? Reason { get; }

    public bool Retryable { get; }

    public const string Unreachable = "unreachable";
    public const string CredentialMissing = "credential_missing";
}

/// <summary>
/// The manager's only view of the OET API (service plane, <c>/v1/internal/fleet</c>). Everything the
/// manager knows about node health comes from here (RW-144): it never connects to a helper to ask.
/// </summary>
public interface IFleetApi
{
    Task<RegisterNodeResult> RegisterNodeAsync(RegisterNodeRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApiNode>> ListNodesAsync(CancellationToken cancellationToken);

    Task<ApiNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken);

    /// <summary>Returns the new policy revision. Throws <see cref="FleetApiException"/> with code <c>policy_revision_conflict</c> on a stale revision.</summary>
    Task<int> PutPolicyAsync(string nodeId, NodePolicy policy, int expectedRevision, CancellationToken cancellationToken);

    Task EnableAsync(string nodeId, CancellationToken cancellationToken);

    Task DrainAsync(string nodeId, CancellationToken cancellationToken);

    Task DisableAsync(string nodeId, CancellationToken cancellationToken);

    Task QuarantineAsync(string nodeId, CancellationToken cancellationToken);

    Task RevokeAsync(string nodeId, CancellationToken cancellationToken);

    Task<ApiToken> RotateTokenAsync(string nodeId, int graceSeconds, int ttlDays, CancellationToken cancellationToken);

    /// <summary>Enqueues the known-answer canary. Throws code <c>canary_in_progress</c> (409) when one is already open.</summary>
    Task<string> EnqueueCanaryAsync(string nodeId, CancellationToken cancellationToken);

    Task<ApiStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<JsonElement> GetStatsAsync(CancellationToken cancellationToken);

    /// <summary>Service-plane job list (<c>GET /jobs?state&amp;kind&amp;nodeId&amp;limit</c>) as raw JSON; readers tolerate shape drift.</summary>
    Task<JsonElement> GetJobsAsync(string? state, string? kind, string? nodeId, int limit, CancellationToken cancellationToken);

    /// <summary>One job (<c>GET /jobs/{id}</c>) as raw JSON; null when the API answers job_not_found.</summary>
    Task<JsonElement?> GetJobAsync(string jobId, CancellationToken cancellationToken);

    /// <summary>Requeue, force-local or cancel a job (the API's three admin job actions).</summary>
    Task PostJobActionAsync(string action, string jobId, CancellationToken cancellationToken);
}
