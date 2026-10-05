using Fleet.Core.Policy;

namespace Fleet.Core.Placement;

public sealed record AgentKind(string Kind, IReadOnlyList<int> SchemaVersions, string EngineVersion);

/// <summary>The capacity block of the node's latest heartbeat (OET-RWP/1 section 4.7).</summary>
public sealed record NodeCapacity(
    int CpuBudgetFreeMilli,
    int MemBudgetFreeMiB,
    int TmpFreeMiB,
    int HeavySlotsFree,
    int EffectiveConcurrency);

/// <summary>What the manager knows about one node, read from the API node object (section 7.1.2).</summary>
public sealed record NodeSnapshot(
    string Id,
    string NodeRef,
    string Status,
    string Health,
    bool Paused,
    NodePolicy Policy,
    IReadOnlyList<AgentKind> Kinds,
    NodeCapacity Capacity,
    int LeasedWeight,
    IReadOnlyDictionary<string, int>? LeasedByKind = null);

public sealed record PlacementRequest(
    string Kind,
    int SchemaVersion,
    string EngineVersion,
    int Weight,
    int CpuMilli,
    int MemMiB,
    int TmpMiB,
    string Purpose = "apply",
    string? TargetNodeId = null);

public enum PlacementKind
{
    Remote,
    Local,
    Wait,
}

public sealed record PlacementDecision(
    PlacementKind Kind,
    string? NodeId,
    string Reason,
    string? ReservationId,
    IReadOnlyList<string> Rejections);

public sealed record PlacementOptions
{
    /// <summary>Kinds whose per-kind feature flag is on. Null means "do not filter by flags".</summary>
    public IReadOnlySet<string>? EnabledKinds { get; init; }

    /// <summary>Queue age after which the primary runs the work even without headroom (section 3.7).</summary>
    public TimeSpan FallbackHardAfter { get; init; } = TimeSpan.FromMinutes(60);
}

/// <summary>Cgroup v2 pressure of the primary's API process (section 3.8). Null members mean unreadable.</summary>
public sealed record PrimaryPressure(double? CpuSomeAvg10, double? MemorySomeAvg10, double? MemoryWorkingSetPct);

public static class PrimaryHeadroom
{
    public const double CpuSomeAvg10Max = 25;
    public const double MemorySomeAvg10Max = 5;
    public const double WorkingSetMaxPct = 75;

    /// <summary>Fails closed: any unreadable figure means there is NO headroom.</summary>
    public static bool HasHeadroom(PrimaryPressure? pressure) =>
        pressure is { CpuSomeAvg10: { } cpu, MemorySomeAvg10: { } memory, MemoryWorkingSetPct: { } workingSet }
        && cpu < CpuSomeAvg10Max
        && memory < MemorySomeAvg10Max
        && workingSet < WorkingSetMaxPct;
}

public sealed record CapacityReservation(
    string Id,
    string NodeId,
    string Kind,
    int Weight,
    int CpuMilli,
    int MemMiB,
    int TmpMiB,
    DateTimeOffset ExpiresAt);

public sealed record ReservedTotals(
    int Weight,
    int CpuMilli,
    int MemMiB,
    int TmpMiB,
    IReadOnlyDictionary<string, int> WeightByKind);

/// <summary>
/// Capacity that was promised by a placement decision but is not yet visible in the node's
/// heartbeat. Entries expire (the next heartbeat reflects the lease) so a lost job can never
/// pin capacity forever. Thread-safe; the engine serialises check-and-reserve.
/// </summary>
public sealed class CapacityReservations
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CapacityReservation> _byId = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private readonly TimeSpan _ttl;

    public CapacityReservations(TimeProvider time, TimeSpan? ttl = null)
    {
        _time = time;
        _ttl = ttl ?? TimeSpan.FromSeconds(45);
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                Prune();
                return _byId.Count;
            }
        }
    }

    public CapacityReservation Reserve(string nodeId, string kind, int weight, int cpuMilli, int memMiB, int tmpMiB)
    {
        lock (_gate)
        {
            var reservation = new CapacityReservation(
                "rsv_" + Guid.NewGuid().ToString("N"),
                nodeId,
                kind,
                weight,
                cpuMilli,
                memMiB,
                tmpMiB,
                _time.GetUtcNow() + _ttl);
            _byId[reservation.Id] = reservation;
            return reservation;
        }
    }

    public bool Release(string reservationId)
    {
        lock (_gate)
        {
            return _byId.Remove(reservationId);
        }
    }

    public ReservedTotals Totals(string nodeId)
    {
        lock (_gate)
        {
            Prune();
            var weight = 0;
            var cpu = 0;
            var mem = 0;
            var tmp = 0;
            var byKind = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var reservation in _byId.Values)
            {
                if (!string.Equals(reservation.NodeId, nodeId, StringComparison.Ordinal))
                {
                    continue;
                }

                weight += reservation.Weight;
                cpu += reservation.CpuMilli;
                mem += reservation.MemMiB;
                tmp += reservation.TmpMiB;
                byKind[reservation.Kind] = byKind.GetValueOrDefault(reservation.Kind) + reservation.Weight;
            }

            return new ReservedTotals(weight, cpu, mem, tmp, byKind);
        }
    }

    private void Prune()
    {
        var now = _time.GetUtcNow();
        var expired = _byId.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToList();
        foreach (var id in expired)
        {
            _byId.Remove(id);
        }
    }
}

/// <summary>
/// Manager-side placement (OET-RWP/1 section 3.8). The API remains the authority at claim time;
/// this engine answers "where would this work go" for the dashboard, for what-if checks and for
/// any producer that asks the manager. It reserves capacity per decision so concurrent
/// callers cannot oversubscribe a node, honours drain/pause, per-kind caps and resource budgets,
/// and only falls back to the primary when it has headroom (or after the hard wait).
/// </summary>
public sealed class PlacementEngine
{
    private readonly object _gate = new();
    private readonly CapacityReservations _reservations;
    private readonly PlacementOptions _options;

    public PlacementEngine(CapacityReservations reservations, PlacementOptions? options = null)
    {
        _reservations = reservations;
        _options = options ?? new PlacementOptions();
    }

    public CapacityReservations Reservations => _reservations;

    public bool Release(string reservationId) => _reservations.Release(reservationId);

    /// <summary>Eligibility of one node for one job (items 1-5 of section 3.8). Pure apart from reading reservations.</summary>
    public bool IsEligible(NodeSnapshot node, PlacementRequest job, out string reason) =>
        TryEligible(node, job, out reason, out _, out _);

    public PlacementDecision Decide(
        PlacementRequest job,
        IReadOnlyList<NodeSnapshot> nodes,
        PrimaryPressure? primary,
        TimeSpan waitedFor)
    {
        lock (_gate)
        {
            NodeSnapshot? best = null;
            var bestNorm = double.MaxValue;
            var bestFree = -1;
            var rejections = new List<string>();

            foreach (var node in nodes)
            {
                if (!TryEligible(node, job, out var reason, out var norm, out var free))
                {
                    rejections.Add(node.NodeRef + ":" + reason);
                    continue;
                }

                var better = best is null
                    || norm < bestNorm - 1e-9
                    || (Math.Abs(norm - bestNorm) <= 1e-9
                        && (free > bestFree
                            || (free == bestFree && string.CompareOrdinal(node.NodeRef, best.NodeRef) < 0)));
                if (better)
                {
                    best = node;
                    bestNorm = norm;
                    bestFree = free;
                }
            }

            if (best is not null)
            {
                var reservation = _reservations.Reserve(best.Id, job.Kind, job.Weight, job.CpuMilli, job.MemMiB, job.TmpMiB);
                return new PlacementDecision(PlacementKind.Remote, best.Id, "remote", reservation.Id, rejections);
            }

            if (string.Equals(job.Purpose, "canary", StringComparison.Ordinal))
            {
                // A canary exists to test one specific node; it never falls back to the primary.
                return new PlacementDecision(PlacementKind.Wait, null, "canary_no_target", null, rejections);
            }

            if (PrimaryHeadroom.HasHeadroom(primary))
            {
                return new PlacementDecision(PlacementKind.Local, null, "local_headroom", null, rejections);
            }

            if (waitedFor >= _options.FallbackHardAfter)
            {
                return new PlacementDecision(PlacementKind.Local, null, "local_hard_after", null, rejections);
            }

            return new PlacementDecision(PlacementKind.Wait, null, "no_node_no_headroom", null, rejections);
        }
    }

    private bool TryEligible(NodeSnapshot node, PlacementRequest job, out string reason, out double norm, out int free)
    {
        norm = 0;
        free = 0;

        var isCanary = string.Equals(job.Purpose, "canary", StringComparison.Ordinal);
        var statusOk = string.Equals(node.Status, "Active", StringComparison.Ordinal)
            || (isCanary && string.Equals(node.Status, "Probation", StringComparison.Ordinal));
        if (!statusOk)
        {
            reason = "status_" + node.Status;
            return false;
        }

        if (node.Paused)
        {
            reason = "paused";
            return false;
        }

        if (!string.Equals(node.Health, "Online", StringComparison.Ordinal))
        {
            reason = "health_" + node.Health;
            return false;
        }

        if (job.TargetNodeId is not null && !string.Equals(job.TargetNodeId, node.Id, StringComparison.Ordinal))
        {
            reason = "target_other_node";
            return false;
        }

        if (!node.Policy.AllowedKinds.Contains(job.Kind, StringComparer.Ordinal))
        {
            reason = "kind_not_allowed";
            return false;
        }

        if (_options.EnabledKinds is not null && !_options.EnabledKinds.Contains(job.Kind))
        {
            reason = "kind_flag_off";
            return false;
        }

        var advertised = node.Kinds.Any(k =>
            string.Equals(k.Kind, job.Kind, StringComparison.Ordinal)
            && string.Equals(k.EngineVersion, job.EngineVersion, StringComparison.Ordinal)
            && k.SchemaVersions.Contains(job.SchemaVersion));
        if (!advertised)
        {
            reason = "kind_version_mismatch";
            return false;
        }

        var reserved = _reservations.Totals(node.Id);
        var cap = Math.Min(node.Policy.MaxConcurrency, node.Capacity.EffectiveConcurrency);
        var used = node.LeasedWeight + reserved.Weight;
        if (used + job.Weight > cap)
        {
            reason = "no_capacity";
            return false;
        }

        var perKindCap = node.Policy.PerKind.TryGetValue(job.Kind, out var limit) ? limit : node.Policy.MaxConcurrency;
        var leasedOfKind = node.LeasedByKind is not null && node.LeasedByKind.TryGetValue(job.Kind, out var leased) ? leased : 0;
        if (leasedOfKind + reserved.WeightByKind.GetValueOrDefault(job.Kind) + job.Weight > perKindCap)
        {
            reason = "kind_limit";
            return false;
        }

        free = node.Capacity.HeavySlotsFree - reserved.Weight;
        if (free < job.Weight
            || node.Capacity.MemBudgetFreeMiB - reserved.MemMiB < job.MemMiB + job.TmpMiB
            || node.Capacity.CpuBudgetFreeMilli - reserved.CpuMilli < job.CpuMilli
            || node.Capacity.TmpFreeMiB - reserved.TmpMiB < job.TmpMiB)
        {
            reason = "no_capacity";
            return false;
        }

        norm = (double)used / Math.Max(1, cap);
        free = cap - used;
        reason = "eligible";
        return true;
    }
}

public enum PressureAction
{
    None,
    Reduced,
    Restored,
    Shed,
}

/// <summary>
/// Hysteresis for node pressure (OET-RWP/1 section 5.4), driven by timestamps so the sampling
/// cadence does not matter. Reduce one slot after the breach has lasted <c>reduceSustain</c> (15 s),
/// again every 15 s while it holds; restore one slot only after a CONTINUOUS calm of
/// <c>restoreAfterSeconds</c> (120 s) below the (lower) restore thresholds; free memory under
/// <c>shedMemFreePct</c> asks to shed the youngest job. Samples in the dead band between the
/// reduce and restore thresholds neither reduce nor count towards a restore.
/// </summary>
public sealed class PressureGovernor
{
    private readonly PressureSettings _settings;
    private readonly int _configured;
    private readonly TimeSpan _reduceSustain;
    private readonly int _shedMemFreePct;
    private DateTimeOffset? _breachSince;
    private DateTimeOffset? _lastReduceAt;
    private DateTimeOffset? _lastShedAt;
    private DateTimeOffset? _calmSince;

    public PressureGovernor(PressureSettings settings, int configuredConcurrency, int reduceSustainSeconds = 15, int shedMemFreePct = 10)
    {
        _settings = settings;
        _configured = Math.Max(0, configuredConcurrency);
        _reduceSustain = TimeSpan.FromSeconds(reduceSustainSeconds);
        _shedMemFreePct = shedMemFreePct;
        EffectiveConcurrency = _configured;
    }

    public int EffectiveConcurrency { get; private set; }

    public bool IsReduced => EffectiveConcurrency < _configured;

    public string Pressure => IsReduced ? "reduced" : "normal";

    public PressureAction Observe(DateTimeOffset at, double cpuPct15s, double memFreePct)
    {
        var action = PressureAction.None;
        var breach = cpuPct15s > _settings.ReduceCpuPct || memFreePct < _settings.ReduceMemFreePct;

        if (breach)
        {
            _calmSince = null;
            _breachSince ??= at;
            var sustained = at - _breachSince.Value >= _reduceSustain;
            var paced = _lastReduceAt is null || at - _lastReduceAt.Value >= _reduceSustain;
            if (sustained && paced && EffectiveConcurrency > 0)
            {
                EffectiveConcurrency--;
                _lastReduceAt = at;
                action = PressureAction.Reduced;
            }

            if (memFreePct < _shedMemFreePct && (_lastShedAt is null || at - _lastShedAt.Value >= _reduceSustain))
            {
                _lastShedAt = at;
                action = PressureAction.Shed;
            }

            return action;
        }

        _breachSince = null;
        var calm = cpuPct15s < _settings.RestoreCpuPct && memFreePct > _settings.RestoreMemFreePct;
        if (!calm)
        {
            _calmSince = null;
            return action;
        }

        _calmSince ??= at;
        if (EffectiveConcurrency < _configured
            && at - _calmSince.Value >= TimeSpan.FromSeconds(_settings.RestoreAfterSeconds))
        {
            EffectiveConcurrency++;
            _calmSince = at;
            action = PressureAction.Restored;
        }

        return action;
    }
}
