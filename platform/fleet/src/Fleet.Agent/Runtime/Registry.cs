using System.Globalization;
using System.Reflection;

namespace Fleet.Agent;

/// <summary>Who this agent process is: its version, image digest, per-process instance id and start time.</summary>
internal sealed class AgentIdentity
{
    public AgentIdentity(string version, string imageDigest, DateTimeOffset? startedAt = null, Guid? instanceId = null)
    {
        Version = version;
        ImageDigest = imageDigest;
        StartedAt = startedAt ?? DateTimeOffset.UtcNow;
        InstanceId = (instanceId ?? Guid.NewGuid()).ToString("D");
    }

    public string Version { get; }
    public string ImageDigest { get; }
    public DateTimeOffset StartedAt { get; }

    /// <summary>Generated at process start, constant for the process lifetime (section 4.1.1).</summary>
    public string InstanceId { get; }

    public static string CurrentVersion()
    {
        var assembly = typeof(AgentIdentity).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    public AgentInfo ToWire(int protocol, bool detailed, long? clockSkewMs = null) => new()
    {
        Version = Version,
        ImageDigest = ImageDigest,
        Protocol = protocol,
        ProtocolsSupported = Wire.SupportedProtocols,
        StartedAt = detailed ? TimeFormat.Rfc3339(StartedAt) : null,
        ClockSkewMs = detailed ? clockSkewMs : null,
    };
}

/// <summary>The executors this process can run right now; rebuilt after every self-check.</summary>
internal sealed class ExecutorRegistry
{
    private readonly object _gate = new();
    private IReadOnlyList<IJobExecutor> _executors = [];

    public IReadOnlyList<IJobExecutor> All
    {
        get { lock (_gate) return _executors; }
    }

    public void Replace(IEnumerable<IJobExecutor> executors)
    {
        lock (_gate) _executors = executors.ToArray();
    }

    public IJobExecutor? Find(string kind, int schemaVersion) =>
        All.FirstOrDefault(e => e.Kind == kind && e.SchemaVersion == schemaVersion);

    /// <summary>Every kind this build can run, for the node heartbeat (the offer only NARROWS what policy allows, A8).</summary>
    public List<KindOffer> Runnable() =>
        All.Select(e => new KindOffer { Kind = e.Kind, SchemaVersions = [e.SchemaVersion], EngineVersion = e.EngineVersion }).ToList();

    /// <summary>
    /// The kinds to offer on this claim: allowed by policy, under their per-kind cap, and admissible right now by the
    /// registry limits (weight, memory including tmpfs, CPU, tmp; section 3.8 item 4).
    /// </summary>
    public List<KindOffer> Offers(DesiredState desired, CapacityAccountant capacity)
    {
        var offers = new List<KindOffer>();
        foreach (var executor in All)
        {
            if (!desired.AllowedKinds.Contains(executor.Kind, StringComparer.Ordinal)) continue;
            if (desired.PerKind.TryGetValue(executor.Kind, out var cap) && capacity.RunningOfKind(executor.Kind) >= cap) continue;
            var limits = KindRegistry.For(executor.Kind);
            if (limits is not null && !capacity.CanAdmit(limits)) continue;
            offers.Add(new KindOffer { Kind = executor.Kind, SchemaVersions = [executor.SchemaVersion], EngineVersion = executor.EngineVersion });
        }

        return offers;
    }
}
