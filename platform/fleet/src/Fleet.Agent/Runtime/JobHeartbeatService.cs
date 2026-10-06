namespace Fleet.Agent;

/// <summary>
/// Renews every held lease on its own cadence (default every 20 s) and enforces the conservative local expiry. One
/// <see cref="Pass"/> is a pure scheduling step over a monotonic clock; the dedicated thread that drives it lives in the
/// runtime, so a CPU-bound or blocked job can never starve it (protocol 4.2.3, RW-053).
/// </summary>
internal sealed class JobHeartbeatService
{
    private readonly IRemoteWorkerApi _api;
    private readonly IMonotonicClock _clock;
    private readonly AgentStatus _status;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new();

    private sealed class Entry
    {
        public required JobLease Lease { get; init; }
        public long NextDueMs { get; set; }
        public int Failures { get; set; }
    }

    public JobHeartbeatService(IRemoteWorkerApi api, IMonotonicClock clock, AgentStatus status, ILogger log)
    {
        _api = api;
        _clock = clock;
        _status = status;
        _log = log;
    }

    public int ActiveCount
    {
        get { lock (_gate) return _entries.Count; }
    }

    public void Register(JobLease lease)
    {
        lock (_gate)
        {
            _entries[lease.JobId] = new Entry { Lease = lease, NextDueMs = _clock.NowMs + lease.HeartbeatEverySeconds * 1000L };
        }
    }

    public void Unregister(string jobId)
    {
        lock (_gate) _entries.Remove(jobId);
    }

    /// <summary>One scheduling pass: expire stale leases, send every due heartbeat concurrently. Returns the sleep before the next pass.</summary>
    public TimeSpan Pass()
    {
        Entry[] snapshot;
        lock (_gate) snapshot = _entries.Values.ToArray();

        var now = _clock.NowMs;
        var due = new List<Entry>();
        foreach (var entry in snapshot)
        {
            var lease = entry.Lease;
            if (lease.Reason != AbortReason.None) continue;
            if (lease.IsExpired(now))
            {
                // Never keep working past the conservative local expiry (RW-054).
                _log.LogWarning("lease {Job} passed its local expiry; aborting", lease.JobId);
                lease.Abort(AbortReason.LocalExpiry);
                continue;
            }

            if (now >= entry.NextDueMs && _status.JobHeartbeatsAllowed) due.Add(entry);
        }

        if (due.Count > 0)
        {
            var sends = due.Select(SendAsync).ToArray();
            try
            {
                Task.WaitAll(sends);
            }
            catch (AggregateException)
            {
                // SendAsync classifies its own failures; nothing here may stop the loop.
            }
        }

        return TimeSpan.FromMilliseconds(500);
    }

    private async Task SendAsync(Entry entry)
    {
        var lease = entry.Lease;
        // The send time is recorded BEFORE the request leaves: the server clock starts later, so the expiry is conservative.
        var sendMs = _clock.NowMs;
        var request = new JobHeartbeatRequest
        {
            Fence = lease.Fence,
            Stage = lease.Stage,
            Metrics = new JobMetricsBlock { RssMiB = lease.RssMiB, CpuPct = lease.CpuPct, ElapsedMs = sendMs - lease.StartedMs },
        };

        ApiResponse<JobHeartbeatResponse> response;
        try
        {
            response = await _api.JobHeartbeatAsync(lease.JobId, request, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning("job heartbeat threw: {Error}", Redact.Exception(ex));
            response = ApiResponse<JobHeartbeatResponse>.Failure(ApiKind.Network);
        }

        switch (response.Kind)
        {
            case ApiKind.Ok:
                var remaining = response.Value?.LeaseRemainingMs ?? 0;
                if (remaining > 0) lease.UpdateExpiry(sendMs, remaining);
                entry.Failures = 0;
                entry.NextDueMs = _clock.NowMs + lease.HeartbeatEverySeconds * 1000L;
                if (response.Value?.DesiredRevision is { } revision) _status.NoteDesiredRevision(revision);
                break;

            case ApiKind.Conflict when response.IsLeaseLost:
                _log.LogWarning("lease {Job} lost reason={Reason}", lease.JobId, response.Reason ?? "unknown");
                lease.Abort(AbortReason.LeaseLost);
                break;

            case ApiKind.NotFound:
                lease.Abort(AbortReason.LeaseLost);
                break;

            case ApiKind.Unauthorized:
                _status.OnAuthFailed();
                lease.Abort(AbortReason.AuthFailed);
                break;

            case ApiKind.Forbidden when response.Code == "node_quarantined":
                lease.Abort(AbortReason.Quarantined);
                break;

            case ApiKind.RouteAbsent or ApiKind.NotImplemented:
                // Rollback skew: stop heartbeating; the lease runs out at its local expiry (5.6).
                _status.OnApiUnsupported();
                break;

            case ApiKind.ProtocolUnsupported:
                _status.OnProtocolMismatch();
                break;

            default:
                entry.Failures++;
                entry.NextDueMs = _clock.NowMs + (long)Backoff.Compute(entry.Failures - 1).TotalMilliseconds;
                break;
        }
    }
}
