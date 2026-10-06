using Fleet.Core.Policy;
using Fleet.Core.Validation;
using Fleet.Manager.Api;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fleet.Manager.Operations;

public sealed record PolicyPushResult(string HostId, string NodeRef, bool Success, int? Revision, string? Error);

/// <summary>
/// The single writer of node policy (OET-RWP/1 section 8.4): a global default plus an optional
/// per-host override, validated against the section 7.3 ranges (and the API's kind registry when
/// known) before it is stored, then pushed with <c>PUT /nodes/{id}/policy</c> using optimistic
/// <c>expectedRevision</c>. The effective policy always carries the current approved-image window.
/// </summary>
public sealed class PolicyService
{
    private static readonly TimeSpan RegistryCacheFor = TimeSpan.FromMinutes(5);
    private static readonly System.Text.Json.JsonSerializerOptions Json = FleetJson.Options;

    private readonly IDbContextFactory<FleetDbContext> _factory;
    private readonly TimeProvider _time;
    private readonly IFleetApi _api;
    private readonly HostStore _hosts;
    private readonly ReleaseService _releases;
    private readonly IAuditService _audit;
    private readonly IEventBus _events;
    private readonly object _registryGate = new();
    private IReadOnlyCollection<string>? _registry;
    private DateTimeOffset _registryAt = DateTimeOffset.MinValue;

    public PolicyService(
        IDbContextFactory<FleetDbContext> factory,
        TimeProvider time,
        IFleetApi api,
        HostStore hosts,
        ReleaseService releases,
        IAuditService audit,
        IEventBus events)
    {
        _factory = factory;
        _time = time;
        _api = api;
        _hosts = hosts;
        _releases = releases;
        _audit = audit;
        _events = events;
    }

    /// <summary>The kinds the API knows (cached 5 minutes), or null when the registry cannot be read.</summary>
    public async Task<IReadOnlyCollection<string>?> RegistryKindsAsync(CancellationToken cancellationToken)
    {
        lock (_registryGate)
        {
            if (_registry is not null && _time.GetUtcNow() - _registryAt < RegistryCacheFor)
            {
                return _registry;
            }
        }

        try
        {
            var status = await _api.GetStatusAsync(cancellationToken);
            var kinds = status.Kinds.Count > 0 ? status.Kinds : null;
            lock (_registryGate)
            {
                _registry = kinds;
                _registryAt = _time.GetUtcNow();
            }

            return kinds;
        }
        catch (FleetApiException)
        {
            return null;
        }
    }

    public async Task<NodePolicy?> GetStoredGlobalAsync(CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Policies.AsNoTracking().FirstOrDefaultAsync(p => p.Scope == "global", cancellationToken);
        return row is null ? null : FromEntity(row);
    }

    /// <summary>The stored global policy, or the section 8.4 default.</summary>
    public async Task<NodePolicy> GetGlobalAsync(CancellationToken cancellationToken) =>
        await GetStoredGlobalAsync(cancellationToken) ?? PolicyDefaults.Global();

    public async Task<NodePolicy?> GetHostOverrideAsync(string hostId, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Policies.AsNoTracking().FirstOrDefaultAsync(p => p.Scope == "host" && p.HostId == hostId, cancellationToken);
        return row is null ? null : FromEntity(row);
    }

    public async Task<NodePolicy> SetGlobalAsync(NodePolicy policy, string actor, CancellationToken cancellationToken)
    {
        var normalized = Normalize(policy);
        PolicyValidator.EnsureValid(normalized, await RegistryKindsAsync(cancellationToken));
        await UpsertAsync("global", null, normalized, cancellationToken);
        await _audit.AppendAsync(actor, "policy.global_set", null, Summary(normalized), cancellationToken);
        _events.Publish("policy.updated", new { scope = "global" });
        return normalized;
    }

    public async Task<NodePolicy> SetHostOverrideAsync(string hostId, NodePolicy policy, string actor, CancellationToken cancellationToken)
    {
        var host = await _hosts.GetAsync(hostId, cancellationToken)
            ?? throw new FleetValidationException(new ValidationIssue("hostId", "host_not_found", "No such host."));
        var normalized = Normalize(policy);
        PolicyValidator.EnsureValid(normalized, await RegistryKindsAsync(cancellationToken));
        await UpsertAsync("host", host.Id, normalized, cancellationToken);
        await _audit.AppendAsync(actor, "policy.host_set", host.NodeRef, Summary(normalized), cancellationToken);
        _events.Publish("policy.updated", new { scope = "host", hostId = host.Id });
        return normalized;
    }

    public async Task ClearHostOverrideAsync(string hostId, string actor, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var removed = await db.Policies.Where(p => p.Scope == "host" && p.HostId == hostId).ExecuteDeleteAsync(cancellationToken);
        if (removed > 0)
        {
            await _audit.AppendAsync(actor, "policy.host_cleared", hostId, null, cancellationToken);
            _events.Publish("policy.updated", new { scope = "host", hostId });
        }
    }

    /// <summary>
    /// Host override, else the stored global policy, else a default scaled to the host facts recorded
    /// by preflight (so a small VPS never inherits the 4 vCPU / 8 GiB budgets). The approved-image
    /// window is always overlaid.
    /// </summary>
    public async Task<NodePolicy> EffectiveAsync(HostEntity host, CancellationToken cancellationToken)
    {
        var baseline = await GetHostOverrideAsync(host.Id, cancellationToken)
            ?? await GetStoredGlobalAsync(cancellationToken)
            ?? (host.CpuCores is { } cores && host.MemMib is { } mem ? PolicyDefaults.ForHost(cores, mem) : PolicyDefaults.Global());
        return PolicyDefaults.WithAgentImage(baseline, await _releases.AgentImagePolicyAsync(cancellationToken));
    }

    /// <summary>Pushes the effective policy to one node. A stale revision is re-read and retried once.</summary>
    public async Task<int> PushAsync(HostEntity host, CancellationToken cancellationToken)
    {
        if (host.ApiNodeId is null)
        {
            throw new InvalidOperationException("The host has no API node yet.");
        }

        var policy = await EffectiveAsync(host, cancellationToken);
        for (var attempt = 0; ; attempt++)
        {
            var node = await _api.GetNodeAsync(host.ApiNodeId, cancellationToken)
                ?? throw new FleetApiException(System.Net.HttpStatusCode.NotFound, "node_not_found", null, false, "The API has no such node.");
            var expected = node.Policy?.Revision ?? 0;
            try
            {
                var revision = await _api.PutPolicyAsync(host.ApiNodeId, policy, expected, cancellationToken);
                await _hosts.UpdateAsync(host.Id, h => h.DesiredRevision = revision, cancellationToken);
                return revision;
            }
            catch (FleetApiException ex) when (ex.Code == "policy_revision_conflict" && attempt == 0)
            {
                // Someone (the owner break-glass, a drain) bumped the revision between read and write: read again.
            }
        }
    }

    /// <summary>Pushes to every host that has a node. One failure never stops the others.</summary>
    public async Task<IReadOnlyList<PolicyPushResult>> PushAllAsync(string actor, CancellationToken cancellationToken)
    {
        var results = new List<PolicyPushResult>();
        foreach (var host in await _hosts.ListWithNodesAsync(cancellationToken))
        {
            if (host.Lifecycle is not ("Active" or "Draining" or "Disabled"))
            {
                continue;
            }

            try
            {
                var revision = await PushAsync(host, cancellationToken);
                results.Add(new PolicyPushResult(host.Id, host.NodeRef, true, revision, null));
            }
            catch (FleetApiException ex)
            {
                results.Add(new PolicyPushResult(host.Id, host.NodeRef, false, null, ex.Code));
            }
        }

        await _audit.AppendAsync(
            actor,
            "policy.push_all",
            null,
            new Dictionary<string, object?> { ["pushed"] = results.Count(r => r.Success), ["failed"] = results.Count(r => !r.Success) },
            cancellationToken);
        return results;
    }

    private static NodePolicy Normalize(NodePolicy policy) =>
        policy with
        {
            AllowedKinds = policy.AllowedKinds?.Distinct(StringComparer.Ordinal).ToList() ?? new List<string>(),
            PerKind = policy.PerKind ?? new Dictionary<string, int>(),
            AgentImage = policy.AgentImage ?? PolicyDefaults.EmptyAgentImage(),
        };

    private static Dictionary<string, object?> Summary(NodePolicy p) => new()
    {
        ["kinds"] = string.Join(',', p.AllowedKinds),
        ["maxConcurrency"] = p.MaxConcurrency,
        ["cpuMilli"] = p.Budgets.CpuMilli,
        ["memMiB"] = p.Budgets.MemMiB,
        ["tmpMiB"] = p.Budgets.TmpMiB,
    };

    private async Task UpsertAsync(string scope, string? hostId, NodePolicy policy, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Policies.FirstOrDefaultAsync(p => p.Scope == scope && p.HostId == hostId, cancellationToken);
        if (row is null)
        {
            row = new PolicyEntity { Id = "pol_" + Guid.NewGuid().ToString("N"), Scope = scope, HostId = hostId, Version = 0 };
            db.Policies.Add(row);
        }

        row.AllowedKinds = string.Join(',', policy.AllowedKinds);
        row.MaxConcurrency = policy.MaxConcurrency;
        row.PerKindJson = System.Text.Json.JsonSerializer.Serialize(policy.PerKind, Json);
        row.CpuBudgetMilli = policy.Budgets.CpuMilli;
        row.MemBudgetMib = policy.Budgets.MemMiB;
        row.TmpBudgetMib = policy.Budgets.TmpMiB;
        row.PressureJson = System.Text.Json.JsonSerializer.Serialize(policy.Pressure, Json);
        row.PollJson = System.Text.Json.JsonSerializer.Serialize(policy.PollSeconds, Json);
        row.Version += 1;
        row.UpdatedAt = _time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    private static NodePolicy FromEntity(PolicyEntity row) => new(
        row.AllowedKinds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        row.MaxConcurrency,
        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(row.PerKindJson, Json) ?? new Dictionary<string, int>(),
        new Budgets(row.CpuBudgetMilli, row.MemBudgetMib, row.TmpBudgetMib),
        System.Text.Json.JsonSerializer.Deserialize<PressureSettings>(row.PressureJson, Json) ?? PolicyDefaults.DefaultPressure,
        System.Text.Json.JsonSerializer.Deserialize<PollSettings>(row.PollJson, Json) ?? PolicyDefaults.DefaultPoll,
        PolicyDefaults.EmptyAgentImage());
}
