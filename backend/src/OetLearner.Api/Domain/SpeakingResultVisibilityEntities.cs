using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// WS6 — admin-controlled gating of what a learner sees on the Speaking
/// result/result-card surface (Developer Implementation Notes §10).
///
/// Mirrors <see cref="WritingResultVisibilityConfig"/>: a single global
/// singleton row (<c>Id == "global"</c>) supplies the defaults; an optional
/// per-role-play-card override row stands in for that card. Resolution prefers
/// the card override, then the global default. Booleans have no tri-state, so
/// an existing override row fully represents that card's effective config.
///
/// Speaking results are advisory until an official assessor finalises them, so
/// the AI estimate and readiness band always carry an "estimate, not official"
/// warning on the surface regardless of these flags.
/// </summary>
public class SpeakingResultVisibilityConfig
{
    [MaxLength(64)]
    public string Id { get; set; } = "global";

    /// <summary>Null for the global default row; otherwise the
    /// <c>RolePlayCard.Id</c> this override applies to.</summary>
    [MaxLength(64)]
    public string? RolePlayCardId { get; set; }

    /// <summary>"We received your recording" confirmation.</summary>
    public bool ShowSubmissionReceived { get; set; } = true;

    /// <summary>Advisory AI scaled-score estimate (0–500).</summary>
    public bool ShowAiEstimate { get; set; } = true;

    /// <summary>Readiness band (Not ready / Developing / Borderline /
    /// Exam-ready / Strong) derived from the estimate.</summary>
    public bool ShowReadinessBand { get; set; } = true;

    /// <summary>Authoritative tutor / assessor score once marked.</summary>
    public bool ShowTutorScore { get; set; } = true;

    /// <summary>Per-criterion breakdown (9 linguistic + clinical criteria).</summary>
    public bool ShowFullCriteria { get; set; } = true;

    /// <summary>Role-play transcript playback.</summary>
    public bool ShowTranscript { get; set; } = true;

    /// <summary>Timestamped assessor comments on the transcript.</summary>
    public bool ShowTutorComments { get; set; } = true;

    /// <summary>AI/tutor recommended follow-up drills.</summary>
    public bool ShowRecommendedDrills { get; set; } = true;

    /// <summary>Whether the learner may re-attempt the role-play.</summary>
    public bool AllowReattempt { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; }
}
