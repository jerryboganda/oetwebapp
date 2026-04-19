using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Immutable record of a pronunciation assessment. Produced by the ASR pipeline
/// (or the speaking-review bridge). Joined by <see cref="PronunciationAttempt"/>
/// for the upload lifecycle.
/// </summary>
[Index(nameof(UserId), nameof(CreatedAt))]
[Index(nameof(DrillId))]
public class PronunciationAssessment
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>FK to the drill. Nullable so the speaking-review bridge can
    /// produce assessments with no drill context.</summary>
    [MaxLength(64)]
    public string? DrillId { get; set; }

    [MaxLength(64)]
    public string? AttemptId { get; set; }                 // PronunciationAttempt.Id or Speaking attempt id

    [MaxLength(64)]
    public string? ConversationSessionId { get; set; }

    public double AccuracyScore { get; set; }              // 0-100
    public double FluencyScore { get; set; }               // 0-100
    public double CompletenessScore { get; set; }          // 0-100
    public double ProsodyScore { get; set; }               // 0-100
    public double OverallScore { get; set; }               // 0-100

    /// <summary>Speaking-band projection as of assessment creation.
    /// Redundant with a call to <c>OetScoring.PronunciationProjectedScaled(OverallScore)</c>
    /// but persisted so history reads don't recompute.</summary>
    public int ProjectedSpeakingScaled { get; set; }

    [MaxLength(4)]
    public string ProjectedSpeakingGrade { get; set; } = "B";

    public string WordScoresJson { get; set; } = "[]";     // Per-word scores with phoneme breakdown
    public string ProblematicPhonemesJson { get; set; } = "[]";
    public string FluencyMarkersJson { get; set; } = "{}"; // Pause locations, speech rate
    public string FindingsJson { get; set; } = "[]";       // Rule-cited findings from grounded AI
    public string FeedbackJson { get; set; } = "{}";       // summary/strengths/improvements/nextDrill

    [MaxLength(32)]
    public string Provider { get; set; } = "mock";         // azure | whisper | mock | speaking-review

    [MaxLength(32)]
    public string RulebookVersion { get; set; } = "1.0.0";

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Tracks the lifecycle of a single pronunciation attempt: audio upload →
/// ASR job → assessment. Separate from <see cref="PronunciationAssessment"/>
/// so we can expose upload status to the learner without exposing scoring detail.
/// </summary>
[Index(nameof(UserId), nameof(CreatedAt))]
[Index(nameof(DrillId), nameof(CreatedAt))]
[Index(nameof(Status))]
public class PronunciationAttempt
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string DrillId { get; set; } = default!;

    /// <summary>Storage key for the audio blob. Resolved via IFileStorage.
    /// Cleared when the retention worker reaps it.</summary>
    [MaxLength(512)]
    public string? AudioStorageKey { get; set; }

    [MaxLength(64)]
    public string? AudioSha256 { get; set; }

    public long? AudioBytes { get; set; }

    [MaxLength(32)]
    public string? AudioMimeType { get; set; }

    public int? AudioDurationMs { get; set; }

    /// <summary>
    /// Upload-to-scoring lifecycle:
    /// - queued        → chunks uploaded, assessment job queued
    /// - processing    → ASR / grounded-AI in flight
    /// - completed     → PronunciationAssessment row created, see AssessmentId
    /// - failed        → see ErrorCode / ErrorMessage
    /// - refused       → blocked before processing (quota, kill-switch, invalid audio)
    /// </summary>
    [MaxLength(16)]
    public string Status { get; set; } = "queued";

    [MaxLength(64)]
    public string? AssessmentId { get; set; }

    [MaxLength(64)]
    public string? ErrorCode { get; set; }

    [MaxLength(512)]
    public string? ErrorMessage { get; set; }

    [MaxLength(32)]
    public string? Provider { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Audio storage-retention deadline. The cleanup worker deletes
    /// the storage blob (but not the scoring row) once this passes.</summary>
    public DateTimeOffset? AudioReapAt { get; set; }
}

public class PronunciationDrill
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(32)]
    public string TargetPhoneme { get; set; } = default!;  // IPA symbol, or "stress"/"intonation"/"cluster"

    [MaxLength(128)]
    public string Label { get; set; } = default!;          // "th (as in 'think')"

    /// <summary>Profession tag. Optional ("all" = cross-profession). Defaults to "all".</summary>
    [MaxLength(48)]
    public string Profession { get; set; } = "all";

    /// <summary>Drill focus category: phoneme | cluster | stress | intonation | prosody | discrimination.</summary>
    [MaxLength(24)]
    public string Focus { get; set; } = "phoneme";

    /// <summary>Optional rulebook rule ID (e.g. "P01.1") that this drill primarily
    /// targets. Referenced in grounded AI prompts so the feedback is consistent.</summary>
    [MaxLength(16)]
    public string? PrimaryRuleId { get; set; }

    public string ExampleWordsJson { get; set; } = "[]";   // Words containing this phoneme
    public string MinimalPairsJson { get; set; } = "[]";   // Minimal pair exercises (ship/sheep)
    public string SentencesJson { get; set; } = "[]";      // Practice sentences

    /// <summary>Raw URL form of the model audio. Retained for back-compat with
    /// v1 callers but all new drills should set <see cref="AudioModelAssetId"/> instead.</summary>
    [MaxLength(512)]
    public string? AudioModelUrl { get; set; }

    /// <summary>FK into MediaAsset for content-addressed model audio. Preferred
    /// over <see cref="AudioModelUrl"/>. When both are present, asset wins.</summary>
    [MaxLength(64)]
    public string? AudioModelAssetId { get; set; }

    public string TipsHtml { get; set; } = string.Empty;   // Articulation tips (sanitized, admin-authored)

    [MaxLength(16)]
    public string Difficulty { get; set; } = "medium";

    [MaxLength(16)]
    public string Status { get; set; } = "active";         // active | archived | draft

    /// <summary>Sort position within a difficulty + profession bucket. Lower = earlier.</summary>
    public int OrderIndex { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Rolling per-user, per-phoneme progress. Driven by attempts and the
/// speaking-review bridge.
/// </summary>
[Index(nameof(UserId), nameof(PhonemeCode), IsUnique = true)]
[Index(nameof(UserId), nameof(AverageScore))]
public class LearnerPronunciationProgress
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(32)]
    public string PhonemeCode { get; set; } = default!;

    public double AverageScore { get; set; }               // Rolling average (0-100)
    public int AttemptCount { get; set; }
    public string ScoreHistoryJson { get; set; } = "[]";   // Last 20 scores
    public DateTimeOffset LastPracticedAt { get; set; }

    /// <summary>Spaced-repetition: when this phoneme is next eligible for review.
    /// Driven by <c>PronunciationSchedulerService</c>.</summary>
    public DateTimeOffset? NextDueAt { get; set; }

    /// <summary>SM-2-lite interval in days (0 = new).</summary>
    public int IntervalDays { get; set; }

    /// <summary>Ease factor for SM-2-lite. Starts at 2.5.</summary>
    public double Ease { get; set; } = 2.5;
}

/// <summary>
/// Minimal-pair listening discrimination attempts. Separate table so scoring
/// doesn't pollute phoneme-level articulation progress.
/// </summary>
[Index(nameof(UserId), nameof(CreatedAt))]
public class LearnerPronunciationDiscriminationAttempt
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string DrillId { get; set; } = default!;

    [MaxLength(32)]
    public string TargetPhoneme { get; set; } = default!;

    public int RoundsTotal { get; set; }
    public int RoundsCorrect { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

