using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

// Phase 2 / Phase 4 of the OET Speaking module roadmap.
//
// The Speaking module ships *two* parallel assessment tracks per session:
//
//   * `SpeakingAiAssessment`     — produced automatically by the AI scorer
//                                  (Phase 2). Always advisory: never
//                                  surfaces as an official OET grade.
//   * `SpeakingTutorAssessment`  — produced by a human tutor (Phase 4).
//                                  Drives the final score shown to the
//                                  learner once `IsFinal` flips.
//
// `SpeakingTimestampedComment` is the shared timeline-anchored comment
// thread used by tutors and the AI to highlight specific moments in the
// audio / transcript. Both the learner UI and the tutor review console
// render the same comments.

[Index(nameof(SpeakingSessionId))]
public class SpeakingAiAssessment
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SpeakingSessionId { get; set; } = default!;

    public SpeakingSession? SpeakingSession { get; set; }

    /// <summary>FK to the `SpeakingTranscript` row this assessment scored.
    /// Snapshotted so re-transcribing later does not silently invalidate
    /// the AI score.</summary>
    [MaxLength(64)]
    public string TranscriptId { get; set; } = default!;

    [MaxLength(32)]
    public string Provider { get; set; } = default!;

    [MaxLength(96)]
    public string ModelId { get; set; } = default!;

    [MaxLength(64)]
    public string PromptTemplateId { get; set; } = "speaking.score.v2";

    // Linguistic criteria — 0..6 each (OET band).
    public int Intelligibility { get; set; }
    public int Fluency { get; set; }
    public int Appropriateness { get; set; }
    public int GrammarExpression { get; set; }

    // Clinical communication criteria — 0..3 each.
    public int RelationshipBuilding { get; set; }
    public int PatientPerspective { get; set; }
    public int Structure { get; set; }
    public int InformationGathering { get; set; }
    public int InformationGiving { get; set; }

    /// <summary>Practice-only scaled score; never an official OET grade.</summary>
    public int EstimatedScaledScore { get; set; }

    [MaxLength(32)]
    public string ReadinessBand { get; set; } = default!;

    /// <summary>JSON object keyed by criterion code with the rationale the
    /// AI surfaced ("why this score"). Used by the per-criterion drawer in
    /// the learner UI.</summary>
    public string PerCriterionRationalesJson { get; set; } = "{}";

    [MaxLength(4000)]
    public string OverallSummary { get; set; } = string.Empty;

    /// <summary>How confident the AI is in its own score (`low` | `medium`
    /// | `high`). Drives the "tutor review recommended" badge.</summary>
    [MaxLength(16)]
    public string ConfidenceBand { get; set; } = "medium";

    public DateTimeOffset GeneratedAt { get; set; }

    /// <summary>JSON array of rulebook entry codes the AI flagged as
    /// applicable (e.g. clinical-communication violations).</summary>
    public string RulebookFindingsJson { get; set; } = "[]";

    /// <summary>Always true in Phase 2/4 — AI scores never become the
    /// official grade; only tutor scores flip `IsFinal`.</summary>
    public bool IsAdvisory { get; set; } = true;
}

[Index(nameof(SpeakingSessionId), nameof(IsFinal))]
public class SpeakingTutorAssessment
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SpeakingSessionId { get; set; } = default!;

    public SpeakingSession? SpeakingSession { get; set; }

    [MaxLength(64)]
    public string TutorId { get; set; } = default!;

    /// <summary>Which marker track this row belongs to in the double-marking
    /// + moderation workflow: <c>primary</c> (default — the first independent
    /// human assessor and the only role the ordinary tutor flow ever writes),
    /// <c>second</c> (a distinct second independent assessor opened via a
    /// moderation case), or <c>moderated</c> (the senior moderator's reconciled
    /// final). The learner-facing dual projection prefers
    /// <c>moderated</c> &gt; <c>primary</c> when choosing the canonical tutor
    /// score, so the ordinary single-marker flow is unchanged.</summary>
    [MaxLength(16)]
    public string MarkerRole { get; set; } = "primary";

    // Linguistic criteria — 0..6 each (OET band).
    public int Intelligibility { get; set; }
    public int Fluency { get; set; }
    public int Appropriateness { get; set; }
    public int GrammarExpression { get; set; }

    // Clinical communication criteria — 0..3 each.
    public int RelationshipBuilding { get; set; }
    public int PatientPerspective { get; set; }
    public int Structure { get; set; }
    public int InformationGathering { get; set; }
    public int InformationGiving { get; set; }

    public int EstimatedScaledScore { get; set; }

    [MaxLength(32)]
    public string ReadinessBand { get; set; } = default!;

    /// <summary>Long-form markdown feedback shown to the learner. No
    /// MaxLength — tutors routinely paste in 2-3k words of analysis.</summary>
    public string OverallFeedbackMarkdown { get; set; } = string.Empty;

    public string StrengthsJson { get; set; } = "[]";

    public string ImprovementsJson { get; set; } = "[]";

    public string RecommendedDrillsJson { get; set; } = "[]";

    [MaxLength(1000)]
    public string RecommendedRulebookEntries { get; set; } = string.Empty;

    /// <summary>False while the tutor is still drafting. Flips to true on
    /// the explicit "submit" action, at which point the learner UI starts
    /// surfacing the score.</summary>
    public bool IsFinal { get; set; } = false;

    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>How long the tutor spent on the review (sum of foreground
    /// time on the marking screen). Drives marker analytics.</summary>
    public int MarkingDurationSeconds { get; set; }

    /// <summary>JSON delta between the AI score and the tutor's score on
    /// each criterion. Null until calibration runs on this session.</summary>
    public string? CalibrationDeltaJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

[Index(nameof(SpeakingSessionId), nameof(StartMs))]
public class SpeakingTimestampedComment
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SpeakingSessionId { get; set; } = default!;

    public SpeakingSession? SpeakingSession { get; set; }

    [MaxLength(64)]
    public string AuthorId { get; set; } = default!;

    /// <summary>One of `tutor` | `ai` | `learner` | `admin`.</summary>
    [MaxLength(16)]
    public string AuthorRole { get; set; } = default!;

    /// <summary>Index into `SpeakingTranscript.SegmentsJson` for fast
    /// rendering without having to recompute timestamp-to-segment
    /// alignment.</summary>
    public int TranscriptSegmentIndex { get; set; }

    public int StartMs { get; set; }

    public int EndMs { get; set; }

    /// <summary>One of the assessment criterion codes (e.g.
    /// `informationGiving`, `patientPerspective`) or `general`.</summary>
    [MaxLength(48)]
    public string CriterionCode { get; set; } = default!;

    /// <summary>One of `info` | `praise` | `minor` | `major`.</summary>
    [MaxLength(16)]
    public string Severity { get; set; } = "info";

    [MaxLength(4000)]
    public string BodyMarkdown { get; set; } = string.Empty;

    /// <summary>Optional FK code into the rulebook entries (e.g.
    /// `cc.empathy.acknowledge`).</summary>
    [MaxLength(64)]
    public string? LinkedRulebookEntryCode { get; set; }

    /// <summary>Optional FK to a `SpeakingDrillItem` the comment
    /// recommends.</summary>
    [MaxLength(64)]
    public string? LinkedDrillId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
