namespace Fleet.Manager.Persistence;

// Entities map 1:1 onto the SQLite tables of OET-RWP/1 section 8.3 (snake_case column names are
// applied by FleetDbContext). Timestamps are stored as unix milliseconds. State-like columns are
// strings so the file stays readable with a plain sqlite3 shell. No entity ever holds a secret:
// credentials carry only AES-GCM ciphertext and a fingerprint hint.

public sealed class HostEntity
{
    public string Id { get; set; } = string.Empty;

    public string NodeRef { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public int SshPort { get; set; } = 22;

    public string? HostKeyAlgo { get; set; }

    public string? HostKeySha256 { get; set; }

    /// <summary>The pinned public key blob (base64). Public data; needed to regenerate known_hosts.</summary>
    public string? HostKeyPublic { get; set; }

    public DateTimeOffset? HostKeyPinnedAt { get; set; }

    public string? Region { get; set; }

    public string? Provider { get; set; }

    public string? Os { get; set; }

    public string? Arch { get; set; }

    public int? CpuCores { get; set; }

    public int? MemMib { get; set; }

    public int? DiskGib { get; set; }

    public string Lifecycle { get; set; } = "Enrolling";

    public string? ApiNodeId { get; set; }

    public string? AgentDigest { get; set; }

    public int DesiredRevision { get; set; }

    public int AppliedRevision { get; set; }

    public long UbagAllocationRevision { get; set; }

    public string UbagAllocationFingerprint { get; set; } = string.Empty;

    public DateTimeOffset? LastStatusAt { get; set; }

    public string? LastStatusJson { get; set; }

    /// <summary>A persistent UI alert (for example <c>host_key_changed</c>) cleared only by an explicit owner action.</summary>
    public string? Alert { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class OperationEntity
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Chain order (client assigned, strictly increasing).</summary>
    public long Seq { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string? HostId { get; set; }

    public string State { get; set; } = string.Empty;

    /// <summary>For a failed enroll: the state to re-enter on retry.</summary>
    public string? ResumeState { get; set; }

    public string? CurrentStep { get; set; }

    public int StepAttempt { get; set; }

    /// <summary>Immutable creation parameters (sealed into the hash). NO secrets.</summary>
    public string ParamsJson { get; set; } = "{}";

    /// <summary>Mutable working data (scanned host-key candidates, progress). NO secrets.</summary>
    public string DataJson { get; set; } = "{}";

    public string Actor { get; set; } = string.Empty;

    public string? RequestId { get; set; }

    public bool CancelRequested { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string? FailureReason { get; set; }

    /// <summary>Sanitized, HTML-encoded, at most 500 characters.</summary>
    public string? FailureDetail { get; set; }

    public string PrevHash { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;
}

public sealed class OperationStepEntity
{
    public string OperationId { get; set; } = string.Empty;

    public int Seq { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>pending, running, done, skipped or failed.</summary>
    public string State { get; set; } = "pending";

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public int? ExitCode { get; set; }

    public string? Summary { get; set; }
}

public sealed class PolicyEntity
{
    public string Id { get; set; } = string.Empty;

    /// <summary><c>global</c> or <c>host</c>.</summary>
    public string Scope { get; set; } = "global";

    public string? HostId { get; set; }

    /// <summary>Comma separated kinds.</summary>
    public string AllowedKinds { get; set; } = string.Empty;

    public int MaxConcurrency { get; set; }

    public string PerKindJson { get; set; } = "{}";

    public int CpuBudgetMilli { get; set; }

    public int MemBudgetMib { get; set; }

    public int TmpBudgetMib { get; set; }

    public string PressureJson { get; set; } = "{}";

    public string PollJson { get; set; } = "{}";

    public int Version { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CredentialEntity
{
    public string Id { get; set; } = string.Empty;

    public string HostId { get; set; } = string.Empty;

    /// <summary><c>owner-bootstrap</c>, <c>manager-ssh</c>, <c>node-token-render</c>, <c>owner-totp</c>, <c>ubag-node-cert</c> or <c>ubag-node-key</c>.</summary>
    public string Purpose { get; set; } = string.Empty;

    /// <summary>Vault record (section 8.5). Null for <c>node-token-render</c>, which keeps only a fingerprint.</summary>
    public byte[]? Ciphertext { get; set; }

    public long KeyId { get; set; }

    public string FingerprintHint { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? RotatedAt { get; set; }

    public DateTimeOffset? DiscardedAt { get; set; }
}

public sealed class ReleaseEntity
{
    public string Id { get; set; } = string.Empty;

    public string Sha { get; set; } = string.Empty;

    public string RunId { get; set; } = string.Empty;

    public string AgentRepository { get; set; } = string.Empty;

    public string AgentDigest { get; set; } = string.Empty;

    public string? AgentImageId { get; set; }

    public string? ManagerDigest { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public bool Approved { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public string? ApprovedBy { get; set; }
}

public sealed class AuditEntity
{
    public long Id { get; set; }

    public DateTimeOffset At { get; set; }

    public string Actor { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string? Target { get; set; }

    public string DetailsJson { get; set; } = "{}";

    public string PrevHash { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;
}

public sealed class OwnerAccountEntity
{
    public string Id { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>The TOTP secret as a vault record (purpose <c>owner-totp</c>).</summary>
    public byte[] TotpSecretCiphertext { get; set; } = Array.Empty<byte>();

    public long TotpLastStep { get; set; }

    public int FailedAttempts { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SchemaInfoEntity
{
    public int Id { get; set; } = 1;

    public int Version { get; set; }

    public DateTimeOffset AppliedAt { get; set; }
}
