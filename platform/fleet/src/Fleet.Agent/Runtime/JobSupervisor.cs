namespace Fleet.Agent;

/// <summary>
/// Owns the running jobs: registers each lease with the heartbeat service, the capacity accountant and the lease registry,
/// runs the job on its own task, releases everything when it ends, sheds the youngest job under memory pressure and drives the
/// graceful shutdown of section 5.5.
/// </summary>
internal sealed class JobSupervisor
{
    private readonly JobRunner _runner;
    private readonly CapacityAccountant _capacity;
    private readonly JobHeartbeatService _heartbeats;
    private readonly LeaseRegistry _leases;
    private readonly IMonotonicClock _clock;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private readonly Dictionary<string, Running> _running = new();

    private sealed class Running
    {
        public required ClaimedJob Job { get; init; }
        public required JobLease Lease { get; init; }
        public required long StartedMs { get; init; }
        public Task Task { get; set; } = Task.CompletedTask;
    }

    public JobSupervisor(JobRunner runner, CapacityAccountant capacity, JobHeartbeatService heartbeats, LeaseRegistry leases, IMonotonicClock clock, ILogger log)
    {
        _runner = runner;
        _capacity = capacity;
        _heartbeats = heartbeats;
        _leases = leases;
        _clock = clock;
        _log = log;
    }

    /// <summary>Raised after a job finished and released its resources (wakes the claim loop).</summary>
    public event Action? JobFinished;

    public int RunningCount
    {
        get { lock (_gate) return _running.Count; }
    }

    public void Start(ClaimedJob job, JobLease lease)
    {
        // Registered synchronously, BEFORE the next claim is composed, so capacity can never be oversubscribed.
        _capacity.Add(job.Id, job.Kind, job.Limits);
        _leases.Add(lease);
        _heartbeats.Register(lease);
        var entry = new Running { Job = job, Lease = lease, StartedMs = _clock.NowMs };
        lock (_gate) _running[job.Id] = entry;
        entry.Task = Task.Run(() => RunAsync(job, lease));
    }

    private async Task RunAsync(ClaimedJob job, JobLease lease)
    {
        try
        {
            await _runner.RunAsync(job, lease).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError("job {Job} runner faulted: {Error}", job.Id, Redact.Exception(ex));
        }
        finally
        {
            _heartbeats.Unregister(job.Id);
            _leases.Remove(job.Id);
            _capacity.Remove(job.Id);
            lock (_gate) _running.Remove(job.Id);
            lease.Dispose();
            JobFinished?.Invoke();
        }
    }

    /// <summary>Memory pressure below 10% free: abort the YOUNGEST running job, reported as fail(pressure_shed) (5.4).</summary>
    public bool ShedYoungest()
    {
        Running? youngest;
        lock (_gate)
        {
            youngest = _running.Values.Where(r => r.Lease.Reason == AbortReason.None).OrderByDescending(r => r.StartedMs).FirstOrDefault();
        }

        if (youngest is null) return false;
        _log.LogWarning("shedding job {Job} under memory pressure", youngest.Job.Id);
        return youngest.Lease.Abort(AbortReason.Shed);
    }

    /// <summary>
    /// Graceful shutdown (5.5): jobs expected to finish within 30 s are let run; the rest are aborted at once and reported as
    /// fail(shutdown). After the 30 s window every remaining job is aborted too.
    /// </summary>
    public async Task ShutdownAsync(CancellationToken hardStop)
    {
        List<Running> running;
        lock (_gate) running = _running.Values.ToList();
        if (running.Count == 0) return;

        var now = _clock.NowMs;
        foreach (var item in running)
        {
            var timeoutMs = Math.Max(1, item.Job.Limits.TimeoutSeconds) * 1000L;
            var remainingMs = timeoutMs - (now - item.StartedMs);
            if (remainingMs > 30_000) item.Lease.Abort(AbortReason.Shutdown);
        }

        var all = Task.WhenAll(running.Select(r => r.Task));
        if (!await WaitAsync(all, TimeSpan.FromSeconds(30), hardStop).ConfigureAwait(false))
        {
            foreach (var item in running) item.Lease.Abort(AbortReason.Shutdown);
            await WaitAsync(all, TimeSpan.FromSeconds(20), hardStop).ConfigureAwait(false);
        }
    }

    private static async Task<bool> WaitAsync(Task task, TimeSpan limit, CancellationToken hardStop)
    {
        try
        {
            var winner = await Task.WhenAny(task, Task.Delay(limit, hardStop)).ConfigureAwait(false);
            return winner == task;
        }
        catch (OperationCanceledException)
        {
            return task.IsCompleted;
        }
    }
}
