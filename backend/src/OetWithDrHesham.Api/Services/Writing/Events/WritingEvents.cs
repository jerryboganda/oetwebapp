namespace OetWithDrHesham.Api.Services.Writing.Events;

/// <summary>
/// Writing Module V2 domain events. Published via <see cref="IWritingEventBus"/>
/// from services after a transaction commits. Handlers receive events on the
/// publishing thread (fire-and-forget Task.Run inside the bus so the publisher
/// is never blocked). Handlers MUST be tolerant of duplicate deliveries.
/// </summary>
public abstract record WritingEvent(string UserId, DateTimeOffset OccurredAt);

/// <summary>Raised when a learner submits a letter for grading.</summary>
public sealed record WritingSubmissionCreated(
    string UserId,
    Guid SubmissionId,
    Guid ScenarioId,
    string Mode,
    string GradingTier,
    string InputSource,
    DateTimeOffset OccurredAt) : WritingEvent(UserId, OccurredAt);

/// <summary>Raised when a grade is persisted for a submission.</summary>
public sealed record WritingGradeReady(
    string UserId,
    Guid SubmissionId,
    Guid GradeId,
    short RawTotal,
    int EstimatedBand,
    string BandLabel,
    DateTimeOffset OccurredAt) : WritingEvent(UserId, OccurredAt);

/// <summary>Raised once per persisted canon violation (post grade).</summary>
public sealed record WritingCanonViolationDetected(
    string UserId,
    Guid SubmissionId,
    Guid ViolationId,
    string RuleId,
    string Severity,
    DateTimeOffset OccurredAt) : WritingEvent(UserId, OccurredAt);

/// <summary>Raised after pathway recompute.</summary>
public sealed record WritingPathwayUpdated(
    string UserId,
    Guid PathwayId,
    int TotalItems,
    string Stage,
    DateTimeOffset OccurredAt) : WritingEvent(UserId, OccurredAt);

/// <summary>Raised when a mock session reaches the submitted state.</summary>
public sealed record WritingMockCompleted(
    string UserId,
    Guid MockSessionId,
    Guid MockId,
    Guid? SubmissionId,
    int ReadingPhaseSeconds,
    int WritingPhaseSeconds,
    DateTimeOffset OccurredAt) : WritingEvent(UserId, OccurredAt);

/// <summary>Raised the first time a learner's readiness crosses 80.</summary>
public sealed record WritingReadinessGreenLight(
    string UserId,
    int Score,
    string? PredictedBandLabel,
    DateTimeOffset OccurredAt) : WritingEvent(UserId, OccurredAt);
