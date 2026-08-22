namespace OetLearner.Api.Contracts;

public sealed record AnswerKeyReportCreateRequest(
    string? QuestionId,
    string? ReasonCode,
    string? Details);

public sealed record AnswerKeyReportUpdateRequest(
    string? Status,
    string? ResolutionNote);

public sealed record AnswerKeyReportLearnerDto(
    string Id,
    string Assessment,
    string AttemptId,
    string PaperId,
    string QuestionId,
    int QuestionNumber,
    string PartCode,
    string ReasonCode,
    string? Details,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record AnswerKeyReportAdminDto(
    string Id,
    string Assessment,
    string AttemptId,
    string PaperId,
    string PaperTitle,
    string QuestionId,
    int QuestionNumber,
    string PartCode,
    string QuestionStemSnapshot,
    string LearnerAnswerSnapshot,
    string OfficialAnswerSnapshot,
    string ReasonCode,
    string? Details,
    string Status,
    string? ResolutionNote,
    string ReportedByUserId,
    string ReportedByUserDisplayName,
    string EditorUrl,
    string ScoringSystemUrl,
    string? ResolvedByAdminId,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
