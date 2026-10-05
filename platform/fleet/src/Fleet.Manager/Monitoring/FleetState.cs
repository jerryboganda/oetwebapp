using System.Globalization;
using System.Text.RegularExpressions;
using Fleet.Core.Placement;
using Fleet.Manager.Api;

namespace Fleet.Manager.Monitoring;

public sealed record IntegrityStatus(
    bool AuditChainIntact,
    bool OperationsChainIntact,
    string? Problem,
    uint MasterKeyId,
    DateTimeOffset CheckedAt);

/// <summary>One change of a node's API status or derived health, as the monitor saw it (the dashboard's health history).</summary>
public sealed record HealthTransition(
    DateTimeOffset At,
    string NodeId,
    string NodeRef,
    string? FromStatus,
    string? FromHealth,
    string Status,
    string Health);

/// <summary>
/// The manager's in-memory picture of the fleet, refreshed by <see cref="NodeMonitor"/> from the OET
/// API (the authoritative liveness view, RW-144) and read by the dashboard, SSE, metrics and placement.
/// It also keeps a short, bounded history of status and health changes per node. The history is in memory
/// only (a restart starts it again); the durable record of what happened to a host is the audit log.
/// </summary>
public sealed class FleetState
{
    /// <summary>Entries kept per node (newest wins).</summary>
    public const int MaxHistoryPerNode = 100;

    private readonly object _gate = new();
    private readonly Dictionary<string, List<HealthTransition>> _history = new(StringComparer.Ordinal);
    private IReadOnlyList<ApiNode> _nodes = Array.Empty<ApiNode>();
    private DateTimeOffset? _lastPollAt;
    private bool _apiReachable;
    private string? _lastApiError;
    private IntegrityStatus? _integrity;

    public IReadOnlyList<ApiNode> Nodes
    {
        get
        {
            lock (_gate)
            {
                return _nodes;
            }
        }
    }

    public DateTimeOffset? LastPollAt
    {
        get
        {
            lock (_gate)
            {
                return _lastPollAt;
            }
        }
    }

    public bool ApiReachable
    {
        get
        {
            lock (_gate)
            {
                return _apiReachable;
            }
        }
    }

    public string? LastApiError
    {
        get
        {
            lock (_gate)
            {
                return _lastApiError;
            }
        }
    }

    public IntegrityStatus? Integrity
    {
        get
        {
            lock (_gate)
            {
                return _integrity;
            }
        }
        set
        {
            lock (_gate)
            {
                _integrity = value;
            }
        }
    }

    public void SetNodes(IReadOnlyList<ApiNode> nodes, DateTimeOffset at)
    {
        lock (_gate)
        {
            foreach (var node in nodes)
            {
                var previous = _nodes.FirstOrDefault(p => string.Equals(p.Id, node.Id, StringComparison.Ordinal));
                if (previous is not null
                    && string.Equals(previous.Status, node.Status, StringComparison.Ordinal)
                    && string.Equals(previous.Health, node.Health, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!_history.TryGetValue(node.Id, out var list))
                {
                    list = new List<HealthTransition>();
                    _history[node.Id] = list;
                }

                list.Add(new HealthTransition(at, node.Id, node.NodeRef, previous?.Status, previous?.Health, node.Status, node.Health));
                if (list.Count > MaxHistoryPerNode)
                {
                    list.RemoveRange(0, list.Count - MaxHistoryPerNode);
                }
            }

            _nodes = nodes;
            _lastPollAt = at;
            _apiReachable = true;
            _lastApiError = null;
        }
    }

    /// <summary>The recorded status and health changes of one node, newest first.</summary>
    public IReadOnlyList<HealthTransition> HistoryFor(string nodeId)
    {
        lock (_gate)
        {
            return _history.TryGetValue(nodeId, out var list)
                ? list.AsEnumerable().Reverse().ToList()
                : Array.Empty<HealthTransition>();
        }
    }

    public void SetApiError(string code, DateTimeOffset at)
    {
        lock (_gate)
        {
            _apiReachable = false;
            _lastApiError = code;
            _lastPollAt = at;
        }
    }
}

/// <summary>Reads the primary's pressure (OET-RWP/1 section 3.8) from the kernel's PSI files. Unreadable means null, and null means no headroom.</summary>
public interface IPrimaryPressureSource
{
    PrimaryPressure? Read();
}

public sealed class ProcPressureSource : IPrimaryPressureSource
{
    private static readonly Regex SomeAvg10 = new(@"some avg10=([0-9]+(?:\.[0-9]+)?)", RegexOptions.CultureInvariant);

    public PrimaryPressure? Read()
    {
        try
        {
            var cpu = ReadAvg10("/proc/pressure/cpu");
            var memory = ReadAvg10("/proc/pressure/memory");
            var workingSet = ReadWorkingSetPct();
            return cpu is null || memory is null || workingSet is null ? null : new PrimaryPressure(cpu, memory, workingSet);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static double? ReadAvg10(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var match = SomeAvg10.Match(File.ReadAllText(path));
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static double? ReadWorkingSetPct()
    {
        const string path = "/proc/meminfo";
        if (!File.Exists(path))
        {
            return null;
        }

        double? total = null;
        double? available = null;
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                total = ParseKb(line);
            }
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                available = ParseKb(line);
            }
        }

        return total is > 0 && available is not null ? (total.Value - available.Value) / total.Value * 100.0 : null;
    }

    private static double? ParseKb(string line)
    {
        var digits = new string(line.Where(char.IsDigit).ToArray());
        return double.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
