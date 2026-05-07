using OetLearner.Api.Domain;

namespace OetLearner.Api.Contracts;

public record PatchGoalsRequest(
    string? ProfessionId,
    DateOnly? TargetExamDate,
    string? OverallGoal,
    string? ExamFamilyCode,
    int? TargetWritingScore,
    int? TargetSpeakingScore,
    int? TargetReadingScore,
    int? TargetListeningScore,
    int? PreviousAttempts,
    List<string>? WeakSubtests,
    int? StudyHoursPerWeek,
    string? TargetCountry,
    string? TargetOrganization,
    Dictionary<string, object?>? DraftState);

public record PatchSectionRequest(Dictionary<string, object?> Values);

public record CreateAttemptRequest(
    string ContentId,
    string? Context,
    string? Mode,
    string? DeviceType,
    string? ParentAttemptId);

public record DraftUpdateRequest(
    string? Content,
    string? Scratchpad,
    Dictionary<string, bool>? Checklist,
    int? DraftVersion);

public record HeartbeatRequest(int ElapsedSeconds, string? DeviceType);

public record SubmitAttemptRequest(string? Content, string? IdempotencyKey);

public record UploadCompleteRequest(
    string? UploadSessionId,
    string? StorageKey,
    string? FileName,
    long? SizeBytes,
    int? DurationSeconds,
    string? CaptureMethod,
    string? ContentType,
    bool? ConsentAccepted,
    string? ConsentText,
    DateTimeOffset? ConsentAcceptedAt);

public record AnswersUpdateRequest(Dictionary<string, string?> Answers);

public record DeviceCheckRequest(
    bool MicrophoneGranted,
    bool NetworkStable,
    string? DeviceType,
    string? TaskId = null,
    double? NoiseLevel = null,
    bool? NoiseAcceptable = null);

// Wave 3 of docs/SPEAKING-MODULE-PLAN.md.
public record StartSpeakingMockSetRequest(string? Mode = "exam");

public record ReviewRequestCreateRequest(
    string AttemptId,
    string Subtest,
    string TurnaroundOption,
    List<string> FocusAreas,
    string? LearnerNotes,
    string PaymentSource,
    string? IdempotencyKey);

public record CheckoutSessionCreateRequest(
    string ProductType,
    int Quantity,
    string? PriceId,
    string? CouponCode = null,
    List<string>? AddOnCodes = null,
    string? QuoteId = null,
    string? IdempotencyKey = null,
    string? Gateway = null);

public record StudyPlanRescheduleRequest(DateOnly? DueDate);

public record StudyPlanSwapRequest(string? ReplacementContentId);

public record MockAttemptCreateRequest(
    string MockType,
    string? SubType,
    string Mode,
    string Profession,
    bool IncludeReview,
    bool StrictTimer,
    string? ReviewSelection = null,
    string? BundleId = null,
    string? TargetCountry = null,
    /// <summary>Spec §2 delivery model — computer (default), paper, or oet_home.</summary>
    string? DeliveryMode = null,
    /// <summary>Spec §3 strictness — learning, exam (default for non-diagnostic), or final_readiness.</summary>
    string? Strictness = null);

public record MockSectionStartRequest(Dictionary<string, object?>? ClientState = null);

public record MockSectionCompleteRequest(
    string? ContentAttemptId,
    int? RawScore,
    int? RawScoreMax,
    int? ScaledScore,
    string? Grade,
    Dictionary<string, object?>? Evidence,
    string? ReviewTurnaroundOption = null);

/// <summary>
/// Mocks V2 Wave 2 — proctoring telemetry batch.
/// Frontend sends 1..N events; backend rate-limits to <see cref="OetLearner.Api.Services.MockService.ProctoringEventCap"/> per attempt.
/// </summary>
public record MockProctoringEventBatchRequest(IReadOnlyList<MockProctoringEventInput> Events);

public record MockProctoringEventInput(
    string Kind,
    DateTimeOffset OccurredAt,
    string? MockSectionAttemptId = null,
    string? Severity = null,
    Dictionary<string, object?>? Metadata = null);

public record MockBookingAssignmentRequest(
    string? AssignedTutorId = null,
    string? AssignedInterlocutorId = null,
    string? Status = null);

public record MockBookingCreateRequest(
    string MockBundleId,
    DateTimeOffset ScheduledStartAt,
    string? TimezoneIana = null,
    string? DeliveryMode = null,
    bool ConsentToRecording = false,
    string? LearnerNotes = null);

public record MockBookingRescheduleRequest(
    DateTimeOffset ScheduledStartAt,
    string? TimezoneIana = null);

public record MockBookingUpdateRequest(
    DateTimeOffset? ScheduledStartAt = null,
    string? TimezoneIana = null,
    string? Status = null,
    bool? ConsentToRecording = null,
    string? LearnerNotes = null);

// Mocks V2 Wave 6 — Live-room transition (Speaking).
public record LiveRoomTransitionRequest(string TargetState);

public record MockBookingRecordingFinalizeRequest(long? DurationMs);

public record MockLeakReportRequest(
    string? MockBundleId,
    string? MockAttemptId,
    string? Reason,
    string? EvidenceUrl = null,
    string? PageOrQuestion = null);

/// <summary>
/// Mocks Wave 8 — admin update body for a <see cref="OetLearner.Api.Domain.MockContentReview"/>
/// row. Status transitions are validated by the service.
/// </summary>
public record MockLeakReportUpdateRequest(
    string Status,
    string? ResolutionNote = null);

/// <summary>
/// Mocks Wave 8 — admin-facing summary of a <see cref="OetLearner.Api.Domain.MockContentReview"/>
/// row. Avoids learner email; uses display name only.
/// </summary>
public record MockLeakReportSummary(
    string Id,
    string? BundleId,
    string? BundleTitle,
    string? AttemptId,
    string Severity,
    string Status,
    string? ReasonCode,
    string? Details,
    string? EvidenceUrl,
    string? PageOrQuestion,
    string? ReportedByUserId,
    string? ReportedByUserDisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolvedByAdminId,
    string? ResolutionNote);

public record AdminMockBundleCreateRequest(
    string Title,
    string MockType,
    string? SubtestCode,
    string? ProfessionId,
    bool AppliesToAllProfessions,
    string? SourceProvenance,
    int? Priority,
    string? TagsCsv,
    string? Difficulty = null,
    string? SourceStatus = null,
    string? QualityStatus = null,
    string? ReleasePolicy = null,
    string? TopicTagsCsv = null,
    string? SkillTagsCsv = null,
    bool? WatermarkEnabled = null,
    bool? RandomiseQuestions = null);

public record AdminMockBundleUpdateRequest(
    string? Title,
    string? MockType,
    string? SubtestCode,
    string? ProfessionId,
    bool? AppliesToAllProfessions,
    string? SourceProvenance,
    int? Priority,
    string? TagsCsv,
    ContentStatus? Status,
    string? Difficulty = null,
    string? SourceStatus = null,
    string? QualityStatus = null,
    string? ReleasePolicy = null,
    string? TopicTagsCsv = null,
    string? SkillTagsCsv = null,
    bool? WatermarkEnabled = null,
    bool? RandomiseQuestions = null);

public record AdminMockBundleSectionRequest(
    string ContentPaperId,
    int? SectionOrder,
    int? TimeLimitMinutes,
    bool? ReviewEligible);

public record AdminMockBundleReorderRequest(IReadOnlyList<string> SectionIds);

public record RevisionSubmitRequest(string Content, string? IdempotencyKey);

/// <summary>
/// Generates a payout batch for completed expert reviews in a pay window.
/// All fields optional: defaults to last 30 days, $5/review when not set on
/// the review row. Used by <c>AdminService.GenerateExpertPayoutsAsync</c>.
/// </summary>
public record GenerateExpertPayoutsRequest(
    DateTimeOffset? PayPeriodStart = null,
    DateTimeOffset? PayPeriodEnd = null,
    decimal? DefaultCompensationPerReview = null);

public record WalletTopUpRequest(
    int Amount,
    string Gateway,
    string? IdempotencyKey = null);

public record RefundIssueRequest(
    decimal Amount,
    string Reason,
    string IdempotencyKey,
    string? AdminNote = null);

public record AdminWalletSpendRequest(
    string WalletId,
    int Amount,
    string Reason,
    string? ReferenceId = null,
    string? IdempotencyKey = null);

public record IeltsWritingTask1Request(
    string Content,
    string GraphType,
    int WordCount,
    string? TargetBand = null);

public record IeltsWritingTask2Request(
    string Content,
    string Prompt,
    string EssayType,
    int WordCount,
    string? TargetBand = null);

public record IeltsModuleResultsRequest(
    double ListeningBand,
    double ReadingBand,
    double WritingBand,
    double SpeakingBand,
    string Pathway);
