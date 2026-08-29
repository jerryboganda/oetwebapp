using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Admin-authored, time-bounded addition to a budget scope's ceiling. Never
/// silently raises a cap: an override exists only when an admin supplied a
/// positive <see cref="AmountUsd"/>, a non-empty <see cref="Reason"/>, and a
/// future <see cref="ExpiresAt"/>. Revocation is a separate write
/// (<see cref="RevokedAt"/>) so the audit trail is never rewritten.
/// </summary>
public class AiBudgetOverride
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Budget scope this override applies to, e.g. <c>global</c> or
    /// <c>class:ScoringCritical</c>.</summary>
    [MaxLength(64)]
    public string Scope { get; set; } = default!;

    public decimal AmountUsd { get; set; }

    [MaxLength(512)]
    public string Reason { get; set; } = default!;

    [MaxLength(64)]
    public string ActorAdminId { get; set; } = default!;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    [MaxLength(64)]
    public string? RevokedByAdminId { get; set; }
}

/// <summary>
/// One fired threshold on a (scope, period) budget. Insert-once via
/// <c>ON CONFLICT DO NOTHING</c> so concurrent commits cannot double-page
/// the same 50/75/90/100 rung.
/// </summary>
public class AiBudgetAlert
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string Scope { get; set; } = default!;

    /// <summary><c>day:yyyy-MM-dd</c> or <c>month:yyyy-MM</c>.</summary>
    [MaxLength(16)]
    public string PeriodKey { get; set; } = default!;

    public int ThresholdPct { get; set; }

    public DateTimeOffset FiredAt { get; set; }
}
