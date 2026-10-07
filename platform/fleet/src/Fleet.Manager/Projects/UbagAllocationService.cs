using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Fleet.Core.Domain;
using Fleet.Core.Policy;
using Fleet.Manager.Api;
using Fleet.Manager.Configuration;
using Fleet.Manager.Monitoring;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Projects;

/// <summary>A wire record of UBAG's allocation_list v1 (node-allocation.schema.json). Snake_case and
/// exactly the fields UBAG's strict parser accepts — one unknown field rejects the whole list, so this
/// record must not grow casually; a schema change is a coordinated UBAG+manager change.</summary>
public sealed class UbagAllocationWire
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("region")]
    public string Region { get; set; } = "default";

    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [JsonPropertyName("cert_identity")]
    public UbagCertIdentityWire CertIdentity { get; set; } = new();

    [JsonPropertyName("cpu_millis")]
    public int CpuMillis { get; set; }

    [JsonPropertyName("memory_bytes")]
    public long MemoryBytes { get; set; }

    [JsonPropertyName("reservation_state")]
    public string ReservationState { get; set; } = "known";

    [JsonPropertyName("state")]
    public string State { get; set; } = "active";

    [JsonPropertyName("max_browser_workloads")]
    public int MaxBrowserWorkloads { get; set; }

    [JsonPropertyName("voice_capable")]
    public bool VoiceCapable { get; set; }

    [JsonPropertyName("valid_until")]
    public DateTimeOffset ValidUntil { get; set; }

    [JsonPropertyName("generation")]
    public long Generation { get; set; }
}

public sealed class UbagCertIdentityWire
{
    /// <summary>The identity UBAG's trust plane expects on the helper certificate:
    /// spiffe://ubag/node/&lt;node_id&gt;. Published only when the manager's CA (UBAG decision D3) has
    /// provisioned the host — until then the pin below stays empty and a helper cannot actually pass
    /// UBAG's mTLS dial, which keeps an unbuilt helper unusable rather than trusted.</summary>
    [JsonPropertyName("uri_san")]
    public string UriSan { get; set; } = string.Empty;

    [JsonPropertyName("spki_sha256")]
    public string SpkiSha256 { get; set; } = string.Empty;
}

public sealed class UbagAllocationListWire
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("generated_at")]
    public DateTimeOffset GeneratedAt { get; set; }

    [JsonPropertyName("allocations")]
    public IReadOnlyList<UbagAllocationWire> Allocations { get; set; } = Array.Empty<UbagAllocationWire>();
}

/// <summary>The built list plus its strong ETag (sha256 of the exact bytes served).</summary>
public sealed record UbagAllocationSnapshot(string Body, string ETag, DateTimeOffset GeneratedAt);

/// <summary>
/// Builds UBAG's allocation_list from the enrolled hosts, OET first (the plan's allocation rule):
/// the grant a host publishes is min(UBAG's defence-in-depth ceiling for its hardware size, the
/// hardware MINUS the OET policy budget the node already runs under). A host publishes only when
/// UBAG is enabled, the host is listed in <c>Fleet:Ubag:Hosts</c>, its lifecycle is Active, its
/// hardware was recorded, and at least one browser workload of capacity remains — every other
/// case simply leaves the host out of the list, which UBAG's poller treats as "no new
/// placements here" (draining). Hosts that are Draining or whose API health is not healthy
/// publish with state "draining" so UBAG stops NEW work but is told the node still exists.
/// </summary>
public sealed class UbagAllocationService
{
    /// <summary>UBAG's ceiling table (plan section 2), CPU millis and MiB. Known sizes get the
    /// agreed conservative numbers; anything else gets 75 % CPU / 62.5 % RAM.</summary>
    internal static (int CpuMilli, int MemMib) CeilingFor(int cpuCores, int memTotalMib) =>
        (cpuCores, memTotalMib) switch
        {
            (2, _) => (1500, 2560),
            (4, _) => (3000, 5120),
            _ => ((int)(cpuCores * 1000L * 3 / 4), (int)((long)memTotalMib * 5 / 8)),
        };

    /// <summary>UBAG node ids are prefixed so they can never collide with an OET node ref, and the
    /// enrolled host id keeps them stable across re-enrollments.</summary>
    public static string NodeIdFor(string hostId) => "ubag-" + hostId;

    /// <summary>Parses the owner's Hosts setting: "*" or a comma-separated id list. An exact-id
    /// entry lists that host whatever its lifecycle (the lifecycle gates still apply); "*" lists
    /// Active hosts only. Unknown/blank tokens are ignored.</summary>
    internal static bool IsListed(string hostsSetting, string hostId, string lifecycle)
    {
        var listed = false;
        foreach (var raw in hostsSetting.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw == "*")
            {
                if (lifecycle is nameof(HostLifecycle.Active))
                {
                    return true;
                }
            }
            else if (string.Equals(raw, hostId, StringComparison.Ordinal))
            {
                listed = true;
            }
        }

        return listed;
    }

    /// <summary>The URI SAN UBAG's trust plane matches (UBAG nodes/types.go URISANPrefix).</summary>
    public static string UriSanFor(string nodeId) => "spiffe://ubag/node/" + nodeId;

    private readonly HostStore _hosts;
    private readonly PolicyService _policies;
    private readonly FleetState _state;
    private readonly UbagTrustService _trust;
    private readonly IOptions<FleetOptions> _options;
    private readonly TimeProvider _time;

    public UbagAllocationService(
        HostStore hosts,
        PolicyService policies,
        FleetState state,
        UbagTrustService trust,
        IOptions<FleetOptions> options,
        TimeProvider time)
    {
        _hosts = hosts;
        _policies = policies;
        _state = state;
        _trust = trust;
        _options = options;
        _time = time;
    }

    /// <summary>Builds the current list, or null when UBAG is disabled (the endpoint answers 503).</summary>
    public async Task<UbagAllocationSnapshot?> BuildAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value.Ubag;
        if (!options.Enabled)
        {
            return null;
        }

        var now = _time.GetUtcNow();
        var hosts = await _hosts.ListAsync(cancellationToken);
        var nodes = _state.Nodes;

        var allocations = new List<UbagAllocationWire>();
        foreach (var host in hosts)
        {
            var allocation = await BuildForAsync(host, nodes, options, now, cancellationToken);
            if (allocation is not null)
            {
                allocations.Add(allocation);
            }
        }

        allocations.Sort((a, b) => string.CompareOrdinal(a.NodeId, b.NodeId));
        var payload = new UbagAllocationListWire
        {
            SchemaVersion = 1,
            GeneratedAt = now,
            Allocations = allocations,
        };
        var body = FleetJson.Serialize(payload);
        var etag = "\"" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant() + "\"";
        return new UbagAllocationSnapshot(body, etag, now);
    }

    private async Task<UbagAllocationWire?> BuildForAsync(
        HostEntity host,
        IReadOnlyList<ApiNode> apiNodes,
        UbagOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (host.Lifecycle
            is nameof(HostLifecycle.Removed) or nameof(HostLifecycle.Removing) or nameof(HostLifecycle.Failed))
        {
            // Gone or going: say nothing. UBAG marks a node missing from a good list as
            // draining, which is exactly the right behaviour for a removed host.
            return null;
        }

        if (!IsListed(options.Hosts, host.Id, host.Lifecycle))
        {
            return null;
        }

        // Unknown hardware publishes nothing: the ceiling table needs facts S1 recorded.
        if (host.CpuCores is not int cores || cores <= 0 || host.MemMib is not int memMib || memMib <= 0)
        {
            return null;
        }

        // OET first: the budget of the policy actually in force on the host (its override,
        // else the global policy, else the scaled default) is the capacity OET may claim.
        // UBAG gets what is left, capped by its own ceiling for this hardware size.
        var policy = await _policies.EffectiveAsync(host, cancellationToken);
        var (ceilingCpu, ceilingMem) = CeilingFor(cores, memMib);
        var cpuMilli = Math.Min(ceilingCpu, cores * 1000 - policy.Budgets.CpuMilli);
        var memMibGrant = Math.Min(ceilingMem, memMib - policy.Budgets.MemMiB);
        if (cpuMilli <= 0 || memMibGrant <= 0)
        {
            return null; // OET's budget consumes the host: nothing to publish.
        }

        var nodeId = NodeIdFor(host.Id);
        var template = string.IsNullOrWhiteSpace(options.EndpointTemplate) ? "{address}:7443" : options.EndpointTemplate;
        var endpoint = options.EndpointOverrides.TryGetValue(host.Id, out var overridden) && !string.IsNullOrWhiteSpace(overridden)
            ? overridden
            : template.Replace("{address}", host.Address, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null; // An addressable helper is a UBAG dialling assumption; never publish a blank one.
        }

        // The trust plane (decision D3): publish ONLY a host whose certificate S10 has actually
        // rendered, with identity and pin set together. A pinless entry is not "capacity without a
        // pin" — UBAG's strict parser rejects any allocation whose uri_san is not exactly the node's
        // identity, and it rejects the WHOLE list, so one unfinished host would blind the gateway to
        // every other grant too. Skip instead: the trust-plane state is repaired by the next S10
        // (rollout or repair) and the host appears on a later poll.
        string uriSan;
        string spkiSha256;
        try
        {
            if (await _trust.GetIdentityAsync(host.Id, cancellationToken) is not { } identity
                || string.IsNullOrEmpty(identity.UriSan) || string.IsNullOrEmpty(identity.SpkiSha256))
            {
                return null;
            }

            uriSan = identity.UriSan;
            spkiSha256 = identity.SpkiSha256;
        }
        catch (Exception ex) when (ex is InvalidOperationException or CryptographicException)
        {
            // Unreadable trust-plane state is the same as no certificate: skip, don't publish.
            // No certificate material can appear in a message, and the exception itself is not
            // logged from a poll path.
            return null;
        }

        return new UbagAllocationWire
        {
            SchemaVersion = 1,
            NodeId = nodeId,
            Region = SanitizeRegion(host.Region),
            Endpoint = endpoint,
            CertIdentity = new UbagCertIdentityWire { UriSan = uriSan, SpkiSha256 = spkiSha256 },
            CpuMillis = cpuMilli,
            MemoryBytes = memMibGrant * 1024L * 1024L,
            ReservationState = "known",
            State = StateFor(host, apiNodes),
            MaxBrowserWorkloads = options.ClampedMaxBrowserWorkloads,
            VoiceCapable = false,
            ValidUntil = now.AddMinutes(options.ClampedGrantTtlMinutes),
            // Monotonic per host in practice: the desired revision only moves up as the owner
            // pushes policy or rolls the host, and UBAG keeps the last-known-good grant on a
            // LOWER generation, so it must never visibly go backwards for the same node id.
            Generation = host.DesiredRevision,
        };
    }

    /// <summary>Draining when the host is draining/disabled or the OET API does not report the
    /// node healthy; active only for a live, healthy, Active host.</summary>
    private static string StateFor(HostEntity host, IReadOnlyList<ApiNode> apiNodes)
    {
        if (host.Lifecycle is not nameof(HostLifecycle.Active))
        {
            return "draining";
        }

        var node = apiNodes.FirstOrDefault(n => string.Equals(n.Id, host.ApiNodeId, StringComparison.Ordinal));
        var healthy = node is not null && IsUsable(node.Status) && IsUsable(node.Health);
        return healthy ? "active" : "draining";
    }

    /// <summary>The API status/health words that mean the node is live and usable (the same family
    /// the dashboard's Fmt maps to "ok": Active/Enabled/Online/healthy/ok). Anything else —
    /// Pending, Probation, Draining, Stale, Offline, unknown — drains UBAG placements.</summary>
    private static bool IsUsable(string value) =>
        value is "Active" or "Enabled" or "Online" or "healthy" or "ok"
            || string.Equals(value, "active", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "enabled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "online", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "healthy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "ok", StringComparison.OrdinalIgnoreCase);

    /// <summary>UBAG's region pattern is ^[a-z0-9][a-z0-9-]{0,31}$; anything else publishes as "default".</summary>
    internal static string SanitizeRegion(string? region)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            return "default";
        }

        var trimmed = region.Trim().ToLowerInvariant();
        if (trimmed.Length == 0 || trimmed.Length > 32)
        {
            return "default";
        }

        foreach (var ch in trimmed)
        {
            var ok = char.IsAsciiLetterOrDigit(ch) || ch == '-';
            if (!ok)
            {
                return "default";
            }
        }

        return char.IsAsciiLetterOrDigit(trimmed[0]) ? trimmed : "default";
    }
}
