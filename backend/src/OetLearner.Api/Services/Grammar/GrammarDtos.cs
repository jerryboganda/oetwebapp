using System.Text.Json;

namespace OetLearner.Api.Services.Grammar;

// ═════════════════════════════════════════════════════════════════════════════
// Grammar DTO projections — learner vs admin
//
// SECURITY INVARIANT:
// Learner-facing records MUST NOT contain CorrectAnswerJson,
// AcceptedAnswersJson, or ExplanationMarkdown until AFTER the learner
// has submitted an answer for that exercise. Admin-facing records
// include them.
// ═════════════════════════════════════════════════════════════════════════════

// ─── Learner DTOs ────────────────────────────────────────────────────────────

public sealed record GrammarTopicLearnerDto(
    string Id,
    string Slug,
    string ExamTypeCode,
    string Name,
    string? Description,
    string? IconEmoji,
    string LevelHint,
    int SortOrder,
    int LessonCount,
    int CompletedCount,
    int MasteredCount,
    double AvgMasteryScore);

public sealed record GrammarLessonSummaryDto(
    string Id,
    string ExamTypeCode,
    string? TopicId,
    string? TopicSlug,
    string Title,
    string? Description,
    string Level,
    string Category,
    int EstimatedMinutes,
    int SortOrder,
    string ProgressStatus,        // not_started | in_progress | completed
    int MasteryScore,
    int ExerciseCount);

public sealed record GrammarLessonLearnerDto(
    string Id,
    string ExamTypeCode,
    string? TopicId,
    string? TopicSlug,
    string Title,
    string? Description,
    string Level,
    string Category,
    int EstimatedMinutes,
    string? PrerequisiteLessonId,
    IReadOnlyList<GrammarContentBlockLearnerDto> ContentBlocks,
    IReadOnlyList<GrammarExerciseLearnerDto> Exercises,
    GrammarLessonProgressDto? Progress);

public sealed record GrammarContentBlockLearnerDto(
    string Id,
    int SortOrder,
    string Type,
    string ContentMarkdown,
    JsonElement? Content);

/// <summary>
/// SECURITY: no CorrectAnswer, AcceptedAnswers, Explanation fields.
/// </summary>
public sealed record GrammarExerciseLearnerDto(
    string Id,
    int SortOrder,
    string Type,
    string PromptMarkdown,
    JsonElement Options,         // may be [] for fill_blank etc.
    string Difficulty,
    int Points);

public sealed record GrammarLessonProgressDto(
    string Status,
    int? ExerciseScore,
    int MasteryScore,
    int AttemptCount,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastAttemptedAt,
    DateTimeOffset? CompletedAt);

public sealed record GrammarRecommendationDto(
    string Id,
    string LessonId,
    string LessonTitle,
    string Source,
    string? SourceRefId,
    string? RuleId,
    double Relevance,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DismissedAt);

public sealed record GrammarOverviewDto(
    IReadOnlyList<GrammarTopicLearnerDto> Topics,
    IReadOnlyList<GrammarRecommendationDto> Recommendations,
    int LessonsCompleted,
    int LessonsMastered,
    int LessonsTotal,
    double OverallMasteryScore);

public sealed record GrammarAttemptRequestDto(
    Dictionary<string, JsonElement> Answers);

public sealed record GrammarAttemptResultDto(
    string LessonId,
    int Score,
    int PointsEarned,
    int MaxPoints,
    int CorrectCount,
    int IncorrectCount,
    int MasteryScore,
    bool Mastered,
    int XpAwarded,
    int ReviewItemsCreated,
    IReadOnlyList<GrammarExerciseResultDto> Exercises);

public sealed record GrammarExerciseResultDto(
    string ExerciseId,
    string Type,
    bool IsCorrect,
    int PointsEarned,
    int MaxPoints,
    JsonElement? UserAnswer,
    JsonElement? CorrectAnswer,
    string? ExplanationMarkdown);

// ─── Admin DTOs ──────────────────────────────────────────────────────────────

public sealed record AdminGrammarTopicCreateRequest(
    string ExamTypeCode,
    string Slug,
    string Name,
    string? Description,
    string? IconEmoji,
    string? LevelHint,
    int? SortOrder);

public sealed record AdminGrammarTopicUpdateRequest(
    string? Slug,
    string? Name,
    string? Description,
    string? IconEmoji,
    string? LevelHint,
    int? SortOrder,
    string? Status);

public sealed record AdminGrammarLessonFullCreateRequest(
    string ExamTypeCode,
    string? TopicId,
    string Title,
    string? Description,
    string? Level,
    string? Category,
    int? EstimatedMinutes,
    int? SortOrder,
    string? PrerequisiteLessonId,
    List<string>? PrerequisiteLessonIds,
    string? SourceProvenance,
    List<AdminGrammarContentBlockDto>? ContentBlocks,
    List<AdminGrammarExerciseDto>? Exercises);

public sealed record AdminGrammarLessonFullUpdateRequest(
    string? TopicId,
    string? Title,
    string? Description,
    string? Level,
    string? Category,
    int? EstimatedMinutes,
    int? SortOrder,
    string? PrerequisiteLessonId,
    List<string>? PrerequisiteLessonIds,
    string? SourceProvenance,
    List<AdminGrammarContentBlockDto>? ContentBlocks,
    List<AdminGrammarExerciseDto>? Exercises,
    string? Status);

public sealed record AdminGrammarContentBlockDto(
    string? Id,
    int SortOrder,
    string Type,
    string ContentMarkdown,
    JsonElement? Content);

public sealed record AdminGrammarExerciseDto(
    string? Id,
    int SortOrder,
    string Type,
    string PromptMarkdown,
    JsonElement? Options,
    JsonElement CorrectAnswer,
    JsonElement? AcceptedAnswers,
    string? ExplanationMarkdown,
    string? Difficulty,
    int? Points);

public sealed record AdminGrammarAiDraftRequest(
    string ExamTypeCode,
    string? TopicSlug,
    string Prompt,
    string? Level,
    int? TargetExerciseCount,
    string? Model);

public sealed record AdminGrammarAiDraftResponse(
    string LessonId,
    string Title,
    string Status,
    int ContentBlockCount,
    int ExerciseCount,
    string? Warning);

public sealed record AdminGrammarImportRequest(
    List<AdminGrammarLessonFullCreateRequest> Lessons);

public sealed record AdminGrammarImportResult(
    int Created,
    int Skipped,
    List<string> Errors);
