using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Fleet.Core.Placement;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Fleet.Manager.Vault;
using Microsoft.EntityFrameworkCore;

namespace Fleet.Manager.Monitoring;

/// <summary>Minimal Prometheus text-format builder (exposition format 0.0.4). Built fresh for every scrape, so no state to leak or lock.</summary>
public sealed class MetricsWriter
{
    private static readonly Regex NamePattern = new(@"\A[a-zA-Z_:][a-zA-Z0-9_:]*\z", RegexOptions.CultureInvariant);

    private readonly StringBuilder _text = new();
    private readonly HashSet<string> _declared = new(StringComparer.Ordinal);

    public void Gauge(string name, string help, double value, params (string Key, string Value)[] labels)
    {
        if (!NamePattern.IsMatch(name))
        {
            throw new ArgumentException("Invalid metric name.", nameof(name));
        }

        if (_declared.Add(name))
        {
            _text.Append("# HELP ").Append(name).Append(' ').Append(EscapeHelp(help)).Append('\n');
            _text.Append("# TYPE ").Append(name).Append(" gauge\n");
        }

        _text.Append(name);
        if (labels.Length > 0)
        {
            _text.Append('{');
            for (var i = 0; i < labels.Length; i++)
            {
                if (!NamePattern.IsMatch(labels[i].Key))
                {
                    throw new ArgumentException("Invalid label name.", nameof(labels));
                }

                if (i > 0)
                {
                    _text.Append(',');
                }

                _text.Append(labels[i].Key).Append("=\"").Append(EscapeLabel(labels[i].Value)).Append('"');
            }

            _text.Append('}');
        }

        _text.Append(' ').Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
    }

    public override string ToString() => _text.ToString();

    private static string EscapeHelp(string help) => help.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private static string EscapeLabel(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
}

/// <summary>Builds the <c>/metrics</c> body from the in-memory fleet picture and two tiny table scans. Counts and ages only: never an address, token or key.</summary>
public sealed class MetricsService
{
    private readonly IDbContextFactory<FleetDbContext> _factory;
    private readonly FleetState _state;
    private readonly RolloutTokenHolder _rolloutToken;
    private readonly TimeProvider _time;

    public MetricsService(
        IDbContextFactory<FleetDbContext> factory,
        FleetState state,
        RolloutTokenHolder rolloutToken,
        TimeProvider time)
    {
        _factory = factory;
        _state = state;
        _rolloutToken = rolloutToken;
        _time = time;
    }

    public async Task<string> RenderAsync(CancellationToken cancellationToken)
    {
        var writer = new MetricsWriter();
        var now = _time.GetUtcNow();
        writer.Gauge("fleet_build_info", "Manager build information.", 1, ("version", HealthReporter.Version));
        writer.Gauge("fleet_api_reachable", "1 when the last poll of the OET API succeeded.", _state.ApiReachable ? 1 : 0);
        if (_state.LastPollAt is { } polled)
        {
            writer.Gauge("fleet_last_poll_timestamp_seconds", "Unix time of the last API poll.", polled.ToUnixTimeSeconds());
        }

        foreach (var group in _state.Nodes.GroupBy(n => (n.Status, n.Health)))
        {
            writer.Gauge("fleet_nodes", "API nodes by status and health.", group.Count(), ("status", group.Key.Status), ("health", group.Key.Health));
        }

        foreach (var node in _state.Nodes)
        {
            var label = ("node", node.NodeRef);
            writer.Gauge("fleet_node_leased_weight", "Leased job weight per node.", node.Leases?.Weight ?? 0, label);
            writer.Gauge("fleet_node_effective_concurrency", "Effective concurrency reported by the node.", node.Capacity?.EffectiveConcurrency ?? 0, label);
            if (node.LastHeartbeatAt is { } beat)
            {
                writer.Gauge("fleet_node_heartbeat_age_seconds", "Seconds since the node's last heartbeat.", Math.Max(0, (now - beat).TotalSeconds), label);
            }
        }

        if (_state.Integrity is { } integrity)
        {
            writer.Gauge("fleet_audit_chain_intact", "1 when the audit hash chain verified at startup.", integrity.AuditChainIntact ? 1 : 0);
            writer.Gauge("fleet_operations_chain_intact", "1 when the operations hash chain verified at startup.", integrity.OperationsChainIntact ? 1 : 0);
            writer.Gauge("fleet_vault_master_key_info", "The master key id in use.", 1, ("key_id", integrity.MasterKeyId.ToString("x8", CultureInfo.InvariantCulture)));
        }

        writer.Gauge("fleet_rollout_token_present", "1 while a per-rollout registry token is held in memory.", _rolloutToken.HasToken ? 1 : 0);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var hosts = await db.Hosts.AsNoTracking().Select(h => h.Lifecycle).ToListAsync(cancellationToken);
        foreach (var group in hosts.GroupBy(l => l))
        {
            writer.Gauge("fleet_hosts", "Hosts by lifecycle.", group.Count(), ("lifecycle", group.Key));
        }

        var operations = await db.Operations.AsNoTracking().Select(o => new { o.Kind, o.State }).ToListAsync(cancellationToken);
        foreach (var group in operations.GroupBy(o => (o.Kind, o.State)))
        {
            writer.Gauge("fleet_operations", "Operations by kind and state.", group.Count(), ("kind", group.Key.Kind), ("state", group.Key.State));
        }

        var alerts = await db.Hosts.AsNoTracking().CountAsync(h => h.Alert != null, cancellationToken);
        writer.Gauge("fleet_host_alerts", "Hosts with a persistent alert.", alerts);
        return writer.ToString();
    }
}

public sealed record HealthReport(
    bool Ok,
    bool Database,
    string? MasterKeyId,
    bool AuditChainIntact,
    bool OperationsChainIntact,
    string? IntegrityProblem,
    bool ApiReachable,
    DateTimeOffset? LastPollAt,
    int Nodes,
    int OpenOperations,
    string Version);

/// <summary>The data behind <c>/healthz</c> (anonymous, minimal) and the authenticated Health page.</summary>
public sealed class HealthReporter
{
    public static readonly string Version = typeof(HealthReporter).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private readonly IDbContextFactory<FleetDbContext> _factory;
    private readonly FleetState _state;

    public HealthReporter(IDbContextFactory<FleetDbContext> factory, FleetState state)
    {
        _factory = factory;
        _state = state;
    }

    public async Task<HealthReport> GetAsync(CancellationToken cancellationToken)
    {
        var database = false;
        var open = 0;
        try
        {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken);
            database = await db.Database.CanConnectAsync(cancellationToken);
            if (database)
            {
                open = await db.Operations.AsNoTracking().CountAsync(
                    o => o.State != "Active" && o.State != "Cancelled" && o.State != "Succeeded" && o.State != "Failed",
                    cancellationToken);
            }
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            database = false;
        }

        var integrity = _state.Integrity;
        var auditOk = integrity?.AuditChainIntact ?? false;
        var opsOk = integrity?.OperationsChainIntact ?? false;
        return new HealthReport(
            database && integrity is not null && auditOk && opsOk,
            database,
            integrity?.MasterKeyId.ToString("x8", CultureInfo.InvariantCulture),
            auditOk,
            opsOk,
            integrity?.Problem,
            _state.ApiReachable,
            _state.LastPollAt,
            _state.Nodes.Count,
            open,
            Version);
    }
}

/// <summary>Asks "where would this work go?" using the live fleet picture, each node's stored policy and the primary's pressure.</summary>
public sealed class PlacementService
{
    private readonly PlacementEngine _engine;
    private readonly FleetState _state;
    private readonly HostStore _hosts;
    private readonly PolicyService _policies;
    private readonly IPrimaryPressureSource _pressure;

    public PlacementService(
        PlacementEngine engine,
        FleetState state,
        HostStore hosts,
        PolicyService policies,
        IPrimaryPressureSource pressure)
    {
        _engine = engine;
        _state = state;
        _hosts = hosts;
        _policies = policies;
        _pressure = pressure;
    }

    public async Task<IReadOnlyList<NodeSnapshot>> SnapshotsAsync(CancellationToken cancellationToken)
    {
        var hosts = await _hosts.ListWithNodesAsync(cancellationToken);
        var snapshots = new List<NodeSnapshot>();
        foreach (var node in _state.Nodes)
        {
            var host = hosts.FirstOrDefault(h => string.Equals(h.ApiNodeId, node.Id, StringComparison.Ordinal));
            if (host is null)
            {
                continue;
            }

            snapshots.Add(node.ToSnapshot(await _policies.EffectiveAsync(host, cancellationToken)));
        }

        return snapshots;
    }

    public async Task<PlacementDecision> DecideAsync(PlacementRequest request, TimeSpan waitedFor, CancellationToken cancellationToken)
    {
        var nodes = await SnapshotsAsync(cancellationToken);
        return _engine.Decide(request, nodes, _pressure.Read(), waitedFor);
    }

    public bool Release(string reservationId) => _engine.Release(reservationId);
}
