using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Lifecycle of a learner credit reservation. Mirrors the reserve → commit /
/// release two-phase pattern already used for platform budget in
/// <see cref="AiBudgetPeriod"/>, so a denied or abandoned operation never
/// leaves a partial debit behind.
/// </summary>
public enum AiCreditReservationState
{
    /// <summary>Units are held against the user's balance but not yet spent.</summary>
    Reserved = 0,
    /// <summary>The operation succeeded; the reservation converted into a
    /// permanent debit.</summary>
    Committed = 1,
    /// <summary>The operation ended without spending the reservation (denied,
    /// cancelled, or exhausted retries); the held units are returned.</summary>
    Released = 2,
}

/// <summary>
/// One row per learner-credit hold made on behalf of an <see cref="AiOperation"/>.
/// <see cref="BusinessReference"/> is the idempotency guard: retrying the
/// same operation attempt must resolve to the same reservation instead of
/// double-debiting the learner's credit balance.
///
/// <para><see cref="BucketKind"/> is a string rather than an enum so the
/// commercial credit-bucket taxonomy (Shared Credits vs the restricted
/// Flexible W/S pool — see
/// <c>docs/OET_2026_MASTER_CATALOGUE_AI_CREDITS_ACCESS.md</c>) can evolve
/// without a migration.</para>
///
/// <para>W1 scope is schema-only: no service reads or writes this table yet.</para>
/// </summary>
public class AiCreditReservation
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string OperationId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>Which credit bucket this reservation draws from, e.g.
    /// <c>shared</c> or <c>flexible_ws</c>.</summary>
    [MaxLength(32)]
    public string BucketKind { get; set; } = default!;

    /// <summary>Number of credit units held (bucket-specific denomination —
    /// always whole units, never fractional USD).</summary>
    public int Units { get; set; }

    public AiCreditReservationState State { get; set; } = AiCreditReservationState.Reserved;

    /// <summary>Idempotency guard: caller-derived reference (e.g.
    /// <c>{operationId}:{attemptNumber}</c>) that is unique per reservation
    /// attempt, so retries resolve to the same row instead of re-debiting.</summary>
    [MaxLength(128)]
    public string BusinessReference { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Navigation back to the owning operation.</summary>
    public AiOperation? Operation { get; set; }
}
