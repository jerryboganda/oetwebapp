namespace Fleet.Agent.Tests;

internal sealed class ScriptedMetrics : IHostMetrics
{
    public HostSample? Next { get; set; } = new HostSample(10, 80, 0.5, 8000, 6400, 4, 40_000);

    public HostSample? Sample() => Next;
}

/// <summary>The agent's runtime parts wired to fakes, without threads or real time. Delays are instant.</summary>
internal sealed class Harness : IDisposable
{
    public const string Version = "1.0.0";

    public FakeClock Clock { get; } = new();
    public FakeApi Api { get; } = new();
    public FakeIo Io { get; } = new();
    public TempDir Dir { get; } = new();
    public ProtocolNegotiator Negotiator { get; } = new();
    public AgentIdentity Identity { get; } = new(Version, TestIds.Digest, instanceId: Guid.Parse("c2f0a6a4-3a55-4d6e-b7f1-0a9b8c7d6e5f"));
    public AgentStatus Status { get; }
    public CapacityAccountant Capacity { get; } = new(new Budgets { CpuMilli = 3000, MemMiB = 5120, TmpMiB = 3072 });
    public PressureController Pressure { get; } = new(2);
    public ScriptedMetrics Metrics { get; } = new();
    public HostSampler Sampler { get; }
    public ExecutorRegistry Executors { get; } = new();
    public LeaseRegistry Leases { get; } = new();
    public ScratchManager Scratch { get; }
    public JobHeartbeatService JobHeartbeats { get; }
    public JobRunner Runner { get; }
    public JobSupervisor Supervisor { get; }
    public ClaimLoop Claims { get; }
    public NodeHeartbeatService Node { get; }

    public Harness(Func<IJobIo>? io = null, IChildRunner? children = null)
    {
        Status = new AgentStatus(TestLog.Instance, TestIds.Digest, Version);
        Sampler = new HostSampler(Metrics);
        Directory.CreateDirectory(Dir.File("scratch"));
        Directory.CreateDirectory(Dir.File("tmp"));
        Scratch = new ScratchManager(Dir.File("scratch"), Dir.File("tmp"), TestLog.Instance);
        Executors.Replace([new PdfChildExecutor(JobKinds.PdfExtract, EngineVersions.Pdf, children ?? new InProcessChildRunner(), TestLog.Instance)]);
        Capacity.SetConfigured(2);
        Capacity.SetEffective(2);
        var jobIo = io?.Invoke() ?? Io;
        JobHeartbeats = new JobHeartbeatService(Api, Clock, Status, TestLog.Instance);
        Runner = new JobRunner(Api, Executors, Scratch, jobIo, Status, Capacity, Clock, TestLog.Instance, delay: (_, _) => Task.CompletedTask);
        Supervisor = new JobSupervisor(Runner, Capacity, JobHeartbeats, Leases, Clock, TestLog.Instance);
        Claims = new ClaimLoop(Api, Status, Capacity, Executors, Supervisor, Identity, Clock, TestLog.Instance, delay: (_, _) => Task.CompletedTask);
        Node = new NodeHeartbeatService(Api, Status, Identity, Negotiator, Capacity, Pressure, Sampler, Executors, Leases, Clock, TestLog.Instance,
            new Budgets { CpuMilli = 3000, MemMiB = 5120, TmpMiB = 3072 });
    }

    /// <summary>First heartbeat answered and the self-check passed: the agent is Ready and may claim.</summary>
    public void MakeReady()
    {
        Node.Tick();
        Status.SetSelfCheck(true);
        Assert.Equal(AgentState.Ready, Status.Decision.State);
    }

    public static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 15_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException("condition not reached");
            await Task.Delay(20);
        }
    }

    public void Dispose() => Dir.Dispose();
}
