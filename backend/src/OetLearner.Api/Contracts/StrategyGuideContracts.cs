namespace OetLearner.Api.Contracts;

public sealed record StrategyGuideProgressDto(
    int ReadPercent,
    bool Completed,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastReadAt,
    DateTimeOffset? CompletedAt,
    bool Bookmarked,
    DateTimeOffset? BookmarkedAt);

public sealed record StrategyGuideCategoryDto(
    string Code,
    string Label,
    int Count);

public sealed record StrategyGuideListItemDto(
    string Id,
    string? Slug,
    string Source,
    string ExamTypeCode,
    string? SubtestCode,
    string Title,
    string? Summary,
    string Category,
    int ReadingTimeMinutes,
    bool IsAccessible,
    bool IsPreviewEligible,
    bool RequiresUpgrade,
    string AccessReason,
    StrategyGuideProgressDto Progress,
    bool Bookmarked,
    string? RecommendedReason,
    string? ProgramId,
    string? ModuleId,
    string? ContentLessonId,
    int SortOrder,
    DateTimeOffset? PublishedAt);

public sealed record StrategyGuideLibraryDto(
    IReadOnlyList<StrategyGuideListItemDto> Items,
    IReadOnlyList<StrategyGuideListItemDto> Recommended,
    IReadOnlyList<StrategyGuideListItemDto> ContinueReading,
    IReadOnlyList<StrategyGuideListItemDto> Bookmarked,
    IReadOnlyList<StrategyGuideCategoryDto> Categories);

public sealed record StrategyGuideDetailDto(
    string Id,
    string? Slug,
    string Source,
    string ExamTypeCode,
    string? SubtestCode,
    string Title,
    string? Summary,
    string Category,
    int ReadingTimeMinutes,
    string? ContentJson,
    string? ContentHtml,
    string? SourceProvenance,
    bool IsAccessible,
    bool IsPreviewEligible,
    bool RequiresUpgrade,
    string AccessReason,
    StrategyGuideProgressDto Progress,
    bool Bookmarked,
    string? RecommendedReason,
    string? ProgramId,
    string? ProgramTitle,
    string? TrackId,
    string? TrackTitle,
    string? ModuleId,
    string? ModuleTitle,
    string? ContentLessonId,
    string? PreviousGuideId,
    string? NextGuideId,
    IReadOnlyList<StrategyGuideListItemDto> RelatedGuides,
    DateTimeOffset? PublishedAt);

public sealed record StrategyGuideProgressRequest(int ReadPercent);

public sealed record StrategyGuideBookmarkRequest(bool Bookmarked);

public sealed record StrategyGuideProgressUpdateResponse(StrategyGuideProgressDto Progress);

public sealed record StrategyGuideBookmarkUpdateResponse(StrategyGuideProgressDto Progress);

public sealed record StrategyGuidePublishValidationErrorDto(
    string Field,
    string Message);

public sealed record StrategyGuidePublishValidationDto(
    bool CanPublish,
    IReadOnlyList<StrategyGuidePublishValidationErrorDto> Errors);

public sealed record StrategyGuideAdminDto(
    string Id,
    string? Slug,
    string ExamTypeCode,
    string? SubtestCode,
    string Title,
    string Summary,
    string Category,
    int ReadingTimeMinutes,
    int SortOrder,
    string Status,
    bool IsPreviewEligible,
    string? ContentLessonId,
    string? ContentJson,
    string? ContentHtml,
    string? SourceProvenance,
    string? RightsStatus,
    string? FreshnessConfidence,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ArchivedAt);

public sealed record StrategyGuideUpsertRequest(
    string? Slug,
    string ExamTypeCode,
    string? SubtestCode,
    string Title,
    string Summary,
    string Category,
    int ReadingTimeMinutes,
    int SortOrder,
    bool IsPreviewEligible,
    string? ContentLessonId,
    string? ContentJson,
    string? ContentHtml,
    string? SourceProvenance,
    string? RightsStatus,
    string? FreshnessConfidence);

public sealed record StrategyGuidePublishResult(
    bool Published,
    StrategyGuidePublishValidationDto Validation,
    StrategyGuideAdminDto? Guide);
