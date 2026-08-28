using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// the versioned, DB-overridable feature policy registry. Every AI call must
/// resolve to exactly one active <see cref="AiFeaturePolicy"/> row (or a
/// static, code-defined fallback — see
/// <c>Services/Rulebook/AiFeaturePolicyRegistry.cs</c>) before a provider is
/// ever selected.
///
/// <para>
/// Mirrors the existing <c>AiFeatureRoutes</c> DB-override-with-static-
/// fallback pattern (see <c>Services/Rulebook/AiFeatureRouteResolver.cs</c>):
/// admins can register a new policy version for a feature via a future admin
/// surface, and the registry always resolves the highest
/// <see cref="PolicyVersion"/> that is <see cref="IsActive"/> and currently
/// effective. Old versions are never mutated in place — a policy change is a
/// new row with a higher <see cref="PolicyVersion"/>, so the audit trail of
/// "what governed this call" never rewrites history.
/// </para>
/// </summary>
[Index(nameof(FeatureCode), nameof(PolicyVersion), IsUnique = true, Name = "UX_AiFeaturePolicies_FeatureCode_PolicyVersion")]
public class AiFeaturePolicy
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Stable feature identifier — matches <see cref="AiFeatureCodes"/> /
    /// <c>Services/Rulebook/AiFeatureRouteResolver.SpeakingAiFeatureCodes</c>.</summary>
    [MaxLength(64)]
    public string FeatureCode { get; set; } = default!;

    /// <summary>Coarse module grouping, e.g. <c>writing</c>, <c>listening</c>,
    /// <c>admin</c>. Mirrors <see cref="AiOperation.Module"/>.</summary>
    [MaxLength(32)]
    public string Module { get; set; } = default!;

    public AiOperationClass OperationClass { get; set; } = AiOperationClass.InteractiveLearning;

    /// <summary>Whether this policy version is a candidate for resolution.
    /// Deactivating a row (rather than deleting it) preserves the historical
    /// record of what a past call's policy trace referred to.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Monotonically increasing per <see cref="FeatureCode"/>. The
    /// registry always resolves the highest active, currently-effective
    /// version — never an older one, even if it was reactivated.</summary>
    public int PolicyVersion { get; set; } = 1;

    /// <summary>When true (the default), the gateway's existing grounding
    /// invariant applies to this feature. False only for the direct
    /// (non-gateway) OCR/STT/Listening-Part-A/B/C calls that never carry a
    /// <c>RulebookPromptBuilder</c> system prompt — see
    /// <c>docs/AI-USAGE-POLICY.md</c> §5 "Direct (non-gateway) AI calls".</summary>
    public bool RequiresGrounding { get; set; } = true;

    /// <summary>Comma-separated list of the canonical-key dimensions this
    /// feature's idempotency key is sensitive to, in
    /// <see cref="OetLearner.Api.Services.Ai.AiIdempotencyKeyBuilder"/> order.
    /// Null means "use the builder's default dimension set".</summary>
    [MaxLength(256)]
    public string? CacheDimensions { get; set; }

    /// <summary>Instant this policy version starts governing new calls.</summary>
    public DateTimeOffset EffectiveFrom { get; set; }

    /// <summary>Instant this policy version stops governing new calls. Null
    /// means "still current".</summary>
    public DateTimeOffset? EffectiveTo { get; set; }

    [MaxLength(128)]
    public string? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
