namespace OetLearner.Api.Contracts;

public sealed record LearnerNextActionResponse(
    string ActionType,
    string Title,
    string Description,
    string Route,
    int Priority,
    string? Deadline,
    string Category);

public sealed record LearnerNextActionsResponse(
    IReadOnlyList<LearnerNextActionResponse> Actions,
    DateTimeOffset GeneratedAt);

public sealed record LearnerReadinessBlockerResponse(
    string SubTest,
    string Criterion,
    double CurrentScore,
    double TargetScore,
    double Gap,
    string Recommendation,
    string ActionRoute);

public sealed record LearnerReadinessBlockersResponse(
    IReadOnlyList<LearnerReadinessBlockerResponse> Blockers,
    double OverallReadiness,
    DateTimeOffset GeneratedAt);

public sealed record LearnerProgressTrendPointResponse(
    string Period,
    double AverageScore,
    int AttemptCount);

public sealed record LearnerProgressTrendResponse(
    IReadOnlyList<LearnerProgressTrendPointResponse> Points,
    double? ProjectedScore,
    string ProjectedAt,
    DateTimeOffset GeneratedAt);
