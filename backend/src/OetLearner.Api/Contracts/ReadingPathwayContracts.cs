using System.Text.Json;

namespace OetLearner.Api.Contracts;

public sealed record StartOnboardingRequest(
    string TargetBand,
    DateTimeOffset? ExamDate,
    int HoursPerWeek,
    string Profession,
    bool HasTakenBefore,
    int? PreviousScore,
    int SelfRatedSpeed,
    int SelfRatedVocabulary);

public sealed record StartPracticeSessionRequest(
    string SessionType,          // drill|mock|wrong_review
    string? FocusSkill,          // S1..S8
    int TargetMinutes,
    Guid? MockTemplateId);       // required for mock

public sealed record SubmitAnswerRequest(
    string QuestionId,
    string SelectedOption,
    int TimeSpentSeconds);

public sealed record AnswerResultResponse(
    bool IsCorrect,
    string? Explanation);

public sealed record PracticeSessionPassageResponse(
    string Id,
    string Title,
    string BodyHtml,
    int PartCode);

public sealed record PracticeSessionQuestionResponse(
    string Id,
    string PassageId,
    string Stem,
    JsonElement Options,
    string QuestionType,
    int PartCode,
    string? SkillCode);

public sealed record PracticeSessionQuestionsResponse(
    Guid SessionId,
    string Mode,
    string? FocusSkill,
    int? TimeLimitSeconds,
    List<PracticeSessionQuestionResponse> Questions,
    List<PracticeSessionPassageResponse> Passages);

public sealed record PracticeSessionSubmitResponse(
    Guid SessionId,
    int Score,
    int TotalQuestions,
    int? DurationSeconds,
    int? ScaledScore);

public sealed record MockSessionResultResponse(
    int Score,
    int TotalQuestions,
    int? DurationSeconds,
    int ScaledScore);

public sealed record SubmitDiagnosticRequest(
    Guid SessionId,
    Dictionary<string, string> Answers);  // questionId -> selectedOption

public sealed record VocabAddRequest(string Word, string Source);
public sealed record VocabReviewRequest(int Quality);  // 0,3,4,5

public sealed record LessonProgressRequest(
    bool? VideoWatched,
    bool? BodyRead,
    bool? Drill1Completed,
    bool? Drill2Completed,
    bool? Drill3Completed,
    int? QuizScore);

public sealed record PostCommentRequest(string Body);
public sealed record PassageQnaRequest(string PassageId, string Message, List<ChatMessageDto> History);
public sealed record ChatMessageDto(string Role, string Content);  // role: "user"|"assistant"

public sealed record ReadingProfileResponse(
    string UserId,
    string CurrentStage,
    string? TargetBand,
    DateTimeOffset? ExamDate,
    int? HoursPerWeek,
    string? Profession,
    bool HasTakenBefore,
    int? PreviousScore,
    int? SelfRatedSpeed,
    int? SelfRatedVocabulary,
    int? ReadinessScore,
    int? PredictedScore,
    DateTimeOffset? OnboardingCompletedAt,
    DateTimeOffset? PathwayGeneratedAt,
    int? WeeksRemaining,
    bool DiagnosticCompleted);

public sealed record DiagnosticQuestionResponse(
    string Id,
    string PartCode,
    string QuestionType,
    int DisplayOrder,
    string Stem,
    JsonElement Options,
    string? TextTitle,
    string? TextHtml,
    string? SkillCode);

public sealed record DiagnosticResultResponse(
    Guid SessionId,
    int Score,
    int TotalQuestions,
    Dictionary<string, decimal> SkillScores,
    string EstimatedOetBand,
    int EstimatedScaledScore,
    int? DurationSeconds,
    int RoadmapWeeks,
    DateTimeOffset? CompletedAt);

public sealed record ReadingPathwayResponse(
    string CurrentStage,
    int TotalWeeks,
    int CurrentWeek,
    int WeeksRemaining,
    int ReadinessScore,
    int? PredictedScore,
    DateTimeOffset? GeneratedAt,
    List<ReadingPathwayWeekResponse> Weeks);

public sealed record ReadingPathwayWeekResponse(
    int WeekNumber,
    string Phase,
    List<string> FocusSkills,
    string Theme,
    bool MockScheduled,
    bool IsCompleted);
