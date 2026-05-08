using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Phase 5 — Tool calling. Tools are deny-by-default (see locked decision
/// <c>p5-rbac-default = A</c>): a row in <see cref="AiTool"/> is the catalog,
/// a row in <see cref="AiFeatureToolGrant"/> is the per-feature opt-in.
/// </summary>
public enum AiToolCategory
{
    /// <summary>Side-effect-free read of indexed data. Safe to expose
    /// broadly.</summary>
    Read = 0,

    /// <summary>Mutates data scoped to the calling user only (e.g.
    /// <c>save_user_note</c>). Never global state.</summary>
    Write = 1,

    /// <summary>Issues outbound HTTP to a host on the allowlist
    /// (<c>AiToolOptions.AllowedExternalHosts</c>). Subject to per-user
    /// daily budget.</summary>
    ExternalNetwork = 2,
}

/// <summary>
/// Outcome of a single tool invocation. Mirrors
/// <see cref="OetLearner.Api.Domain.AiCallOutcome"/> but tool-specific.
/// </summary>
public enum AiToolOutcome
{
    Success = 0,
    /// <summary>Args JSON failed schema validation.</summary>
    ArgsInvalid = 1,
    /// <summary>Feature is not granted this tool.</summary>
    RbacDenied = 2,
    /// <summary>Underlying provider/HTTP/API call failed.</summary>
    ProviderError = 3,
    /// <summary>External-network tool tried to reach a host outside
    /// <c>AiToolOptions.AllowedExternalHosts</c>.</summary>
    BlockedHost = 4,
    /// <summary>Per-user daily budget exhausted for external-network tools.</summary>
    BudgetExceeded = 5,
    /// <summary>Tool executor threw an unhandled exception.</summary>
    ExecutionError = 6,
}

/// <summary>
/// Catalog row for a tool the gateway may expose to the model. Seeded
/// idempotently by <c>AiToolSeedWorker</c>; admin endpoints expose only
/// <see cref="AiFeatureToolGrant"/> CRUD, not direct tool CRUD (the
/// catalog is code-defined).
/// </summary>
[Index(nameof(Code), IsUnique = true)]
public class AiTool
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Stable code matching <c>IAiToolExecutor.Code</c>.</summary>
    [MaxLength(64)]
    public string Code { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    public AiToolCategory Category { get; set; } = AiToolCategory.Read;

    /// <summary>JSON Schema 2020-12 describing the args contract. Cap 8 KiB.</summary>
    [MaxLength(8192)]
    public string JsonSchemaArgs { get; set; } = "{}";

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }
}

/// <summary>
/// Per-feature tool grant. Deny-by-default: the absence of an active row
/// means the model is never told the tool exists.
/// </summary>
[Index(nameof(FeatureCode), nameof(ToolCode), IsUnique = true)]
[Index(nameof(FeatureCode))]
public class AiFeatureToolGrant
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Matches a constant in <c>AiFeatureCodes</c>.</summary>
    [MaxLength(64)]
    public string FeatureCode { get; set; } = default!;

    /// <summary>Matches <see cref="AiTool.Code"/>.</summary>
    [MaxLength(64)]
    public string ToolCode { get; set; } = default!;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }
}

/// <summary>
/// One row per individual tool call. Linked to <c>AiUsageRecord</c> so that
/// "one logical AI call" remains "one usage record + N tool invocations".
/// Args + result hashed (SHA-256 hex) for audit without storing raw payloads.
/// </summary>
[Index(nameof(AiUsageRecordId), nameof(TurnIndex))]
[Index(nameof(FeatureCode), nameof(CreatedAt))]
[Index(nameof(ToolCode), nameof(CreatedAt))]
public class AiToolInvocation
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AiUsageRecordId { get; set; } = default!;

    [MaxLength(64)]
    public string FeatureCode { get; set; } = default!;

    [MaxLength(64)]
    public string ToolCode { get; set; } = default!;

    public AiToolCategory Category { get; set; }

    [MaxLength(64)]
    public string? UserId { get; set; }

    /// <summary>1-based turn index inside the multi-turn loop.</summary>
    public int TurnIndex { get; set; }

    [MaxLength(64)]
    public string ArgsHash { get; set; } = string.Empty;

    [MaxLength(64)]
    public string ResultHash { get; set; } = string.Empty;

    public AiToolOutcome Outcome { get; set; }

    [MaxLength(64)]
    public string? ErrorCode { get; set; }

    [MaxLength(512)]
    public string? ErrorMessage { get; set; }

    public int LatencyMs { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
