using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.RemoteJobs;

// ═════════════════════════════════════════════════════════════════════════════
// Wire models of the job plane (OET-RWP/1 section 4). Requests are parsed with STRICT options and
// every property is nullable so a missing or mistyped member becomes a 400 bad_request from Validate(),
// never a framework-shaped error. Unknown members are ignored (additive change rule).
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>What the agent can run now: kind, schema versions and the exact engine version.</summary>
public sealed class RemoteKindOfferDto
{
    public string? Kind { get; set; }
    public List<int>? SchemaVersions { get; set; }
    public string? EngineVersion { get; set; }
}

/// <summary>Capacity block of claim and node heartbeat. Advisory but required; numbers only.</summary>
public sealed class RemoteCapacityDto
{
    public long? CpuCoresTotal { get; set; }
    public long? CpuBudgetMilli { get; set; }
    public long? CpuBudgetFreeMilli { get; set; }
    public long? MemTotalMiB { get; set; }
    public long? MemAvailableMiB { get; set; }
    public long? MemBudgetMiB { get; set; }
    public long? MemBudgetFreeMiB { get; set; }
    public long? TmpBudgetMiB { get; set; }
    public long? TmpFreeMiB { get; set; }
    public long? DiskFreeMiB { get; set; }
    public long? HeavySlotsTotal { get; set; }
    public long? HeavySlotsFree { get; set; }
    public long? ConfiguredConcurrency { get; set; }
    public long? EffectiveConcurrency { get; set; }
}

public sealed class RemoteAgentDto
{
    public string? Version { get; set; }
    public string? ImageDigest { get; set; }
    public int? Protocol { get; set; }
    public List<int>? ProtocolsSupported { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public long? ClockSkewMs { get; set; }
}

public sealed class RemoteClaimRequestDto
{
    public Guid? ClaimId { get; set; }
    public Guid? InstanceId { get; set; }
    public long? AppliedRevision { get; set; }
    public List<RemoteKindOfferDto>? Kinds { get; set; }
    public RemoteCapacityDto? Capacity { get; set; }
    public RemoteAgentDto? Agent { get; set; }
}

public sealed class RemoteLoadDto
{
    public double? CpuPct { get; set; }
    public double? CpuPct15s { get; set; }
    public double? MemFreePct { get; set; }
    public double? Load1 { get; set; }
    public string? Pressure { get; set; }
}

public sealed class RemoteLeaseReportDto
{
    public string? JobId { get; set; }
    public long? Fence { get; set; }
    public string? Stage { get; set; }
    public long? ElapsedMs { get; set; }
}

public sealed class RemoteNodeHeartbeatRequestDto
{
    public Guid? InstanceId { get; set; }
    public long? AppliedRevision { get; set; }
    public string? State { get; set; }
    public RemoteAgentDto? Agent { get; set; }
    public List<RemoteKindOfferDto>? Kinds { get; set; }
    public RemoteCapacityDto? Capacity { get; set; }
    public RemoteLoadDto? Load { get; set; }
    public List<RemoteLeaseReportDto>? Leases { get; set; }
}

public sealed class RemoteJobHeartbeatRequestDto
{
    public long? Fence { get; set; }
    public string? Stage { get; set; }
    public Dictionary<string, double>? Metrics { get; set; }
}

public sealed class RemoteCompleteRequestDto
{
    public long? Fence { get; set; }
    public string? ResultSha256 { get; set; }
    public string? ResultJson { get; set; }
    public List<RemoteOutputRefDto>? Outputs { get; set; }
    public Dictionary<string, double>? Metrics { get; set; }
}

public sealed class RemoteOutputRefDto
{
    public string? Name { get; set; }
    public long? SizeBytes { get; set; }
    public string? Sha256 { get; set; }
}

public sealed class RemoteFailRequestDto
{
    public long? Fence { get; set; }
    public bool? Retryable { get; set; }
    public string? Code { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, double>? Metrics { get; set; }
}

/// <summary>Validation of the wire models; each returns null when valid, else a short client-safe message.</summary>
public static partial class RemoteWireValidation
{
    public const long MaxNumber = 1_000_000_000_000;

    [GeneratedRegex("^[A-Za-z0-9._:+/-]{1,96}$", RegexOptions.CultureInvariant)]
    private static partial Regex EngineVersionPattern();

    [GeneratedRegex("^[0-9A-Za-z._+-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex AgentVersionPattern();

    [GeneratedRegex("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ImageDigestPattern();

    [GeneratedRegex("^[a-z_]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex StagePattern();

    [GeneratedRegex("^[A-Za-z0-9 _.:/=,()-]{0,200}$", RegexOptions.CultureInvariant)]
    private static partial Regex FailMessagePattern();

    public static bool IsEngineVersion(string? value) => value is not null && EngineVersionPattern().IsMatch(value);

    public static bool IsStage(string? value) => value is not null && StagePattern().IsMatch(value);

    public static bool IsFailMessage(string? value) => value is not null && FailMessagePattern().IsMatch(value);

    public static string? Kinds(List<RemoteKindOfferDto>? kinds)
    {
        if (kinds is null || kinds.Count is < 1 or > 8) return "kinds must contain 1 to 8 entries.";
        foreach (var offer in kinds)
        {
            if (offer is null) return "kinds must not contain null entries.";
            if (string.IsNullOrWhiteSpace(offer.Kind) || offer.Kind.Length > 48) return "kinds[].kind is required.";
            if (offer.SchemaVersions is null || offer.SchemaVersions.Count is < 1 or > 4
                || offer.SchemaVersions.Any(v => v < 1 || v > 1000))
            {
                return "kinds[].schemaVersions must contain 1 to 4 positive integers.";
            }

            if (!IsEngineVersion(offer.EngineVersion)) return "kinds[].engineVersion is invalid.";
        }

        return null;
    }

    public static string? Capacity(RemoteCapacityDto? capacity)
    {
        if (capacity is null) return "capacity is required.";
        if (capacity.CpuBudgetFreeMilli is null || capacity.MemBudgetFreeMiB is null || capacity.TmpFreeMiB is null
            || capacity.HeavySlotsFree is null || capacity.EffectiveConcurrency is null)
        {
            return "capacity.cpuBudgetFreeMilli, memBudgetFreeMiB, tmpFreeMiB, heavySlotsFree and effectiveConcurrency are required.";
        }

        foreach (var value in new[]
        {
            capacity.CpuCoresTotal, capacity.CpuBudgetMilli, capacity.CpuBudgetFreeMilli, capacity.MemTotalMiB,
            capacity.MemAvailableMiB, capacity.MemBudgetMiB, capacity.MemBudgetFreeMiB, capacity.TmpBudgetMiB,
            capacity.TmpFreeMiB, capacity.DiskFreeMiB, capacity.HeavySlotsTotal, capacity.HeavySlotsFree,
            capacity.ConfiguredConcurrency, capacity.EffectiveConcurrency,
        })
        {
            if (value is < 0 or > MaxNumber) return "capacity values must be between 0 and 1000000000000.";
        }

        return null;
    }

    public static string? Agent(RemoteAgentDto? agent)
    {
        if (agent is null) return "agent is required.";
        if (agent.Version is null || !AgentVersionPattern().IsMatch(agent.Version)) return "agent.version is invalid.";
        if (agent.ImageDigest is null || !ImageDigestPattern().IsMatch(agent.ImageDigest)) return "agent.imageDigest is invalid.";
        if (agent.Protocol is null or < 1) return "agent.protocol is required.";
        if (agent.ProtocolsSupported is null || agent.ProtocolsSupported.Count is < 1 or > 8
            || agent.ProtocolsSupported.Any(p => p < 1))
        {
            return "agent.protocolsSupported is invalid.";
        }

        return null;
    }

    public static string? Claim(RemoteClaimRequestDto? request)
    {
        if (request is null) return "A JSON body is required.";
        if (request.ClaimId is null || request.ClaimId == Guid.Empty) return "claimId is required.";
        if (request.InstanceId is null || request.InstanceId == Guid.Empty) return "instanceId is required.";
        if (request.AppliedRevision is null or < 0) return "appliedRevision is required.";
        return Kinds(request.Kinds) ?? Capacity(request.Capacity) ?? Agent(request.Agent);
    }

    private static readonly HashSet<string> NodeStates = new(StringComparer.Ordinal)
    {
        "starting", "ready", "draining", "paused", "degraded", "protocol_mismatch", "stopping",
    };

    public static string? NodeHeartbeat(RemoteNodeHeartbeatRequestDto? request)
    {
        if (request is null) return "A JSON body is required.";
        if (request.InstanceId is null || request.InstanceId == Guid.Empty) return "instanceId is required.";
        if (request.AppliedRevision is null or < 0) return "appliedRevision is required.";
        if (request.State is null || !NodeStates.Contains(request.State)) return "state is invalid.";
        if (request.Leases is null || request.Leases.Count > 256) return "leases must be an array of at most 256 entries.";
        foreach (var lease in request.Leases)
        {
            if (lease is null || !RemoteIds.IsJobId(lease.JobId) || lease.Fence is null or < 0)
            {
                return "leases[] entries need a valid jobId and fence.";
            }
        }

        if (request.Load is { Pressure: not null } load && load.Pressure is not ("normal" or "reduced"))
        {
            return "load.pressure is invalid.";
        }

        return Kinds(request.Kinds) ?? Capacity(request.Capacity) ?? Agent(request.Agent);
    }

    public static string? JobHeartbeat(RemoteJobHeartbeatRequestDto? request)
    {
        if (request is null) return "A JSON body is required.";
        if (request.Fence is null or < 0) return "fence is required.";
        if (request.Stage is not null && !IsStage(request.Stage)) return "stage is invalid.";
        if (request.Metrics is { Count: > 32 }) return "metrics has too many entries.";
        return null;
    }

    public static string? Fail(RemoteFailRequestDto? request)
    {
        if (request is null) return "A JSON body is required.";
        if (request.Fence is null or < 0) return "fence is required.";
        if (string.IsNullOrWhiteSpace(request.Code)) return "code is required.";
        if (request.Message is not null && !IsFailMessage(request.Message)) return "message is invalid.";
        if (request.Metrics is { Count: > 32 }) return "metrics has too many entries.";
        return null;
    }
}
