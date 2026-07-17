using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain;

// Phase 5 of the OET Speaking module roadmap.
//
// `SpeakingDrillItem` is a small, single-focus practice item (a "drill")
// targeting one or two assessment criteria. Drills are typed (`Opening`,
// `Empathy`, `LayLanguage`, …) so the recommender can map a low score on
// a specific criterion to the right kind of practice.
//
// `SpeakingDrillAttempt` is one learner attempt against a drill item.
// Tracks the audio + transcript + AI feedback for the attempt and is the
// row that powers the "did the drill move the needle?" analytics.

public enum SpeakingDrillKind
{
    Opening = 0,
    Empathy = 1,
    Ice = 2,
    OpenQuestion = 3,
    LayLanguage = 4,
    Signposting = 5,
    CheckingUnderstanding = 6,
    Reassurance = 7,
    Closing = 8,
    Pronunciation = 9,
    Fluency = 10,
    Grammar = 11,
}

public enum SpeakingDrillAttemptSource
{
    RecommendedPostAssessment = 0,
    ManualBrowse = 1,
    LearningPathStage = 2,
}

[Index(nameof(DrillKind))]
public class SpeakingDrillItem
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>FK to the underlying `ContentItem` that holds shared
    /// concerns (publish status, profession scoping, exam family).</summary>
    [MaxLength(64)]
    public string ContentItemId { get; set; } = default!;

    public ContentItem? ContentItem { get; set; }

    public SpeakingDrillKind DrillKind { get; set; }

    /// <summary>JSON array of criterion codes this drill targets (e.g.
    /// `["informationGiving","layLanguage"]`).</summary>
    public string TargetCriteriaJson { get; set; } = "[]";

    /// <summary>Recommend this drill to learners whose latest session
    /// score on the target criterion is below this threshold (0..6).
    /// Null = always available, never auto-recommended.</summary>
    public int? RecommendedAfterSessionScoreBelow { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

[Index(nameof(UserId), nameof(DrillItemId))]
public class SpeakingDrillAttempt
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string DrillItemId { get; set; } = default!;

    public SpeakingDrillItem? DrillItem { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>0..6 mini-band on the drill's primary criterion. Null
    /// until the attempt is scored.</summary>
    public int? Score { get; set; }

    /// <summary>Optional FK to a `SpeakingRecording` row holding the
    /// drill audio.</summary>
    [MaxLength(64)]
    public string? AudioRecordingId { get; set; }

    /// <summary>Optional FK to a `SpeakingTranscript` row holding the
    /// drill transcript.</summary>
    [MaxLength(64)]
    public string? TranscriptId { get; set; }

    /// <summary>JSON blob of structured AI feedback ("you used 3 lay-
    /// language reformulations; try 2 more").</summary>
    public string AiFeedbackJson { get; set; } = "{}";

    public SpeakingDrillAttemptSource Source { get; set; } = SpeakingDrillAttemptSource.ManualBrowse;
}
