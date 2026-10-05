using System.Globalization;

namespace Fleet.Agent;

/// <summary>
/// The node-level heartbeat (protocol 4.7): liveness, the capacity report and the desired-state channel, every 15 s on its own
/// dedicated thread. <see cref="Tick"/> sends one heartbeat, applies the answer and returns the delay until the next one.
/// While the API cannot be spoken to (auth failure, rollback skew, protocol mismatch) it becomes the slow probe of section 5.2.
/// </summary>
internal sealed class NodeHeartbeatService
{
    public const int IntervalSeconds = 15;

    private readonly IRemoteWorkerApi _api;
    private readonly AgentStatus _status;
    private readonly AgentIdentity _identity;
    private readonly ProtocolNegotiator _negotiator;
    private readonly CapacityAccountant _capacity;
    private readonly PressureController _pressure;
    private readonly HostSampler _sampler;
    private readonly ExecutorRegistry _executors;
    private readonly LeaseRegistry _leases;
    private readonly IMonotonicClock _clock;
    private readonly ILogger _log;
    private readonly Budgets _fallbackBudgets;
    private readonly Action _touchHealth;
    private int _apiUnsupportedStep;
    private long _clockSkewMs;

    public NodeHeartbeatService(IRemoteWorkerApi api, AgentStatus status, AgentIdentity identity, ProtocolNegotiator negotiator,
        CapacityAccountant capacity, PressureController pressure, HostSampler sampler, ExecutorRegistry executors, LeaseRegistry leases,
        IMonotonicClock clock, ILogger log, Budgets fallbackBudgets, Action? touchHealth = null)
    {
        _api = api;
        _status = status;
        _identity = identity;
        _negotiator = negotiator;
        _capacity = capacity;
        _pressure = pressure;
        _sampler = sampler;
        _executors = executors;
        _leases = leases;
        _clock = clock;
        _log = log;
        _fallbackBudgets = fallbackBudgets;
        _touchHealth = touchHealth ?? (() => { });
    }

    /// <summary>Sent once more on shutdown with state=stopping (section 5.5). Best effort, one attempt.</summary>
    public void SendFinal()
    {
        try
        {
            var request = BuildRequest();
            request.State = "stopping";
            _api.NodeHeartbeatAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _log.LogInformation("final heartbeat not delivered: {Error}", Redact.Exception(ex));
        }
    }

    public TimeSpan Tick()
    {
        var decision = _status.Decision;
        if (decision.State == AgentState.ProtocolMismatch)
        {
            // Probe with each supported number in turn (section 5.2).
            _negotiator.NextProbe();
        }

        ApiResponse<NodeHeartbeatResponse> response;
        var sentAt = DateTimeOffset.UtcNow;
        try
        {
            response = _api.NodeHeartbeatAsync(BuildRequest(), CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _log.LogWarning("node heartbeat threw: {Error}", Redact.Exception(ex));
            response = ApiResponse<NodeHeartbeatResponse>.Failure(ApiKind.Network);
        }

        _touchHealth();
        return Apply(response, sentAt);
    }

    internal TimeSpan Apply(ApiResponse<NodeHeartbeatResponse> response, DateTimeOffset sentAt)
    {
        if (response.DesiredRevision is { } revision) _status.NoteDesiredRevision(revision);

        switch (response.Kind)
        {
            case ApiKind.Ok when response.Value is { } body:
                _apiUnsupportedStep = 0;
                ApplyBody(body, sentAt);
                var hinted = body.NextHeartbeatSeconds ?? IntervalSeconds;
                return TimeSpan.FromSeconds(Math.Clamp(hinted, 5, 60));

            case ApiKind.Ok:
                _status.OnNodeHeartbeatFailure();
                return TimeSpan.FromSeconds(5);

            case ApiKind.Unauthorized:
                _status.OnAuthFailed();
                return TimeSpan.FromSeconds(300);

            case ApiKind.Conflict when response.Code == "instance_superseded":
                _status.OnSuperseded();
                return TimeSpan.FromSeconds(60);

            case ApiKind.ProtocolUnsupported:
                _status.OnProtocolMismatch();
                return TimeSpan.FromSeconds(60);

            case ApiKind.RouteAbsent or ApiKind.NotImplemented:
                _status.OnApiUnsupported();
                var step = Math.Min(_apiUnsupportedStep++, 4);
                return TimeSpan.FromSeconds(Math.Min(300, 30 * (1 << step)));

            case ApiKind.RateLimited:
                _status.OnNodeHeartbeatFailure();
                return response.RetryAfter is { } wait ? Backoff.RetryAfterWithJitter(wait) : TimeSpan.FromSeconds(IntervalSeconds);

            default:
                _status.OnNodeHeartbeatFailure();
                var failures = Math.Max(1, _status.ConsecutiveHeartbeatFailures);
                var backoff = Backoff.Compute(failures - 1);
                return backoff < TimeSpan.FromSeconds(IntervalSeconds) ? backoff : TimeSpan.FromSeconds(IntervalSeconds);
        }
    }

    private void ApplyBody(NodeHeartbeatResponse body, DateTimeOffset sentAt)
    {
        if (DateTimeOffset.TryParse(body.ServerTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var server))
        {
            _clockSkewMs = (long)(sentAt - server).TotalMilliseconds;
        }

        _status.OnNodeHeartbeatOk(body);
        var desired = body.Desired;
        if (desired is not null)
        {
            _pressure.SetConfigured(desired.MaxConcurrency);
            _capacity.SetConfigured(desired.MaxConcurrency);
            _capacity.SetBudgets(desired.Budgets is { MemMiB: > 0 } budgets ? budgets : _fallbackBudgets);
        }

        foreach (var verdict in body.LeaseAudit)
        {
            if (verdict.Valid) continue;
            // The API says this lease is no longer ours: abort at once, never complete or fail (4.7.3).
            _leases.Find(verdict.JobId, verdict.Fence)?.Abort(AbortReason.LeaseLost);
        }
    }

    internal NodeHeartbeatRequest BuildRequest()
    {
        var decision = _status.Decision;
        var snapshot = _capacity.Snapshot();
        var sample = _sampler.Latest;
        var now = _clock.NowMs;
        var cpu15 = _sampler.CpuPct15s;

        return new NodeHeartbeatRequest
        {
            InstanceId = _identity.InstanceId,
            AppliedRevision = _status.AppliedRevision,
            State = AgentStateMachine.WireState(decision.State),
            DegradedReason = (decision.State == AgentState.Degraded) ? decision.Reason : null,
            Agent = _identity.ToWire(_api.CurrentProtocol, detailed: true, clockSkewMs: _clockSkewMs),
            Kinds = _executors.Advertised(),
            Capacity = new NodeCapacity
            {
                CpuCoresTotal = sample?.CpuCores ?? Environment.ProcessorCount,
                CpuBudgetMilli = snapshot.CpuBudget,
                CpuBudgetFreeMilli = snapshot.CpuFree,
                MemTotalMiB = sample?.MemTotalMiB ?? 0,
                MemAvailableMiB = sample?.MemAvailableMiB ?? 0,
                MemBudgetMiB = snapshot.MemBudget,
                MemBudgetFreeMiB = snapshot.MemFree,
                TmpBudgetMiB = snapshot.TmpBudget,
                TmpFreeMiB = snapshot.TmpFree,
                DiskFreeMiB = sample?.DiskFreeMiB ?? 0,
                HeavySlotsTotal = snapshot.HeavySlotsTotal,
                HeavySlotsFree = snapshot.HeavySlotsFree,
                ConfiguredConcurrency = snapshot.Configured,
                EffectiveConcurrency = snapshot.Effective,
            },
            Load = new LoadBlock
            {
                CpuPct = Math.Round(sample?.CpuPct ?? 0, 1),
                CpuPct15s = Math.Round(cpu15, 1),
                MemFreePct = Math.Round(sample?.MemFreePct ?? 100, 1),
                Load1 = Math.Round(sample?.Load1 ?? 0, 2),
                Pressure = _pressure.Reduced ? "reduced" : "normal",
            },
            Leases = _leases.Snapshot().Select(l => new LeaseReport
            {
                JobId = l.JobId,
                Fence = l.Fence,
                Stage = l.Stage,
                ElapsedMs = Math.Max(0, now - l.StartedMs),
            }).ToList(),
        };
    }
}
