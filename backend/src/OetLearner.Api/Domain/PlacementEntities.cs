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

/// <summary>
/// Admin-approved extra-time accommodation for a candidate's placement test.
/// Extra time is NEVER self-selectable: a grant exists only because an admin
/// created it, and the row records who approved it, when, and how much. At
/// most one grant per learner is active at a time (creating a new one
/// supersedes the previous — see the admin endpoints). A grant is active
/// while <see cref="RevokedAt"/> is null. <see cref="Reference"/> is an
/// administrative reference only (e.g. a ticket id) — never clinical or
/// health detail.
/// </summary>
[Index(nameof(LearnerUserId), nameof(ApprovedAt))]
public class PlacementAccommodation
{
    /// <summary><c>pacc_</c> + guid N.</summary>
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string LearnerUserId { get; set; } = default!;

    /// <summary>Extra time as a percentage of each timed unit (1..100).</summary>
    public int ExtraTimePercent { get; set; }

    [MaxLength(200)]
    public string? Reference { get; set; }

    [MaxLength(64)]
    public string ApprovedByUserId { get; set; } = default!;

    [MaxLength(128)]
    public string ApprovedByName { get; set; } = default!;

    public DateTimeOffset ApprovedAt { get; set; }

    [MaxLength(64)]
    public string? RevokedByUserId { get; set; }

    [MaxLength(128)]
    public string? RevokedByName { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary><c>"superseded"</c> when replaced by a newer grant, otherwise
    /// the admin's free-text reason.</summary>
    [MaxLength(200)]
    public string? RevokedReason { get; set; }
}

/// <summary>
/// Which placement session (assessment attempt) actually ran with a given
/// accommodation. One row per engine session (unique <see cref="SessionId"/>);
/// <see cref="ExtraTimePercent"/> is a snapshot so later grant changes never
/// rewrite what a past attempt received.
/// </summary>
[Index(nameof(SessionId), IsUnique = true)]
public class PlacementAccommodationUse
{
    /// <summary><c>pacu_</c> + guid N.</summary>
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AccommodationId { get; set; } = default!;

    [MaxLength(64)]
    public string LearnerUserId { get; set; } = default!;

    /// <summary>Session id on the private assessment engine (<c>ses_…</c>).</summary>
    [MaxLength(64)]
    public string SessionId { get; set; } = default!;

    public int ExtraTimePercent { get; set; }

    public DateTimeOffset AppliedAt { get; set; }
}
