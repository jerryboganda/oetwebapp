using System.ComponentModel.DataAnnotations;

namespace OetWithDrHesham.Api.Domain;

// ─────────────────────────────────────────────────────────────────────────────
// OET Writing exam-faithful closure (spec §4/§6/§9/§12/§14/§15/§17).
//
// These entities extend the existing Writing V2 module so that an admin-authored
// WritingScenario carries the full task definition (recipient, model answer,
// key/irrelevant content checklists), learner attempts emit a forensic event
// trail, and tutors can persist span-level annotations, double-mark, moderate,
// and gate result visibility. String status fields (not C# enums) are used to
// match the rest of the Writing V2 module (e.g. WritingSubmission.Status).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Forensic event trail for a Writing attempt (spec §17.7, table
/// writing_attempt_events). Captures the lifecycle and integrity signals
/// (paste, focus loss) for both paper and computer simulation modes.
/// </summary>
public class WritingAttemptEvent
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>Set once the submission row exists; null for pre-submit events.</summary>
    public Guid? SubmissionId { get; set; }

    /// <summary>Mock/diagnostic session id, or the scenario id for free practice.</summary>
    [MaxLength(64)]
    public string? SessionId { get; set; }

    public Guid? ScenarioId { get; set; }

    /// <summary>paper | computer.</summary>
    [MaxLength(16)]
    public string Mode { get; set; } = "computer";

    /// <summary>
    /// attempt_started | reading_started | reading_ended | writing_started |
    /// response_typed | auto_saved | paste | focus_lost | submit_clicked |
    /// timer_expired | attempt_locked.
    /// </summary>
    [MaxLength(32)]
    public string EventType { get; set; } = default!;

    public DateTimeOffset Timestamp { get; set; }

    public string PayloadJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Persisted span-level tutor annotation on a submission (spec §14.2 / §17.9,
/// table writing_feedback). Offset-based so the learner feedback screen can
/// re-render the highlight over the original letter content.
/// </summary>
public class WritingFeedbackAnnotation
{
    public Guid Id { get; set; }

    public Guid SubmissionId { get; set; }

    public Guid? ReviewId { get; set; }

    [MaxLength(64)]
    public string TutorId { get; set; } = default!;

    /// <summary>c1..c6, or null for a general annotation.</summary>
    [MaxLength(8)]
    public string? Criterion { get; set; }

    [MaxLength(2000)]
    public string HighlightedText { get; set; } = string.Empty;

    public int StartOffset { get; set; }

    public int EndOffset { get; set; }

    /// <summary>high | medium | low.</summary>
    [MaxLength(16)]
    public string Severity { get; set; } = "medium";

    [MaxLength(2000)]
    public string? Suggestion { get; set; }

    public string FeedbackText { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Double-marking + senior moderation record for a submission (spec §14.4 /
/// §17.10, table writing_moderation). Created when a scenario's MarkingMode is
/// "double"; escalates to a senior marker when first/second variance exceeds the
/// configured threshold.
/// </summary>
public class WritingModeration
{
    public Guid Id { get; set; }

    public Guid SubmissionId { get; set; }

    [MaxLength(64)]
    public string? FirstMarkerId { get; set; }

    [MaxLength(64)]
    public string? SecondMarkerId { get; set; }

    [MaxLength(64)]
    public string? SeniorMarkerId { get; set; }

    public string? FirstScoreJson { get; set; }

    public string? SecondScoreJson { get; set; }

    public string? FinalScoreJson { get; set; }

    /// <summary>Absolute raw-total delta between first and second markers.</summary>
    public int? VariancePoints { get; set; }

    [MaxLength(500)]
    public string? VarianceReason { get; set; }

    [MaxLength(1000)]
    public string? FinalDecisionNote { get; set; }

    /// <summary>
    /// pending_first | pending_second | pending_moderation | finalized.
    /// </summary>
    [MaxLength(24)]
    public string Status { get; set; } = "pending_first";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Admin-controlled gating of what a learner sees after submission (spec §15.1).
/// One global singleton (Id = "global"); optional per-scenario overrides keyed by
/// ScenarioId. Resolution prefers the scenario row, then the global default.
/// </summary>
public class WritingResultVisibilityConfig
{
    [MaxLength(64)]
    public string Id { get; set; } = "global";

    /// <summary>Null for the global default row.</summary>
    public Guid? ScenarioId { get; set; }

    public bool ShowSubmissionReceived { get; set; } = true;

    public bool ShowAiEstimate { get; set; }

    public bool ShowTutorScore { get; set; } = true;

    public bool ShowFullCriteria { get; set; } = true;

    public bool ShowAnnotatedResponse { get; set; } = true;

    public bool ShowMissingContent { get; set; } = true;

    public bool ShowModelAnswer { get; set; } = true;

    public bool ShowContentChecklist { get; set; } = true;

    public bool AllowRewrite { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; }
}
