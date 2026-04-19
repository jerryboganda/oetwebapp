namespace OetLearner.Api.Contracts;

public sealed record VideoLessonProgressDto(
    int WatchedSeconds,
    bool Completed,
    int PercentComplete,
    DateTimeOffset? LastWatchedAt);

public sealed record VideoLessonListItemDto(
    string Id,
    string Source,
    string ExamTypeCode,
    string? SubtestCode,
    string Title,
    string? Description,
    int DurationSeconds,
    string? ThumbnailUrl,
    string Category,
    string? InstructorName,
    string DifficultyLevel,
    bool IsAccessible,
    bool IsPreviewEligible,
    bool RequiresUpgrade,
    VideoLessonProgressDto? Progress,
    string? ProgramId,
    string? ModuleId,
    int SortOrder);

public sealed record VideoLessonChapterDto(
    int TimeSeconds,
    string Title);

public sealed record VideoLessonResourceDto(
    string Title,
    string? Url,
    string? Type);

public sealed record VideoLessonDetailDto(
    string Id,
    string Source,
    string ExamTypeCode,
    string? SubtestCode,
    string Title,
    string? Description,
    int DurationSeconds,
    string? ThumbnailUrl,
    string? VideoUrl,
    string? CaptionUrl,
    string? TranscriptUrl,
    string Category,
    string? InstructorName,
    string DifficultyLevel,
    bool IsAccessible,
    bool IsPreviewEligible,
    bool RequiresUpgrade,
    string AccessReason,
    string? MediaAssetId,
    string? ProgramId,
    string? ProgramTitle,
    string? TrackId,
    string? TrackTitle,
    string? ModuleId,
    string? ModuleTitle,
    string? PreviousLessonId,
    string? NextLessonId,
    IReadOnlyList<VideoLessonChapterDto> Chapters,
    IReadOnlyList<VideoLessonResourceDto> Resources,
    VideoLessonProgressDto? Progress);

public sealed record VideoLessonProgramDto(
    string Id,
    string Title,
    string? Description,
    string ExamTypeCode,
    string? ThumbnailUrl,
    bool IsAccessible,
    IReadOnlyList<VideoLessonProgramTrackDto> Tracks);

public sealed record VideoLessonProgramTrackDto(
    string Id,
    string Title,
    string? Description,
    string? SubtestCode,
    IReadOnlyList<VideoLessonProgramModuleDto> Modules);

public sealed record VideoLessonProgramModuleDto(
    string Id,
    string Title,
    string? Description,
    int EstimatedDurationMinutes,
    IReadOnlyList<VideoLessonListItemDto> Lessons);

public sealed record VideoLessonProgressUpdateResponse(
    bool Completed,
    int WatchedSeconds,
    int PercentComplete,
    DateTimeOffset LastWatchedAt);

public sealed record VideoProgressRequest(int WatchedSeconds);
