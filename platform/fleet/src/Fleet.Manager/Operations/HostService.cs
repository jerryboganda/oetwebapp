using System.Text.Json;
using Fleet.Core.Domain;
using Fleet.Core.Policy;
using Fleet.Core.Validation;
using Fleet.Manager.Api;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Persistence;

namespace Fleet.Manager.Operations;

public sealed record HostDetail(HostView Host, ApiNode? Node, OperationView? EnrollOperation);

/// <summary>
/// Read models and the maintenance actions on existing hosts: drain, disable, enable, remove, token
/// rotation, repair and the rolling image update. Each action creates ONE durable operation (a duplicate
/// request while one is open returns it), so a click is never lost to a restart and never runs twice.
/// </summary>
public sealed class HostService
{
    private readonly HostStore _hosts;
    private readonly OperationStore _operations;
    private readonly OperationRunner _runner;
    private readonly OperationSignal _signal;
    private readonly ReleaseService _releases;
    private readonly PolicyService _policies;
    private readonly IFleetApi _api;
    private readonly IAuditService _audit;
    private readonly IEventBus _events;

    public HostService(
        HostStore hosts,
        OperationStore operations,
        OperationRunner runner,
        OperationSignal signal,
        ReleaseService releases,
        PolicyService policies,
        IFleetApi api,
        IAuditService audit,
        IEventBus events)
    {
        _hosts = hosts;
        _operations = operations;
        _runner = runner;
        _signal = signal;
        _releases = releases;
        _policies = policies;
        _api = api;
        _audit = audit;
        _events = events;
    }

    public async Task<IReadOnlyList<HostView>> ListHostsAsync(CancellationToken cancellationToken) =>
        (await _hosts.ListAsync(cancellationToken)).Select(HostView.From).ToList();

    public async Task<HostDetail> GetHostAsync(string hostId, bool includeNode, CancellationToken cancellationToken)
    {
        var host = await _hosts.GetAsync(hostId, cancellationToken) ?? throw new FleetNotFoundException("The host");
        ApiNode? node = null;
        if (includeNode && host.ApiNodeId is not null)
        {
            try
            {
                node = await _api.GetNodeAsync(host.ApiNodeId, cancellationToken);
            }
            catch (FleetApiException)
            {
                // The API being unreachable must not hide the host; the node stays null.
            }
        }

        var enroll = await _operations.FindEnrollAsync(host.Id, cancellationToken);
        return new HostDetail(HostView.From(host), node, enroll is null ? null : await ViewAsync(enroll, cancellationToken));
    }

    public async Task<IReadOnlyList<OperationView>> ListOperationsAsync(int take, string? hostId, CancellationToken cancellationToken)
    {
        var rows = await _operations.ListAsync(take, hostId, cancellationToken);
        var views = new List<OperationView>(rows.Count);
        foreach (var row in rows)
        {
            views.Add(await ViewAsync(row, cancellationToken));
        }

        return views;
    }

    public async Task<OperationView> GetOperationAsync(string operationId, CancellationToken cancellationToken)
    {
        var op = await _operations.GetAsync(operationId, cancellationToken) ?? throw new FleetNotFoundException("The operation");
        return await ViewAsync(op, cancellationToken);
    }

    // ---- actions ---------------------------------------------------------------------------

    public Task<OperationView> StartDrainAsync(string hostId, string actor, CancellationToken cancellationToken) =>
        StartAsync(OperationKind.Drain, hostId, actor, StepPlans.Drain(), "{}", host =>
            host.Lifecycle is nameof(HostLifecycle.Active) or nameof(HostLifecycle.Draining)
                ? null
                : "Only an active host can be drained.", cancellationToken);

    public Task<OperationView> StartDisableAsync(string hostId, string actor, CancellationToken cancellationToken) =>
        StartAsync(OperationKind.Disable, hostId, actor, StepPlans.Disable(), "{}", host =>
            host.Lifecycle is nameof(HostLifecycle.Active) or nameof(HostLifecycle.Draining) or nameof(HostLifecycle.Disabled)
                ? null
                : "Only an active, draining or disabled host can be disabled.", cancellationToken);

    public Task<OperationView> StartEnableAsync(string hostId, string actor, CancellationToken cancellationToken) =>
        StartAsync(OperationKind.Enable, hostId, actor, StepPlans.Enable(), "{}", host =>
            host.Lifecycle is nameof(HostLifecycle.Disabled) or nameof(HostLifecycle.Draining)
                ? (host.Alert is null ? null : "Clear the host-key alert (re-pin) before enabling this host.")
                : "Only a disabled or draining host can be enabled.", cancellationToken);

    public Task<OperationView> StartRotateTokenAsync(string hostId, string actor, CancellationToken cancellationToken) =>
        StartAsync(OperationKind.RotateToken, hostId, actor, StepPlans.RotateToken(), "{}", host =>
            host.ApiNodeId is not null && host.AgentDigest is not null
                && host.Lifecycle is nameof(HostLifecycle.Active) or nameof(HostLifecycle.Draining) or nameof(HostLifecycle.Disabled)
                ? null
                : "The host has no running agent whose token could be rotated.", cancellationToken);

    /// <summary>Remove: drain, wait for leases (strict), uninstall ONLY fleet-owned components, revoke the node, erase the vault records.</summary>
    public async Task<OperationView> StartRemoveAsync(string hostId, bool force, string actor, CancellationToken cancellationToken)
    {
        var host = await _hosts.GetAsync(hostId, cancellationToken) ?? throw new FleetNotFoundException("The host");
        var open = await _operations.FindOpenAsync(OperationKind.Remove, hostId, cancellationToken);
        if (open is not null)
        {
            return await ViewAsync(open, cancellationToken);
        }

        if (host.Lifecycle is nameof(HostLifecycle.Removed) or nameof(HostLifecycle.Removing))
        {
            throw new FleetOperationException("invalid_state", "The host is already removed.");
        }

        var enroll = await _operations.FindEnrollAsync(hostId, cancellationToken);
        if (enroll is not null && enroll.State is not (nameof(EnrollmentState.Active) or nameof(EnrollmentState.Cancelled)))
        {
            // An unfinished enrollment is abandoned first (only possible from the states of OET-RWP/1 section 8.1).
            if (!await _runner.CancelAsync(enroll.Id, actor, cancellationToken))
            {
                throw new FleetOperationException("operation_in_progress", "The enrollment is running; wait for it to finish or fail, then remove the host.");
            }
        }

        var paramsJson = JsonSerializer.Serialize(new Dictionary<string, object?> { ["force"] = force }, FleetJson.Options);
        var op = await _operations.CreateAsync(
            new NewOperation(OperationKind.Remove, hostId, nameof(GenericOperationState.Queued), StepPlans.Remove(), paramsJson, actor, null),
            cancellationToken);
        await _hosts.UpdateAsync(hostId, h => h.Lifecycle = nameof(HostLifecycle.Removing), cancellationToken);
        await Announce(actor, "host.remove_requested", host.NodeRef, op, new Dictionary<string, object?> { ["force"] = force }, cancellationToken);
        return await ViewAsync(op, cancellationToken);
    }

    /// <summary>Repair re-runs the root-level steps (S1..S8) and needs a fresh temporary owner key.</summary>
    public async Task<OperationView> StartRepairAsync(string hostId, string actor, CancellationToken cancellationToken)
    {
        var host = await _hosts.GetAsync(hostId, cancellationToken) ?? throw new FleetNotFoundException("The host");
        var open = await _operations.FindOpenAsync(OperationKind.Repair, hostId, cancellationToken);
        if (open is not null)
        {
            return await ViewAsync(open, cancellationToken);
        }

        if (host.HostKeyAlgo is null || host.Lifecycle is nameof(HostLifecycle.Removed) or nameof(HostLifecycle.Removing))
        {
            throw new FleetOperationException("invalid_state", "Only an enrolled host with a pinned key can be repaired.");
        }

        if (host.Alert == FailureReasons.HostKeyChanged)
        {
            throw new FleetOperationException("repin_required", "Re-pin the host key before repairing this host.");
        }

        var op = await _operations.CreateAsync(
            new NewOperation(OperationKind.Repair, hostId, nameof(GenericOperationState.AwaitingOwner), StepPlans.Repair(), "{}", actor, null),
            cancellationToken);
        await Announce(actor, "host.repair_requested", host.NodeRef, op, null, cancellationToken);
        return await ViewAsync(op, cancellationToken);
    }

    /// <summary>
    /// Rolling update to an APPROVED digest: one host at a time (drain, wait, pull, run, verify, enable, canary).
    /// A failure halts the operation and leaves every later host untouched; the previous approved digest is the rollback.
    /// </summary>
    public async Task<OperationView> StartRolloutAsync(string digest, string actor, CancellationToken cancellationToken)
    {
        if (!InputValidator.IsValidImageDigest(digest))
        {
            throw new FleetValidationException(new ValidationIssue("digest", "digest_invalid", "digest must be sha256:<64 hex>."));
        }

        var open = await _operations.FindOpenRolloutAsync(cancellationToken);
        if (open is not null)
        {
            return await ViewAsync(open, cancellationToken);
        }

        var release = await _releases.FindApprovedByDigestAsync(digest, cancellationToken)
            ?? throw new FleetValidationException(new ValidationIssue("digest", "image_digest_unapproved", "That digest is not an approved release."));
        // Active and Draining hosts take part (a host drained by a failed rollout must be reachable by the rollback);
        // a Disabled host is the way to keep a host out of a rollout. Every host a rollout finishes is enabled again.
        var candidates = (await _hosts.ListByLifecycleAsync(nameof(HostLifecycle.Active), cancellationToken))
            .Concat(await _hosts.ListByLifecycleAsync(nameof(HostLifecycle.Draining), cancellationToken));
        var hosts = candidates
            .Where(h => h.ApiNodeId is not null)
            .OrderBy(h => h.NodeRef, StringComparer.Ordinal)
            .ToList();
        if (hosts.Count == 0)
        {
            throw new FleetOperationException("no_hosts", "There is no active host to update.");
        }

        // Every node must already ACCEPT the new digest (approvedDigests window) before an agent running it can claim work.
        await _policies.PushAllAsync(actor, cancellationToken);

        var paramsJson = JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["digest"] = digest,
                ["release"] = release.Sha,
                ["hosts"] = hosts.Select(h => h.NodeRef).ToList(),
            },
            FleetJson.Options);
        var op = await _operations.CreateAsync(
            new NewOperation(OperationKind.Rollout, null, nameof(GenericOperationState.Queued), StepPlans.Rollout(hosts.Select(h => h.NodeRef)), paramsJson, actor, null),
            cancellationToken);
        await Announce(actor, "rollout.requested", release.Sha, op, new Dictionary<string, object?> { ["digest"] = digest, ["hosts"] = hosts.Count }, cancellationToken);
        return await ViewAsync(op, cancellationToken);
    }

    // ---- plumbing --------------------------------------------------------------------------

    private async Task<OperationView> StartAsync(
        OperationKind kind,
        string hostId,
        string actor,
        IReadOnlyList<string> steps,
        string paramsJson,
        Func<HostEntity, string?> precondition,
        CancellationToken cancellationToken)
    {
        var host = await _hosts.GetAsync(hostId, cancellationToken) ?? throw new FleetNotFoundException("The host");
        var open = await _operations.FindOpenAsync(kind, hostId, cancellationToken);
        if (open is not null)
        {
            return await ViewAsync(open, cancellationToken);
        }

        var problem = precondition(host);
        if (problem is not null)
        {
            throw new FleetOperationException("invalid_state", problem);
        }

        var op = await _operations.CreateAsync(
            new NewOperation(kind, hostId, nameof(GenericOperationState.Queued), steps, paramsJson, actor, null),
            cancellationToken);
        await Announce(actor, "host." + OperationKinds.ToWire(kind) + "_requested", host.NodeRef, op, null, cancellationToken);
        return await ViewAsync(op, cancellationToken);
    }

    private async Task Announce(
        string actor,
        string action,
        string target,
        OperationEntity op,
        IReadOnlyDictionary<string, object?>? details,
        CancellationToken cancellationToken)
    {
        var merged = new Dictionary<string, object?>(details ?? new Dictionary<string, object?>()) { ["operation"] = op.Id };
        await _audit.AppendAsync(actor, action, target, merged, cancellationToken);
        _events.Publish("operation.updated", new { id = op.Id, kind = op.Kind, state = op.State });
        _signal.Kick();
    }

    private async Task<OperationView> ViewAsync(OperationEntity op, CancellationToken cancellationToken) =>
        OperationView.From(op, await _operations.GetStepsAsync(op.Id, cancellationToken));
}
