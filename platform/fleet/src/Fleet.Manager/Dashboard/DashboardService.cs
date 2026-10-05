using System.Diagnostics;
using Fleet.Core.Audit;
using Fleet.Core.Crypto;
using Fleet.Core.Domain;
using Fleet.Core.Placement;
using Fleet.Core.Policy;
using Fleet.Manager.Api;
using Fleet.Manager.Configuration;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Monitoring;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Fleet.Manager.Vault;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Dashboard;

/// <summary>
/// Everything the owner console reads, assembled from the manager's own services (the same ones the JSON API uses). It never writes:
/// actions go through <see cref="EnrollmentService"/>, <see cref="HostService"/>, <see cref="PolicyService"/>, <see cref="ReleaseService"/> and
/// <see cref="CredentialService"/>. A call to the OET API is bounded to a few seconds so a slow API can never hang a page.
/// </summary>
public sealed partial class DashboardService
{
    private static readonly TimeSpan ApiBudget = TimeSpan.FromSeconds(4);

    private readonly HostStore _hosts;
    private readonly OperationStore _operations;
    private readonly FleetState _state;
    private readonly PolicyService _policies;
    private readonly ReleaseService _releases;
    private readonly PlacementService _placement;
    private readonly PlacementEngine _engine;
    private readonly IPrimaryPressureSource _pressure;
    private readonly IFleetApi _api;
    private readonly IAuditService _audit;
    private readonly CredentialStore _credentials;
    private readonly RolloutTokenHolder _pullToken;
    private readonly VaultCipher _cipher;
    private readonly IOptions<FleetOptions> _options;
    private readonly TimeProvider _time;

    public DashboardService(
        HostStore hosts,
        OperationStore operations,
        FleetState state,
        PolicyService policies,
        ReleaseService releases,
        PlacementService placement,
        PlacementEngine engine,
        IPrimaryPressureSource pressure,
        IFleetApi api,
        IAuditService audit,
        CredentialStore credentials,
        RolloutTokenHolder pullToken,
        VaultCipher cipher,
        IOptions<FleetOptions> options,
        TimeProvider time)
    {
        _hosts = hosts;
        _operations = operations;
        _state = state;
        _policies = policies;
        _releases = releases;
        _placement = placement;
        _engine = engine;
        _pressure = pressure;
        _api = api;
        _audit = audit;
        _credentials = credentials;
        _pullToken = pullToken;
        _cipher = cipher;
        _options = options;
        _time = time;
    }

    // ---- shared pieces ----------------------------------------------------------------------

    private static bool IsTerminal(string state) =>
        state is nameof(EnrollmentState.Active) or nameof(EnrollmentState.Cancelled) or nameof(GenericOperationState.Succeeded);

    /// <summary>What the owner has to do next for an operation, in words (empty when nothing is needed).</summary>
    internal static (string Next, bool NeedsOwner) NextActionFor(OperationEntity op)
    {
        switch (op.State)
        {
            case nameof(EnrollmentState.HostKeyPending):
                return ("Compare the host-key fingerprint with your provider console", true);
            case nameof(EnrollmentState.HostKeyConfirmed):
            case nameof(GenericOperationState.AwaitingOwner):
                return ("Provide the temporary SSH key", true);
            case nameof(EnrollmentState.ImageAwaitingSync):
                return ("Waiting for the CI image sync (it supplies the pull token)", false);
            case nameof(EnrollmentState.Failed):
                return op.FailureReason switch
                {
                    FailureReasons.OwnerCredentialExpired or FailureReasons.AuthFailed => ("Provide the SSH key again, then retry", true),
                    FailureReasons.HostKeyChanged => ("Verify the host key out of band and re-pin it", true),
                    _ => ("Read the failure below, then retry or cancel", true),
                };
            default:
                return (string.Empty, false);
        }
    }

    private static OpSummary Summarize(OperationEntity op, IReadOnlyDictionary<string, HostEntity> hosts)
    {
        HostEntity? host = null;
        if (op.HostId is not null)
        {
            hosts.TryGetValue(op.HostId, out host);
        }

        var (next, needsOwner) = NextActionFor(op);
        return new OpSummary(
            op.Id,
            op.Kind,
            op.HostId,
            host?.DisplayName ?? (op.Kind == OperationKinds.ToWire(OperationKind.Rollout) ? "All active helpers" : "—"),
            op.State,
            op.ResumeState,
            op.CurrentStep,
            op.FailureReason,
            string.IsNullOrEmpty(op.FailureDetail) ? null : Fmt.Untrusted(op.FailureDetail, 500),
            op.StartedAt,
            op.UpdatedAt,
            op.FinishedAt,
            op.Actor,
            next,
            needsOwner,
            !IsTerminal(op.State));
    }

    private PrimaryView ReadPrimary()
    {
        var pressure = _pressure.Read();
        long workingSet = 0;
        var uptime = TimeSpan.Zero;
        try
        {
            using var self = Process.GetCurrentProcess();
            workingSet = self.WorkingSet64 / (1024 * 1024);
            uptime = DateTime.UtcNow - self.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // The footprint is informational; a platform that will not report it shows zero.
        }

        return new PrimaryView(
            pressure is not null,
            pressure?.CpuSomeAvg10,
            pressure?.MemorySomeAvg10,
            pressure?.MemoryWorkingSetPct,
            PrimaryHeadroom.HasHeadroom(pressure),
            workingSet,
            uptime < TimeSpan.Zero ? TimeSpan.Zero : uptime);
    }

    /// <summary>The slots a node can use at once: the smaller of its policy limit and what its agent reports as effective (pressure can lower it).</summary>
    internal static int SlotCap(ApiNode node)
    {
        var policy = node.Policy?.MaxConcurrency;
        var effective = node.Capacity?.EffectiveConcurrency;
        if (policy is null && effective is null)
        {
            return 0;
        }

        return Math.Max(0, Math.Min(policy ?? int.MaxValue, effective ?? int.MaxValue));
    }

    private static string PresenceOf(ApiNode? node) => node?.Health switch
    {
        "Online" => "online",
        "Stale" => "stale",
        "Offline" => "offline",
        "Unseen" => "unseen",
        _ => "none",
    };

    private HelperLine BuildLine(HostEntity host, IReadOnlyList<ApiNode> nodes, IReadOnlyDictionary<string, OpSummary> openByHost, DateTimeOffset now)
    {
        var node = host.ApiNodeId is null ? null : nodes.FirstOrDefault(n => string.Equals(n.Id, host.ApiNodeId, StringComparison.Ordinal));
        double? utilisation = null;
        if (node is not null)
        {
            var cap = SlotCap(node);
            if (cap > 0)
            {
                utilisation = Math.Min(1.0, (double)(node.Leases?.Weight ?? 0) / cap);
            }
        }

        TimeSpan? heartbeatAge = node?.LastHeartbeatAt is { } beat ? now - beat : null;
        openByHost.TryGetValue(host.Id, out var open);
        return new HelperLine(HostView.From(host), node, HostStatusParser.Parse(host.LastStatusJson), PresenceOf(node), utilisation, heartbeatAge, open);
    }

    // ---- fleet overview ---------------------------------------------------------------------

    public async Task<FleetOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var hosts = await _hosts.ListAsync(cancellationToken);
        var byId = hosts.ToDictionary(h => h.Id, StringComparer.Ordinal);
        var recent = await _operations.ListAsync(200, null, cancellationToken);
        var summaries = recent.Select(o => Summarize(o, byId)).ToList();

        // ListAsync is newest first, so the first open operation of a host is its latest.
        var openByHost = new Dictionary<string, OpSummary>(StringComparer.Ordinal);
        foreach (var summary in summaries.Where(s => s.IsOpen && s.HostId is not null))
        {
            openByHost.TryAdd(summary.HostId!, summary);
        }

        var nodes = _state.Nodes;
        var lines = hosts
            .Where(h => h.Lifecycle != nameof(HostLifecycle.Removed))
            .Select(h => BuildLine(h, nodes, openByHost, now))
            .ToList();

        var totals = Totals(lines);
        var attention = await AttentionAsync(lines, summaries, now, cancellationToken);
        var integrity = _state.Integrity;
        return new FleetOverview(
            now,
            ReadPrimary(),
            lines,
            totals,
            attention,
            summaries.Where(s => s.IsOpen).Take(25).ToList(),
            _state.ApiReachable,
            _state.LastApiError is null ? null : Fmt.Untrusted(_state.LastApiError, 60),
            _state.LastPollAt,
            integrity is { AuditChainIntact: true, OperationsChainIntact: true },
            _pullToken.HasToken);
    }

    private static FleetTotals Totals(IReadOnlyList<HelperLine> lines)
    {
        int Count(string lifecycle) => lines.Count(l => l.Host.Lifecycle == lifecycle);

        var slotsTotal = 0;
        var slotsUsed = 0;
        var cpuFree = 0;
        var memFree = 0;
        foreach (var line in lines.Where(l => l.Node is { Status: "Active", Health: "Online" }))
        {
            var node = line.Node!;
            slotsTotal += SlotCap(node);
            slotsUsed += node.Leases?.Weight ?? 0;
            cpuFree += node.Capacity?.CpuBudgetFreeMilli ?? 0;
            memFree += node.Capacity?.MemBudgetFreeMiB ?? 0;
        }

        // A disabled helper is expected to be quiet; an active or draining one that is not online is not.
        var offline = lines.Count(l => l.Host.Lifecycle is nameof(HostLifecycle.Active) or nameof(HostLifecycle.Draining) && l.Presence != "online");
        return new FleetTotals(
            lines.Count,
            Count(nameof(HostLifecycle.Active)),
            Count(nameof(HostLifecycle.Draining)),
            Count(nameof(HostLifecycle.Disabled)),
            Count(nameof(HostLifecycle.Enrolling)),
            Count(nameof(HostLifecycle.Failed)),
            offline,
            slotsTotal,
            slotsUsed,
            slotsTotal > 0 ? Math.Min(1.0, (double)slotsUsed / slotsTotal) : null,
            cpuFree,
            memFree);
    }

    private async Task<IReadOnlyList<AttentionItem>> AttentionAsync(
        IReadOnlyList<HelperLine> lines,
        IReadOnlyList<OpSummary> summaries,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var items = new List<AttentionItem>();

        if (!_state.ApiReachable)
        {
            items.Add(_state.LastPollAt is null
                ? new AttentionItem("warn", "The OET API has not been polled yet. Helper health appears after the first poll.", null)
                : new AttentionItem("bad", "The OET API is not reachable (" + Fmt.Untrusted(_state.LastApiError, 60) + "). Helper health below may be out of date.", null));
        }

        var integrity = _state.Integrity;
        if (integrity is null)
        {
            items.Add(new AttentionItem("warn", "The audit and operations chains have not been verified yet.", "/Health"));
        }
        else if (!integrity.AuditChainIntact || !integrity.OperationsChainIntact)
        {
            items.Add(new AttentionItem("bad", "A tamper-evidence check failed: " + Fmt.Untrusted(integrity.Problem, 160), "/Health"));
        }

        foreach (var line in lines)
        {
            var name = Fmt.Untrusted(line.Host.DisplayName, 80);
            var href = "/Hosts/Detail/" + Uri.EscapeDataString(line.Host.Id);
            if (line.Host.Alert == FailureReasons.HostKeyChanged)
            {
                items.Add(new AttentionItem("bad", name + ": the SSH host key changed. The helper was disabled; verify it out of band and re-pin it.", href));
            }
            else if (line.Host.Alert == "quarantined")
            {
                items.Add(new AttentionItem("bad", name + " was quarantined by the OET API (integrity strikes or a failed self-test).", href));
            }
            else if (line.Host.Lifecycle is nameof(HostLifecycle.Active) or nameof(HostLifecycle.Draining) && line.Presence != "online")
            {
                items.Add(new AttentionItem("warn", name + " is " + (line.Presence == "none" ? "not reporting" : line.Presence) + " (last heartbeat " + Fmt.Ago(line.Node?.LastHeartbeatAt, now) + ").", href));
            }

            if (line.Node?.Tokens?.NextExpiryAt is { } expiry && expiry - now < TimeSpan.FromDays(5) && line.Host.Lifecycle == nameof(HostLifecycle.Active))
            {
                items.Add(new AttentionItem("warn", name + ": the node token expires " + Fmt.Until(expiry, now) + ". Rotate it.", "/Credentials"));
            }
        }

        foreach (var summary in summaries.Where(s => s.IsOpen && s.NeedsOwner))
        {
            items.Add(new AttentionItem(
                summary.State == nameof(EnrollmentState.Failed) || summary.State == nameof(GenericOperationState.Failed) ? "bad" : "info",
                Fmt.KindLabel(summary.Kind) + " of " + Fmt.Untrusted(summary.HostName, 80) + ": " + summary.NextAction + ".",
                "/Operations/Detail/" + Uri.EscapeDataString(summary.Id)));
        }

        foreach (var summary in summaries.Where(s => s.IsOpen && s.State == nameof(EnrollmentState.ImageAwaitingSync)))
        {
            items.Add(new AttentionItem("warn", Fmt.Untrusted(summary.HostName, 80) + " is waiting for an image sync from CI.", "/Operations/Detail/" + Uri.EscapeDataString(summary.Id)));
        }

        var releases = await _releases.ListAsync(cancellationToken);
        if (releases.Any(r => !r.Approved))
        {
            items.Add(new AttentionItem("info", "An agent image is waiting for your approval.", "/Operations?view=releases"));
        }

        return items
            .OrderBy(i => i.Severity switch { "bad" => 0, "warn" => 1, _ => 2 })
            .ToList();
    }

    // ---- one helper ---------------------------------------------------------------------------

    public static HostActions ActionsFor(HostEntity host)
    {
        var live = host.Lifecycle is nameof(HostLifecycle.Active) or nameof(HostLifecycle.Draining) or nameof(HostLifecycle.Disabled);
        var gone = host.Lifecycle is nameof(HostLifecycle.Removed) or nameof(HostLifecycle.Removing);
        return new HostActions(
            host.Lifecycle is nameof(HostLifecycle.Active) or nameof(HostLifecycle.Draining),
            host.Lifecycle is nameof(HostLifecycle.Active) or nameof(HostLifecycle.Draining) or nameof(HostLifecycle.Disabled),
            host.Lifecycle is nameof(HostLifecycle.Disabled) or nameof(HostLifecycle.Draining) && host.Alert is null,
            host.ApiNodeId is not null && host.AgentDigest is not null && live,
            host.HostKeyAlgo is not null && !gone && host.Alert != FailureReasons.HostKeyChanged,
            !gone,
            host.Alert == FailureReasons.HostKeyChanged);
    }

    public async Task<HostPageView?> GetHostAsync(string hostId, CancellationToken cancellationToken)
    {
        var host = await _hosts.GetAsync(hostId, cancellationToken);
        if (host is null)
        {
            return null;
        }

        var now = _time.GetUtcNow();
        var all = await _hosts.ListAsync(cancellationToken);
        var byId = all.ToDictionary(h => h.Id, StringComparer.Ordinal);
        var ops = (await _operations.ListAsync(100, host.Id, cancellationToken)).Select(o => Summarize(o, byId)).ToList();

        // At most one open operation per host is shown on the helper line (the newest); the full list follows below it.
        var openByHost = new Dictionary<string, OpSummary>(StringComparer.Ordinal);
        var firstOpen = ops.FirstOrDefault(s => s.IsOpen);
        if (firstOpen is not null)
        {
            openByHost[host.Id] = firstOpen;
        }

        var line = BuildLine(host, _state.Nodes, openByHost, now);

        var policy = new PolicyView(
            await _policies.EffectiveAsync(host, cancellationToken),
            await _policies.GetHostOverrideAsync(host.Id, cancellationToken) is not null,
            await _policies.GetGlobalAsync(cancellationToken),
            host.DesiredRevision,
            host.AppliedRevision);
        var registry = await _policies.RegistryKindsAsync(cancellationToken);
        var kinds = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var kind in registry ?? Array.Empty<string>())
        {
            kinds.Add(kind);
        }

        foreach (var kind in policy.Global.AllowedKinds.Concat(policy.Effective.AllowedKinds))
        {
            kinds.Add(kind);
        }

        var history = host.ApiNodeId is null ? Array.Empty<HealthTransition>() : _state.HistoryFor(host.ApiNodeId);
        var opIds = ops.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        var audit = (await _audit.ListAsync(400, cancellationToken))
            .Where(a => string.Equals(a.Target, host.NodeRef, StringComparison.Ordinal) || (a.Target is not null && opIds.Contains(a.Target)))
            .Take(60)
            .Select(ToAuditLine)
            .ToList();

        var credentials = new List<CredentialLine>();
        foreach (var purpose in new[] { CredentialPurposes.OwnerBootstrap, CredentialPurposes.ManagerSsh, CredentialPurposes.NodeTokenRender })
        {
            var info = await _credentials.GetInfoAsync(host.Id, purpose, cancellationToken);
            if (info is not null && info.DiscardedAt is null)
            {
                credentials.Add(ToCredentialLine(host, info));
            }
        }

        return new HostPageView(
            now,
            line,
            ActionsFor(host),
            policy,
            kinds.ToList(),
            history,
            ops,
            audit,
            credentials,
            ops.FirstOrDefault(s => s.Kind == "enroll"));
    }

    private static AuditLine ToAuditLine(AuditRecord record) => new(
        record.Id,
        record.At,
        Fmt.Untrusted(record.Actor, 64),
        Fmt.Untrusted(record.Action, 64),
        record.Target is null ? null : Fmt.Untrusted(record.Target, 128),
        Fmt.Untrusted(record.DetailsJson, 400));

    private static CredentialLine ToCredentialLine(HostEntity host, CredentialInfo info) => new(
        host.Id,
        host.NodeRef,
        host.DisplayName,
        info.Purpose,
        info.FingerprintHint,
        info.CreatedAt,
        info.ExpiresAt,
        info.RotatedAt);

    // ---- operations ---------------------------------------------------------------------------

    public async Task<OperationPageView?> GetOperationAsync(string operationId, CancellationToken cancellationToken)
    {
        var op = await _operations.GetAsync(operationId, cancellationToken);
        if (op is null)
        {
            return null;
        }

        var steps = await _operations.GetStepsAsync(op.Id, cancellationToken);
        var host = op.HostId is null ? null : await _hosts.GetAsync(op.HostId, cancellationToken);
        CredentialLine? saved = null;
        if (host is not null)
        {
            var info = await _credentials.GetInfoAsync(host.Id, CredentialPurposes.OwnerBootstrap, cancellationToken);
            if (info is not null && info.DiscardedAt is null && info.HasCiphertext && (info.ExpiresAt is null || info.ExpiresAt > _time.GetUtcNow()))
            {
                saved = ToCredentialLine(host, info);
            }
        }

        bool canCancel;
        if (op.Kind == "enroll")
        {
            canCancel = EnrollmentStateMachine.TryParse(op.State, out var state) && EnrollmentStateMachine.CanCancel(state);
        }
        else
        {
            canCancel = op.State is nameof(GenericOperationState.Queued) or nameof(GenericOperationState.Running)
                or nameof(GenericOperationState.AwaitingOwner) or nameof(GenericOperationState.Failed);
        }

        return new OperationPageView(
            _time.GetUtcNow(),
            OperationView.From(op, steps),
            host is null ? null : HostView.From(host),
            host?.DisplayName ?? (op.Kind == OperationKinds.ToWire(OperationKind.Rollout) ? "All active helpers" : "—"),
            EnrollmentService.AcceptanceFor(op),
            saved,
            op.State == nameof(EnrollmentState.Failed),
            canCancel,
            _pullToken.HasToken);
    }

    public async Task<OperationsPageView> GetOperationsAsync(string? kind, string? state, CancellationToken cancellationToken)
    {
        var all = await _hosts.ListAsync(cancellationToken);
        var byId = all.ToDictionary(h => h.Id, StringComparer.Ordinal);
        var recent = (await _operations.ListAsync(200, null, cancellationToken)).Select(o => Summarize(o, byId)).ToList();
        var filtered = recent.AsEnumerable();
        if (!string.IsNullOrEmpty(kind))
        {
            filtered = filtered.Where(o => string.Equals(o.Kind, kind, StringComparison.Ordinal));
        }

        if (state == "open")
        {
            filtered = filtered.Where(o => o.IsOpen);
        }
        else if (state == "failed")
        {
            filtered = filtered.Where(o => o.State == "Failed");
        }
        else if (state == "done")
        {
            filtered = filtered.Where(o => !o.IsOpen);
        }

        return new OperationsPageView(
            _time.GetUtcNow(),
            filtered.Take(100).ToList(),
            Enum.GetValues<OperationKind>().Select(OperationKinds.ToWire).ToList(),
            kind,
            state,
            recent.Count(o => o.IsOpen));
    }

    public async Task<ReleasesView> GetReleasesAsync(CancellationToken cancellationToken)
    {
        var releases = await _releases.ListAsync(cancellationToken);
        var window = (await _releases.AgentImagePolicyAsync(cancellationToken)).ApprovedDigests;
        var current = await _releases.GetCurrentApprovedAsync(cancellationToken);
        var all = await _hosts.ListAsync(cancellationToken);
        var byId = all.ToDictionary(h => h.Id, StringComparer.Ordinal);
        var recent = (await _operations.ListAsync(200, null, cancellationToken)).Select(o => Summarize(o, byId)).ToList();
        return new ReleasesView(
            _time.GetUtcNow(),
            releases
                .Select(r => new ReleaseLine(r, current is not null && string.Equals(current.Id, r.Id, StringComparison.Ordinal), window.Contains(r.AgentDigest, StringComparer.Ordinal)))
                .ToList(),
            _pullToken.HasToken,
            recent.Count(o => o.IsOpen && o.State == nameof(EnrollmentState.ImageAwaitingSync)),
            recent.FirstOrDefault(o => o.IsOpen && o.Kind == OperationKinds.ToWire(OperationKind.Rollout)),
            all.Count(h => h.ApiNodeId is not null && h.Lifecycle is nameof(HostLifecycle.Active) or nameof(HostLifecycle.Draining)));
    }

    public async Task<AuditPageView> GetAuditAsync(int take, bool verify, CancellationToken cancellationToken)
    {
        var lines = (await _audit.ListAsync(Math.Clamp(take, 1, 500), cancellationToken)).Select(ToAuditLine).ToList();
        if (!verify)
        {
            return new AuditPageView(lines, null, null, 0);
        }

        var result = await _audit.VerifyAsync(cancellationToken);
        return new AuditPageView(lines, result.Intact, result.Problem is null ? null : Fmt.Untrusted(result.Problem, 200), result.Checked);
    }

    // ---- the OET API, bounded ---------------------------------------------------------------

    private static async Task<(T? Value, string? Error)> ApiAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(ApiBudget);
        try
        {
            return (await call(linked.Token), null);
        }
        catch (FleetApiException ex)
        {
            return (default, Fmt.Untrusted(ex.Code, 60));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (default, "timeout");
        }
    }
}
