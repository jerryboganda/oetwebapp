using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain;

// OET Speaking module — double-marking + senior moderation closure
// (Developer Implementation Notes §15.4 / §15.5, table speaking_moderation).
//
// EXAM-INTEGRITY INVARIANT:
//   The ordinary tutor flow (AI advisory + ONE primary human assessor) is
//   unchanged. A moderation case is the *exception* path opened when a tutor
//   escalates a session ("send to moderation"), when AI↔tutor divergence is
//   wide, or when a learner dispute is raised. The case adds a second
//   independent human assessor and — when the two human markers diverge beyond
//   the configured threshold — a senior moderator who records the reconciled
//   final. Separation of duties is enforced in the service: the second marker
//   must differ from the first, and the moderator must differ from both.
//
// Each marker's score is persisted as its own strictly-separate
// <see cref="SpeakingTutorAssessment"/> row (distinguished by
// <see cref="SpeakingTutorAssessment.MarkerRole"/>), exactly mirroring the
// Writing module's double-marking design (writing_moderation). This row is the
// coordinating record that tracks the lifecycle and the variance between the
// two human markers.

/// <summary>
/// Double-marking + senior moderation record for a finished speaking session.
/// One row per session (unique index on <see cref="SpeakingSessionId"/>).
/// </summary>
[Index(nameof(SpeakingSessionId), IsUnique = true)]
[Index(nameof(Status))]
public class SpeakingModerationCase
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SpeakingSessionId { get; set; } = default!;

    public SpeakingSession? SpeakingSession { get; set; }

    /// <summary>Why the case was opened: <c>tutor_request</c> |
    /// <c>wide_divergence</c> | <c>dispute</c>.</summary>
    [MaxLength(24)]
    public string Reason { get; set; } = "tutor_request";

    // ── First (primary) human marker ────────────────────────────────────
    [MaxLength(64)]
    public string? FirstMarkerId { get; set; }

    /// <summary>FK to the primary tutor's <see cref="SpeakingTutorAssessment"/>.</summary>
    [MaxLength(64)]
    public string? FirstAssessmentId { get; set; }

    public string? FirstScoreJson { get; set; }

    // ── Second (independent) human marker ───────────────────────────────
    [MaxLength(64)]
    public string? SecondMarkerId { get; set; }

    [MaxLength(64)]
    public string? SecondAssessmentId { get; set; }

    public string? SecondScoreJson { get; set; }

    // ── Senior moderator ────────────────────────────────────────────────
    [MaxLength(64)]
    public string? ModeratorId { get; set; }

    [MaxLength(64)]
    public string? FinalAssessmentId { get; set; }

    public string? FinalScoreJson { get; set; }

    /// <summary>Absolute scaled-score delta between the first and second human
    /// markers. Drives the auto-finalize vs escalate decision.</summary>
    public int? VariancePoints { get; set; }

    [MaxLength(500)]
    public string? VarianceReason { get; set; }

    [MaxLength(1000)]
    public string? FinalDecisionNote { get; set; }

    /// <summary>When true the moderator requested the learner reattempt the
    /// session rather than releasing a final score.</summary>
    public bool RequestReattempt { get; set; }

    /// <summary>
    /// pending_second | pending_moderation | finalized | reattempt_requested.
    /// </summary>
    [MaxLength(24)]
    public string Status { get; set; } = "pending_second";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
