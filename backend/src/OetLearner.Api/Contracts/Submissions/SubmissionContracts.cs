namespace OetLearner.Api.Contracts.Submissions;

/// <summary>
/// Filter/sort/pagination parameters for the Submission History list
/// endpoint. Matches the <c>/v1/submissions</c> query string.
///
/// - <see cref="Cursor"/> is opaque (base64(submittedAt|attemptId|sort)).
/// - Sorting options: <c>date-desc</c> (default), <c>date-asc</c>,
///   <c>score-desc</c>, <c>score-asc</c>.
/// - <see cref="PassOnly"/> filtering is applied in-memory because
///   country-aware Writing resolution cannot be translated to SQL reliably.
/// </summary>
public record SubmissionListQuery(
    string? Cursor,
    int? Limit,
    string? Subtest,
    string? Context,
    string? ReviewStatus,
    DateTimeOffset? From,
    DateTimeOffset? To,
    bool PassOnly,
    string? Q,
    string? Sort,
    bool IncludeHidden);

public record SubmissionListResponse(
    IReadOnlyList<SubmissionListItem> Items,
    string? NextCursor,
    int Total,
    SubmissionFacets Facets,
    IReadOnlyDictionary<string, List<SparklinePoint>> Sparkline);

public record SparklinePoint(DateTimeOffset At, int? Scaled);

public record SubmissionFacets(
    IReadOnlyDictionary<string, int> BySubtest,
    IReadOnlyDictionary<string, int> ByContext,
    IReadOnlyDictionary<string, int> ByReviewStatus);

public record SubmissionListItem(
    string SubmissionId,
    string ContentId,
    string TaskName,
    string Subtest,
    string Context,
    DateTimeOffset AttemptDate,
    string State,
    string ReviewStatus,
    string? EvaluationId,
    int? ScaledScore,
    string ScoreLabel,
    string PassState,
    string PassLabel,
    int? RequiredScaled,
    string? Grade,
    string? ComparisonGroupId,
    string? ParentAttemptId,
    int RevisionDepth,
    bool CanRequestReview,
    bool IsHidden,
    SubmissionActions Actions);

public record SubmissionActions(
    string? ReopenFeedbackRoute,
    string? CompareRoute,
    string? RequestReviewRoute);

public record SubmissionDetailResponse(
    SubmissionListItem Submission,
    EvidenceSummary EvidenceSummary,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Issues,
    IReadOnlyList<CriterionFeedback> Criteria,
    IReadOnlyList<RevisionNode> RevisionLineage,
    ReviewLineage? ReviewLineage);

public record EvidenceSummary(
    string Title,
    string ScoreLabel,
    string StateLabel,
    string ReviewLabel,
    string NextActionLabel);

public record RevisionNode(
    string AttemptId,
    int Order,
    string Label,
    DateTimeOffset SubmittedAt,
    int? ScaledScore,
    bool IsCurrent);

public record ReviewLineage(
    string ReviewRequestId,
    string State,
    string StateLabel,
    string? TurnaroundOption,
    int CreditsCharged,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt);

public record SubmissionComparisonResponse(
    bool CanCompare,
    string? Reason,
    string? ReasonLabel,
    string? ComparisonGroupId,
    CompareSide? Left,
    CompareSide? Right,
    int? ScaledDelta,
    IReadOnlyList<CriterionDelta> CriterionDeltas,
    string? Summary);

public record CompareSide(
    string AttemptId,
    string Subtest,
    DateTimeOffset SubmittedAt,
    string? EvaluationId,
    int? ScaledScore,
    string ScoreLabel,
    string PassState,
    string? Grade);

public record CriterionDelta(
    string Name,
    int LeftScore,
    int RightScore,
    int MaxScore,
    string Direction);

/// <summary>
/// Criterion feedback serialized inside <c>Evaluation.CriterionScoresJson</c>.
/// Kept permissive on the string-max fields because historical data uses
/// a variety of shapes.
/// </summary>
public record CriterionFeedback(string Name, int Score, int MaxScore, string? Explanation);
