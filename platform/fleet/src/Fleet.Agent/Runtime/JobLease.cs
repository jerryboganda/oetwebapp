using System.Collections.Concurrent;

namespace Fleet.Agent;

internal enum AbortReason
{
    None = 0,
    /// <summary>409 lease_lost or leaseAudit invalid: abort at once, never call complete or fail (RW-055).</summary>
    LeaseLost,
    /// <summary>Local conservative expiry passed (RW-054): abort, never call complete.</summary>
    LocalExpiry,
    Quarantined,
    AuthFailed,
    Superseded,
    /// <summary>409 stale_input: discard, no report.</summary>
    StaleInput,
    /// <summary>Graceful shutdown: report fail(shutdown).</summary>
    Shutdown,
    /// <summary>Memory pressure shed: report fail(pressure_shed).</summary>
    Shed,
    /// <summary>limits.timeoutSeconds exceeded: report fail(timeout).</summary>
    Timeout,
}

/// <summary>
/// The agent-side view of one lease: monotonic local expiry, one abort token with exactly one winning reason.
/// </summary>
internal sealed class JobLease : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private long _expiryMs;
    private int _reason;
    private int _rssMiB;
    private int _cpuTenths;
    private volatile string _stage = "starting";

    public JobLease(string jobId, long fence, int heartbeatEverySeconds, long startedMs, long initialExpiryMs)
    {
        JobId = jobId;
        Fence = fence;
        HeartbeatEverySeconds = Math.Clamp(heartbeatEverySeconds, 5, 60);
        StartedMs = startedMs;
        _expiryMs = initialExpiryMs;
    }

    public string JobId { get; }
    public long Fence { get; }
    public int HeartbeatEverySeconds { get; }
    public long StartedMs { get; }

    /// <summary>Monotonic time after which the agent must treat the lease as lost.</summary>
    public long LocalExpiryMs => Interlocked.Read(ref _expiryMs);

    public AbortReason Reason => (AbortReason)Volatile.Read(ref _reason);

    public CancellationToken Token => _cts.Token;

    public string Stage
    {
        get => _stage;
        set => _stage = value;
    }

    public int RssMiB
    {
        get => Volatile.Read(ref _rssMiB);
        set => Volatile.Write(ref _rssMiB, value);
    }

    public double CpuPct
    {
        get => Volatile.Read(ref _cpuTenths) / 10.0;
        set => Volatile.Write(ref _cpuTenths, (int)Math.Round(value * 10.0));
    }

    /// <summary>
    /// localLeaseExpiry = sendMono + leaseRemainingMs - 5000 ms (protocol 4.2.3 rule 2). The send time is taken BEFORE the
    /// request leaves, so the server's receipt time is later and the result is conservative.
    /// </summary>
    public void UpdateExpiry(long sendMonoMs, long leaseRemainingMs) =>
        Interlocked.Exchange(ref _expiryMs, sendMonoMs + leaseRemainingMs - Wire.LeaseSafetyMarginMs);

    public bool IsExpired(long nowMs) => nowMs >= LocalExpiryMs;

    /// <summary>First caller wins; later callers keep the original reason.</summary>
    public bool Abort(AbortReason reason)
    {
        if (reason == AbortReason.None) return false;
        if (Interlocked.CompareExchange(ref _reason, (int)reason, 0) != 0) return false;
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The job already finished and released its lease.
        }

        return true;
    }

    public void Dispose() => _cts.Dispose();
}

/// <summary>All leases the agent currently believes it holds (reported in every node heartbeat, section 4.7.1).</summary>
internal sealed class LeaseRegistry
{
    private readonly ConcurrentDictionary<string, JobLease> _leases = new();

    public int Count => _leases.Count;

    public void Add(JobLease lease) => _leases[lease.JobId] = lease;

    public void Remove(string jobId) => _leases.TryRemove(jobId, out _);

    public JobLease? Find(string jobId, long fence) =>
        _leases.TryGetValue(jobId, out var lease) && lease.Fence == fence ? lease : null;

    public IReadOnlyList<JobLease> Snapshot() => _leases.Values.ToArray();

    public void AbortAll(AbortReason reason)
    {
        foreach (var lease in _leases.Values) lease.Abort(reason);
    }
}
