using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// OET-owned record of one completed placement-test session run on the
/// private GEPA engine. The learner's durable, account-linked result
/// history lives HERE — the engine's own retention may expire recordings
/// and (for sessions without results) prune its rows, but this table is
/// the candidate's verifiable history. One row per engine session.
/// </summary>
[Index(nameof(LearnerUserId), nameof(CreatedAt))]
[Index(nameof(SessionId), IsUnique = true)]
public class PlacementResult
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string LearnerUserId { get; set; } = default!;

    /// <summary>Session id on the private assessment engine
    /// (<c>ses_…</c>), unique per placement attempt.</summary>
    [MaxLength(64)]
    public string SessionId { get; set; } = default!;

    /// <summary>Routing-ruleset version the session ran under
    /// (provenance — what the result was measured with).</summary>
    [MaxLength(32)]
    public string RulesetVersion { get; set; } = default!;

    /// <summary>The engine's assembled result report, stored verbatim as
    /// JSON (skill-first profile: per-skill CEFR status/band, headline,
    /// confidence, readiness layer).</summary>
    public string ResultJson { get; set; } = default!;

    /// <summary><c>"completed"</c> when all four skills are measured;
    /// <c>"partial"</c> when the learner stopped early (uneven profiles
    /// are first-class results, never failures).</summary>
    [MaxLength(16)]
    public string Status { get; set; } = default!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
