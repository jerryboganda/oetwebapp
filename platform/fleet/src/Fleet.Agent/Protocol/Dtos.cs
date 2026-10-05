using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fleet.Agent;

// JSON bodies of OET-RWP/1. Readers ignore unknown members (section 0.3); writers omit nulls.
// Property names serialise camelCase through ProtocolJson.Options.

internal static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static byte[] ToUtf8(object value) => JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), Options);
}

// ---- shared blocks -------------------------------------------------------------------------------------------

internal sealed class KindOffer
{
    public string Kind { get; set; } = "";
    public int[] SchemaVersions { get; set; } = [];
    public string EngineVersion { get; set; } = "";
}

internal sealed class AgentInfo
{
    public string Version { get; set; } = "";
    public string ImageDigest { get; set; } = "";
    public int Protocol { get; set; }
    public int[] ProtocolsSupported { get; set; } = [];
    public string? StartedAt { get; set; }
    public long? ClockSkewMs { get; set; }
}

internal sealed class ClaimCapacity
{
    public long CpuBudgetFreeMilli { get; set; }
    public long MemBudgetFreeMiB { get; set; }
    public long TmpFreeMiB { get; set; }
    public int HeavySlotsFree { get; set; }
    public int EffectiveConcurrency { get; set; }
}

internal sealed class NodeCapacity
{
    public int CpuCoresTotal { get; set; }
    public long CpuBudgetMilli { get; set; }
    public long CpuBudgetFreeMilli { get; set; }
    public long MemTotalMiB { get; set; }
    public long MemAvailableMiB { get; set; }
    public long MemBudgetMiB { get; set; }
    public long MemBudgetFreeMiB { get; set; }
    public long TmpBudgetMiB { get; set; }
    public long TmpFreeMiB { get; set; }
    public long DiskFreeMiB { get; set; }
    public int HeavySlotsTotal { get; set; }
    public int HeavySlotsFree { get; set; }
    public int ConfiguredConcurrency { get; set; }
    public int EffectiveConcurrency { get; set; }
}

internal sealed class LoadBlock
{
    public double CpuPct { get; set; }
    public double CpuPct15s { get; set; }
    public double MemFreePct { get; set; }
    public double Load1 { get; set; }
    public string Pressure { get; set; } = "normal";
}

internal sealed class LeaseReport
{
    public string JobId { get; set; } = "";
    public long Fence { get; set; }
    public string? Stage { get; set; }
    public long ElapsedMs { get; set; }
}

// ---- node heartbeat (4.7) ------------------------------------------------------------------------------------

internal sealed class NodeHeartbeatRequest
{
    public string InstanceId { get; set; } = "";
    public long AppliedRevision { get; set; }
    public string State { get; set; } = "starting";
    public AgentInfo Agent { get; set; } = new();
    public List<KindOffer> Kinds { get; set; } = [];
    public NodeCapacity Capacity { get; set; } = new();
    public LoadBlock Load { get; set; } = new();
    public List<LeaseReport> Leases { get; set; } = [];
    public object? Canary { get; set; }

    /// <summary>Additive extension (section 0.3): why the agent is degraded, for example unapproved_digest.</summary>
    public string? DegradedReason { get; set; }
}

internal sealed class NodeRef
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "";
}

internal sealed class FeaturesBlock
{
    public bool Enabled { get; set; }
    public Dictionary<string, bool> Kinds { get; set; } = new();
}

internal sealed class LeaseVerdict
{
    public string JobId { get; set; } = "";
    public long Fence { get; set; }
    public bool Valid { get; set; }
    public long LeaseRemainingMs { get; set; }
}

internal sealed class NodeHeartbeatResponse
{
    public string? ServerTime { get; set; }
    public NodeRef? Node { get; set; }
    public FeaturesBlock? Features { get; set; }
    public DesiredState? Desired { get; set; }
    public List<LeaseVerdict> LeaseAudit { get; set; } = [];
    public int? NextHeartbeatSeconds { get; set; }
}

// ---- desired state (7.3) -------------------------------------------------------------------------------------

internal sealed class Budgets
{
    public long CpuMilli { get; set; }
    public long MemMiB { get; set; }
    public long TmpMiB { get; set; }
}

internal sealed class PressureSettings
{
    public int ReduceCpuPct { get; set; } = 80;
    public int ReduceMemFreePct { get; set; } = 20;
    public int RestoreCpuPct { get; set; } = 60;
    public int RestoreMemFreePct { get; set; } = 25;
    public int RestoreAfterSeconds { get; set; } = 120;
}

internal sealed class PollSettings
{
    public int Idle { get; set; } = 10;
    public int Min { get; set; } = 5;
    public int Max { get; set; } = 30;
}

internal sealed class AgentImageDesired
{
    public List<string> ApprovedDigests { get; set; } = [];
    public string? Target { get; set; }
    public string? MinVersion { get; set; }
}

internal sealed class DesiredState
{
    public long Revision { get; set; }
    public bool Drain { get; set; }
    public bool Paused { get; set; }
    public List<string> AllowedKinds { get; set; } = [];
    public int MaxConcurrency { get; set; } = 2;
    public Dictionary<string, int> PerKind { get; set; } = new();
    public Budgets? Budgets { get; set; }
    public PressureSettings? Pressure { get; set; }
    public PollSettings? PollSeconds { get; set; }
    public AgentImageDesired? AgentImage { get; set; }
}

// ---- claim (4.1) ---------------------------------------------------------------------------------------------

internal sealed class ClaimRequest
{
    public string ClaimId { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public long AppliedRevision { get; set; }
    public List<KindOffer> Kinds { get; set; } = [];
    public ClaimCapacity Capacity { get; set; } = new();
    public AgentInfo Agent { get; set; } = new();
}

internal sealed class InputRef
{
    public string Name { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string? ContentType { get; set; }
}

internal sealed class JobLimits
{
    public int Weight { get; set; } = 1;
    public int CpuMilli { get; set; }
    public int MemMiB { get; set; }
    public int TmpMiB { get; set; }
    public int TimeoutSeconds { get; set; }
    public long MaxInputBytes { get; set; }
    public long MaxResultBytes { get; set; }
    public long MaxOutputBytes { get; set; }
    public int MaxOutputs { get; set; }
}

internal sealed class ClaimedJob
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public int SchemaVersion { get; set; }
    public string Purpose { get; set; } = "apply";
    public string EngineVersion { get; set; } = "";
    public int Attempt { get; set; }
    public int MaxAttempts { get; set; }
    public long Fence { get; set; }
    public string? LeaseExpiresAt { get; set; }
    public long LeaseRemainingMs { get; set; }
    public int HeartbeatEverySeconds { get; set; } = 20;
    public string? DeadlineAt { get; set; }
    public int DeadlineSeconds { get; set; }
    public List<InputRef> Inputs { get; set; } = [];
    public JsonElement Params { get; set; }
    public JobLimits Limits { get; set; } = new();
}

internal sealed class ClaimResponse
{
    public ClaimedJob? Job { get; set; }
    public string? ServerTime { get; set; }
    public DesiredState? Desired { get; set; }
}

// ---- job heartbeat (4.2) -------------------------------------------------------------------------------------

internal sealed class JobMetricsBlock
{
    public long RssMiB { get; set; }
    public double CpuPct { get; set; }
    public long ElapsedMs { get; set; }
}

internal sealed class JobHeartbeatRequest
{
    public long Fence { get; set; }
    public string? Stage { get; set; }
    public JobMetricsBlock? Metrics { get; set; }
}

internal sealed class JobHeartbeatResponse
{
    public string? LeaseExpiresAt { get; set; }
    public long LeaseRemainingMs { get; set; }
    public string? ServerTime { get; set; }
    public long? DesiredRevision { get; set; }
}

// ---- complete / fail / outputs (4.4 - 4.6) -------------------------------------------------------------------

internal sealed class OutputRef
{
    public string Name { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
}

internal sealed class CompleteMetrics
{
    public long DurationMs { get; set; }
    public long PeakRssMiB { get; set; }
    public long InputBytes { get; set; }
}

internal sealed class CompleteRequest
{
    public long Fence { get; set; }
    public string ResultSha256 { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public List<OutputRef> Outputs { get; set; } = [];
    public CompleteMetrics? Metrics { get; set; }
}

internal sealed class CompleteResponse
{
    public string? Status { get; set; }
    public string? Outcome { get; set; }
    public bool Replayed { get; set; }
    public string? Code { get; set; }
    public string? ServerTime { get; set; }
}

internal sealed class FailMetrics
{
    public long ElapsedMs { get; set; }
    public long PeakRssMiB { get; set; }
}

internal sealed class FailRequest
{
    public long Fence { get; set; }
    public bool Retryable { get; set; }
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public FailMetrics? Metrics { get; set; }
}

internal sealed class FailResponse
{
    public string? Status { get; set; }
    public bool Replayed { get; set; }
}

internal sealed class OutputAck
{
    public string Name { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public bool Replaced { get; set; }
}

/// <summary>application/problem+json envelope (section 2.5).</summary>
internal sealed class ProblemBody
{
    public string? Code { get; set; }
    public string? Message { get; set; }
    public bool? Retryable { get; set; }
    public string? Reason { get; set; }
    public int? RetryAfterSeconds { get; set; }
    public string? CorrelationId { get; set; }
    public int[]? Supported { get; set; }
}
