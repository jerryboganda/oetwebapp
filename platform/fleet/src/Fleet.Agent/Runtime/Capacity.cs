namespace Fleet.Agent;

/// <summary>Registry defaults of section 6.0, used for pre-claim admission (the claim response carries the binding limits).</summary>
internal static class KindRegistry
{
    private static readonly Dictionary<string, JobLimits> Defaults = new()
    {
        [JobKinds.PdfExtract] = new JobLimits { Weight = 1, CpuMilli = 1000, MemMiB = 2048, TmpMiB = 256, TimeoutSeconds = 120, MaxInputBytes = 104_857_600, MaxResultBytes = 8_388_608 },
        [JobKinds.CompanionIndexPrep] = new JobLimits { Weight = 1, CpuMilli = 1000, MemMiB = 2048, TmpMiB = 256, TimeoutSeconds = 120, MaxInputBytes = 104_857_600, MaxResultBytes = 8_388_608 },
        [JobKinds.MediaAudioExtract] = new JobLimits { Weight = 2, CpuMilli = 2000, MemMiB = 1024, TmpMiB = 1536, TimeoutSeconds = 900, MaxInputBytes = 805_306_368, MaxResultBytes = 262_144, MaxOutputBytes = 134_217_728, MaxOutputs = 64 },
        [JobKinds.MediaSpeakingJoin] = new JobLimits { Weight = 1, CpuMilli = 1000, MemMiB = 512, TmpMiB = 192, TimeoutSeconds = 240, MaxInputBytes = 67_108_864, MaxResultBytes = 16_384, MaxOutputBytes = 4_194_304, MaxOutputs = 1 },
    };

    public static JobLimits? For(string kind) => Defaults.TryGetValue(kind, out var limits) ? limits : null;
}

/// <summary>
/// Budget bookkeeping for the helper (protocol 3.8 item 4, 5.4, 6.0): weights against effective concurrency, plus CPU,
/// memory and tmpfs budgets. tmpfs is RAM, so a job reserves memMiB + tmpMiB of the memory budget (RW-116).
/// </summary>
internal sealed class CapacityAccountant
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Reservation> _running = new();
    private Budgets _budgets;
    private int _configured = 2;
    private int _effective = 2;
    private long _cacheMiB;

    private sealed record Reservation(string Kind, JobLimits Limits);

    public CapacityAccountant(Budgets budgets) => _budgets = budgets;

    public void SetBudgets(Budgets budgets)
    {
        lock (_gate) _budgets = budgets;
    }

    public void SetConfigured(int configured)
    {
        lock (_gate) _configured = Math.Clamp(configured, 0, 8);
    }

    public void SetEffective(int effective)
    {
        lock (_gate) _effective = Math.Clamp(effective, 0, 8);
    }

    /// <summary>Tmpfs held by the input cache counts against both the memory and tmp budgets.</summary>
    public void SetCacheMiB(long cacheMiB)
    {
        lock (_gate) _cacheMiB = Math.Max(0, cacheMiB);
    }

    public Budgets Budgets
    {
        get { lock (_gate) return _budgets; }
    }

    public int Configured
    {
        get { lock (_gate) return _configured; }
    }

    public int Effective
    {
        get { lock (_gate) return Math.Min(_configured, _effective); }
    }

    public void Add(string jobId, string kind, JobLimits limits)
    {
        lock (_gate) _running[jobId] = new Reservation(kind, limits);
    }

    public void Remove(string jobId)
    {
        lock (_gate) _running.Remove(jobId);
    }

    public int RunningCount
    {
        get { lock (_gate) return _running.Count; }
    }

    public CapacitySnapshot Snapshot()
    {
        lock (_gate)
        {
            var weight = 0;
            long cpu = 0;
            long mem = 0;
            long tmp = 0;
            foreach (var item in _running.Values)
            {
                weight += Math.Max(1, item.Limits.Weight);
                cpu += item.Limits.CpuMilli;
                mem += (long)item.Limits.MemMiB + item.Limits.TmpMiB;
                tmp += item.Limits.TmpMiB;
            }

            var effective = Math.Min(_configured, _effective);
            return new CapacitySnapshot(
                CpuBudget: _budgets.CpuMilli,
                MemBudget: _budgets.MemMiB,
                TmpBudget: _budgets.TmpMiB,
                CpuFree: Math.Max(0, _budgets.CpuMilli - cpu),
                MemFree: Math.Max(0, _budgets.MemMiB - mem - _cacheMiB),
                TmpFree: Math.Max(0, _budgets.TmpMiB - tmp - _cacheMiB),
                HeavySlotsTotal: _configured,
                HeavySlotsFree: Math.Max(0, effective - weight),
                LeasedWeight: weight,
                Configured: _configured,
                Effective: effective);
        }
    }

    public int RunningOfKind(string kind)
    {
        lock (_gate) return _running.Values.Count(r => r.Kind == kind);
    }

    /// <summary>
    /// Whether a job with these limits fits RIGHT NOW (RW-116): weight against free slots, memory including tmpfs, CPU
    /// and tmp. Used both to decide which kinds to offer and to double-check a claimed job.
    /// </summary>
    public bool CanAdmit(JobLimits limits)
    {
        var snapshot = Snapshot();
        return Math.Max(1, limits.Weight) <= snapshot.HeavySlotsFree
            && snapshot.MemFree >= (long)limits.MemMiB + limits.TmpMiB
            && snapshot.CpuFree >= limits.CpuMilli
            && snapshot.TmpFree >= limits.TmpMiB;
    }

    /// <summary>A claimed job whose limits can never fit the helper budgets is a server misconfiguration (limits_exceeded).</summary>
    public bool FitsBudgetsEver(JobLimits limits)
    {
        var budgets = Budgets;
        return (long)limits.MemMiB + limits.TmpMiB <= budgets.MemMiB
            && limits.CpuMilli <= budgets.CpuMilli
            && limits.TmpMiB <= budgets.TmpMiB;
    }
}

internal sealed record CapacitySnapshot(
    long CpuBudget,
    long MemBudget,
    long TmpBudget,
    long CpuFree,
    long MemFree,
    long TmpFree,
    int HeavySlotsTotal,
    int HeavySlotsFree,
    int LeasedWeight,
    int Configured,
    int Effective);
