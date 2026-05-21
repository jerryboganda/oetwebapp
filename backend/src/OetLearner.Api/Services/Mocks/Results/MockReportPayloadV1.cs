namespace OetLearner.Api.Services.Mocks.Results;

// MockReportPayloadV1 is the formal, versioned contract for MockReport.PayloadJson.
// Bumping the version requires updating consumers in lib/mocks/report-payload.ts and the report page.
// All optional fields are nullable so the aggregator can omit data that is not yet available
// (e.g. trend requires >= 2 attempts; pass prediction requires Phase 3 calibration; teacher
// feedback fragments populate as expert reviews complete).
public sealed record MockReportPayloadV1(
    string PayloadSchemaVersion,
    string Id,
    string ReportId,
    string MockAttemptId,
    string Title,
    string Date,
    string? Profession,
    string? TargetCountry,
    string? DeliveryMode,
    string? Strictness,
    string? ReleasePolicy,
    string OverallScore,
    string? OverallGrade,
    string Summary,
    IReadOnlyList<MockReportSubTestV1> SubTests,
    MockReportWeakestCriterionV1 WeakestCriterion,
    MockReportReviewSummaryV1 ReviewSummary,
    IReadOnlyList<MockReportPerModuleReadinessV1> PerModuleReadiness,
    IReadOnlyList<MockReportPartScoreV1> PartScores,
    IReadOnlyList<MockReportTimingAnalysisV1> TimingAnalysis,
    IReadOnlyList<MockReportErrorCategoryV1> ErrorCategories,
    MockReportReviewSummaryV1 TeacherReviewState,
    MockReportBookingAdviceV1 BookingAdvice,
    MockReportRetakeAdviceV1 RetakeAdvice,
    MockReportProctoringSummaryV1 ProctoringSummary,
    IReadOnlyList<MockReportRemediationActionV1> RemediationPlan,
    MockReportPriorComparisonV1 PriorComparison,
    // Phase 1 introduces these fields; population is gated by the corresponding
    // service work (narrative renderer ships in Phase 1.5, pass-prediction in Phase 3,
    // trend in Phase 3, teacher feedback fragments as reviews complete).
    MockReportWeaknessNarrativeV1? WeaknessNarrative,
    MockReportPassPredictionV1? PassPrediction,
    MockReportTrendV1? Trend,
    IReadOnlyList<MockReportPerQuestionTimingV1>? PerQuestionTiming,
    IReadOnlyList<MockReportTeacherFeedbackFragmentV1>? TeacherFeedbackFragments);

public sealed record MockReportSubTestV1(
    string Id,
    string Name,
    string Score,
    string RawScore,
    int? ScaledScore,
    string? Grade,
    string State,
    string EvidenceSource,
    string? ContentPaperTitle,
    string? ReviewRequestId,
    string? ReviewState);

public sealed record MockReportWeakestCriterionV1(
    string Subtest,
    string Criterion,
    string Description);

public sealed record MockReportReviewSummaryV1(
    int Queued,
    int InReview,
    int Completed,
    int Pending);

public sealed record MockReportPerModuleReadinessV1(
    string Subtest,
    int? ScaledScore,
    string? Grade,
    string Rag,
    string Message,
    int? PassThreshold);

public sealed record MockReportPartScoreV1(
    string Subtest,
    string? RawScore,
    int? ScaledScore,
    string? Grade,
    string? State,
    string? EvidenceSource);

public sealed record MockReportTimingAnalysisV1(
    string SectionId,
    string Subtest,
    DateTimeOffset? StartedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? DeadlineAt,
    int? SecondsUsed);

public sealed record MockReportErrorCategoryV1(
    string Category,
    string Subtest,
    string Severity,
    string Description);

public sealed record MockReportBookingAdviceV1(
    string Status,
    string Message,
    string? Route,
    int? Score);

public sealed record MockReportRetakeAdviceV1(
    int RecommendedWindowDays,
    string NextMockType,
    string Subtest,
    string Message);

public sealed record MockReportProctoringSummaryV1(
    int TotalEvents,
    bool AdvisoryOnly,
    int CriticalEvents,
    int WarningEvents,
    IReadOnlyList<MockReportProctoringKindV1> ByKind,
    string Message);

public sealed record MockReportProctoringKindV1(string Kind, int Count);

public sealed record MockReportRemediationActionV1(
    string Day,
    string Title,
    string Description,
    string Route);

public sealed record MockReportPriorComparisonV1(
    bool Exists,
    string PriorMockName,
    string OverallTrend,
    string Details);

// Weakness narrative is a richer description than `WeakestCriterion` — it lists multiple
// weakness tags with per-tag drills. Renderer is built in Phase 1.5.
public sealed record MockReportWeaknessNarrativeV1(
    string Headline,
    string Body,
    IReadOnlyList<MockReportWeaknessTagV1> Tags);

public sealed record MockReportWeaknessTagV1(
    string Tag,
    string Subtest,
    string Description,
    string? DrillId,
    string? DrillRouteHref);

// Pass-prediction is qualitative on purpose — we never surface a numeric probability
// (avoids false precision). Populated by Phase 3's calibration logic.
public sealed record MockReportPassPredictionV1(
    string ConfidenceBand,
    string Verdict,
    string Rationale);

// Trend block requires at least 2 completed reports for the same learner.
// Populated by Phase 3's MockReadinessTrendService.
public sealed record MockReportTrendV1(
    int AttemptsConsidered,
    string OverallTrend,
    bool ConsistentGreen,
    string Message);

public sealed record MockReportPerQuestionTimingV1(
    string SectionId,
    string Subtest,
    string ItemId,
    int? SecondsSpent,
    bool? Correct);

public sealed record MockReportTeacherFeedbackFragmentV1(
    string Subtest,
    string ReviewRequestId,
    string Criterion,
    string Comment,
    string? AnchorRef);
