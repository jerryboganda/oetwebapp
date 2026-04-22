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
    string Scratchpad,
    IReadOnlyList<ExpertChecklistItemResponse> ChecklistItems,
    DateTimeOffset SavedAt);

public sealed record ExpertChecklistItemResponse(
    string Id,
    string Label,
    bool Checked);

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

public sealed record ExpertQueueFilterMetadataResponse(
    IReadOnlyList<string> Types,
    IReadOnlyList<string> Professions,
    IReadOnlyList<string> Priorities,
    IReadOnlyList<string> Statuses,
    IReadOnlyList<string> ConfidenceBands,
    IReadOnlyList<string> AssignmentStates);

public sealed record ExpertReviewHistoryEntryResponse(
    DateTimeOffset Timestamp,
    string Action,
    string? ActorName,
    string? Details);

public sealed record ExpertReviewHistoryResponse(
    string ReviewRequestId,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int DraftVersionCount,
    IReadOnlyList<ExpertReviewHistoryEntryResponse> Entries);

public sealed record ExpertLearnerReviewContextResponse(
    string Id,
    string Name,
    string Profession,
    string GoalScore,
    DateTimeOffset? ExamDate,
    int ReviewsInScope,
    IReadOnlyList<ExpertLearnerSubtestScoreResponse> SubTestScores,
    IReadOnlyList<ExpertPriorReviewResponse> PriorReviews);

public sealed record ExpertLearnerListItemResponse(
    string Id,
    string Name,
    string Profession,
    string GoalScore,
    DateTimeOffset? ExamDate,
    int ReviewsInScope,
    IReadOnlyList<string> SubTests,
    string LastReviewId,
    string LastReviewType,
    string LastReviewState,
    DateTimeOffset LastReviewAt);

public sealed record ExpertLearnerDirectoryResponse(
    IReadOnlyList<ExpertLearnerListItemResponse> Items,
    int TotalCount,
    int Page,
    int PageSize,
    DateTimeOffset LastUpdatedAt);

public sealed record ExpertDashboardAvailabilityResponse(
    string Timezone,
    string TodayKey,
    bool ActiveToday,
    string? TodayWindow,
    DateTimeOffset? LastUpdatedAt);

public sealed record ExpertDashboardActivityResponse(
    DateTimeOffset Timestamp,
    string Type,
    string Title,
    string? Description,
    string? Route);

public sealed record ExpertDashboardResponse(
    ExpertMetricsSummaryResponse Metrics,
    int ActiveAssignedReviews,
    int OverdueAssignedReviews,
    int SavedDraftCount,
    int CalibrationDueCount,
    int AssignedLearnerCount,
    DateTimeOffset GeneratedAt,
    ExpertDashboardAvailabilityResponse Availability,
    IReadOnlyList<ExpertQueueItemResponse> AssignedReviews,
    IReadOnlyList<ExpertQueueItemResponse> ResumeDrafts,
    IReadOnlyList<ExpertDashboardActivityResponse> RecentActivity);

public sealed record ExpertCalibrationCaseSummaryResponse(
    string Id,
    string Title,
    string Profession,
    string SubTest,
    string Type,
    int BenchmarkScore,
    int? ReviewerScore,
    double? AlignmentScore,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record ExpertCalibrationNoteResponse(
    string Id,
    string Type,
    string Message,
    string? CaseId,
    DateTimeOffset CreatedAt);

public sealed record ExpertCalibrationArtifactResponse(
    string Kind,
    string Title,
    string Content);

public sealed record ExpertCalibrationRubricEntryResponse(
    string Criterion,
    int BenchmarkScore,
    string Rationale);

public sealed record ExpertCalibrationSubmissionResponse(
    string ReviewerId,
    string ReviewerName,
    int ReviewerScore,
    double AlignmentScore,
    string DisagreementSummary,
    string Notes,
    Dictionary<string, int> SubmittedScores,
    DateTimeOffset SubmittedAt,
    bool IsDraft = false,
    DateTimeOffset? UpdatedAt = null);

public sealed record ExpertCalibrationCaseDetailResponse(
    string Id,
    string Title,
    string Profession,
    string SubTest,
    string Type,
    string BenchmarkLabel,
    int BenchmarkScore,
    string Difficulty,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ExpertCalibrationArtifactResponse> Artifacts,
    IReadOnlyList<ExpertCalibrationRubricEntryResponse> BenchmarkRubric,
    IReadOnlyList<string> ReferenceNotes,
    ExpertCalibrationSubmissionResponse? ExistingSubmission);

// ── Calibration history (supplement: GET /v1/expert/calibration/history) ──
public sealed record ExpertCalibrationHistoryEntryResponse(
    string Id,
    string CaseId,
    string CaseTitle,
    string Profession,
    string SubTest,
    int BenchmarkScore,
    int ReviewerScore,
    double AlignmentScore,
    string DisagreementSummary,
    DateTimeOffset SubmittedAt);

public sealed record ExpertCalibrationHistoryResponse(
    IReadOnlyList<ExpertCalibrationHistoryEntryResponse> Entries,
    int TotalCount,
    DateTimeOffset GeneratedAt);

// ── Calibration alignment aggregate (supplement: GET /v1/expert/calibration/alignment) ──
public sealed record ExpertCalibrationAlignmentBreakdownResponse(
    string SubTest,
    int SubmissionCount,
    double AverageAlignment,
    double? LatestAlignment);

public sealed record ExpertCalibrationAlignmentTrendPointResponse(
    DateTimeOffset SubmittedAt,
    double AlignmentScore);

public sealed record ExpertCalibrationAlignmentResponse(
    int TotalSubmissions,
    double OverallAverageAlignment,
    double? LatestAlignment,
    double? PreviousAlignment,
    double? DeltaFromPrevious,
    IReadOnlyList<ExpertCalibrationAlignmentBreakdownResponse> PerSubTest,
    IReadOnlyList<ExpertCalibrationAlignmentTrendPointResponse> Trend,
    DateTimeOffset GeneratedAt);

// ── Availability constraints (supplement: GET /v1/expert/availability/constraints) ──
public sealed record ExpertAvailabilityConstraintsResponse(
    int MinNoticeHours,
    int MaxHoursPerWeek,
    int MaxExceptionsPerMonth,
    string MinSlotDuration,
    string MaxSlotDuration,
    IReadOnlyList<string> SupportedTimezones,
    IReadOnlyList<string> DayKeys);
