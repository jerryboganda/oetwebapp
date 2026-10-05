namespace Fleet.Agent;

/// <summary>
/// The claim loop (protocol 4.1, 5.1): lease at most ONE job per request, again immediately after a job was returned, otherwise
/// at the poll interval. A claim is never retried inside one tick; a claim whose answer was lost is replayed with the SAME
/// claimId on the next poll so the server hands the same job back instead of leaking a lease.
/// </summary>
internal sealed class ClaimLoop
{
    private readonly IRemoteWorkerApi _api;
    private readonly AgentStatus _status;
    private readonly CapacityAccountant _capacity;
    private readonly ExecutorRegistry _executors;
    private readonly JobSupervisor _supervisor;
    private readonly AgentIdentity _identity;
    private readonly IMonotonicClock _clock;
    private readonly ILogger _log;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private string? _pendingClaimId;

    public ClaimLoop(IRemoteWorkerApi api, AgentStatus status, CapacityAccountant capacity, ExecutorRegistry executors, JobSupervisor supervisor,
        AgentIdentity identity, IMonotonicClock clock, ILogger log, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _api = api;
        _status = status;
        _capacity = capacity;
        _executors = executors;
        _supervisor = supervisor;
        _identity = identity;
        _clock = clock;
        _log = log;
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
        _supervisor.JobFinished += Wake;
        _status.Changed += _ => Wake();
    }

    /// <summary>Last claim id whose answer was lost (visible to tests).</summary>
    internal string? PendingClaimId => _pendingClaimId;

    public void Wake() => _wake.Release();

    public async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var (delay, wakeable) = await StepAsync(stop).ConfigureAwait(false);
                if (delay <= TimeSpan.Zero) continue;
                if (wakeable)
                {
                    await _wake.WaitAsync(delay, stop).ConfigureAwait(false);
                }
                else
                {
                    await _delay(delay, stop).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError("claim loop iteration failed: {Error}", Redact.Exception(ex));
                try
                {
                    await _delay(TimeSpan.FromSeconds(2), stop).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>One iteration. Returns the pause before the next one; <c>Wakeable</c> pauses end early when a slot frees up.</summary>
    internal async Task<(TimeSpan Delay, bool Wakeable)> StepAsync(CancellationToken ct)
    {
        var decision = _status.Decision;
        if (!decision.MayClaim) return (TimeSpan.FromSeconds(1), true);

        var desired = _status.Desired;
        var poll = desired.PollSeconds ?? new PollSettings();
        var offers = _executors.Offers(desired, _capacity);
        if (offers.Count == 0) return (TimeSpan.FromSeconds(1), true);

        var snapshot = _capacity.Snapshot();
        var request = new ClaimRequest
        {
            ClaimId = _pendingClaimId ?? Guid.NewGuid().ToString("D"),
            InstanceId = _identity.InstanceId,
            AppliedRevision = _status.AppliedRevision,
            Kinds = offers,
            Capacity = new ClaimCapacity
            {
                CpuBudgetFreeMilli = snapshot.CpuFree,
                MemBudgetFreeMiB = snapshot.MemFree,
                TmpFreeMiB = snapshot.TmpFree,
                HeavySlotsFree = snapshot.HeavySlotsFree,
                EffectiveConcurrency = snapshot.Effective,
            },
            Agent = _identity.ToWire(_api.CurrentProtocol, detailed: false),
        };

        var sendMs = _clock.NowMs;
        var response = await _api.ClaimAsync(request, ct).ConfigureAwait(false);
        if (response.DesiredRevision is { } revision) _status.NoteDesiredRevision(revision);

        switch (response.Kind)
        {
            case ApiKind.Ok:
                _pendingClaimId = null;
                if (response.Value?.Job is { } job && StartJob(job, sendMs)) return (TimeSpan.Zero, false);
                return (Poll(null, poll), false);

            case ApiKind.NoContent:
                _pendingClaimId = null;
                if (response.NoContentReason == "draining") _status.OnServerDraining();
                return (Poll(response.RetryAfter, poll), false);

            case ApiKind.RateLimited:
                _pendingClaimId = null;
                return (Poll(response.RetryAfter ?? TimeSpan.FromSeconds(10), poll), false);

            case ApiKind.Unauthorized:
                _pendingClaimId = null;
                _status.OnAuthFailed();
                return (TimeSpan.FromSeconds(5), false);

            case ApiKind.Forbidden:
                _pendingClaimId = null;
                if (response.Code is ("node_quarantined" or "node_disabled" or "node_not_active") and { } forbidden) _status.OnNodeForbidden(forbidden);
                return (Poll(TimeSpan.FromSeconds(30), poll), false);

            case ApiKind.Conflict when response.Code == "instance_superseded":
                _status.OnSuperseded();
                return (TimeSpan.FromSeconds(5), false);

            case ApiKind.ProtocolUnsupported:
                _pendingClaimId = null;
                _status.OnProtocolMismatch();
                return (TimeSpan.FromSeconds(5), false);

            case ApiKind.RouteAbsent or ApiKind.NotImplemented:
                _pendingClaimId = null;
                _status.OnApiUnsupported();
                return (TimeSpan.FromSeconds(5), false);

            case ApiKind.Canceled:
                return (TimeSpan.Zero, false);

            default:
                // Network, timeout, 5xx: the server may have leased a job whose answer was lost. Keep the claim id so the next
                // poll replays it (4.1.4), and never retry inside this tick.
                if (response.Transient) _pendingClaimId = request.ClaimId;
                else _pendingClaimId = null;
                _log.LogWarning("claim answered {Kind} status={Status}", response.Kind, response.Status);
                return (Poll(response.RetryAfter, poll), false);
        }
    }

    private bool StartJob(ClaimedJob job, long sendMs)
    {
        if (!Wire.JobIdPattern.IsMatch(job.Id) || job.Fence < 0 || job.LeaseRemainingMs <= 0)
        {
            _log.LogError("claim returned a malformed job; ignoring it");
            return false;
        }

        // Conservative local expiry from the CLAIM send time, the same rule as for heartbeats (4.2.3 rule 2).
        var lease = new JobLease(job.Id, job.Fence, job.HeartbeatEverySeconds, _clock.NowMs, sendMs + job.LeaseRemainingMs - Wire.LeaseSafetyMarginMs);
        _log.LogInformation("job claimed {Job} kind={Kind} fence={Fence} attempt={Attempt}", job.Id, job.Kind, job.Fence, job.Attempt);
        _supervisor.Start(job, lease);
        return true;
    }

    /// <summary>Retry-After (or the idle poll) with a floor of pollSeconds.min and up to +20% jitter (section 2.3).</summary>
    internal static TimeSpan Poll(TimeSpan? retryAfter, PollSettings poll)
    {
        var baseDelay = retryAfter ?? TimeSpan.FromSeconds(poll.Idle);
        var floor = TimeSpan.FromSeconds(Math.Max(1, poll.Min));
        if (baseDelay < floor) baseDelay = floor;
        return Backoff.RetryAfterWithJitter(baseDelay);
    }
}
