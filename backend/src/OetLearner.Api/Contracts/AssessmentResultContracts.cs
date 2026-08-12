namespace OetLearner.Api.Contracts;

/// <summary>Shared candidate-safe part summary for legacy mock results.</summary>
public sealed record MockPartBreakdownResponse(
    string PartCode,
    int RawScore,
    int MaxRawScore,
    int CorrectCount,
    int IncorrectCount,
    int UnansweredCount,
    decimal AccuracyPercentage);

/// <summary>Candidate-safe timing summary for a legacy mock result.</summary>
public sealed record MockTimeUsedResponse(
    int? TotalMilliseconds,
    IReadOnlyList<MockTimeUsedSectionResponse> Sections);

public sealed record MockTimeUsedSectionResponse(
    string SectionCode,
    int? ElapsedMilliseconds);

/// <summary>Candidate-safe item review disclosed only by completed mock results.</summary>
public sealed record MockReviewItemResponse(
    string QuestionId,
    string PartCode,
    int Number,
    string QuestionType,
    string? Stem,
    string? LearnerAnswer,
    string? CorrectAnswer,
    bool IsCorrect,
    bool IsUnanswered,
    int PointsEarned,
    int MaxPoints,
    string? ErrorCategory,
    string? Explanation,
    string? Evidence,
    int? EvidenceStartMilliseconds = null,
    int? EvidenceEndMilliseconds = null);

/// <summary>Deterministic error-pattern summary derived from completed mock items.</summary>
public sealed record MockErrorSummaryResponse(
    string ErrorCategory,
    int Count,
    IReadOnlyList<string> QuestionIds);

/// <summary>Candidate-safe next step link; it is not an AI score or pass claim.</summary>
public sealed record MockNextStepResponse(
    string Title,
    string Description,
    string Route);
