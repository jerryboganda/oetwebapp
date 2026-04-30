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
    string? TargetCountry = null);

public record MockSectionStartRequest(Dictionary<string, object?>? ClientState = null);

public record MockSectionCompleteRequest(
    string? ContentAttemptId,
    int? RawScore,
    int? RawScoreMax,
    int? ScaledScore,
    string? Grade,
    Dictionary<string, object?>? Evidence,
    string? ReviewTurnaroundOption = null);

public record AdminMockBundleCreateRequest(
    string Title,
    string MockType,
    string? SubtestCode,
    string? ProfessionId,
    bool AppliesToAllProfessions,
    string? SourceProvenance,
    int? Priority,
    string? TagsCsv);

public record AdminMockBundleUpdateRequest(
    string? Title,
    string? MockType,
    string? SubtestCode,
    string? ProfessionId,
    bool? AppliesToAllProfessions,
    string? SourceProvenance,
    int? Priority,
    string? TagsCsv,
    ContentStatus? Status);

public record AdminMockBundleSectionRequest(
    string ContentPaperId,
    int? SectionOrder,
    int? TimeLimitMinutes,
    bool? ReviewEligible);

public record AdminMockBundleReorderRequest(IReadOnlyList<string> SectionIds);

public record RevisionSubmitRequest(string Content, string? IdempotencyKey);

public record WalletTopUpRequest(
    int Amount,
    string Gateway,
    string? IdempotencyKey = null);
