using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Contracts;

/// <summary>
/// Writing Module V2 — request/response DTOs.
///
/// The shape of every record here is the API contract with the frontend
/// (<c>lib/writing/api.ts</c> + <c>lib/writing/types.ts</c>). ASP.NET Core's
/// default minimal-API JSON serializer converts PascalCase property names to
/// camelCase, which matches the TS expectations exactly.
///
/// Do NOT duplicate DTOs already declared in <see cref="WritingPathwayContracts" />.
/// V2 shapes use the <c>V2</c> suffix where their structure diverges from V1.
/// </summary>

// ─────────────────────────────────────────────────────────────────────────────
// Onboarding & profile
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingAccommodationProfileDto(
    decimal ExtendedTimerMultiplier,
    bool LargeText,
    bool ScreenReaderMode,
    bool DyslexiaFriendlyFont);

public sealed record WritingProfileResponseV2(
    string UserId,
    string CurrentStage,
    string Profession,
    string? SubDiscipline,
    int? YearsExperience,
    string TargetBand,
    string? ExamDate,
    int DaysPerWeek,
    int MinutesPerDay,
    string TargetCountry,
    IReadOnlyList<string> LetterTypeFocus,
    int? ReadinessScore,
    int? PredictedScore,
    DateTimeOffset? OnboardingCompletedAt,
    DateTimeOffset? PathwayGeneratedAt,
    int? WeeksRemaining,
    bool DiagnosticCompleted,
    bool OptInCommunity,
    bool OptInLeaderboard,
    bool OptInDataForTraining,
    WritingAccommodationProfileDto? AccommodationProfile,
    string? CanonVersionPinned);

public sealed record WritingProfileUpdateRequest(
    [property: Required, StringLength(64)] string Profession,
    [property: StringLength(64)] string? SubDiscipline,
    [property: Range(0, 80)] int? YearsExperience,
    [property: Required, StringLength(8)] string TargetBand,
    string? ExamDate,
    [property: Range(1, 7)] int DaysPerWeek,
    [property: Range(15, 240)] int MinutesPerDay,
    [property: StringLength(32)] string? TargetCountry,
    IReadOnlyList<string>? LetterTypeFocus,
    bool? OptInCommunity,
    bool? OptInLeaderboard,
    bool? OptInDataForTraining);

public sealed record WritingBudgetAssessmentResponse(
    int MinutesAvailablePerDay,
    int MinutesPerDayMin,
    int MinutesPerDayMax,
    int DaysPerWeek,
    int? WeeksToExam,
    int TotalMinutes,
    IReadOnlyList<string> ConflictsWithOtherModules);

// ─────────────────────────────────────────────────────────────────────────────
// Diagnostic
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingDiagnosticStartRequest(Guid? ScenarioId);

public sealed record WritingDiagnosticSessionResponse(
    Guid Id,
    Guid ScenarioId,
    string Phase,
    int ReadingSecondsRemaining,
    int WritingSecondsRemaining,
    DateTimeOffset StartedAt,
    DateTimeOffset? ReadingPhaseEndedAt,
    DateTimeOffset? SubmittedAt,
    Guid? SubmissionId);

public sealed record WritingDiagnosticSubmitRequest(
    [property: Required] string LetterContent,
    [property: Range(0, 5000)] int WordCount,
    [property: Range(0, 7200)] int TimeSpentSeconds);

public sealed record WritingDiagnosticResultsResponse(
    Guid SessionId,
    Guid SubmissionId,
    WritingGradeResponseV2 Grade,
    WritingPathwayResponseV2 PathwayPreview);

// ─────────────────────────────────────────────────────────────────────────────
// Pathway V2 / today
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingPathwayItemResponse(
    Guid Id,
    int OrderIndex,
    string Stage,
    string Phase,
    int WeekNumber,
    string? FocusSkill,
    string? FocusCriterion,
    string ItemKind,
    string? ContentRefId,
    string Title,
    string Description,
    int EstimatedMinutes,
    bool IsCompleted);

public sealed record WritingPathwayResponseV2(
    string CurrentStage,
    int TotalWeeks,
    int CurrentWeek,
    int WeeksRemaining,
    int ReadinessScore,
    string? PredictedBand,
    DateTimeOffset? GeneratedAt,
    DateTimeOffset? LastRecalculatedAt,
    IReadOnlyDictionary<string, double> WeaknessVector,
    IReadOnlyDictionary<string, double> SubSkillMastery,
    IReadOnlyList<WritingPathwayItemResponse> Items);

public sealed record WritingTodayPlanItemResponseV2(
    Guid Id,
    int Ordinal,
    string ItemKind,
    string? FocusSkill,
    string? FocusCriterion,
    int EstimatedMinutes,
    string Title,
    string Description,
    string ActionHref,
    string? ContentId,
    string Status);

public sealed record WritingTodayPlanResponseV2(
    string Date,
    IReadOnlyList<WritingTodayPlanItemResponseV2> Items,
    int TotalMinutes,
    int CompletedCount,
    int RegenerationsRemaining);

// ─────────────────────────────────────────────────────────────────────────────
// Submissions / grades / appeals / disputes
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingSubmissionCreateRequest(
    [property: Required] Guid ScenarioId,
    [property: Required, StringLength(16)] string Mode,
    [property: Required] string LetterContent,
    [property: Range(0, 5000)] int WordCount,
    [property: Range(0, 7200)] int TimeSpentSeconds,
    [property: StringLength(16)] string? InputSource,
    [property: StringLength(32)] string? SimulationMode);

public sealed record WritingReviseRequest(
    [property: Required] string LetterContent,
    [property: Range(0, 5000)] int WordCount,
    [property: Range(0, 7200)] int TimeSpentSeconds);

public sealed record WritingAppealRequest(
    [property: StringLength(1000)] string? Reason);

public sealed record WritingDisputeViolationRequest(
    [property: Required] Guid ViolationId,
    [property: Required, StringLength(8)] string RuleId,
    [property: Required, StringLength(500)] string Reason);

public sealed record WritingSubmissionResponse(
    Guid Id,
    string UserId,
    Guid ScenarioId,
    string Mode,
    string LetterContent,
    string ContentHash,
    int WordCount,
    int TimeSpentSeconds,
    DateTimeOffset StartedAt,
    DateTimeOffset SubmittedAt,
    bool IsRevision,
    Guid? OriginalSubmissionId,
    string Status,
    string GradingTier,
    string InputSource);

public sealed record WritingPerCriterionFeedbackResponse(
    int Score,
    string Feedback,
    string? ExemplarFix,
    IReadOnlyList<string> CitedRuleIds);

public sealed record WritingExemplarComparisonHighlightResponse(
    string CandidateSnippet,
    string ExemplarSnippet,
    string Kind);

public sealed record WritingExemplarComparisonResponse(
    Guid ExemplarId,
    string ExemplarLetterType,
    double SimilarityScore,
    IReadOnlyList<WritingExemplarComparisonHighlightResponse> HighlightedDifferences);

public sealed record WritingRevisionInviteResponse(bool ShouldOffer, string Reason);

public sealed record WritingGradeResponseV2(
    Guid Id,
    Guid SubmissionId,
    int C1Purpose,
    int C2Content,
    int C3Conciseness,
    int C4Genre,
    int C5Organisation,
    int C6Language,
    int RawTotal,
    int EstimatedBand,
    string BandLabel,
    IReadOnlyDictionary<string, WritingPerCriterionFeedbackResponse> PerCriterion,
    IReadOnlyList<string> TopThreePriorities,
    string ConfidenceFlag,
    string ModelUsed,
    string CanonVersion,
    IReadOnlyList<WritingCanonViolationResponse> CanonViolations,
    WritingExemplarComparisonResponse? ExemplarComparison,
    WritingRevisionInviteResponse RevisionInvite,
    DateTimeOffset GradedAt);

public sealed record WritingScoreAppealResponse(
    Guid Id,
    Guid SubmissionId,
    string Status,
    int OriginalRawTotal,
    int? SecondOpinionRawTotal,
    int? FinalRawTotal,
    string? Reasoning,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ResolvedAt);

// ─────────────────────────────────────────────────────────────────────────────
// Drafts V2
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingDraftV2UpsertRequest(
    [property: Required] string Content,
    [property: Range(0, 5000)] int WordCount,
    [property: Range(0, 7200)] int TimeSpentSeconds);

public sealed record WritingDraftV2Response(
    string UserId,
    Guid ScenarioId,
    string Mode,
    string Content,
    int WordCount,
    int TimeSpentSeconds,
    DateTimeOffset LastSavedAt);

// ─────────────────────────────────────────────────────────────────────────────
// Scenarios
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingScenarioStructuredSentenceResponse(
    int Index,
    string Text,
    string Relevance);

public sealed record WritingScenarioResponse(
    Guid Id,
    string Title,
    string LetterType,
    string Profession,
    string? SubDiscipline,
    IReadOnlyList<string> Topics,
    int Difficulty,
    string CaseNotesMarkdown,
    IReadOnlyList<WritingScenarioStructuredSentenceResponse> CaseNotesStructured,
    bool IsDiagnostic,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? StimulusPdfMediaAssetId = null,
    string? StimulusPdfDownloadPath = null);

public sealed record WritingScenarioListResponse(
    IReadOnlyList<WritingScenarioResponse> Items,
    int Total);

public sealed record WritingScenarioUpsertRequest(
    [property: Required, StringLength(200)] string Title,
    [property: Required, StringLength(8)] string LetterType,
    [property: Required, StringLength(32)] string Profession,
    [property: StringLength(64)] string? SubDiscipline,
    IReadOnlyList<string>? Topics,
    [property: Range(1, 5)] int Difficulty,
    [property: Required] string CaseNotesMarkdown,
    IReadOnlyList<WritingScenarioStructuredSentenceResponse>? CaseNotesStructured,
    bool? IsDiagnostic,
    [property: StringLength(16)] string? Status);

public sealed record WritingScenarioGenerateRequest(
    [property: Required, StringLength(32)] string Profession,
    [property: Required, StringLength(8)] string LetterType,
    [property: Range(1, 5)] int Difficulty,
    [property: StringLength(500)] string? Topic,
    [property: StringLength(500)] string? Instructions);

// ─────────────────────────────────────────────────────────────────────────────
// Exemplars
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingExemplarAnnotationResponse(
    Guid Id,
    int CharStart,
    int CharEnd,
    string? RuleId,
    string Note);

public sealed record WritingExemplarResponse(
    Guid Id,
    Guid? ScenarioId,
    string Profession,
    string LetterType,
    int Difficulty,
    string TargetBand,
    string LetterContent,
    IReadOnlyList<WritingExemplarAnnotationResponse> Annotations,
    string? AuthorNote,
    string Status);

public sealed record WritingExemplarListResponse(
    IReadOnlyList<WritingExemplarResponse> Items,
    int Total);

public sealed record WritingExemplarUpsertRequest(
    Guid? ScenarioId,
    [property: Required, StringLength(32)] string Profession,
    [property: Required, StringLength(8)] string LetterType,
    [property: Range(1, 5)] int Difficulty,
    [property: Required, StringLength(8)] string TargetBand,
    [property: Required] string LetterContent,
    IReadOnlyList<WritingExemplarAnnotationResponse>? Annotations,
    [property: StringLength(1000)] string? AuthorNote,
    [property: StringLength(16)] string? Status);

public sealed record WritingExemplarTestGradeResponse(
    Guid ExemplarId,
    WritingGradeResponseV2 Grade,
    bool PassesQualityBar);

// ─────────────────────────────────────────────────────────────────────────────
// Drills
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingDrillResponse(
    Guid Id,
    string DrillType,
    string InputVariant,
    string TargetSubSkill,
    string? TargetCanonRuleId,
    IReadOnlyList<string> AppliesToProfessions,
    IReadOnlyList<string> AppliesToLetterTypes,
    int Difficulty,
    string PromptMarkdown,
    string? ExpectedAnswer,
    IReadOnlyList<string>? Alternatives,
    IReadOnlyList<string>? Options,
    string GradingMethod,
    string Status);

public sealed record WritingDrillListResponse(
    IReadOnlyList<WritingDrillResponse> Items,
    int Total);

public sealed record WritingDrillAttemptRequestV2(
    [property: Required] Guid DrillId,
    [property: Required, StringLength(4000)] string ResponseText,
    int? SelectedOptionIndex,
    IReadOnlyList<string>? OrderedItems);

public sealed record WritingDrillAttemptResultResponse(
    Guid DrillId,
    bool IsCorrect,
    string FeedbackText,
    string? CitedRuleId,
    DateTimeOffset NextDueAt,
    double EaseFactor);

public sealed record WritingDrillUpsertRequest(
    [property: Required, StringLength(64)] string DrillType,
    [property: Required, StringLength(8)] string InputVariant,
    [property: Required, StringLength(4)] string TargetSubSkill,
    [property: StringLength(8)] string? TargetCanonRuleId,
    IReadOnlyList<string>? AppliesToProfessions,
    IReadOnlyList<string>? AppliesToLetterTypes,
    [property: Range(1, 5)] int Difficulty,
    [property: Required] string PromptMarkdown,
    string? ExpectedAnswer,
    IReadOnlyList<string>? Alternatives,
    IReadOnlyList<string>? Options,
    [property: Required, StringLength(16)] string GradingMethod,
    [property: StringLength(16)] string? Status);

// ─────────────────────────────────────────────────────────────────────────────
// Case-note drills
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingCaseNoteDrillSentenceResponseV2(
    int Index,
    string Text,
    string? Relevance);

public sealed record WritingCaseNoteDrillResponseV2(
    Guid Id,
    string Format,
    Guid? ScenarioId,
    string Profession,
    string PromptMarkdown,
    IReadOnlyList<WritingCaseNoteDrillSentenceResponseV2> Sentences,
    IReadOnlyList<string>? Options,
    string Status);

public sealed record WritingCaseNoteDrillListResponseV2(
    IReadOnlyList<WritingCaseNoteDrillResponseV2> Items,
    int Total);

public sealed record WritingCaseNoteDrillAttemptRequestV2(
    [property: Required] IReadOnlyList<int> SelectedIndices);

public sealed record WritingCaseNoteDrillSentenceVerdictResponse(
    int Index,
    string? LearnerLabel,
    string CorrectLabel,
    string Verdict);

public sealed record WritingCaseNoteDrillAttemptResultResponseV2(
    Guid DrillId,
    int ScorePercent,
    IReadOnlyList<WritingCaseNoteDrillSentenceVerdictResponse> PerSentence);

// ─────────────────────────────────────────────────────────────────────────────
// Lessons V2
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingLessonQuizQuestionResponseV2(
    string Id,
    string Question,
    IReadOnlyList<string> Options,
    int CorrectIndex,
    string Explanation);

public sealed record WritingLessonResponseV2(
    Guid Id,
    string SubSkill,
    int OrderInCourse,
    string Title,
    string BodyMarkdown,
    string? VideoUrl,
    int EstimatedMinutes,
    IReadOnlyList<WritingLessonQuizQuestionResponseV2> QuizQuestions,
    string Status);

public sealed record WritingLessonCompletionResponseV2(
    Guid LessonId,
    DateTimeOffset CompletedAt,
    int QuizScore,
    int QuizAttempts);

public sealed record WritingLessonListResponseV2(
    IReadOnlyList<WritingLessonResponseV2> Items,
    IReadOnlyList<WritingLessonCompletionResponseV2> Completions);

public sealed record WritingLessonCompleteRequestV2(
    [property: Range(0, 5)] int QuizScore,
    IReadOnlyList<int>? QuizAnswers);

public sealed record WritingLessonUpsertRequest(
    [property: Required, StringLength(4)] string SubSkill,
    [property: Range(0, 999)] int OrderInCourse,
    [property: Required, StringLength(200)] string Title,
    [property: Required] string BodyMarkdown,
    [property: StringLength(500)] string? VideoUrl,
    [property: Range(1, 60)] int EstimatedMinutes,
    IReadOnlyList<WritingLessonQuizQuestionResponseV2>? QuizQuestions,
    [property: StringLength(16)] string? Status);

// ─────────────────────────────────────────────────────────────────────────────
// Mocks
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingMockResponse(
    Guid Id,
    Guid ScenarioId,
    string Title,
    string Status);

public sealed record WritingMockListResponse(
    IReadOnlyList<WritingMockResponse> Items);

public sealed record WritingMockStartRequest([property: Required] Guid MockId);

public sealed record WritingMockSessionResponse(
    Guid Id,
    Guid MockId,
    Guid ScenarioId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? ReadingPhaseEndedAt,
    DateTimeOffset? SubmittedAt,
    Guid? SubmissionId,
    int ReadingSecondsRemaining,
    int WritingSecondsRemaining);

public sealed record WritingMockSubmitRequest(
    [property: Required] string LetterContent,
    [property: Range(0, 5000)] int WordCount,
    [property: Range(0, 7200)] int TimeSpentSeconds);

public sealed record WritingMockResultsResponse(
    WritingMockSessionResponse Session,
    WritingGradeResponseV2 Grade);

// ─────────────────────────────────────────────────────────────────────────────
// Coach (Haiku 4.5 hints)
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingCoachHintRequest(
    [property: Required] string SessionId,
    [property: Required] Guid ScenarioId,
    [property: Required] string LetterContent,
    [property: Range(0, 5000)] int WordCount,
    [property: Required, StringLength(8)] string LetterType,
    [property: Required, StringLength(32)] string Profession);

public sealed record WritingCoachHintResponse(
    string Id,
    string SessionId,
    string Category,
    string Text,
    string? RuleId,
    int? CharStart,
    int? CharEnd,
    DateTimeOffset CreatedAt,
    bool Dismissed);

public sealed record WritingCoachHintsResponse(IReadOnlyList<WritingCoachHintResponse> Hints);

// ─────────────────────────────────────────────────────────────────────────────
// Stats & readiness
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingReadinessSubScoresResponse(
    int MockAverage,
    int Trajectory,
    int CanonCleanRate,
    int TimeMgmt,
    int TypeConsistency);

public sealed record WritingReadinessResponseV2(
    string Date,
    int Score,
    WritingReadinessSubScoresResponse SubScores,
    string? PredictedBand,
    int? DeltaVsLastWeek,
    DateTimeOffset ComputedAt);

public sealed record WritingBandHistoryPointResponse(
    Guid SubmissionId,
    string Date,
    int RawTotal,
    int EstimatedBand,
    string LetterType,
    bool IsRevision);

public sealed record WritingCriteriaScoresResponse(
    double C1,
    double C2,
    double C3,
    double C4,
    double C5,
    double C6);

public sealed record WritingStatsDashboardResponse(
    string? LatestBand,
    int? LatestRawTotal,
    int TrendDeltaLastFive,
    string TargetBand,
    int? DaysToExam,
    int StreakDays,
    string? TopWeaknessCriterion,
    string? TopWeaknessLabel,
    WritingReadinessResponseV2? Readiness);

public sealed record WritingStatsBandsResponse(
    IReadOnlyList<WritingBandHistoryPointResponse> History,
    int? TargetBand);

public sealed record WritingStatsCriteriaResponse(
    WritingCriteriaScoresResponse Current,
    WritingCriteriaScoresResponse Target);

public sealed record WritingStatsLetterTypeRowResponse(
    string LetterType,
    int Attempts,
    double AverageBand);

public sealed record WritingStatsLetterTypesResponse(IReadOnlyList<WritingStatsLetterTypeRowResponse> Rows);

public sealed record WritingStatsCanonRuleStatResponse(
    string RuleId,
    string RuleText,
    int Count,
    int TrendLast30Days);

public sealed record WritingStatsCanonResponse(IReadOnlyList<WritingStatsCanonRuleStatResponse> TopViolations);

public sealed record WritingStatsTimeBucketResponse(string BucketLabel, int Count);

public sealed record WritingStatsTimeResponse(
    int AverageCompletionSeconds,
    int PercentCompletedWithin40Min,
    IReadOnlyList<WritingStatsTimeBucketResponse> Distribution);

public sealed record WritingStatsSkillsResponse(IReadOnlyDictionary<string, double> Mastery);

public sealed record WritingStatsCalendarDayResponse(string Date, int Count);

public sealed record WritingStatsCalendarResponse(IReadOnlyList<WritingStatsCalendarDayResponse> Days);

public sealed record WritingStatsExportResponse(string Url);

// ─────────────────────────────────────────────────────────────────────────────
// Canon library
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingCanonRuleResponseV2(
    string Id,
    string Category,
    IReadOnlyList<string> AppliesToLetterTypes,
    IReadOnlyList<string> AppliesToProfessions,
    string Severity,
    string RuleText,
    IReadOnlyList<string> CorrectExamples,
    IReadOnlyList<string> IncorrectExamples,
    string DetectionType,
    string? LessonId,
    string Version,
    bool Active);

public sealed record WritingCanonRuleListResponseV2(IReadOnlyList<WritingCanonRuleResponseV2> Items);

public sealed record WritingCanonViolationResponse(
    Guid Id,
    Guid SubmissionId,
    string RuleId,
    string RuleText,
    string Severity,
    string Snippet,
    int LineNumber,
    int CharStart,
    int CharEnd,
    string? SuggestedFix,
    bool Disputed,
    string? DisputeResolution);

public sealed record WritingCanonViolationListResponse(IReadOnlyList<WritingCanonViolationResponse> Items);

public sealed record WritingCanonRuleUpsertRequest(
    [property: Required, StringLength(64)] string Id,
    [property: Required, StringLength(64)] string Category,
    IReadOnlyList<string>? AppliesToLetterTypes,
    IReadOnlyList<string>? AppliesToProfessions,
    [property: Required, StringLength(16)] string Severity,
    [property: Required] string RuleText,
    IReadOnlyList<string>? CorrectExamples,
    IReadOnlyList<string>? IncorrectExamples,
    [property: Required, StringLength(16)] string DetectionType,
    string? DetectionConfigJson,
    string? LessonId,
    [property: StringLength(32)] string? Version,
    bool? Active);

public sealed record WritingCanonRuleTestRequest(
    [property: Required] string Letter,
    [property: StringLength(8)] string? LetterType,
    [property: StringLength(32)] string? Profession);

public sealed record WritingCanonRuleTestResponse(
    string RuleId,
    bool Triggered,
    IReadOnlyList<WritingCanonViolationResponse> Violations);

// ─────────────────────────────────────────────────────────────────────────────
// Mistakes
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingCommonMistakeResponse(
    Guid Id,
    string Category,
    string Summary,
    string ExampleWrong,
    string ExampleRight,
    string? CanonRuleId,
    string? RelatedSubSkill);

public sealed record WritingCommonMistakeListResponse(IReadOnlyList<WritingCommonMistakeResponse> Items);

public sealed record WritingLearnerMistakeStatResponse(
    Guid MistakeId,
    int OccurrenceCount,
    DateTimeOffset LastOccurredAt);

public sealed record WritingLearnerMistakeRowResponse(
    Guid Id,
    string Category,
    string Summary,
    string ExampleWrong,
    string ExampleRight,
    string? CanonRuleId,
    string? RelatedSubSkill,
    WritingLearnerMistakeStatResponse Stat);

public sealed record WritingLearnerMistakeListResponse(IReadOnlyList<WritingLearnerMistakeRowResponse> Items);

public sealed record WritingMistakeUpsertRequest(
    [property: Required, StringLength(64)] string Category,
    [property: Required, StringLength(500)] string Summary,
    [property: Required] string ExampleWrong,
    [property: Required] string ExampleRight,
    [property: StringLength(8)] string? CanonRuleId,
    [property: StringLength(4)] string? RelatedSubSkill);

// ─────────────────────────────────────────────────────────────────────────────
// Tutor review (learner-facing)
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingTutorReviewResponse(
    Guid Id,
    Guid SubmissionId,
    string TutorId,
    string? TutorDisplayName,
    string Status,
    string? FreeTextFeedback,
    IReadOnlyDictionary<string, string>? PerCriterionComments,
    IReadOnlyDictionary<string, double>? ScoreOverride,
    DateTimeOffset? SubmittedAt);

public sealed record WritingTutorReviewRequestPayload(
    [property: StringLength(16)] string? Priority);

// ─────────────────────────────────────────────────────────────────────────────
// Tutor portal
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingTutorQueueItemResponse(
    Guid SubmissionId,
    string UserId,
    string Profession,
    string LetterType,
    int WordCount,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ClaimedAt,
    string? ClaimedByTutorId,
    string Status);

public sealed record WritingTutorQueueResponse(IReadOnlyList<WritingTutorQueueItemResponse> Items);

public sealed record WritingTutorReviewSubmitRequest(
    [property: Required] Guid SubmissionId,
    [property: StringLength(4000)] string? FreeTextFeedback,
    IReadOnlyDictionary<string, string>? PerCriterionComments,
    IReadOnlyDictionary<string, double>? ScoreOverride);

public sealed record WritingTutorReviewDetailResponse(
    WritingSubmissionResponse Submission,
    WritingGradeResponseV2? Grade);

public sealed record WritingTutorCalibrationResponse(
    string TutorId,
    double AgreementCoefficient,
    bool RequiresRecalibration,
    DateTimeOffset? LastCalibratedAt);

// ─────────────────────────────────────────────────────────────────────────────
// OCR
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingOcrJobResponse(
    Guid Id,
    Guid? SubmissionId,
    string Status,
    string? Provider,
    int? ConfidenceScore,
    string? ExtractedText,
    IReadOnlyList<string> ImageUrls,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

// ─────────────────────────────────────────────────────────────────────────────
// Showcase
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingShowcasePostResponse(
    Guid Id,
    Guid SubmissionId,
    string AnonymizedLetterContent,
    string Profession,
    string LetterType,
    string Status,
    DateTimeOffset PublishedAt,
    int ReactionCount);

public sealed record WritingShowcaseListResponse(
    IReadOnlyList<WritingShowcasePostResponse> Items,
    int Total);

// ─────────────────────────────────────────────────────────────────────────────
// Tools (rewrite / paraphrase / ask / outline)
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingRewriteRequest(
    [property: Required] string Text,
    [property: Required, StringLength(8)] string LetterType,
    [property: Required, StringLength(32)] string Profession,
    bool? PreserveFacts);

public sealed record WritingDiffSegmentResponse(string Kind, string Text);

public sealed record WritingRewriteResultResponse(
    string OriginalText,
    string RewrittenText,
    IReadOnlyList<WritingDiffSegmentResponse> Diff);

public sealed record WritingParaphraseRequest(
    [property: Required] string Text,
    [property: StringLength(16)] string? Formality);

public sealed record WritingParaphraseAlternativeResponse(string Formality, string Text);

public sealed record WritingParaphraseResultResponse(
    string OriginalText,
    IReadOnlyList<WritingParaphraseAlternativeResponse> Alternatives);

public sealed record WritingAskRequest(
    [property: StringLength(64)] string? ThreadId,
    [property: Required] string LetterContent,
    [property: Required] Guid ScenarioId,
    [property: Required, StringLength(2000)] string Question);

public sealed record WritingAskMessageResponse(
    string Role,
    string Content,
    DateTimeOffset CreatedAt);

public sealed record WritingAskTurnResponse(
    string ThreadId,
    WritingAskMessageResponse Reply);

public sealed record WritingOutlineRequest(
    [property: Required] Guid ScenarioId,
    [property: Required, StringLength(8)] string LetterType,
    [property: Required, StringLength(32)] string Profession);

public sealed record WritingOutlineParagraphResponse(
    int Paragraph,
    string Purpose,
    IReadOnlyList<string> SuggestedSentences);

public sealed record WritingOutlineResultResponse(
    Guid ScenarioId,
    IReadOnlyList<WritingOutlineParagraphResponse> Outline);

// ─────────────────────────────────────────────────────────────────────────────
// Admin content audit
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingContentAuditEntryResponse(
    Guid Id,
    string EntityType,
    string EntityId,
    string Action,
    string ActorUserId,
    string? Note,
    DateTimeOffset OccurredAt);

public sealed record WritingContentAuditListResponse(IReadOnlyList<WritingContentAuditEntryResponse> Items);
