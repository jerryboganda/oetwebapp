using System.ComponentModel.DataAnnotations;

namespace OetWithDrHesham.Api.Contracts;

public sealed record WritingStartOnboardingRequest(
    [property: Required, StringLength(64)] string Profession,
    [property: Required, StringLength(8)] string TargetBand,
    DateTimeOffset? ExamDate,
    [property: Range(1, 7)] int DaysPerWeek,
    [property: Range(15, 180)] int MinutesPerDay,
    [property: StringLength(32)] string? TargetCountry,
    List<string>? LetterTypeFocus);

public sealed record WritingProfileResponse(
    string UserId,
    string CurrentStage,
    string Profession,
    string TargetBand,
    DateTimeOffset? ExamDate,
    int DaysPerWeek,
    int MinutesPerDay,
    string TargetCountry,
    IReadOnlyList<string> LetterTypeFocus,
    int? ReadinessScore,
    int? PredictedScore,
    string? LastDiagnosticEvaluationId,
    DateTimeOffset? OnboardingCompletedAt,
    DateTimeOffset? PathwayGeneratedAt,
    int? WeeksRemaining,
    bool DiagnosticCompleted);

public sealed record WritingPathwayResponse(
    string CurrentStage,
    int TotalWeeks,
    int CurrentWeek,
    int WeeksRemaining,
    int ReadinessScore,
    int? PredictedScore,
    DateTimeOffset? GeneratedAt,
    IReadOnlyList<WritingPathwayWeekResponse> Weeks);

public sealed record WritingPathwayWeekResponse(
    int WeekNumber,
    string Phase,
    IReadOnlyList<string> FocusSkills,
    IReadOnlyList<string> FocusLetterTypes,
    string Theme,
    bool MockScheduled,
    bool IsCompleted);

public sealed record WritingTodayPlanResponse(
    DateOnly Date,
    IReadOnlyList<WritingTodayPlanItemResponse> Items,
    int TotalMinutes,
    int CompletedCount);

public sealed record WritingTodayPlanItemResponse(
    Guid Id,
    int Ordinal,
    string ItemType,
    string? FocusSkill,
    string? FocusCriterion,
    int EstimatedMinutes,
    string Title,
    string Description,
    string ActionHref,
    string? ContentId,
    string Status);

public sealed record WritingPlanItemSkipRequest([property: StringLength(128)] string? Reason);

public sealed record WritingCanonResponse(
    IReadOnlyList<WritingCanonRuleResponse> Rules,
    IReadOnlyList<WritingCanonViolationStatResponse> RecentViolations,
    int TotalRules,
    int TotalRecentViolations);

public sealed record WritingCanonRuleResponse(
    string RuleId,
    string Category,
    string Severity,
    string RuleText,
    IReadOnlyList<string> AppliesToLetterTypes,
    IReadOnlyList<string> AppliesToProfessions,
    IReadOnlyList<string> CorrectExamples,
    IReadOnlyList<string> IncorrectExamples,
    string? LessonHref);

public sealed record WritingCanonViolationStatResponse(
    string RuleId,
    int Count,
    string Severity,
    DateTimeOffset LastSeenAt);

public sealed record WritingLessonProgressResponse(
    bool BodyRead,
    bool DrillCompleted,
    int? QuizScore,
    int QuizAttempts,
    DateTimeOffset? CompletedAt);

public sealed record WritingLessonListItemResponse(
    Guid Id,
    string Slug,
    string Title,
    string SkillCode,
    int OrderIndex,
    int EstimatedMinutes,
    bool IsUnlocked,
    WritingLessonProgressResponse? Progress);

public sealed record WritingLessonDetailResponse(
    Guid Id,
    string Slug,
    string Title,
    string SkillCode,
    int OrderIndex,
    int EstimatedMinutes,
    string BodyMarkdownEn,
    string DrillPrompt,
    IReadOnlyList<WritingLessonQuizQuestionResponse> Quiz,
    string? PreviousSlug,
    string? NextSlug,
    bool IsUnlocked,
    WritingLessonProgressResponse? Progress);

public sealed record WritingLessonQuizQuestionResponse(
    string Id,
    string Prompt,
    IReadOnlyList<string> Options);

public sealed record WritingLessonProgressRequest(
    bool? BodyRead,
    bool? DrillCompleted,
    [property: Range(0, 5)] int? QuizScore);

public sealed record WritingDrillSummaryResponse(
    Guid Id,
    string DrillType,
    string TargetSubSkill,
    string? TargetCanonRuleId,
    int Difficulty,
    int EstimatedMinutes,
    string Title,
    int AttemptCount,
    DateTimeOffset? NextDueAt);

public sealed record WritingDrillDetailResponse(
    Guid Id,
    string DrillType,
    string TargetSubSkill,
    string? TargetCanonRuleId,
    int Difficulty,
    int EstimatedMinutes,
    string Title,
    string PromptMarkdown,
    string GradingMethod,
    int AttemptCount,
    DateTimeOffset? NextDueAt);

public sealed record WritingDrillAttemptRequest(
    [property: Required, StringLength(4000)] string ResponseText,
    [property: Range(0, 7200)] int? TimeSpentSeconds);

public sealed record WritingDrillAttemptResponse(
    Guid AttemptId,
    bool IsCorrect,
    string FeedbackText,
    DateTimeOffset? NextDueAt,
    int Repetitions);

public sealed record WritingCaseNoteDrillSummaryResponse(
    Guid Id,
    string Title,
    string Profession,
    string LetterType,
    int Difficulty,
    int SentenceCount,
    int AttemptCount);

public sealed record WritingCaseNoteDrillDetailResponse(
    Guid Id,
    string Title,
    string Profession,
    string LetterType,
    string Format,
    string CaseNotesMarkdown,
    int Difficulty,
    int SentenceCount,
    IReadOnlyList<WritingCaseNoteDrillSentenceResponse> Sentences,
    int AttemptCount);

public sealed record WritingCaseNoteDrillSentenceResponse(
    Guid Id,
    int Ordinal,
    string SentenceText);

public sealed record WritingCaseNoteDrillAttemptRequest(
    Dictionary<Guid, string> Responses,
    [property: Range(0, 7200)] int? TimeSpentSeconds);

public sealed record WritingCaseNoteDrillAttemptResponse(
    Guid AttemptId,
    int CorrectCount,
    int TotalCount,
    double ScorePercent,
    IReadOnlyList<WritingCaseNoteDrillFeedbackResponse> Feedback);

public sealed record WritingCaseNoteDrillFeedbackResponse(
    Guid SentenceId,
    bool IsCorrect,
    string CorrectLabel,
    string? Rationale);