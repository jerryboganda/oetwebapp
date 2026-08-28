using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// One row per provider attempt made against an <see cref="AiOperation"/>.
/// This plain, non-partitioned table is the authoritative structural
/// guarantee behind "exactly one provider attempt is ever in flight per
/// operation at a time" — the composite primary key
/// (<see cref="OperationId"/>, <see cref="AttemptNumber"/>) makes a second
/// worker racing to record the same attempt number fail at the database
/// rather than relying on application-level locking alone.
///
/// <para>
/// W1 scope is schema-only: no worker writes this table yet. It exists so a
/// later wave can wire the retry loop to it without a further migration.
/// </para>
/// </summary>
public class AiOperationAttempt
{
    [MaxLength(64)]
    public string OperationId { get; set; } = default!;

    /// <summary>1-based attempt ordinal for this operation.</summary>
    public int AttemptNumber { get; set; }

    /// <summary>The <see cref="AiUsageRecord"/> row this attempt produced,
    /// once one exists. Null while the attempt is still in flight.</summary>
    [MaxLength(64)]
    public string? AiUsageRecordId { get; set; }

    /// <summary>Whether a provider was actually contacted for this attempt
    /// (false when the attempt was refused pre-flight, e.g. budget/quota
    /// denial, and never reached a provider).</summary>
    public bool ProviderInvoked { get; set; }

    /// <summary>Provider-side request/correlation id, when the provider
    /// returns one. Lets support correlate a call with the vendor's logs.</summary>
    [MaxLength(128)]
    public string? ProviderRequestId { get; set; }

    /// <summary>Raw HTTP status the provider returned, when applicable.</summary>
    public int? ProviderHttpStatus { get; set; }

    /// <summary>Short machine reason this attempt is being retried (e.g.
    /// <c>provider_429</c>, <c>timeout</c>, <c>transient_5xx</c>). Null on
    /// the final/successful attempt.</summary>
    [MaxLength(64)]
    public string? RetryReason { get; set; }

    /// <summary>Coarse error classification for analytics, independent of
    /// the free-text provider error message (kept on <see cref="AiUsageRecord"/>).</summary>
    [MaxLength(64)]
    public string? ErrorClass { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Navigation back to the owning operation.</summary>
    public AiOperation? Operation { get; set; }
}
