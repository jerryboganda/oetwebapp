namespace OetLearner.Api.Contracts;

public sealed record LiveClassListQuery(
    string? ProfessionTrack,
    string? Type,
    string? TutorProfileId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page = 1,
    int PageSize = 20);

public sealed record LiveClassSessionSummaryDto(
    string Id,
    DateTimeOffset ScheduledStartAt,
    DateTimeOffset ScheduledEndAt,
    int Capacity,
    int EnrolledCount,
    string Status,
    bool IsEnrolled,
    bool IsJoinAvailable,
    int CreditCost);

/// <summary>Admin-only session summary with Zoom provisioning fields.</summary>
public sealed record AdminLiveClassSessionSummaryDto(
    string Id,
    DateTimeOffset ScheduledStartAt,
    DateTimeOffset ScheduledEndAt,
    int Capacity,
    int EnrolledCount,
    string Status,
    bool IsEnrolled,
    bool IsJoinAvailable,
    int CreditCost,
    long? ZoomMeetingId,
    string? ZoomError);

public sealed record LiveClassListItemDto(
    string Id,
    string Slug,
    string Title,
    string? TitleAr,
    string Description,
    string? DescriptionAr,
    string Type,
    string ProfessionTrack,
    string Level,
    string? TutorProfileId,
    string? TutorDisplayName,
    int CreditCost,
    string Status,
    string? CoverImageUrl,
    IReadOnlyList<LiveClassSessionSummaryDto> Sessions);

public sealed record LiveClassDetailDto(
    string Id,
    string Slug,
    string Title,
    string? TitleAr,
    string Description,
    string? DescriptionAr,
    string Type,
    string ProfessionTrack,
    string Level,
    string? TutorProfileId,
    string? TutorDisplayName,
    int DefaultDurationMinutes,
    int DefaultCapacity,
    int CreditCost,
    string Status,
    string? CoverImageUrl,
    IReadOnlyList<string> Tags,
    IReadOnlyList<LiveClassSessionSummaryDto> Sessions);

public sealed record LiveClassEnrollmentDto(
    string Id,
    string ClassSessionId,
    string UserId,
    DateTimeOffset EnrolledAt,
    int CreditsCharged,
    string Status,
    DateTimeOffset? CancelledAt,
    string? CancellationReason);

public sealed record LiveClassEnrollRequest(string? IdempotencyKey);

public sealed record LiveClassCancelEnrollmentRequest(string? Reason);

public sealed record LiveClassJoinTokenResponse(
    string Provider,
    string? SdkKey,
    string? Signature,
    string MeetingNumber,
    string UserName,
    string? UserEmail,
    int Role,
    string? PassWord,
    string? Zak,
    string? JoinUrl,
    DateTimeOffset ExpiresAt);

public sealed record LiveClassRecordingDto(
    string Id,
    string ClassSessionId,
    string Status,
    string? VideoUrl,
    string? TranscriptUrl,
    string? TranscriptText,
    string? AiSummary,
    string? AiSummaryAr,
    IReadOnlyList<LiveClassRecordingChapterDto> Chapters,
    IReadOnlyList<string> ActionItems,
    DateTimeOffset? ExpiresAt);

public sealed record LiveClassRecordingChapterDto(int StartSeconds, string Title, string Summary);

/// <summary>Admin-only class detail that includes Zoom fields on each session.</summary>
public sealed record AdminLiveClassDetailDto(
    string Id,
    string Slug,
    string Title,
    string? TitleAr,
    string Description,
    string? DescriptionAr,
    string Type,
    string ProfessionTrack,
    string Level,
    string? TutorProfileId,
    string? TutorDisplayName,
    int DefaultDurationMinutes,
    int DefaultCapacity,
    int CreditCost,
    string Status,
    string? CoverImageUrl,
    IReadOnlyList<string> Tags,
    IReadOnlyList<AdminLiveClassSessionSummaryDto> Sessions);

public sealed record AdminLiveClassUpsertRequest(
    string Title,
    string? TitleAr,
    string Description,
    string? DescriptionAr,
    string Type,
    string ProfessionTrack,
    string Level,
    string? TutorProfileId,
    DateTimeOffset ScheduledStartAt,
    int DurationMinutes,
    int Capacity,
    int CreditCost,
    string? CoverImageUrl,
    IReadOnlyList<string>? Tags,
    bool AutoPublish = false);

public sealed record AdminLiveClassSessionAddRequest(
    DateTimeOffset ScheduledStartAt,
    int? DurationMinutes,
    int? Capacity);

public sealed record AdminLiveClassSessionUpdateRequest(
    DateTimeOffset? ScheduledStartAt,
    int? DurationMinutes,
    int? Capacity,
    string? CancellationReason);

public sealed record AdminLiveClassStatusRequest(string? Reason);

public sealed record LiveClassAnalyticsDto(
    int TotalClasses,
    int UpcomingSessions,
    int LiveSessions,
    int CompletedSessions,
    int TotalEnrollments,
    int TotalAttended,
    double AttendanceRate,
    int RecordingFailures);