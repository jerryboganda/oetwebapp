using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Durable circuit-breaker row for one credential or provider. W3 of the AI
/// cost/reliability remediation (incident INC-2026-CLAUDE-01): a credential
/// that starts returning 401/403/402 (or an invalid model/config) must stop
/// being sent traffic for a cool-down, and a provider that trips a short
/// failure window must open so retries cannot storm a dying upstream.
///
/// <para>
/// <see cref="Kind"/> is <c>credential</c> or <c>provider</c>.
/// <see cref="State"/> is <c>closed</c>, <c>open</c>, or <c>half_open</c>.
/// Uniqueness is on (<see cref="Kind"/>, <see cref="Key"/>).
/// </para>
/// </summary>
public class AiCircuitState
{
    [Key]
    [MaxLength(128)]
    public string Id { get; set; } = default!;

    /// <summary><c>credential</c> or <c>provider</c>.</summary>
    [MaxLength(32)]
    public string Kind { get; set; } = default!;

    [MaxLength(128)]
    public string Key { get; set; } = default!;

    /// <summary><c>closed</c>, <c>open</c>, or <c>half_open</c>.</summary>
    [MaxLength(16)]
    public string State { get; set; } = default!;

    public int FailureCount { get; set; }

    public DateTimeOffset? OpenedAt { get; set; }

    public DateTimeOffset? OpenUntil { get; set; }

    public DateTimeOffset? LastFailureAt { get; set; }

    [MaxLength(64)]
    public string? LastFailureCode { get; set; }

    /// <summary>When <see cref="State"/> is <c>half_open</c>, exactly one
    /// probe may be in flight. Further callers are denied until the probe
    /// records success (close) or failure (re-open).</summary>
    public bool ProbeInFlight { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
