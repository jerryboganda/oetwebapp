using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

// ═════════════════════════════════════════════════════════════════════════════
// Remote-worker boundary (OET-RWP/1). Four Postgres-only tables, hand-authored in
// migration 20270110090000_AddRemoteWorkersAndJobs. The entities exist in the EF
// model for EVERY provider so SQLite/InMemory hosts still build; claim, fenced
// completion and the reaper are raw SQL and are only registered on Npgsql.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Node (helper VPS) statuses. Stored as the exact strings below (CHECK-constrained).</summary>
public static class RemoteNodeStatus
{
    public const string Pending = "Pending";
    public const string Probation = "Probation";
    public const string Active = "Active";
    public const string Draining = "Draining";
    public const string Disabled = "Disabled";
    public const string Quarantined = "Quarantined";
    public const string Revoked = "Revoked";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Pending, Probation, Active, Draining, Disabled, Quarantined, Revoked,
    };
}

/// <summary>Job states (OET-RWP/1 section 3.1). Stored as the exact strings below.</summary>
public static class RemoteJobState
{
    public const string Queued = "Queued";
    public const string Leased = "Leased";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Quarantined = "Quarantined";
    public const string FallbackLocal = "FallbackLocal";
    public const string Cancelled = "Cancelled";

    /// <summary>States a producer may reset to Queued (T2) and a manager may requeue.</summary>
    public static readonly IReadOnlySet<string> Terminal = new HashSet<string>(StringComparer.Ordinal)
    {
        Succeeded, Failed, Quarantined, FallbackLocal, Cancelled,
    };
}

public static class RemoteJobPurpose
{
    public const string Apply = "apply";
    public const string Shadow = "shadow";
    public const string Canary = "canary";
}

/// <summary>A rented helper VPS as the API sees it. Never holds a plaintext secret.</summary>
public class RemoteWorker
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Stable idempotency key chosen by the manager (<c>^[a-z0-9][a-z0-9-]{2,62}$</c>).</summary>
    [MaxLength(64)]
    public string NodeRef { get; set; } = default!;

    [MaxLength(128)]
    public string DisplayName { get; set; } = default!;

    [MaxLength(16)]
    public string Status { get; set; } = RemoteNodeStatus.Pending;

    [MaxLength(200)]
    public string? StatusReason { get; set; }

    public DateTimeOffset StatusChangedAt { get; set; }

    /// <summary>Recorded for the processor register (data-residency); changing it is an owner action.</summary>
    [MaxLength(32)]
    public string? Region { get; set; }

    [MaxLength(64)]
    public string? Provider { get; set; }

    /// <summary>Kinds the manager allows on this node. Enforced server-side at claim.</summary>
    public string[] AllowedKinds { get; set; } = [];

    public int MaxConcurrency { get; set; } = 2;

    /// <summary>JSON object: kind -> max concurrent weight.</summary>
    public string KindLimitsJson { get; set; } = "{}";

    public int CpuBudgetMilli { get; set; } = 3000;
    public int MemBudgetMiB { get; set; } = 5120;
    public int TmpBudgetMiB { get; set; } = 3072;

    public string PressureJson { get; set; } = "{}";
    public string PollJson { get; set; } = "{}";
    public string DesiredAgentJson { get; set; } = "{}";

    /// <summary>Only set by an explicit policy field; behaves like Draining without a restart.</summary>
    public bool Paused { get; set; }

    /// <summary>Increments on EVERY change of policy or status (the agent's desired.revision).</summary>
    public long PolicyRevision { get; set; } = 1;

    public long AppliedRevision { get; set; }

    [MaxLength(32)]
    public string? AgentVersion { get; set; }

    [MaxLength(80)]
    public string? AgentImageDigest { get; set; }

    public int? ProtocolVersion { get; set; }

    /// <summary>What the agent can run now: [{kind, schemaVersions[], engineVersion}]. Only ever NARROWS the offer.</summary>
    public string KindsJson { get; set; } = "[]";

    [MaxLength(64)]
    public string? CurrentInstanceId { get; set; }

    /// <summary>Agent-reported start time of <see cref="CurrentInstanceId"/> (orders two instances sharing a token).</summary>
    public DateTimeOffset? CurrentInstanceStartedAt { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public DateTimeOffset? LastClaimAt { get; set; }
    public string? LastCapacityJson { get; set; }
    public string? LastLoadJson { get; set; }

    public int IntegrityStrikes { get; set; }
    public DateTimeOffset? StrikeWindowStartedAt { get; set; }
    public int DeclinedInARow { get; set; }

    public DateTimeOffset? LastCanaryAt { get; set; }
    public bool? LastCanaryOk { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string CreatedBy { get; set; } = default!;
}

/// <summary>Hashed bearer credential. <c>Kind</c> is <c>node</c> (job plane) or <c>fleet</c> (service plane).</summary>
public class RemoteCredential
{
    /// <summary>16 lowercase hex characters (64 random bits); the lookup key.</summary>
    [Key]
    [MaxLength(16)]
    public string TokenId { get; set; } = default!;

    [MaxLength(8)]
    public string Kind { get; set; } = default!;

    [MaxLength(64)]
    public string? NodeId { get; set; }

    /// <summary>Lowercase hex SHA-256 of the ASCII bytes of the 43-character secret.</summary>
    [MaxLength(64)]
    public string SecretHash { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    [MaxLength(64)]
    public string CreatedBy { get; set; } = default!;
}

/// <summary>One unit of work leased to exactly one node at a time, fenced and idempotent.</summary>
public class RemoteJob
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(48)]
    public string Kind { get; set; } = default!;

    public int SchemaVersion { get; set; } = 1;

    [MaxLength(16)]
    public string Purpose { get; set; } = RemoteJobPurpose.Apply;

    [MaxLength(48)]
    public string ResourceType { get; set; } = default!;

    [MaxLength(64)]
    public string ResourceId { get; set; } = default!;

    [MaxLength(256)]
    public string IdempotencyKey { get; set; } = default!;

    [MaxLength(64)]
    public string InputSha256 { get; set; } = default!;

    [MaxLength(96)]
    public string EngineVersion { get; set; } = default!;

    [MaxLength(64)]
    public string SettingsHash { get; set; } = default!;

    public string ParamsJson { get; set; } = "{}";
    public string InputsJson { get; set; } = "[]";
    public string LimitsJson { get; set; } = "{}";

    public short Weight { get; set; } = 1;
    public short Priority { get; set; }

    [MaxLength(64)]
    public string? TargetNodeId { get; set; }

    [MaxLength(16)]
    public string State { get; set; } = RemoteJobState.Queued;

    public int Attempt { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public int ReleaseCount { get; set; }

    /// <summary>Incremented by exactly 1 at claim and nowhere else; never reset, never decremented.</summary>
    public long FenceToken { get; set; }

    [MaxLength(64)]
    public string? LeaseOwner { get; set; }

    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? DeadlineAt { get; set; }
    public DateTimeOffset? LeasedAt { get; set; }

    [MaxLength(64)]
    public string? ClaimNonce { get; set; }

    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? FallbackAfter { get; set; }

    [MaxLength(64)]
    public string EnqueuedBy { get; set; } = default!;

    [MaxLength(64)]
    public string? ResultSha256 { get; set; }

    public string? ResultSummaryJson { get; set; }

    /// <summary>Parked result of a <c>Deferred</c> job, consumed (and cleared) by an API-side consumer.</summary>
    public string? ResultJson { get; set; }

    [MaxLength(16)]
    public string? ApplyOutcome { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
    public long? SettledFence { get; set; }

    [MaxLength(64)]
    public string? SettledBy { get; set; }

    [MaxLength(64)]
    public string? SettledCode { get; set; }

    [MaxLength(64)]
    public string? FailureCode { get; set; }

    [MaxLength(256)]
    public string? FailureMessage { get; set; }

    [MaxLength(64)]
    public string? LastFailedNodeId { get; set; }

    public string? MetricsJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A binary output a node uploaded for <c>(JobId, Fence, Name)</c> (media kinds only).</summary>
public class RemoteJobOutput
{
    [MaxLength(64)]
    public string JobId { get; set; } = default!;

    public long Fence { get; set; }

    [MaxLength(64)]
    public string Name { get; set; } = default!;

    [MaxLength(64)]
    public string Sha256 { get; set; } = default!;

    public long SizeBytes { get; set; }

    /// <summary>Final per-job key <c>remote-jobs/{jobId}/{fence}/{name}</c>; never sent to a node.</summary>
    [MaxLength(512)]
    public string StorageKey { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
}
