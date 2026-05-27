using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Persistent state for a Writing diagnostic session. Replaces the
/// previous <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/>
/// backed store so a candidate who reloads mid-diagnostic does not lose
/// their reading-phase progress when the process restarts.
///
/// One row per <c>StartDiagnosticAsync</c> call. <see cref="ExpiresAt"/> is
/// always <c>StartedAt + 2h</c>; the cleanup cron deletes abandoned
/// (un-submitted) rows past that cutoff, while submitted rows are retained
/// for audit/forensics. All reads filter by <see cref="UserId"/> for
/// multi-tenant isolation.
/// </summary>
public class WritingDiagnosticSession
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public Guid ScenarioId { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? ReadingPhaseEndedAt { get; set; }

    public Guid? SubmissionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>UTC <c>StartedAt + 2h</c>. Matches the previous cache TTL.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
