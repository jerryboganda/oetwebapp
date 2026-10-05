using System.Text.Json;
using Fleet.Core.Domain;
using Fleet.Core.Placement;
using Fleet.Core.Policy;
using Fleet.Manager.Api;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Operations;
using Fleet.Manager.Vault;

namespace Fleet.Manager.Dashboard;

// Workloads, policies, credentials and the project overview.
public sealed partial class DashboardService
{
    // ---- workloads ----------------------------------------------------------------------------

    /// <summary>Which area of the OET platform a job kind belongs to (display only).</summary>
    internal static string ProjectOf(string kind)
    {
        if (kind.StartsWith("pdf.", StringComparison.Ordinal))
        {
            return "OET · Content papers";
        }

        if (kind.StartsWith("companion.", StringComparison.Ordinal))
        {
            return "OET · AI companion";
        }

        return kind.StartsWith("media.", StringComparison.Ordinal) ? "OET · Media (live classes, speaking)" : "OET";
    }

    /// <summary>Parses the OET API's <c>GET /stats</c> (queue depth per kind and state, oldest queued age, leased count, leased weight per node). Tolerant: anything missing is zero.</summary>
    internal static QueueStats ParseStats(JsonElement root)
    {
        var kinds = new List<KindQueue>();
        long? oldest = null;
        var leasedCount = 0;
        var weights = new Dictionary<string, int>(StringComparer.Ordinal);
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new QueueStats(kinds, oldest, leasedCount, weights);
        }

        if (root.TryGetProperty("queue", out var queue) && queue.ValueKind == JsonValueKind.Object)
        {
            foreach (var kind in queue.EnumerateObject())
            {
                if (kind.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                kinds.Add(new KindQueue(
                    kind.Name,
                    CountOf(kind.Value, "Queued"),
                    CountOf(kind.Value, "Leased"),
                    CountOf(kind.Value, "Succeeded"),
                    CountOf(kind.Value, "Failed"),
                    CountOf(kind.Value, "Quarantined"),
                    CountOf(kind.Value, "FallbackLocal"),
                    CountOf(kind.Value, "Cancelled")));
            }
        }

        if (root.TryGetProperty("oldestQueuedAgeSeconds", out var age) && age.ValueKind == JsonValueKind.Number && age.TryGetInt64(out var seconds))
        {
            oldest = seconds;
        }

        if (root.TryGetProperty("leasedCount", out var leased) && leased.ValueKind == JsonValueKind.Number && leased.TryGetInt32(out var leasedValue))
        {
            leasedCount = leasedValue;
        }

        if (root.TryGetProperty("leasedWeightByNode", out var byNode) && byNode.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in byNode.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt32(out var weight))
                {
                    weights[entry.Name] = weight;
                }
            }
        }

        return new QueueStats(kinds, oldest, leasedCount, weights);
    }

    private static int CountOf(JsonElement states, string state) =>
        states.TryGetProperty(state, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count) ? count : 0;

    public async Task<WorkloadsView> GetWorkloadsAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var (stats, error) = await ApiAsync(token => _api.GetStatsAsync(token), cancellationToken);
        var statsAvailable = error is null;
        var queue = statsAvailable
            ? ParseStats(stats)
            : new QueueStats(Array.Empty<KindQueue>(), null, 0, new Dictionary<string, int>(StringComparer.Ordinal));

        var snapshots = await _placement.SnapshotsAsync(cancellationToken);
        var globalPolicy = await _policies.GetGlobalAsync(cancellationToken);
        var registry = await _policies.RegistryKindsAsync(cancellationToken);
        var primary = ReadPrimary();

        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var kind in globalPolicy.AllowedKinds.Concat(registry ?? Array.Empty<string>()).Concat(queue.Kinds.Select(k => k.Kind)))
        {
            names.Add(kind);
        }

        foreach (var snapshot in snapshots)
        {
            foreach (var kind in snapshot.Policy.AllowedKinds.Concat(snapshot.Kinds.Select(k => k.Kind)))
            {
                names.Add(kind);
            }
        }

        var workloads = new List<KindWorkload>();
        foreach (var name in names)
        {
            var advertised = snapshots.SelectMany(s => s.Kinds).FirstOrDefault(k => string.Equals(k.Kind, name, StringComparison.Ordinal));
            var schema = advertised is null || advertised.SchemaVersions.Count == 0 ? 1 : advertised.SchemaVersions[0];
            var request = new PlacementRequest(name, schema, advertised?.EngineVersion ?? string.Empty, 1, 0, 0, 0);

            var eligible = new List<NodeSnapshot>();
            var rejections = new List<string>();
            foreach (var snapshot in snapshots)
            {
                if (_engine.IsEligible(snapshot, request, out var reason))
                {
                    eligible.Add(snapshot);
                }
                else if (rejections.Count < 8)
                {
                    rejections.Add(snapshot.NodeRef + ": " + reason);
                }
            }

            // The engine's own choice: lowest normalised load, then most free slots, then name.
            var best = eligible
                .OrderBy(n => (double)(n.LeasedWeight + _engine.Reservations.Totals(n.Id).Weight) / Math.Max(1, Math.Min(n.Policy.MaxConcurrency, n.Capacity.EffectiveConcurrency)))
                .ThenByDescending(n => n.Capacity.HeavySlotsFree)
                .ThenBy(n => n.NodeRef, StringComparer.Ordinal)
                .FirstOrDefault();

            string placement;
            string reasonText;
            if (best is not null)
            {
                placement = "helper";
                reasonText = "The next job runs on " + best.NodeRef + " (lowest load of " + eligible.Count + " eligible helper" + (eligible.Count == 1 ? string.Empty : "s") + ").";
            }
            else if (primary.HasHeadroom)
            {
                placement = "primary";
                reasonText = "No eligible helper; the primary has headroom, so the job runs there.";
            }
            else
            {
                placement = "wait";
                reasonText = "No eligible helper and no headroom on the primary: the job waits (after 60 minutes it runs on the primary regardless).";
            }

            workloads.Add(new KindWorkload(
                name,
                ProjectOf(name),
                queue.Kinds.FirstOrDefault(k => string.Equals(k.Kind, name, StringComparison.Ordinal)) ?? new KindQueue(name, 0, 0, 0, 0, 0, 0, 0),
                snapshots.Count(s => s.Policy.AllowedKinds.Contains(name, StringComparer.Ordinal)),
                eligible.Select(n => n.NodeRef).ToList(),
                placement,
                reasonText,
                rejections));
        }

        var hosts = await _hosts.ListWithNodesAsync(cancellationToken);
        var nodeLines = new List<NodeLoadLine>();
        foreach (var node in _state.Nodes)
        {
            var host = hosts.FirstOrDefault(h => string.Equals(h.ApiNodeId, node.Id, StringComparison.Ordinal));
            if (host is null)
            {
                continue;
            }

            nodeLines.Add(new NodeLoadLine(
                host.Id,
                host.NodeRef,
                node.Status,
                node.Health,
                node.Leases?.Count ?? 0,
                node.Leases?.Weight ?? 0,
                SlotCap(node),
                string.Join(", ", (node.Agent?.Kinds ?? Array.Empty<ApiKindDto>()).Select(k => k.Kind))));
        }

        return new WorkloadsView(
            now,
            statsAvailable,
            error,
            queue.OldestQueuedAgeSeconds,
            queue.LeasedCount,
            workloads,
            nodeLines,
            primary);
    }

    // ---- policies -----------------------------------------------------------------------------

    public async Task<PoliciesView> GetPoliciesAsync(CancellationToken cancellationToken)
    {
        var stored = await _policies.GetStoredGlobalAsync(cancellationToken);
        var registry = await _policies.RegistryKindsAsync(cancellationToken);
        var hosts = await _hosts.ListAsync(cancellationToken);
        var lines = new List<PolicyHostLine>();
        foreach (var host in hosts.Where(h => h.Lifecycle != nameof(HostLifecycle.Removed) && h.ApiNodeId is not null))
        {
            var effective = await _policies.EffectiveAsync(host, cancellationToken);
            lines.Add(new PolicyHostLine(
                host.Id,
                host.NodeRef,
                host.DisplayName,
                host.Lifecycle,
                await _policies.GetHostOverrideAsync(host.Id, cancellationToken) is not null,
                host.DesiredRevision,
                host.AppliedRevision,
                effective.MaxConcurrency,
                string.Join(", ", effective.AllowedKinds)));
        }

        return new PoliciesView(
            _time.GetUtcNow(),
            stored ?? PolicyDefaults.Global(),
            stored is null,
            registry?.OrderBy(k => k, StringComparer.Ordinal).ToList() ?? new List<string>(),
            registry is not null,
            lines);
    }

    // ---- credentials --------------------------------------------------------------------------

    public async Task<CredentialsView> GetCredentialsAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var hosts = await _hosts.ListAsync(cancellationToken);
        var live = hosts.Where(h => h.Lifecycle != nameof(HostLifecycle.Removed)).ToList();
        var owner = new List<CredentialLine>();
        var manager = new List<CredentialLine>();
        var tokens = new List<TokenLine>();
        foreach (var host in live)
        {
            var ownerInfo = await _credentials.GetInfoAsync(host.Id, CredentialPurposes.OwnerBootstrap, cancellationToken);
            if (ownerInfo is not null && ownerInfo.DiscardedAt is null)
            {
                owner.Add(ToCredentialLine(host, ownerInfo));
            }

            var managerInfo = await _credentials.GetInfoAsync(host.Id, CredentialPurposes.ManagerSsh, cancellationToken);
            if (managerInfo is not null && managerInfo.DiscardedAt is null)
            {
                manager.Add(ToCredentialLine(host, managerInfo));
            }

            var tokenInfo = await _credentials.GetInfoAsync(host.Id, CredentialPurposes.NodeTokenRender, cancellationToken);
            var node = host.ApiNodeId is null ? null : _state.Nodes.FirstOrDefault(n => string.Equals(n.Id, host.ApiNodeId, StringComparison.Ordinal));
            if (tokenInfo is not null || node is not null)
            {
                tokens.Add(new TokenLine(
                    host.Id,
                    host.NodeRef,
                    host.DisplayName,
                    tokenInfo?.FingerprintHint,
                    tokenInfo?.CreatedAt,
                    node?.Tokens?.ActiveCount,
                    node?.Tokens?.NextExpiryAt,
                    ActionsFor(host).CanRotateToken));
            }
        }

        var byId = hosts.ToDictionary(h => h.Id, StringComparer.Ordinal);
        var awaiting = (await _operations.ListAsync(200, null, cancellationToken))
            .Where(o => EnrollmentService.AcceptanceFor(o) != OwnerKeyAcceptance.No)
            .Select(o => Summarize(o, byId))
            .ToList();

        var secrets = _options.Value.Secrets;
        bool Present(string file) => SecretFile.TryRead(Path.Combine(secrets.Directory, file)) is not null;
        var services = new List<ServiceCredentialLine>
        {
            new("Fleet API credential", Present(secrets.ApiCredentialFile), "Authenticates the manager to the OET API. A secret file mounted into the container; replace the file and it is picked up within seconds."),
            new("CI sync token", Present(secrets.SyncTokenFile), "Lets the CI sync job post a release record. Without the file the sync endpoint does not exist."),
            new("Metrics scrape token", Present(secrets.MetricsTokenFile), "Lets a scraper read /metrics. Without the file only an owner session can."),
        };

        return new CredentialsView(
            now,
            owner,
            manager,
            tokens,
            services,
            awaiting,
            _cipher.Keys.CurrentKeyId.ToString("x8"),
            _cipher.Keys.KeyIds.Count > 1,
            _pullToken.HasToken);
    }

    // ---- projects -----------------------------------------------------------------------------

    public async Task<ProjectsView> GetProjectsAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var primary = ReadPrimary();
        var (status, statusError) = await ApiAsync(token => _api.GetStatusAsync(token), cancellationToken);
        var statusFetched = statusError is null && status is not null;
        var secrets = _options.Value.Secrets;
        var nodes = _state.Nodes;
        var releases = await _releases.ListAsync(cancellationToken);
        var current = await _releases.GetCurrentApprovedAsync(cancellationToken);

        var oet = new OetIntegration(
            _state.ApiReachable,
            _state.LastApiError is null ? null : Fmt.Untrusted(_state.LastApiError, 60),
            _state.LastPollAt,
            SecretFile.TryRead(Path.Combine(secrets.Directory, secrets.ApiCredentialFile)) is not null,
            statusFetched,
            statusError,
            statusFetched ? status!.ProtocolCurrent : null,
            statusFetched ? status!.ProtocolMinimum : null,
            statusFetched ? status!.Kinds.Select(k => Fmt.Untrusted(k, 48)).ToList() : new List<string>(),
            nodes.Count,
            nodes.Count(n => n.Status == "Active"),
            nodes.Count(n => n.Health == "Online"),
            SecretFile.TryRead(Path.Combine(secrets.Directory, secrets.SyncTokenFile)) is not null,
            SecretFile.TryRead(Path.Combine(secrets.Directory, secrets.MetricsTokenFile)) is not null,
            current is null ? null : current.Sha[..Math.Min(12, current.Sha.Length)],
            releases.Count(r => !r.Approved));

        var projects = new List<ProjectLine>
        {
            new(
                "OET learner platform",
                "Hosted on the primary: API (blue/green), web app, database. Offloads PDF, companion and media jobs to helpers.",
                _state.ApiReachable ? "ok" : "bad",
                _state.ApiReachable
                    ? nodes.Count(n => n.Status == "Active") + " active helper(s) registered with the OET API."
                    : "The OET API is not reachable from this console."),
            new(
                "Fleet manager (this console)",
                "The oet-fleet compose project on the primary: no public ingress, reached over the SSH tunnel.",
                "ok",
                "Working set " + primary.ConsoleWorkingSetMiB + " MiB, up " + Fmt.Age(primary.ConsoleUptime) + "."),
            new(
                "Other projects on the primary",
                "Co-tenants of the primary VPS.",
                "unknown",
                "Not visible to the manager: it has no container runtime access by design, so it cannot attribute CPU or memory to a project. The pressure figures on this page cover the whole primary."),
        };

        return new ProjectsView(now, primary, oet, projects);
    }
}
