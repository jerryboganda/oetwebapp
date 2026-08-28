using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Platform-side spend tracking bucket for a scope + period. One row per
/// (<see cref="Scope"/>, <see cref="PeriodKey"/>) pair — a worker reserves
/// against <see cref="ReservedUsd"/> before calling a provider and commits
/// the actual cost into <see cref="CommittedUsd"/> once known, so budget
/// enforcement never depends on a slower <see cref="AiUsageRecord"/> read
/// path.
///
/// <para>
/// <see cref="Scope"/> is a small closed vocabulary of prefixes, e.g.
/// <c>global</c>, <c>user:{id}</c>, <c>tenant:{id}</c>, <c>feature:{code}</c> —
/// deliberately a string (not an FK) so new scopes can be introduced without
/// a schema change. <see cref="PeriodKey"/> follows the existing
/// <c>AiQuotaCounter.PeriodKey</c> convention: <c>day:yyyy-MM-dd</c> or
/// <c>month:yyyy-MM</c>.
/// </para>
///
/// <para>
/// Optimistic concurrency uses the PostgreSQL system column <c>xmin</c> (see
/// <c>LearnerDbContext.ConfigureXminToken</c>) rather than an app-managed row
/// version — two workers racing to reserve against the same period must not
/// silently overwrite each other's reservation.
/// </para>
///
/// <para>W1 scope is schema-only: no service reads or writes this table yet.</para>
/// </summary>
public class AiBudgetPeriod
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string Scope { get; set; } = default!;

    /// <summary><c>day:yyyy-MM-dd</c> or <c>month:yyyy-MM</c>.</summary>
    [MaxLength(16)]
    public string PeriodKey { get; set; } = default!;

    public decimal LimitUsd { get; set; }

    /// <summary>Sum of in-flight reservations against this period that have
    /// not yet been committed or released.</summary>
    public decimal ReservedUsd { get; set; }

    /// <summary>Sum of actual spend once attempts complete.</summary>
    public decimal CommittedUsd { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
