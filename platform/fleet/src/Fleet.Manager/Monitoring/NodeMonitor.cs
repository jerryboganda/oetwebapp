using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fleet.Core.Audit;
using Fleet.Core.Domain;
using Fleet.Core.Placement;
using Fleet.Core.Policy;
using Fleet.Manager.Api;
using Fleet.Manager.Configuration;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Fleet.Manager.Vault;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Monitoring;

/// <summary>
/// Node health and state tracking. The OET API is the authoritative liveness view (OET-RWP/1 section
/// 7.4, RW-144): every <c>NodePollSeconds</c> the monitor reads <c>GET /nodes</c> with the fleet-service
/// credential, mirrors what it learns onto the host rows, raises SSE events and alerts, and schedules the
/// 14-day token rotation. Every <c>HostStatusPollMinutes</c> it also asks each active helper for its
/// restricted <c>status</c> over SSH (the only inbound path to a helper). It never connects to an agent.
/// </summary>
public sealed class NodeMonitor : BackgroundService
{
    private readonly ConcurrentDictionary<string, int> _mismatches = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _lastKey = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PressureGovernor> _governors = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, JsonNode Json)> _ctlStatus = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _ctlLatencyMs = new(StringComparer.Ordinal);

    private readonly IFleetApi _api;
    private readonly HostStore _hosts;
    private readonly FleetState _state;
    private readonly IEventBus _events;
    private readonly IAuditService _audit;
    private readonly HostAccess _access;
    private readonly HostSecurityService _hostSecurity;
    private readonly HostService _hostService;
    private readonly CredentialStore _credentials;
    private readonly IOptions<FleetOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<NodeMonitor> _logger;
    private DateTimeOffset _lastCtlPoll = DateTimeOffset.MinValue;

    public NodeMonitor(
        IFleetApi api,
        HostStore hosts,
        FleetState state,
        IEventBus events,
        IAuditService audit,
        HostAccess access,
        HostSecurityService hostSecurity,
        HostService hostService,
        CredentialStore credentials,
        IOptions<FleetOptions> options,
        TimeProvider time,
        ILogger<NodeMonitor> logger)
    {
        _api = api;
        _hosts = hosts;
        _state = state;
        _events = events;
        _audit = audit;
        _access = access;
        _hostSecurity = hostSecurity;
        _hostService = hostService;
        _credentials = credentials;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <summary>The pressure the governor currently derives for a node (reduced after sustained load, restored with hysteresis).</summary>
    public PressureGovernor? GovernorFor(string nodeId) => _governors.TryGetValue(nodeId, out var governor) ? governor : null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Workers.Enabled)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.Value.Timing.NodePollSeconds));
        using var timer = new PeriodicTimer(interval, _time);
        try
        {
            do
            {
                try
                {
                    await PollOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError("Node poll failed unexpectedly: {Type}", ex.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    /// <summary>One monitoring pass. Public so tests (and the dashboard's refresh button) can run it on demand.</summary>
    public async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        IReadOnlyList<ApiNode> nodes;
        try
        {
            nodes = await _api.ListNodesAsync(cancellationToken);
        }
        catch (FleetApiException ex)
        {
            _state.SetApiError(ex.Code, now);
            _events.Publish("api.unreachable", new { code = ex.Code });
            return;
        }

        _state.SetNodes(nodes, now);
        var hosts = await _hosts.ListWithNodesAsync(cancellationToken);
        var pollCtl = now - _lastCtlPoll >= TimeSpan.FromMinutes(Math.Max(1, _options.Value.Timing.HostStatusPollMinutes));
        if (pollCtl)
        {
            _lastCtlPoll = now;
        }

        foreach (var host in hosts)
        {
            var node = nodes.FirstOrDefault(n => string.Equals(n.Id, host.ApiNodeId, StringComparison.Ordinal));
            if (node is null)
            {
                continue;
            }

            ObservePressure(node, now);

            if (pollCtl && host.Lifecycle is nameof(HostLifecycle.Active) or nameof(HostLifecycle.Draining) && host.HostKeyAlgo is not null)
            {
                await PollCtlAsync(host, now, cancellationToken);
            }

            await MirrorAsync(host, node, now, cancellationToken);
            PublishIfChanged(host, node);
        }

        await ScheduleTokenRotationsAsync(hosts, cancellationToken);
    }

    private void ObservePressure(ApiNode node, DateTimeOffset now)
    {
        if (node.Load is null || node.Policy is null)
        {
            return;
        }

        var governor = _governors.GetOrAdd(
            node.Id,
            _ => new PressureGovernor(PolicyDefaults.DefaultPressure, node.Policy.MaxConcurrency));
        governor.Observe(now, node.Load.CpuPct, node.Load.MemFreePct);
    }

    private async Task PollCtlAsync(HostEntity host, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var started = _time.GetTimestamp();
        var result = await _access.CtlAsync(host, "status", Array.Empty<string>(), null, cancellationToken);

        // The management-plane round trip (SSH to the restricted ctl and back), shown on the dashboard as the helper's latency.
        _ctlLatencyMs[host.Id] = (int)Math.Min(int.MaxValue, Math.Max(0, _time.GetElapsedTime(started).TotalMilliseconds));
        if (result.Success)
        {
            try
            {
                var parsed = JsonNode.Parse(result.Stdout);
                if (parsed is not null)
                {
                    _ctlStatus[host.Id] = (now, parsed);
                }
            }
            catch (JsonException)
            {
                _ctlStatus[host.Id] = (now, new JsonObject { ["ok"] = false, ["error"] = "unreadable status" });
            }

            return;
        }

        if (result.FailureReason == FailureReasons.HostKeyChanged)
        {
            await _hostSecurity.OnHostKeyChangedAsync(host.Id, "system", cancellationToken);
            return;
        }

        _ctlStatus[host.Id] = (now, new JsonObject { ["ok"] = false, ["reason"] = result.FailureReason ?? "ctl_failed" });
    }

    /// <summary>Copies what the API knows onto the host row. A lifecycle that disagrees with the node is only adopted after two consecutive polls (the owner may be mid-action).</summary>
    private async Task MirrorAsync(HostEntity host, ApiNode node, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var summary = new JsonObject
        {
            ["status"] = node.Status,
            ["health"] = node.Health,
            ["lastHeartbeatAt"] = node.LastHeartbeatAt?.ToString("O"),
            ["leases"] = node.Leases?.Count,
            ["effectiveConcurrency"] = node.Capacity?.EffectiveConcurrency,
            ["agentVersion"] = node.Agent?.Version,
            ["agentDigest"] = node.Agent?.ImageDigest,
            ["integrityStrikes"] = node.IntegrityStrikes,
        };
        if (_ctlStatus.TryGetValue(host.Id, out var ctl))
        {
            var hostSummary = new JsonObject { ["polledAt"] = ctl.At.ToString("O"), ["status"] = ctl.Json.DeepClone() };
            if (_ctlLatencyMs.TryGetValue(host.Id, out var latencyMs))
            {
                hostSummary["latencyMs"] = latencyMs;
            }

            summary["host"] = hostSummary;
        }

        var json = LogScrubber.Scrub(summary.ToJsonString(), 4000);
        var desiredLifecycle = DesiredLifecycle(host, node);
        var adopt = desiredLifecycle is not null && desiredLifecycle != host.Lifecycle && _mismatches.AddOrUpdate(host.Id, 1, (_, n) => n + 1) >= 2;
        if (desiredLifecycle is null || desiredLifecycle == host.Lifecycle)
        {
            _mismatches.TryRemove(host.Id, out _);
        }

        var quarantined = node.Status == "Quarantined";
        await _hosts.UpdateAsync(
            host.Id,
            h =>
            {
                h.DesiredRevision = node.Policy?.Revision ?? h.DesiredRevision;
                h.AppliedRevision = node.AppliedRevision;
                h.LastStatusAt = now;
                h.LastStatusJson = json;
                if (!string.IsNullOrEmpty(node.Agent?.ImageDigest))
                {
                    h.AgentDigest = node.Agent!.ImageDigest;
                }

                if (adopt)
                {
                    h.Lifecycle = desiredLifecycle!;
                }

                if (quarantined && h.Alert is null)
                {
                    h.Alert = "quarantined";
                }
                else if (!quarantined && h.Alert == "quarantined")
                {
                    h.Alert = null;
                }
            },
            cancellationToken);

        if (adopt)
        {
            _mismatches.TryRemove(host.Id, out _);
            await _audit.AppendAsync(
                "system",
                "host.lifecycle_synced",
                host.NodeRef,
                new Dictionary<string, object?> { ["from"] = host.Lifecycle, ["to"] = desiredLifecycle, ["nodeStatus"] = node.Status },
                cancellationToken);
        }

        if (quarantined && host.Alert is null)
        {
            _events.Publish("alert.raised", new { hostId = host.Id, nodeRef = host.NodeRef, alert = "quarantined" });
        }
    }

    private static string? DesiredLifecycle(HostEntity host, ApiNode node)
    {
        if (host.Lifecycle is not (nameof(HostLifecycle.Active) or nameof(HostLifecycle.Draining) or nameof(HostLifecycle.Disabled)))
        {
            return null;
        }

        return node.Status switch
        {
            "Active" => nameof(HostLifecycle.Active),
            "Draining" => nameof(HostLifecycle.Draining),
            "Disabled" or "Quarantined" => nameof(HostLifecycle.Disabled),
            _ => null,
        };
    }

    private void PublishIfChanged(HostEntity host, ApiNode node)
    {
        var key = string.Join(
            '|',
            node.Status,
            node.Health,
            node.Leases?.Count.ToString() ?? "-",
            node.Agent?.ImageDigest ?? "-",
            node.Capacity?.EffectiveConcurrency.ToString() ?? "-",
            node.Load?.Pressure ?? "-");
        if (_lastKey.TryGetValue(host.Id, out var previous) && previous == key)
        {
            return;
        }

        _lastKey[host.Id] = key;
        _events.Publish(
            "node.updated",
            new { hostId = host.Id, nodeRef = host.NodeRef, status = node.Status, health = node.Health, leases = node.Leases?.Count ?? 0 });
    }

    /// <summary>Node tokens are rotated after 14 days (30-day TTL). One rotation operation per host; a failed one stays visible instead of respawning.</summary>
    private async Task ScheduleTokenRotationsAsync(IReadOnlyList<HostEntity> hosts, CancellationToken cancellationToken)
    {
        var threshold = TimeSpan.FromDays(Math.Max(1, _options.Value.Timing.TokenRotateAfterDays));
        var now = _time.GetUtcNow();
        foreach (var host in hosts)
        {
            if (host.Lifecycle != nameof(HostLifecycle.Active) || host.AgentDigest is null)
            {
                continue;
            }

            var info = await _credentials.GetInfoAsync(host.Id, CredentialPurposes.NodeTokenRender, cancellationToken);
            if (info is null || now - info.CreatedAt < threshold)
            {
                continue;
            }

            try
            {
                await _hostService.StartRotateTokenAsync(host.Id, "system", cancellationToken);
            }
            catch (FleetOperationException)
            {
                // The host is not in a rotatable state right now; try again at the next poll.
            }
        }
    }
}
