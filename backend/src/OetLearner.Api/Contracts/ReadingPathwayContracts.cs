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
    Guid QuestionId,
    string SelectedOption,
    int TimeSpentSeconds);

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
