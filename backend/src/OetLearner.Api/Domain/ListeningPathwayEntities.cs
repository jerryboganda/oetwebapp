using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Module Pathway — Phase 1 Domain Entities
//
// 5-stage learning pathway: onboarding → diagnostic → foundation →
// practice → mastery. Mirrors the Reading pathway (ReadingPathwayEntities.cs)
// but for Listening: 8 sub-skills L1..L8, 4 target accents, audio-first UX,
// note-taking + spelling tolerance, accent-aware diagnostic and roadmap.
//
// Naming convention: types are *not* prefixed `Listening*` unless they would
// otherwise collide with an existing Listening entity (e.g. `ListeningPracticeSession`
// stays prefixed because the existing `ListeningAttempt`/`ListeningAnswer` path
// has different shape). Pathway-pure types use `LearnerListening*` to keep them
// distinct from the V2 attempt-level entities in `ListeningEntities.cs`.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Per-learner Listening profile captured during onboarding.</summary>
public class LearnerListeningProfile
{
    public Guid Id { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    [MaxLength(8)] public string TargetBand { get; set; } = default!;   // "B" | "B+" | "A"
    public DateTimeOffset? ExamDate { get; set; }
    public int HoursPerWeek { get; set; }
    [MaxLength(64)] public string Profession { get; set; } = default!;

    /// <summary>Typical English exposure source (american_tv | british_tv | both |
    /// australian | mixed | other). Drives accent-prep weighting.</summary>
    [MaxLength(32)] public string EnglishExposureSource { get; set; } = "mixed";

    public int ComfortBritish { get; set; }     // 1–5
    public int ComfortAustralian { get; set; }  // 1–5
    public int ComfortVarious { get; set; }     // 1–5

    public bool HasTakenBefore { get; set; }
    public int? PreviousScore { get; set; }

    public int SelfRatedSpeed { get; set; }       // 1–5
    public int SelfRatedNoteTaking { get; set; }  // 1–5
    public int SelfRatedSpelling { get; set; }    // 1–5

    /// <summary>onboarding | audio_check | diagnostic | foundation | practice | mastery</summary>
    [MaxLength(32)] public string CurrentStage { get; set; } = "onboarding";

    public int? CurrentReadinessScore { get; set; }
    public int? PredictedScore { get; set; }

    public DateTimeOffset OnboardingCompletedAt { get; set; }
    public DateTimeOffset? AudioCheckPassedAt { get; set; }
    public DateTimeOffset? PathwayGeneratedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Generated multi-week study pathway for a Listening learner.</summary>
public class LearnerListeningPathway
{
    public Guid Id { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    public int TotalWeeks { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    /// <summary>Serialized list of weekly plan items — phase, focus skills/accents,
    /// daily drill counts, mock cadence. See <c>ListeningPathwayGenerator</c>.</summary>
    public string WeeksJson { get; set; } = "[]";
}

/// <summary>One row per user per Listening sub-skill L1..L8 — rolling mastery score.</summary>
public class LearnerListeningSkillScore
{
    public Guid Id { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    /// <summary>L1=detail_capture, L2=note_taking_speed, L3=spelling_accuracy,
    /// L4=gist, L5=distractor_recognition, L6=inference, L7=speaker_stance,
    /// L8=accent_adaptation.</summary>
    [MaxLength(4)] public string SkillCode { get; set; } = default!;
    public decimal CurrentScore { get; set; }      // 0.00–10.00
    public decimal DiagnosticScore { get; set; }   // baseline set after diagnostic
    public int QuestionsAttempted { get; set; }
    public int QuestionsCorrect { get; set; }
    public DateTimeOffset LastPracticedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Per-user per-accent rolling competence profile (BR/AU/US/NN).</summary>
public class LearnerAccentProgress
{
    public Guid Id { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    /// <summary>british | australian | us | non_native</summary>
    [MaxLength(16)] public string Accent { get; set; } = default!;
    public decimal AccuracyPercentage { get; set; }  // 0.00–100.00
    public int QuestionsAttempted { get; set; }
    public int QuestionsCorrect { get; set; }
    public int MinutesListened { get; set; }
    public int SelfConfidenceRating { get; set; }    // 1–5 self-reported
    public DateTimeOffset LastPracticedAt { get; set; }
}

/// <summary>A single Listening practice/diagnostic/mock session.</summary>
public class ListeningPracticeSession
{
    public Guid Id { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    /// <summary>diagnostic | drill | mini_quiz | mock | wrong_review |
    /// dictation | accent_drill | pronunciation_review</summary>
    [MaxLength(32)] public string SessionType { get; set; } = default!;
    [MaxLength(4)] public string? FocusSkill { get; set; }
    [MaxLength(16)] public string? FocusAccent { get; set; }
    /// <summary>Ordered string[] of <see cref="ListeningQuestion.Id"/> values
    /// (string keys, max 64 chars). Stored as JSON.</summary>
    public string QuestionIdsJson { get; set; } = "[]";
    /// <summary>Ordered Guid[] of audio asset / extract IDs participating in this
    /// session — used for transcript fetch + replay scoping.</summary>
    public string AudioAssetIdsJson { get; set; } = "[]";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int? DurationSeconds { get; set; }
    public int? Score { get; set; }
    public int? TotalQuestions { get; set; }
    public string MetadataJson { get; set; } = "{}";
}

/// <summary>Fine-grained per-question attempt history including wrong-review state.</summary>
public class ListeningQuestionAttempt
{
    public Guid Id { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    /// <summary>FK to <see cref="ListeningQuestion.Id"/> — string (max 64) for
    /// consistency with the V2 schema. Diverges from spec §25.5 (Guid) deliberately.</summary>
    [MaxLength(64)] public string ListeningQuestionId { get; set; } = default!;
    public Guid? PracticeSessionId { get; set; }
    public Guid? AudioAssetId { get; set; }
    /// <summary>For MCQ: "A"/"B"/"C". For Part A gap-fill: free text up to 256 chars.</summary>
    [MaxLength(256)] public string? SelectedOption { get; set; }
    /// <summary>Verbatim learner text for Part A — preserved alongside SelectedOption
    /// so spelling-tolerance analytics can be replayed.</summary>
    public string? LearnerAnswer { get; set; }
    public bool IsCorrect { get; set; }
    public bool IsUnknown { get; set; }   // "I don't know" answer in diagnostic
    public bool IsSpellingCorrectMeaningWrong { get; set; }
    public bool IsMeaningCorrectSpellingWrong { get; set; }
    public int ReplaysUsed { get; set; }
    public int TimeSpentSeconds { get; set; }
    public bool MarkedForReview { get; set; }
    public string? NoteText { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public bool InReviewQueue { get; set; }
    public DateTimeOffset? NextReviewAt { get; set; }
    public int ReviewIntervalIndex { get; set; }  // 0=1day, 1=3days, 2=7days
    public int ConsecutiveCorrect { get; set; }   // cleared from queue after 2
}

/// <summary>Auto-saved learner notes attached to a session/question (Part A).</summary>
public class ListeningPracticeNote
{
    public Guid Id { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    public Guid? PracticeSessionId { get; set; }
    [MaxLength(64)] public string? ListeningQuestionId { get; set; }
    public string NoteMarkdown { get; set; } = "";
    public int CharacterCount { get; set; }
    public DateTimeOffset LastSavedAt { get; set; }
}
