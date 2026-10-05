namespace Fleet.Agent;

/// <summary>
/// Composition root and lifecycle of the helper agent (protocol 5.1 - 5.5). Dedicated threads: node heartbeat (15 s), job
/// heartbeat (all leases), pressure sampler (5 s). One async claim loop. Jobs run on their own tasks, CPU-heavy parsing in an
/// isolated child process. Stateless: nothing is persisted across restarts (H8).
/// </summary>
internal sealed class AgentRuntime
{
    private readonly AgentOptions _options;
    private readonly ILogger _log;
    private readonly IMonotonicClock _clock;
    private readonly IChildRunner _children;
    private readonly IProcessRunner _processes;
    private readonly AgentIdentity _identity;
    private readonly CapacityAccountant _capacity;
    private readonly PressureController _pressure;
    private readonly HostSampler _sampler;
    private readonly ScratchManager _scratch;
    private readonly InputCache _cache;
    private readonly LeaseRegistry _leases = new();
    private readonly ExecutorRegistry _executors = new();
    private readonly JobHeartbeatService _jobHeartbeats;
    private readonly NodeHeartbeatService _nodeHeartbeat;
    private readonly JobSupervisor _supervisor;
    private readonly ClaimLoop _claims;
    private readonly SelfCheck _selfCheck;
    private readonly DedicatedLoop _nodeLoop;
    private readonly DedicatedLoop _jobLoop;
    private readonly DedicatedLoop _pressureLoop;
    private readonly CancellationTokenSource _stop = new();
    private Task _claimTask = Task.CompletedTask;
    private Task _selfCheckTask = Task.CompletedTask;

    public AgentRuntime(AgentOptions options, IRemoteWorkerApi api, ProtocolNegotiator negotiator, AgentIdentity identity,
        ILoggerFactory loggers, IMonotonicClock clock, IHostMetrics metrics, IChildRunner children, IProcessRunner processes)
    {
        _options = options;
        _identity = identity;
        _clock = clock;
        _children = children;
        _processes = processes;
        _log = loggers.CreateLogger("Fleet.Agent");

        Status = new AgentStatus(_log, identity.ImageDigest, identity.Version);
        _capacity = new CapacityAccountant(options.Budgets);
        _capacity.SetConfigured(2);
        _capacity.SetEffective(2);
        _pressure = new PressureController(2);
        _sampler = new HostSampler(metrics);
        _scratch = new ScratchManager(options.ScratchDirectory, options.TmpDirectory, _log);

        // The cache is capped at a quarter of the tmp budget (and 512 MiB) so it can never starve a job of tmpfs.
        var cacheCap = Math.Min(512L, options.Budgets.TmpMiB / 4) * 1024 * 1024;
        _cache = new InputCache(_scratch.CacheDirectory, cacheCap, 64L * 1024 * 1024, TimeSpan.FromMinutes(10), clock);

        var io = new ApiJobIo(api, _cache, Status, clock, _log, cacheChanged: UpdateCacheCapacity);
        _jobHeartbeats = new JobHeartbeatService(api, clock, Status, _log);
        var runner = new JobRunner(api, _executors, _scratch, io, Status, _capacity, clock, _log);
        _supervisor = new JobSupervisor(runner, _capacity, _jobHeartbeats, _leases, clock, _log);
        _nodeHeartbeat = new NodeHeartbeatService(api, Status, identity, negotiator, _capacity, _pressure, _sampler, _executors, _leases,
            clock, _log, options.Budgets, touchHealth: () => HealthFile.Touch(options.HealthFile));
        _claims = new ClaimLoop(api, Status, _capacity, _executors, _supervisor, identity, clock, _log);
        _selfCheck = new SelfCheck(children, processes, _scratch, options.FfmpegPath, _log);

        _nodeLoop = new DedicatedLoop("node-heartbeat", _nodeHeartbeat.Tick, _log);
        _jobLoop = new DedicatedLoop("job-heartbeat", _jobHeartbeats.Pass, _log, ThreadPriority.AboveNormal);
        _pressureLoop = new DedicatedLoop("pressure-sampler", PressureTick, _log);

        Status.DesiredRevisionStale += _nodeLoop.Poke;
        Status.Changed += OnStatusChanged;
    }

    public AgentStatus Status { get; }

    /// <summary>Raised with the process exit code when the agent must terminate itself (3 = instance superseded).</summary>
    public event Action<int>? ExitRequested;

    public void Start()
    {
        // The heartbeat threads are dedicated; a generous floor keeps HTTP completions flowing even when many jobs await I/O.
        ThreadPool.SetMinThreads(Math.Max(16, Environment.ProcessorCount * 4), Math.Max(16, Environment.ProcessorCount * 4));
        _scratch.WipeAtStart();
        _log.LogInformation("agent starting version={Version} instance={Instance} node={Node} engine={Engine}",
            _identity.Version, _identity.InstanceId, _options.NodeId, EngineVersions.Pdf);

        _nodeLoop.Start();
        _jobLoop.Start();
        _pressureLoop.Start();
        _selfCheckTask = Task.Run(() => SelfCheckLoopAsync(_stop.Token));
        _claimTask = Task.Run(() => _claims.RunAsync(_stop.Token));
    }

    /// <summary>Graceful shutdown of section 5.5.</summary>
    public async Task StopAsync(CancellationToken hardStop)
    {
        _log.LogInformation("agent stopping");
        Status.BeginStopping();
        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already stopped.
        }

        await Task.WhenAny(Task.WhenAll(_claimTask, _selfCheckTask), Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        await _supervisor.ShutdownAsync(hardStop).ConfigureAwait(false);
        _cache.Clear();
        if (Status.Facts is { Superseded: false, AuthFailed: false }) _nodeHeartbeat.SendFinal();
        _nodeLoop.Stop(TimeSpan.FromSeconds(3));
        _jobLoop.Stop(TimeSpan.FromSeconds(3));
        _pressureLoop.Stop(TimeSpan.FromSeconds(3));
        try
        {
            _scratch.WipeAtStart();
        }
        catch (Exception ex)
        {
            _log.LogWarning("final scratch wipe incomplete: {Error}", Redact.Exception(ex));
        }

        _log.LogInformation("agent stopped");
    }

    // ---- loops and handlers --------------------------------------------------------------------------------

    private TimeSpan PressureTick()
    {
        if (_sampler.Tick() && _sampler.Latest is { } sample)
        {
            var settings = Status.Desired.Pressure ?? new PressureSettings();
            var decision = _pressure.Observe(_clock.NowMs, _sampler.CpuPct15s, sample.MemFreePct, settings);
            _capacity.SetEffective(decision.Effective);
            Status.SetPressureReduced(decision.Reduced);
            if (decision.ShedYoungest) _supervisor.ShedYoungest();
        }

        return TimeSpan.FromSeconds(PressureController.SampleSeconds);
    }

    private async Task SelfCheckLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            SelfCheckResult result;
            try
            {
                result = await _selfCheck.RunAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError("self-check crashed: {Error}", Redact.Exception(ex));
                result = new SelfCheckResult(false, null, false);
            }

            ApplySelfCheck(result);
            try
            {
                // A failed check is retried soon (an operator may have fixed the host); a healthy one is re-run every 6 hours.
                await Task.Delay(result.PdfOk ? TimeSpan.FromHours(6) : TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal void ApplySelfCheck(SelfCheckResult result)
    {
        var executors = new List<IJobExecutor>();
        if (result.PdfOk)
        {
            executors.Add(new PdfChildExecutor(JobKinds.PdfExtract, EngineVersions.Pdf, _children, _log));
            if (CompanionChunkAdapter.Available)
            {
                executors.Add(new PdfChildExecutor(JobKinds.CompanionIndexPrep, EngineVersions.CompanionIndexPrep, _children, _log));
            }
        }

        if (result.MediaOk)
        {
            var audio = EngineVersions.Media(result.FfmpegVersion, "audio-extract:1");
            if (audio is not null) executors.Add(new AudioExtractExecutor(audio, _processes, _options.FfmpegPath, _log));
            var join = EngineVersions.Media(result.FfmpegVersion, "ffmpeg-pcm-join:1");
            if (join is not null && PcmJoinAdapter.Available) executors.Add(new SpeakingJoinExecutor(join, _processes, _options.FfmpegPath, _log));
        }

        _executors.Replace(executors);
        Status.SetSelfCheck(result.PdfOk);
        _log.LogInformation("executors offered: {Kinds}", string.Join(",", executors.Select(e => e.Kind)));
    }

    private void OnStatusChanged(AgentDecision decision)
    {
        switch (decision.State)
        {
            case AgentState.AuthFailed:
                // Stop everything (2.7): no further call may be made for these jobs.
                _leases.AbortAll(AbortReason.AuthFailed);
                _cache.Clear();
                UpdateCacheCapacity();
                break;

            case AgentState.Superseded:
                _leases.AbortAll(AbortReason.Superseded);
                _cache.Clear();
                ExitRequested?.Invoke(3);
                break;

            case AgentState.Draining when decision.Reason == "node_quarantined":
                _leases.AbortAll(AbortReason.Quarantined);
                _cache.Clear();
                UpdateCacheCapacity();
                break;
        }
    }

    private void UpdateCacheCapacity() => _capacity.SetCacheMiB(_cache.TotalBytes / (1024 * 1024));
}

/// <summary>Liveness marker for the container HEALTHCHECK (the agent exposes no port).</summary>
internal static class HealthFile
{
    public static void Touch(string path)
    {
        try
        {
            File.WriteAllText(path, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception)
        {
            // A missing health file only makes the container report unhealthy; never stop the loop for it.
        }
    }
}
