namespace OetLearner.Api.Contracts;

public sealed record ExpertMeResponse(
    string UserId,
    string Role,
    string DisplayName,
    string Email,
    string Timezone,
    bool IsActive,
    string[] Specialties,
    DateTimeOffset CreatedAt);

public sealed record ExpertReviewActionsResponse(
    bool CanClaim,
    bool CanRelease,
    bool CanOpen,
    bool CanSaveDraft,
    bool CanSubmit,
    bool CanRequestRework,
    bool ReadOnly);

public sealed record ExpertQueueItemResponse(
    string Id,
    string LearnerId,
    string LearnerName,
    string Profession,
    string SubTest,
    string Type,
    string AiConfidence,
    string Priority,
    DateTimeOffset SlaDue,
    string SlaState,
    bool IsOverdue,
    string? AssignedReviewerId,
    string? AssignedReviewerName,
    string AssignmentState,
    string Status,
    string? ContentId,
    string AttemptId,
    DateTimeOffset CreatedAt,
    ExpertReviewActionsResponse AvailableActions);

public sealed record ExpertQueueResponse(
    IReadOnlyList<ExpertQueueItemResponse> Items,
    int TotalCount,
    int Page,
    int PageSize,
    DateTimeOffset LastUpdatedAt);

public sealed record ExpertArtifactStateResponse(
    string State,
    bool IsStale,
    string? Message);

public sealed record ExpertAnchoredCommentResponse(
    string Id,
    string? Criterion,
    string Text,
    int StartOffset,
    int EndOffset,
    DateTimeOffset CreatedAt);

public sealed record ExpertTimestampCommentResponse(
    string Id,
    string? Criterion,
    string Text,
    double TimestampStart,
    double? TimestampEnd,
    DateTimeOffset CreatedAt);

public sealed record ExpertDraftResponse(
    int Version,
    string State,
    Dictionary<string, int> Scores,
    Dictionary<string, string> CriterionComments,
    string FinalComment,
    IReadOnlyList<ExpertAnchoredCommentResponse> AnchoredComments,
    IReadOnlyList<ExpertTimestampCommentResponse> TimestampComments,
    DateTimeOffset SavedAt);

public sealed record ExpertTranscriptLineResponse(
    string Id,
    string Speaker,
    double StartTime,
    double EndTime,
    string Text);

public sealed record ExpertAiFlagResponse(
    string Id,
    string Type,
    string Message,
    double TimestampStart,
    double? TimestampEnd,
    string Severity);

public sealed record ExpertSpeakingRoleCardResponse(
    string Role,
    string Setting,
    string Patient,
    string Task,
    string? Background);

public sealed record ExpertWritingReviewBundleResponse(
    string Id,
    string LearnerId,
    string LearnerName,
    string Profession,
    string SubTest,
    string Type,
    string AiConfidence,
    string Priority,
    DateTimeOffset SlaDue,
    string SlaState,
    bool IsOverdue,
    string? AssignedReviewerId,
    string? AssignedReviewerName,
    string AssignmentState,
    string Status,
    string? ContentId,
    string AttemptId,
    DateTimeOffset CreatedAt,
    string LearnerResponse,
    string CaseNotes,
    string AiDraftFeedback,
    Dictionary<string, int> AiSuggestedScores,
    string? ModelAnswer,
    ExpertDraftResponse? ExistingDraft,
    ExpertReviewActionsResponse Permissions,
    Dictionary<string, ExpertArtifactStateResponse> ArtifactStatus);

public sealed record ExpertSpeakingReviewBundleResponse(
    string Id,
    string LearnerId,
    string LearnerName,
    string Profession,
    string SubTest,
    string Type,
    string AiConfidence,
    string Priority,
    DateTimeOffset SlaDue,
    string SlaState,
    bool IsOverdue,
    string? AssignedReviewerId,
    string? AssignedReviewerName,
    string AssignmentState,
    string Status,
    string? ContentId,
    string AttemptId,
    DateTimeOffset CreatedAt,
    string AudioUrl,
    IReadOnlyList<ExpertTranscriptLineResponse> TranscriptLines,
    ExpertSpeakingRoleCardResponse RoleCard,
    IReadOnlyList<ExpertAiFlagResponse> AiFlags,
    Dictionary<string, int> AiSuggestedScores,
    ExpertDraftResponse? ExistingDraft,
    ExpertReviewActionsResponse Permissions,
    Dictionary<string, ExpertArtifactStateResponse> ArtifactStatus);

public sealed record ExpertLearnerSubtestScoreResponse(
    string SubTest,
    int? LatestScore,
    string? LatestGrade,
    int Attempts);

public sealed record ExpertPriorReviewResponse(
    string Id,
    string Type,
    string ReviewerName,
    DateTimeOffset Date,
    string OverallComment);

public sealed record ExpertLearnerProfileResponse(
    string Id,
    string Name,
    string Profession,
    string GoalScore,
    DateTimeOffset? ExamDate,
    int AttemptsCount,
    DateTimeOffset JoinedAt,
    int TotalReviews,
    IReadOnlyList<ExpertLearnerSubtestScoreResponse> SubTestScores,
    IReadOnlyList<ExpertPriorReviewResponse> PriorReviews,
    string VisibilityScope);

public sealed record ExpertMetricsSummaryResponse(
    int TotalReviewsCompleted,
    int DraftReviews,
    double AverageSlaCompliance,
    double AverageCalibrationAlignment,
    double ReworkRate,
    double AverageTurnaroundHours);

public sealed record ExpertCompletionPointResponse(
    string Day,
    int Count);

public sealed record ExpertMetricsResponse(
    ExpertMetricsSummaryResponse Metrics,
    IReadOnlyList<ExpertCompletionPointResponse> CompletionData,
    int Days,
    DateTimeOffset GeneratedAt);
