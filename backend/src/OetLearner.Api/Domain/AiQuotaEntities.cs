using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

// ═════════════════════════════════════════════════════════════════════════════
// AI Usage Management — Slice 2 entities (quota plans + counters + budget)
//
// Entities added here are configurable surfaces of the policies documented in
// docs/AI-USAGE-POLICY.md. Call sites never read these tables directly — they
// go through IAiQuotaService / IAiPolicyService.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Enumerates the options for §3 "Period & reset policy". Values map 1:1 to
/// <c>docs/AI-USAGE-POLICY.md</c> §3.
/// </summary>
public enum AiQuotaPeriod
{
    Monthly = 0,
    Daily = 1,
    Weekly = 2,
    Rolling30d = 3,
    NeverExpire = 4,
}

/// <summary>§3 RolloverPolicy.</summary>
public enum AiQuotaRolloverPolicy
{
    Expire = 0,
    RolloverCapped = 1,
    RolloverFull = 2,
}

/// <summary>§4 OveragePolicy.</summary>
public enum AiOveragePolicy
{
    Deny = 0,
    AllowWithCharge = 1,
    AutoUpgrade = 2,
    DegradeToSmallerModel = 3,
}

/// <summary>§7 global kill-switch scope.</summary>
public enum AiKillSwitchScope
{
    /// <summary>Kill only platform-funded calls. BYOK continues.</summary>
    PlatformKeysOnly = 0,
    /// <summary>Hard-disable every AI call (including BYOK).</summary>
    AllCalls = 1,
}

/// <summary>
/// Quota plan attached to a subscription tier. Admins CRUD these via
/// <c>/v1/admin/ai/plans</c>.
/// </summary>
[Index(nameof(Code), IsUnique = true)]
public class AiQuotaPlan
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Stable code, e.g. <c>starter</c>, <c>pro</c>, <c>enterprise</c>.
    /// Match this against billing plan codes to auto-attach at subscription
    /// creation time.</summary>
    [MaxLength(64)]
    public string Code { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    public AiQuotaPeriod Period { get; set; } = AiQuotaPeriod.Monthly;

    /// <summary>Primary cap. Semantic depends on <see cref="Period"/>.</summary>
    public int MonthlyTokenCap { get; set; }

    /// <summary>Per-day safety cap as a raw integer (not %). 0 = disabled.
    /// Applied on top of <see cref="MonthlyTokenCap"/>.</summary>
    public int DailyTokenCap { get; set; }

    /// <summary>Max concurrent in-flight requests per user. 0 = unlimited.</summary>
    public int MaxConcurrentRequests { get; set; }

    public AiQuotaRolloverPolicy RolloverPolicy { get; set; } = AiQuotaRolloverPolicy.Expire;

    /// <summary>If <see cref="RolloverPolicy"/> is <c>RolloverCapped</c>,
    /// max rollover as % of <see cref="MonthlyTokenCap"/>.</summary>
    public int RolloverCapPct { get; set; } = 20;

    public AiOveragePolicy OveragePolicy { get; set; } = AiOveragePolicy.Deny;

    /// <summary>Price per 1,000 tokens when <see cref="OveragePolicy"/> is
    /// <c>AllowWithCharge</c>. Stored as decimal to avoid FP drift.</summary>
    public decimal? OverageRatePer1kTokens { get; set; }

    /// <summary>If <see cref="OveragePolicy"/> is <c>AutoUpgrade</c>, the
    /// plan code to upgrade to.</summary>
    [MaxLength(64)]
    public string? AutoUpgradeTargetPlanCode { get; set; }

    /// <summary>If <see cref="OveragePolicy"/> is <c>DegradeToSmallerModel</c>,
    /// the fallback model identifier.</summary>
    [MaxLength(128)]
    public string? DegradeModel { get; set; }

    /// <summary>Comma-separated allow-list of feature codes (see
    /// <c>AiFeatureCodes</c>). Empty = all features permitted.</summary>
    [MaxLength(1024)]
    public string AllowedFeaturesCsv { get; set; } = string.Empty;

    /// <summary>Comma-separated allow-list of model IDs. Empty = all models.</summary>
    [MaxLength(1024)]
    public string AllowedModelsCsv { get; set; } = string.Empty;

    /// <summary>Is this plan active? Soft-delete flag.</summary>
    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Running counter row, one per (user, period-key). Updated inside the same
/// transaction as the gateway call to keep quota enforcement correct under
/// contention. A concurrency token on <see cref="RowVersion"/> guards against
/// lost updates when two calls race.
/// </summary>
[Index(nameof(UserId), nameof(PeriodKey), IsUnique = true)]
public class AiQuotaCounter
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>Kind of period this counter tracks: <c>month:2026-04</c>,
    /// <c>day:2026-04-18</c>, <c>week:2026-W16</c>.</summary>
    [MaxLength(32)]
    public string PeriodKey { get; set; } = default!;

    public int TokensUsed { get; set; }
    public int RequestsCount { get; set; }
    public decimal CostAccumulatedUsd { get; set; }

    public DateTimeOffset LastUpdatedAt { get; set; }

    [ConcurrencyCheck]
    public int RowVersion { get; set; }
}

/// <summary>
/// Per-user override row (§1 <c>AiCredentialMode</c>, per-user quota uplift,
/// feature overrides). One row per user; absence means "use plan defaults".
/// </summary>
public class AiUserQuotaOverride
{
    [Key]
    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>If non-null, overrides the plan's monthly cap for this user.</summary>
    public int? MonthlyTokenCapOverride { get; set; }

    /// <summary>If non-null, overrides the plan's daily cap.</summary>
    public int? DailyTokenCapOverride { get; set; }

    /// <summary>Plan code to use regardless of subscription. Admin grant.</summary>
    [MaxLength(64)]
    public string? ForcePlanCode { get; set; }

    /// <summary>Temporary disable switch for this specific user.</summary>
    public bool AiDisabled { get; set; }

    [MaxLength(512)]
    public string? Reason { get; set; }

    [MaxLength(64)]
    public string? GrantedByAdminId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>
/// Singleton row (id = <c>global</c>). Wraps §7 "global safety controls".
/// </summary>
public class AiGlobalPolicy
{
    [Key]
    [MaxLength(32)]
    public string Id { get; set; } = "global";

    public bool KillSwitchEnabled { get; set; }
    public AiKillSwitchScope KillSwitchScope { get; set; } = AiKillSwitchScope.PlatformKeysOnly;

    [MaxLength(256)]
    public string? KillSwitchReason { get; set; }

    /// <summary>
    /// CSV list of specific feature codes to disable (e.g.
    /// <c>conversation.evaluation,speaking.grade</c>). Empty = no per-feature
    /// disable. Calls whose feature code appears here are refused with
    /// <c>feature_disabled</c> before any quota / BYOK check, regardless of
    /// the global kill-switch state.
    /// </summary>
    [MaxLength(1024)]
    public string DisabledFeaturesCsv { get; set; } = string.Empty;

    public decimal MonthlyBudgetUsd { get; set; }
    public int SoftWarnPct { get; set; } = 80;
    public int HardKillPct { get; set; } = 100;
    public decimal CurrentSpendUsd { get; set; }

    /// <summary>§1 <c>AllowByokOnScoringFeatures</c>.</summary>
    public bool AllowByokOnScoringFeatures { get; set; } = false;

    /// <summary>§1 <c>AllowByokOnNonScoringFeatures</c>.</summary>
    public bool AllowByokOnNonScoringFeatures { get; set; } = true;

    /// <summary>§1 <c>DefaultPlatformProviderId</c>. Must match an active
    /// <see cref="AiProvider"/> row.</summary>
    [MaxLength(64)]
    public string DefaultPlatformProviderId { get; set; } = "digitalocean-serverless";

    /// <summary>§6 <c>ByokErrorCooldownHours</c>.</summary>
    public int ByokErrorCooldownHours { get; set; } = 24;

    /// <summary>§6 <c>ByokTransientRetryCount</c>.</summary>
    public int ByokTransientRetryCount { get; set; } = 2;

    /// <summary>§7 <c>AiAnomalyDetectionEnabled</c>.</summary>
    public bool AnomalyDetectionEnabled { get; set; } = true;

    /// <summary>§7 <c>AnomalyMultiplierX</c>.</summary>
    public decimal AnomalyMultiplierX { get; set; } = 10m;

    /// <summary>Optimistic-concurrency guard for admin edits.</summary>
    [ConcurrencyCheck]
    public int RowVersion { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }
}
