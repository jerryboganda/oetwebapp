using OetWithDrHesham.Api.Domain.Classes;

namespace OetWithDrHesham.Api.Contracts.Classes;

public sealed record TutorProfileDto(
    string Id,
    string UserId,
    string DisplayName,
    string? DisplayNameAr,
    string Bio,
    string? BioAr,
    string? AvatarUrl,
    IReadOnlyList<string> Specialties,
    IReadOnlyList<string> Languages,
    decimal? HourlyRateUsd,
    string TimeZone,
    string? ZoomUserId,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TutorUpsertRequest(
    string DisplayName,
    string? DisplayNameAr,
    string? Bio,
    string? BioAr,
    string? AvatarUrl,
    IReadOnlyList<string>? Specialties,
    IReadOnlyList<string>? Languages,
    decimal? HourlyRateUsd,
    string? TimeZone,
    bool? IsActive);

public sealed record TutorAvailabilityDto(
    string Id,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool IsActive);

public sealed record TutorAvailabilityUpsertRequest(
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool IsActive);

public sealed record TutorEarningsLineDto(
    string ClassSessionId,
    string LiveClassId,
    string ClassTitle,
    DateTimeOffset ScheduledStartAt,
    int AttendedCount,
    int CreditCost,
    decimal CreditUsdValue,
    decimal RevenueSharePercent,
    decimal GrossUsd,
    decimal NetUsd);

public sealed record TutorEarningsDto(
    DateTimeOffset? From,
    DateTimeOffset? To,
    decimal GrossUsd,
    decimal NetUsd,
    decimal RevenueSharePercent,
    IReadOnlyList<TutorEarningsLineDto> Lines);

public sealed record ClassFeedbackSubmitRequest(
    int Rating,
    string? Comment,
    bool? RecommendToFriend);

public sealed record ClassFeedbackDto(
    string Id,
    string ClassSessionId,
    string UserId,
    int Rating,
    string? Comment,
    bool RecommendToFriend,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ClassFeedbackAggregateDto(
    int Count,
    double AverageRating,
    double RecommendPercent,
    IReadOnlyList<ClassFeedbackDto> RecentComments);

public sealed record LiveClassTranscriptDto(string ClassSessionId, string TranscriptText, DateTimeOffset? ProcessedAt);

public sealed record LiveClassWaitlistEntryDto(string Id, string ClassSessionId, string UserId, int Position, DateTimeOffset JoinedAt);

public sealed record TutorAttendanceLineDto(
    string UserId,
    string? DisplayName,
    DateTimeOffset JoinedAt,
    DateTimeOffset? LeftAt,
    int DurationSeconds);

public sealed record TutorClassSessionCreateRequest(
    DateTimeOffset ScheduledStartAt,
    int? DurationMinutes,
    int? Capacity);

public sealed record TutorClassCreateRequest(
    string Title,
    string? TitleAr,
    string Description,
    string? DescriptionAr,
    string Type,
    string ProfessionTrack,
    string Level,
    DateTimeOffset ScheduledStartAt,
    int DurationMinutes,
    int Capacity,
    int CreditCost,
    string? CoverImageUrl,
    IReadOnlyList<string>? Tags,
    bool AutoPublish = false);

public sealed record TutorClassUpdateRequest(
    string? Title,
    string? TitleAr,
    string? Description,
    string? DescriptionAr,
    string? CoverImageUrl,
    int? CreditCost,
    int? DefaultCapacity,
    int? DefaultDurationMinutes,
    IReadOnlyList<string>? Tags);
