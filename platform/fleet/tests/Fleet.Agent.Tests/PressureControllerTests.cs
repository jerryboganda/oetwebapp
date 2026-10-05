namespace Fleet.Agent.Tests;

/// <summary>RW-124: admission pressure with hysteresis (protocol 5.4).</summary>
public sealed class PressureControllerTests
{
    private static readonly PressureSettings Settings = new();
    private const long Step = PressureController.SampleSeconds * 1000L;

    private static PressureDecision Feed(PressureController controller, ref long now, double cpu, double memFree, int samples)
    {
        PressureDecision last = default;
        for (var i = 0; i < samples; i++)
        {
            last = controller.Observe(now, cpu, memFree, Settings);
            now += Step;
        }

        return last;
    }

    [Fact]
    public void reduces_only_after_three_consecutive_over_samples()
    {
        var controller = new PressureController(2);
        long now = 0;

        Assert.Equal(2, Feed(controller, ref now, cpu: 90, memFree: 50, samples: 2).Effective);
        var third = Feed(controller, ref now, cpu: 90, memFree: 50, samples: 1);

        Assert.Equal(1, third.Effective);
        Assert.True(third.Reduced);
    }

    [Fact]
    public void a_calm_sample_resets_the_over_counter()
    {
        var controller = new PressureController(2);
        long now = 0;

        Feed(controller, ref now, cpu: 90, memFree: 50, samples: 2);
        Feed(controller, ref now, cpu: 40, memFree: 50, samples: 1);
        var after = Feed(controller, ref now, cpu: 90, memFree: 50, samples: 2);

        Assert.Equal(2, after.Effective);
    }

    [Fact]
    public void low_free_memory_alone_triggers_a_reduction()
    {
        var controller = new PressureController(2);
        long now = 0;

        var decision = Feed(controller, ref now, cpu: 10, memFree: 15, samples: 3);

        Assert.Equal(1, decision.Effective);
    }

    [Fact]
    public void steps_down_at_most_once_per_fifteen_seconds_and_never_below_zero()
    {
        var controller = new PressureController(2);
        long now = 0;

        // Samples at 0,5,10 s: first reduction at t=10 s. The next sample (15 s) is only 5 s later: no second step yet.
        Feed(controller, ref now, cpu: 95, memFree: 50, samples: 3);
        Assert.Equal(1, controller.Effective);
        Feed(controller, ref now, cpu: 95, memFree: 50, samples: 1);
        Assert.Equal(1, controller.Effective);
        Feed(controller, ref now, cpu: 95, memFree: 50, samples: 2);
        Assert.Equal(0, controller.Effective);
        Feed(controller, ref now, cpu: 95, memFree: 50, samples: 12);
        Assert.Equal(0, controller.Effective);
    }

    [Fact]
    public void restores_only_after_a_continuous_calm_period_and_one_step_per_period()
    {
        var controller = new PressureController(2);
        long now = 0;
        Feed(controller, ref now, cpu: 95, memFree: 50, samples: 9); // down to 0
        Assert.Equal(0, controller.Effective);

        // 119 s of calm: nothing yet (calm starts at the first calm sample).
        Feed(controller, ref now, cpu: 30, memFree: 60, samples: 24);
        Assert.Equal(0, controller.Effective);

        // 120 s reached: one step up, then another full period for the next.
        Feed(controller, ref now, cpu: 30, memFree: 60, samples: 1);
        Assert.Equal(1, controller.Effective);
        Feed(controller, ref now, cpu: 30, memFree: 60, samples: 23);
        Assert.Equal(1, controller.Effective);
        var back = Feed(controller, ref now, cpu: 30, memFree: 60, samples: 1);
        Assert.Equal(2, back.Effective);
        Assert.False(back.Reduced);
    }

    [Fact]
    public void the_hysteresis_band_neither_reduces_nor_restores_and_restarts_the_calm_clock()
    {
        var controller = new PressureController(2);
        long now = 0;
        Feed(controller, ref now, cpu: 95, memFree: 50, samples: 3);
        Assert.Equal(1, controller.Effective);

        // 70% CPU is between restore (60) and reduce (80): the calm clock must not run.
        Feed(controller, ref now, cpu: 70, memFree: 50, samples: 40);
        Assert.Equal(1, controller.Effective);

        // Calm again: the full period must elapse from the first calm sample.
        Feed(controller, ref now, cpu: 30, memFree: 60, samples: 24);
        Assert.Equal(1, controller.Effective);
        Feed(controller, ref now, cpu: 30, memFree: 60, samples: 1);
        Assert.Equal(2, controller.Effective);
    }

    [Fact]
    public void memory_below_ten_percent_asks_to_shed_but_cpu_pressure_never_does()
    {
        var controller = new PressureController(2);
        long now = 0;

        var cpuOnly = Feed(controller, ref now, cpu: 99, memFree: 50, samples: 10);
        var memory = controller.Observe(now, 10, 9, Settings);

        Assert.False(cpuOnly.ShedYoungest);
        Assert.True(memory.ShedYoungest);
    }

    [Fact]
    public void a_policy_change_of_max_concurrency_clamps_and_raises()
    {
        var controller = new PressureController(4);
        controller.SetConfigured(2);
        Assert.Equal(2, controller.Effective);

        controller.SetConfigured(6);
        Assert.Equal(6, controller.Effective);
    }

    [Fact]
    public void proc_parsers_read_cpu_and_memory_from_captured_text()
    {
        var cpu = ProcHostMetrics.ReadCpuTimes("cpu  100 10 50 800 40 5 5 0 0 0\ncpu0 1 1 1 1 1 1 1 1 1 1\n");
        var mem = ProcHostMetrics.ParseMemInfo("MemTotal:        8089600 kB\nMemFree:          100000 kB\nMemAvailable:    6291456 kB\n");

        Assert.Equal((1010L, 840L), cpu);
        Assert.Equal((8089600L, 6291456L), mem);
        Assert.Null(ProcHostMetrics.ParseMemInfo("MemTotal: 1 kB\n"));
        Assert.Null(ProcHostMetrics.ReadCpuTimes("nothing here"));
    }

    [Fact]
    public void host_sampler_keeps_a_three_sample_cpu_average()
    {
        var samples = new Queue<double>([10, 20, 30, 40]);
        var sampler = new HostSampler(new ScriptedMetrics(() => new HostSample(samples.Dequeue(), 80, 0.5, 8000, 6400, 4, 1000)));

        for (var i = 0; i < 4; i++) Assert.True(sampler.Tick());

        Assert.Equal(30, sampler.CpuPct15s, 3); // (20 + 30 + 40) / 3
    }

    private sealed class ScriptedMetrics : IHostMetrics
    {
        private readonly Func<HostSample?> _next;

        public ScriptedMetrics(Func<HostSample?> next) => _next = next;

        public HostSample? Sample() => _next();
    }
}
