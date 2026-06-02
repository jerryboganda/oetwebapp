using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

// ═════════════════════════════════════════════════════════════════════════════
// Reading Module Pathway — WS1 Domain Entities
//
// 5-stage learning pathway: onboarding → diagnostic → foundation →
// practice → mastery.  Each entity below is a direct 1-to-1 mapping of the
// Reading Module Pathway Plan spec.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Per-learner profile captured during onboarding.</summary>
public class LearnerReadingProfile
{
    public Guid Id { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    [MaxLength(8)] public string TargetBand { get; set; } = default!;   // "B" | "B+" | "A"
    public DateTimeOffset? ExamDate { get; set; }
    public int HoursPerWeek { get; set; }
    [MaxLength(64)] public string Profession { get; set; } = default!;
    public bool HasTakenBefore { get; set; }
    public int? PreviousScore { get; set; }
    public int SelfRatedSpeed { get; set; }       // 1–5
    public int SelfRatedVocabulary { get; set; }  // 1–5
    [MaxLength(32)] public string CurrentStage { get; set; } = "onboarding"; // onboarding|diagnostic|foundation|practice|mastery
    public int? CurrentReadinessScore { get; set; }
    public int? PredictedScore { get; set; }
    public DateTimeOffset OnboardingCompletedAt { get; set; }
    public DateTimeOffset? PathwayGeneratedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>AI-generated multi-week study pathway for a learner.</summary>
public class LearnerReadingPathway
{
    public Guid Id { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    public int TotalWeeks { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string WeeksJson { get; set; } = "[]";  // serialized list of PathwayWeek (owned as JSON)
}

/// <summary>One row per user per skill S1..S8 — rolling mastery score.</summary>
public class LearnerSkillScore
{
    public Guid Id { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    [MaxLength(4)] public string SkillCode { get; set; } = default!;  // S1..S8
    public decimal CurrentScore { get; set; }      // 0.00–10.00
    public decimal DiagnosticScore { get; set; }   // baseline set after diagnostic
    public int QuestionsAttempted { get; set; }
    public int QuestionsCorrect { get; set; }
    public DateTimeOffset LastPracticedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Single item on a learner's AI-generated daily plan.</summary>
public class ReadingDailyPlanItem
{
    public Guid Id { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    public DateOnly PlanDate { get; set; }
    public int Ordinal { get; set; }
    [MaxLength(32)] public string ItemType { get; set; } = default!;  // drill|vocab_review|wrong_review|lesson|mock|strategy_read
    [MaxLength(4)] public string? FocusSkill { get; set; }
    public int EstimatedMinutes { get; set; }
    public string PayloadJson { get; set; } = "{}";
    [MaxLength(16)] public string Status { get; set; } = "pending";  // pending|in_progress|completed|skipped
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>Sub-skill foundation lesson authored by the content team.</summary>
public class ReadingLesson
{
    public Guid Id { get; set; }
    [MaxLength(128)] public string Slug { get; set; } = default!;
    [MaxLength(256)] public string Title { get; set; } = default!;
    [MaxLength(256)] public string TitleAr { get; set; } = "";
    [MaxLength(4)] public string SkillCode { get; set; } = default!;  // S1..S8
    public int OrderIndex { get; set; }
    public int EstimatedMinutes { get; set; }
    public string? VideoUrl { get; set; }
    public string BodyMarkdownEn { get; set; } = "";
    public string BodyMarkdownAr { get; set; } = "";
    public string DrillQuestionIdsJson { get; set; } = "[]";   // Guid[] serialized
    public string QuizQuestionIdsJson { get; set; } = "[]";
    public Guid? PrerequisiteLessonId { get; set; }
    public bool IsPublished { get; set; }
}

/// <summary>Per-learner completion state for a single foundation lesson.</summary>
public class LearnerLessonProgress
{
    public Guid Id { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    public Guid LessonId { get; set; }
    public bool VideoWatched { get; set; }
    public bool BodyRead { get; set; }
    public bool Drill1Completed { get; set; }
    public bool Drill2Completed { get; set; }
    public bool Drill3Completed { get; set; }
    public int? QuizScore { get; set; }    // out of 5
    public int QuizAttempts { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>Strategy article authored by content team (e.g. distractor recognition).</summary>
public class ReadingStrategy
{
    public Guid Id { get; set; }
    [MaxLength(128)] public string Slug { get; set; } = default!;
    [MaxLength(256)] public string Title { get; set; } = default!;
    [MaxLength(256)] public string TitleAr { get; set; } = "";
    [MaxLength(64)] public string Category { get; set; } = default!;  // distractor_recognition|time_management|scanning|inference|exam_day
    public string ApplicablePartsJson { get; set; } = "[]";  // ["A","B","C"]
    public int EstimatedReadMinutes { get; set; }
    public string BodyMarkdownEn { get; set; } = "";
    public string BodyMarkdownAr { get; set; } = "";
    public string? VideoUrl { get; set; }
    public Guid? LinkedDrillId { get; set; }
    public string RelatedStrategyIdsJson { get; set; } = "[]";
    [MaxLength(32)] public string UnlockStage { get; set; } = "foundation";  // foundation|practice|mastery
    public int Difficulty { get; set; }  // 1–5
    public bool IsPublished { get; set; }
}

/// <summary>
/// Per-learner read/favourite state for a strategy article.
/// NOTE: Named ReadingStrategyProgress (not LearnerStrategyProgress) because
/// LearnerStrategyProgress is already taken by LearningContentEntities.cs.
/// </summary>
public class ReadingStrategyProgress
{
    public Guid Id { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    public Guid StrategyId { get; set; }
    public bool MarkedAsRead { get; set; }
    public bool Favorited { get; set; }
    public DateTimeOffset ReadAt { get; set; }
}

/// <summary>Master vocabulary word record shared across all users.</summary>
public class VocabularyWord
{
    public Guid Id { get; set; }
    [MaxLength(128)] public string Word { get; set; } = default!;
    [MaxLength(32)] public string PartOfSpeech { get; set; } = "";
    public string DefinitionEn { get; set; } = "";
    public string DefinitionAr { get; set; } = "";
    [MaxLength(128)] public string PronunciationIpa { get; set; } = "";
    public string? AudioUrl { get; set; }
    public string ExampleEn { get; set; } = "";
    public string ExampleAr { get; set; } = "";
    public string HealthcareContext { get; set; } = "";
    public string ProfessionRelevanceJson { get; set; } = "[]";  // string[]
    public int Difficulty { get; set; }  // 1–10
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Per-user spaced-repetition state for a vocabulary word (SM-2 algorithm).
/// NOTE: Named LearnerVocabularyItem (not LearnerVocabulary) because
/// LearnerVocabulary is already taken by VocabularyEntities.cs.
/// </summary>
public class LearnerVocabularyItem
{
    public Guid Id { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    public Guid VocabularyWordId { get; set; }
    [MaxLength(32)] public string Source { get; set; } = "manual";  // practice|mock|manual|curated_list
    public decimal Easiness { get; set; } = 2.5m;  // SM-2 ease factor
    public int IntervalDays { get; set; } = 1;
    public int Repetitions { get; set; }
    public int RetentionScore { get; set; }  // 0–100
    public DateTimeOffset NextReviewAt { get; set; }
    public DateTimeOffset? LastReviewedAt { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}

/// <summary>Curated vocabulary list authored by Dr Ahmed.</summary>
public class VocabularyList
{
    public Guid Id { get; set; }
    [MaxLength(128)] public string Slug { get; set; } = default!;
    [MaxLength(256)] public string Name { get; set; } = default!;
    [MaxLength(256)] public string NameAr { get; set; } = "";
    public string Description { get; set; } = "";
    public string WordIdsJson { get; set; } = "[]";  // Guid[] serialized
    public bool IsPublished { get; set; }
}

/// <summary>A single reading practice session (diagnostic, drill, mock, etc.).</summary>
public class ReadingPracticeSession
{
    public Guid Id { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    [MaxLength(32)] public string SessionType { get; set; } = default!;  // diagnostic|drill|mini_quiz|mock|wrong_review|vocab_review
    [MaxLength(4)] public string? FocusSkill { get; set; }
    public string QuestionIdsJson { get; set; } = "[]";  // ordered Guid[]
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int? DurationSeconds { get; set; }
    public int? Score { get; set; }
    public int? TotalQuestions { get; set; }
    public string MetadataJson { get; set; } = "{}";
}

/// <summary>Fine-grained per-question attempt history including wrong-review queue state.</summary>
public class ReadingQuestionAttempt
{
    public Guid Id { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    public Guid ReadingQuestionId { get; set; }
    public Guid? PracticeSessionId { get; set; }
    [MaxLength(64)] public string? SelectedOption { get; set; }  // A|B|C|D or text
    public bool IsCorrect { get; set; }
    public bool IsUnknown { get; set; }   // "I don't know" answer in diagnostic
    public int TimeSpentSeconds { get; set; }
    public bool MarkedForReview { get; set; }
    public string? NoteText { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public bool InReviewQueue { get; set; }
    public DateTimeOffset? NextReviewAt { get; set; }
    public int ReviewIntervalIndex { get; set; }  // 0=1day, 1=3days, 2=7days
    public int ConsecutiveCorrect { get; set; }   // cleared from queue after 2
}

/// <summary>Links a mock test to an ordered set of reading question IDs.</summary>
public class ReadingMockTemplate
{
    public Guid Id { get; set; }
    [MaxLength(256)] public string Title { get; set; } = default!;
    public int Difficulty { get; set; }  // 1–5
    public string QuestionIdsJson { get; set; } = "[]";  // ordered Guid[] of 42 questions
    public bool IsPublished { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Daily activity record used to compute streak length.</summary>
public class StreakRecord
{
    public Guid Id { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    public DateOnly Date { get; set; }
    public bool HasActivity { get; set; }
    public int QuestionsAnsweredToday { get; set; }  // streak requires ≥8
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
}

/// <summary>Per-learner XP total and level state (gamification).</summary>
// Explicit table name to avoid case-collision with the platform-wide
// GamificationEntities.LearnerXP → "LearnerXPs". SQLite is case-insensitive,
// so "LearnerXps" == "LearnerXPs" at the DB level.
[System.ComponentModel.DataAnnotations.Schema.Table("ReadingLearnerXps")]
public class LearnerXp
{
    public Guid Id { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    public int TotalXp { get; set; }
    public int CurrentLevel { get; set; }
    public int XpToNextLevel { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Badge awarded to a learner for reaching a milestone.</summary>
public class LearnerBadge
{
    public Guid Id { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    [MaxLength(64)] public string BadgeCode { get; set; } = default!;
    public DateTimeOffset EarnedAt { get; set; }
}

/// <summary>
/// Discussion comment on a specific reading question.
/// NOTE: Named ReadingQuestionDiscussionComment (not QuestionDiscussionComment)
/// to be explicit about the module and avoid generic naming collisions.
/// </summary>
public class ReadingQuestionDiscussionComment
{
    public Guid Id { get; set; }
    public Guid ReadingQuestionId { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = default!;
    public string Body { get; set; } = default!;
    public int Upvotes { get; set; }
    public bool IsFromTutor { get; set; }
    public bool IsHidden { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
