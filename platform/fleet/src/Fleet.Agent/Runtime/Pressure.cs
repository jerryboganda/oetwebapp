using System.Globalization;

namespace Fleet.Agent;

/// <summary>One host-wide measurement (HOST values from /proc, never cgroup values, protocol 5.4).</summary>
internal readonly record struct HostSample(
    double CpuPct,
    double MemFreePct,
    double Load1,
    long MemTotalMiB,
    long MemAvailableMiB,
    int CpuCores,
    long DiskFreeMiB);

internal interface IHostMetrics
{
    /// <summary>Returns null when the platform cannot supply the numbers (never on the Linux container).</summary>
    HostSample? Sample();
}

internal readonly record struct PressureDecision(int Effective, bool Reduced, bool ShedYoungest);

/// <summary>
/// Admission pressure controller with hysteresis (protocol 5.4, RW-124). Pure state machine: feed it one sample per 5 s.
/// Reduce: host cpu15s above reduceCpuPct OR memFree below reduceMemFreePct for 3 consecutive samples (15 s), one step at
/// most every 15 s while the condition holds. Restore: cpu15s below restoreCpuPct AND memFree above restoreMemFreePct
/// continuously for restoreAfterSeconds, one step per interval. Shed: memFree below 10% sheds the youngest job.
/// Running jobs are never touched for CPU pressure.
/// </summary>
internal sealed class PressureController
{
    public const int SampleSeconds = 5;
    public const int ConsecutiveSamplesToReduce = 3;
    public const int ReduceStepSeconds = 15;
    public const double ShedMemFreePct = 10;

    private readonly object _gate = new();
    private int _configured;
    private int _effective;
    private int _overSamples;
    private long _lastReduceMs = long.MinValue;
    private long _calmSinceMs = -1;

    public PressureController(int configured)
    {
        _configured = Math.Max(0, configured);
        _effective = _configured;
    }

    public int Effective
    {
        get { lock (_gate) return _effective; }
    }

    public bool Reduced
    {
        get { lock (_gate) return _effective < _configured; }
    }

    /// <summary>A policy change of maxConcurrency: a lowered ceiling clamps now, a raised one applies unless pressure holds it down.</summary>
    public void SetConfigured(int configured)
    {
        lock (_gate)
        {
            var wasReduced = _effective < _configured;
            _configured = Math.Max(0, configured);
            _effective = wasReduced ? Math.Min(_effective, _configured) : _configured;
        }
    }

    public PressureDecision Observe(long nowMs, double cpuPct15s, double memFreePct, PressureSettings settings)
    {
        lock (_gate)
        {
            var over = cpuPct15s > settings.ReduceCpuPct || memFreePct < settings.ReduceMemFreePct;
            var calm = cpuPct15s < settings.RestoreCpuPct && memFreePct > settings.RestoreMemFreePct;

            if (over)
            {
                _overSamples++;
                _calmSinceMs = -1;
                if (_overSamples >= ConsecutiveSamplesToReduce
                    && (_lastReduceMs == long.MinValue || nowMs - _lastReduceMs >= ReduceStepSeconds * 1000L)
                    && _effective > 0)
                {
                    _effective--;
                    _lastReduceMs = nowMs;
                }
            }
            else
            {
                _overSamples = 0;
                if (calm)
                {
                    if (_calmSinceMs < 0) _calmSinceMs = nowMs;
                    if (_effective < _configured && nowMs - _calmSinceMs >= settings.RestoreAfterSeconds * 1000L)
                    {
                        _effective++;
                        _calmSinceMs = nowMs;
                    }
                }
                else
                {
                    // Inside the hysteresis band nothing improves, and the continuous-calm clock restarts.
                    _calmSinceMs = -1;
                }
            }

            return new PressureDecision(_effective, _effective < _configured, memFreePct < ShedMemFreePct);
        }
    }
}

/// <summary>Keeps the last three CPU samples so cpuPct15s is a real 15 s average (3 samples of 5 s).</summary>
internal sealed class HostSampler
{
    private readonly IHostMetrics _metrics;
    private readonly Queue<double> _cpu = new();
    private readonly object _gate = new();
    private HostSample? _latest;

    public HostSampler(IHostMetrics metrics) => _metrics = metrics;

    public HostSample? Latest
    {
        get { lock (_gate) return _latest; }
    }

    public double CpuPct15s
    {
        get
        {
            lock (_gate) return _cpu.Count == 0 ? 0 : _cpu.Average();
        }
    }

    public bool Tick()
    {
        var sample = _metrics.Sample();
        if (sample is null) return false;
        lock (_gate)
        {
            _latest = sample;
            _cpu.Enqueue(sample.Value.CpuPct);
            while (_cpu.Count > 3) _cpu.Dequeue();
        }

        return true;
    }
}

/// <summary>/proc based host metrics. Parsing is static so it can be tested with captured text.</summary>
internal sealed class ProcHostMetrics : IHostMetrics
{
    private readonly string _procRoot;
    private readonly string _diskPath;
    private (long Total, long Idle)? _previous;

    public ProcHostMetrics(string procRoot = "/proc", string diskPath = "/")
    {
        _procRoot = procRoot;
        _diskPath = diskPath;
    }

    public HostSample? Sample()
    {
        try
        {
            var stat = ReadCpuTimes(File.ReadAllText(Path.Combine(_procRoot, "stat")));
            var mem = ParseMemInfo(File.ReadAllText(Path.Combine(_procRoot, "meminfo")));
            if (stat is null || mem is null) return null;

            double cpu = 0;
            if (_previous is { } previous)
            {
                var total = stat.Value.Total - previous.Total;
                var idle = stat.Value.Idle - previous.Idle;
                if (total > 0) cpu = Math.Clamp(100.0 * (1.0 - (double)idle / total), 0, 100);
            }

            _previous = stat;

            var load1 = 0.0;
            var loadPath = Path.Combine(_procRoot, "loadavg");
            if (File.Exists(loadPath))
            {
                var first = File.ReadAllText(loadPath).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (first is not null) double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out load1);
            }

            long diskFree = 0;
            try
            {
                diskFree = new DriveInfo(_diskPath).AvailableFreeSpace / (1024 * 1024);
            }
            catch (Exception)
            {
                // Disk numbers are informational only.
            }

            var totalMiB = mem.Value.TotalKiB / 1024;
            var availMiB = mem.Value.AvailableKiB / 1024;
            var freePct = totalMiB > 0 ? 100.0 * availMiB / totalMiB : 100.0;
            return new HostSample(cpu, freePct, load1, totalMiB, availMiB, Environment.ProcessorCount, diskFree);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Parses the aggregate "cpu" line of /proc/stat into (total, idle) jiffies. Guest time is already inside user.</summary>
    public static (long Total, long Idle)? ReadCpuTimes(string procStat)
    {
        foreach (var line in procStat.Split('\n'))
        {
            if (!line.StartsWith("cpu ", StringComparison.Ordinal)) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) return null;
            var values = new long[Math.Min(parts.Length - 1, 8)];
            for (var i = 0; i < values.Length; i++)
            {
                if (!long.TryParse(parts[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out values[i])) return null;
            }

            // user nice system idle iowait irq softirq steal
            var idle = values[3] + (values.Length > 4 ? values[4] : 0);
            return (values.Sum(), idle);
        }

        return null;
    }

    public static (long TotalKiB, long AvailableKiB)? ParseMemInfo(string meminfo)
    {
        long total = -1;
        long available = -1;
        foreach (var line in meminfo.Split('\n'))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal)) total = KiB(line);
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal)) available = KiB(line);
        }

        return total > 0 && available >= 0 ? (total, available) : null;
    }

    private static long KiB(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : -1;
    }
}
