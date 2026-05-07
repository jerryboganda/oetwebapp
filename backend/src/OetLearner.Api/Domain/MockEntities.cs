using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

[Index(nameof(Status), nameof(MockType))]
[Index(nameof(ProfessionId), nameof(MockType))]
[Index(nameof(Slug), IsUnique = true)]
public class MockBundle
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(200)]
    public string Slug { get; set; } = default!;

    [MaxLength(16)]
    public string ExamFamilyCode { get; set; } = "oet";

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = "oet";

    [MaxLength(16)]
    public string MockType { get; set; } = "full";

    [MaxLength(32)]
    public string? SubtestCode { get; set; }

    [MaxLength(32)]
    public string? ProfessionId { get; set; }

    public bool AppliesToAllProfessions { get; set; } = true;

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    public int EstimatedDurationMinutes { get; set; }

    public int Priority { get; set; }

    [MaxLength(512)]
    public string TagsCsv { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Difficulty { get; set; } = "exam_ready";

    [MaxLength(32)]
    public string SourceStatus { get; set; } = MockSourceStatuses.NeedsReview;

    [MaxLength(32)]
    public string QualityStatus { get; set; } = MockQualityStatuses.Draft;

    [MaxLength(32)]
    public string ReleasePolicy { get; set; } = MockReleasePolicies.Instant;

    [MaxLength(512)]
    public string TopicTagsCsv { get; set; } = string.Empty;

    [MaxLength(512)]
    public string SkillTagsCsv { get; set; } = string.Empty;

    public bool WatermarkEnabled { get; set; } = true;

    public bool RandomiseQuestions { get; set; }

    [MaxLength(256)]
    public string? SourceProvenance { get; set; }

    [MaxLength(64)]
    public string? CreatedByAdminId { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    public ICollection<MockBundleSection> Sections { get; set; } = new List<MockBundleSection>();
}

[Index(nameof(MockBundleId), nameof(SectionOrder), IsUnique = true)]
[Index(nameof(MockBundleId), nameof(SubtestCode))]
[Index(nameof(ContentPaperId))]
public class MockBundleSection
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string MockBundleId { get; set; } = default!;

    public int SectionOrder { get; set; }

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(64)]
    public string ContentPaperId { get; set; } = default!;

    public int TimeLimitMinutes { get; set; }

    public bool ReviewEligible { get; set; }

    public bool IsRequired { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public MockBundle? MockBundle { get; set; }
    public ContentPaper? ContentPaper { get; set; }
}

[Index(nameof(MockAttemptId), nameof(MockBundleSectionId), IsUnique = true)]
[Index(nameof(MockAttemptId), nameof(SubtestCode))]
[Index(nameof(ContentAttemptId))]
public class MockSectionAttempt
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string MockAttemptId { get; set; } = default!;

    [MaxLength(64)]
    public string MockBundleSectionId { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    public AttemptState State { get; set; } = AttemptState.NotStarted;

    [MaxLength(64)]
    public string ContentPaperId { get; set; } = default!;

    [MaxLength(512)]
    public string LaunchRoute { get; set; } = default!;

    [MaxLength(64)]
    public string? ContentAttemptId { get; set; }

    public int? RawScore { get; set; }
    public int? RawScoreMax { get; set; }
    public int? ScaledScore { get; set; }

    [MaxLength(8)]
    public string? Grade { get; set; }

    public string FeedbackJson { get; set; } = "{}";

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? DeadlineAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public MockAttempt? MockAttempt { get; set; }
    public MockBundleSection? MockBundleSection { get; set; }
}

[Index(nameof(UserId), nameof(State))]
[Index(nameof(MockAttemptId), IsUnique = true)]
public class MockReviewReservation
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string MockAttemptId { get; set; } = default!;

    [MaxLength(64)]
    public string WalletId { get; set; } = default!;

    public MockReviewReservationState State { get; set; } = MockReviewReservationState.Reserved;

    public int ReservedCredits { get; set; }
    public int ConsumedCredits { get; set; }
    public int ReleasedCredits { get; set; }

    [MaxLength(32)]
    public string Selection { get; set; } = "none";

    public DateTimeOffset ReservedAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public Guid? DebitTransactionId { get; set; }
    public Guid? ReleaseTransactionId { get; set; }

    public MockAttempt? MockAttempt { get; set; }
}


/// <summary>
/// Scheduled mock, final-readiness, and live Speaking session booking.
/// Learner-facing projections expose candidate cards only; tutor/interlocutor
/// data stays in expert/admin views.
/// </summary>
[Index(nameof(UserId), nameof(ScheduledStartAt))]
[Index(nameof(Status), nameof(ScheduledStartAt))]
[Index(nameof(AssignedTutorId), nameof(ScheduledStartAt))]
[Index(nameof(MockBundleId))]
public class MockBooking
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string MockBundleId { get; set; } = default!;

    [MaxLength(64)]
    public string? MockAttemptId { get; set; }

    public DateTimeOffset ScheduledStartAt { get; set; }

    [MaxLength(80)]
    public string TimezoneIana { get; set; } = "UTC";

    [MaxLength(32)]
    public string Status { get; set; } = MockBookingStatuses.Scheduled;

    [MaxLength(64)]
    public string? AssignedTutorId { get; set; }

    [MaxLength(64)]
    public string? AssignedInterlocutorId { get; set; }

    public int RescheduleCount { get; set; }

    public bool ConsentToRecording { get; set; }

    [MaxLength(16)]
    public string DeliveryMode { get; set; } = MockDeliveryModes.Computer;

    [MaxLength(32)]
    public string LiveRoomState { get; set; } = MockLiveRoomStates.Waiting;

    [MaxLength(128)]
    public string? ZoomMeetingId { get; set; }

    [MaxLength(512)]
    public string? ZoomJoinUrl { get; set; }

    [MaxLength(512)]
    public string? ZoomStartUrl { get; set; }

    [MaxLength(64)]
    public string? ZoomMeetingPassword { get; set; }

    [MaxLength(2000)]
    public string? LearnerNotes { get; set; }

    /// <summary>
    /// Mocks V2 Wave 6 — JSON manifest of recording chunks accepted via
    /// POST /v1/mock-bookings/{id}/recording-chunk. Shape:
    ///   { "chunks": [ { "part": int, "sha256": "..", "key": "..", "bytes": long, "mimeType": ".." }, ... ] }
    /// Only populated when <see cref="ConsentToRecording"/> is true.
    /// Stored as <c>text</c> (unbounded) — a 20-min recording at 5s/chunk
    /// can produce 240 manifest entries (~50 KB JSON), exceeding the prior
    /// 8000-byte VARCHAR cap. The migration emits <c>text</c> in Postgres.
    /// </summary>
    [Column(TypeName = "text")]
    public string? RecordingManifestJson { get; set; }

    public long? RecordingDurationMs { get; set; }

    public DateTimeOffset? RecordingFinalizedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public MockBundle? MockBundle { get; set; }
    public MockAttempt? MockAttempt { get; set; }
}

/// <summary>
/// Learner/admin content-integrity report for suspected leaked, copied, or
/// rights-unclear mock material.
/// </summary>
[Index(nameof(Status), nameof(Severity))]
[Index(nameof(MockBundleId))]
[Index(nameof(ReportedByUserId), nameof(CreatedAt))]
public class MockContentReview
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string? MockBundleId { get; set; }

    [MaxLength(64)]
    public string? MockAttemptId { get; set; }

    [MaxLength(64)]
    public string? ReportedByUserId { get; set; }

    [MaxLength(32)]
    public string ReviewType { get; set; } = "leak_report";

    [MaxLength(16)]
    public string Severity { get; set; } = "high";

    [MaxLength(32)]
    public string Status { get; set; } = "open";

    [MaxLength(2000)]
    public string Notes { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    [MaxLength(64)]
    public string? ResolvedByAdminId { get; set; }

    public MockBundle? MockBundle { get; set; }
    public MockAttempt? MockAttempt { get; set; }
}

/// <summary>
/// Proctoring telemetry recorded during a mock attempt (Wave 2 — Mocks V2).
/// One row per discrete event. Aggregations live in MockReport.ProctoringSummary.
/// </summary>
[Index(nameof(MockAttemptId), nameof(OccurredAt))]
[Index(nameof(MockSectionAttemptId))]
[Index(nameof(Kind))]
public class MockProctoringEvent
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string MockAttemptId { get; set; } = default!;

    [MaxLength(64)]
    public string? MockSectionAttemptId { get; set; }

    /// <summary>
    /// Canonical kind. See <see cref="MockProctoringKinds"/> for allowed values.
    /// </summary>
    [MaxLength(48)]
    public string Kind { get; set; } = default!;

    /// <summary>
    /// "info" | "warning" | "critical". Drives admin dashboard colour and notification routing.
    /// </summary>
    [MaxLength(16)]
    public string Severity { get; set; } = "info";

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// JSON object with kind-specific extra fields (e.g. duration, target, error code).
    /// </summary>
    public string MetadataJson { get; set; } = "{}";

    public MockAttempt? MockAttempt { get; set; }
    public MockSectionAttempt? MockSectionAttempt { get; set; }
}

/// <summary>
/// Canonical proctoring event kinds. Frontend and backend MUST share this contract.
/// </summary>
public static class MockProctoringKinds
{
    public const string FullscreenExit = "fullscreen_exit";
    public const string VisibilityHidden = "visibility_hidden";
    public const string TabSwitch = "tab_switch";
    public const string PasteBlocked = "paste_blocked";
    public const string CopyBlocked = "copy_blocked";
    public const string MicCheckPassed = "mic_check_passed";
    public const string MicCheckFailed = "mic_check_failed";
    public const string CamCheckPassed = "cam_check_passed";
    public const string CamCheckFailed = "cam_check_failed";
    public const string AudioIssueReported = "audio_issue_reported";
    public const string AudioPlaybackPassed = "audio_playback_passed";
    public const string AudioPlaybackFailed = "audio_playback_failed";
    public const string NetworkDrop = "network_drop";
    public const string MultipleDisplaysDetected = "multiple_displays_detected";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        FullscreenExit, VisibilityHidden, TabSwitch, PasteBlocked, CopyBlocked,
        MicCheckPassed, MicCheckFailed, CamCheckPassed, CamCheckFailed,
        AudioIssueReported, AudioPlaybackPassed, AudioPlaybackFailed,
        NetworkDrop, MultipleDisplaysDetected,
    };

    public static readonly IReadOnlySet<string> Severities = new HashSet<string>(StringComparer.Ordinal)
    {
        "info", "warning", "critical",
    };

    public static string DefaultSeverity(string kind) => kind switch
    {
        TabSwitch or FullscreenExit or VisibilityHidden or MultipleDisplaysDetected => "warning",
        MicCheckFailed or CamCheckFailed or NetworkDrop or AudioIssueReported or AudioPlaybackFailed => "warning",
        PasteBlocked or CopyBlocked => "info",
        _ => "info",
    };
}

public static class MockReleasePolicies
{
    public const string Instant = "instant";
    public const string AfterTeacherMarking = "after_teacher_marking";
    public const string Scheduled = "scheduled";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Instant, AfterTeacherMarking, Scheduled,
    };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}

public static class MockSourceStatuses
{
    public const string Original = "original";
    public const string Licensed = "licensed";
    public const string OfficialSample = "official_sample";
    public const string NeedsReview = "needs_review";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Original, Licensed, OfficialSample, NeedsReview,
    };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}

public static class MockQualityStatuses
{
    public const string Draft = "draft";
    public const string InReview = "in_review";
    public const string Approved = "approved";
    public const string Pilot = "pilot";
    public const string Retired = "retired";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Draft, InReview, Approved, Pilot, Retired,
    };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}

public static class MockBookingStatuses
{
    public const string Scheduled = "scheduled";
    public const string Confirmed = "confirmed";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string TutorNoShow = "tutor_no_show";
    public const string LearnerNoShow = "learner_no_show";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Scheduled, Confirmed, InProgress, Completed, Cancelled, TutorNoShow, LearnerNoShow,
    };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}

public static class MockLiveRoomStates
{
    public const string Waiting = "waiting";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string TutorNoShow = "tutor_no_show";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Waiting, InProgress, Completed, TutorNoShow,
    };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}
