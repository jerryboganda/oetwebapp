using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public sealed class VideoLessonService(LearnerDbContext db, MediaNormalizationService mediaService)
{
    private const string FeatureFlagKey = "video_lessons";
    private const string LessonType = "video_lesson";

    public async Task<bool> IsEnabledAsync(CancellationToken ct)
    {
        var flags = await db.FeatureFlags
            .AsNoTracking()
            .Where(f => f.Key == FeatureFlagKey || f.Key == "video-lessons")
            .ToListAsync(ct);
        var flag = flags
            .OrderByDescending(f => f.UpdatedAt)
            .FirstOrDefault();

        return flag?.Enabled ?? true;
    }

    public async Task<IReadOnlyList<VideoLessonListItemDto>> ListLessonsAsync(
        string userId,
        string? examTypeCode,
        string? subtestCode,
        string? category,
        CancellationToken ct)
    {
        var rows = await QueryHierarchyLessons(
                examTypeCode,
                subtestCode,
                category,
                sort: HierarchyLessonSort.FullOutline)
            .ToListAsync(ct);

        var legacy = await QueryLegacyLessons(examTypeCode, subtestCode, category)
            .OrderBy(lesson => lesson.SortOrder)
            .ToListAsync(ct);

        var progress = await LoadProgressAsync(userId, rows.Select(row => row.Lesson.Id).Concat(legacy.Select(l => l.Id)), ct);
        var access = await LoadAccessScopeAsync(userId, ct);

        var items = rows
            .Select(row => ToListItem(row, access, GetProgress(progress, row.Lesson.Id, DurationFor(row))))
            .Concat(legacy.Select(lesson => ToLegacyListItem(lesson, GetProgress(progress, lesson.Id, lesson.DurationSeconds))))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return items;
    }

    public async Task<VideoLessonDetailDto?> GetLessonAsync(string userId, string lessonId, CancellationToken ct)
    {
        var row = await QueryHierarchyLessons(
                lessonId: lessonId,
                preferredLessonId: lessonId)
            .FirstOrDefaultAsync(ct);

        if (row is not null)
        {
            var progress = await LoadProgressAsync(userId, [row.Lesson.Id], ct);
            var access = await LoadAccessScopeAsync(userId, ct);
            var accessState = ResolveAccess(row, access);
            var duration = DurationFor(row);
            var mediaAssetId = row.Lesson.MediaAssetId;
            var mediaAccess = accessState.IsAccessible && mediaAssetId is not null && row.Media?.Status == MediaAssetStatus.Ready
                ? await mediaService.GetSignedMediaUrlAsync(mediaAssetId, userId, ct)
                : null;
            var videoUrl = mediaAccess?.Success == true ? mediaAccess.Url : null;

            var (previousLessonId, nextLessonId) = await GetAdjacentLessonIdsAsync(row.Lesson, ct);
            var resources = await GetModuleResourcesAsync(row.Module.Id, ct);

            return new VideoLessonDetailDto(
                row.Lesson.Id,
                "content_hierarchy",
                row.Program.ExamTypeCode,
                row.Track.SubtestCode,
                row.Lesson.Title,
                DescriptionFor(row),
                duration,
                ThumbnailFor(row),
                videoUrl,
                accessState.IsAccessible ? row.Media?.CaptionPath : null,
                accessState.IsAccessible ? row.Media?.TranscriptPath : null,
                CategoryFor(row),
                null,
                DifficultyFor(row),
                accessState.IsAccessible,
                accessState.IsPreviewEligible,
                accessState.RequiresUpgrade,
                accessState.Reason,
                mediaAssetId,
                row.Program.Id,
                row.Program.Title,
                row.Track.Id,
                row.Track.Title,
                row.Module.Id,
                row.Module.Title,
                previousLessonId,
                nextLessonId,
                [],
                resources,
                GetProgress(progress, row.Lesson.Id, duration));
        }

        var legacy = await db.VideoLessons.AsNoTracking()
            .FirstOrDefaultAsync(lesson => lesson.Id == lessonId && lesson.Status == "active", ct);

        if (legacy is null)
        {
            return null;
        }

        var legacyProgress = await LoadProgressAsync(userId, [legacy.Id], ct);

        return new VideoLessonDetailDto(
            legacy.Id,
            "legacy_video_lesson",
            legacy.ExamTypeCode,
            legacy.SubtestCode,
            legacy.Title,
            legacy.Description,
            legacy.DurationSeconds,
            legacy.ThumbnailUrl,
            legacy.VideoUrl,
            null,
            null,
            legacy.Category,
            legacy.InstructorName,
            "standard",
            true,
            false,
            false,
            "legacy_access",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            ParseChapters(legacy.ChaptersJson),
            ParseResources(legacy.ResourcesJson),
            GetProgress(legacyProgress, legacy.Id, legacy.DurationSeconds));
    }

    public async Task<VideoLessonProgramDto?> GetProgramAsync(string userId, string programId, CancellationToken ct)
    {
        var program = await db.ContentPrograms.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == programId && p.Status == ContentStatus.Published, ct);
        if (program is null)
        {
            return null;
        }

        var rows = await QueryHierarchyLessons(
                programId: programId,
                sort: HierarchyLessonSort.ProgramOutline)
            .ToListAsync(ct);

        var progress = await LoadProgressAsync(userId, rows.Select(row => row.Lesson.Id), ct);
        var access = await LoadAccessScopeAsync(userId, ct);
        var listItems = rows
            .Select(row => new { Row = row, Item = ToListItem(row, access, GetProgress(progress, row.Lesson.Id, DurationFor(row))) })
            .ToList();

        var tracks = listItems
            .GroupBy(item => item.Row.Track.Id)
            .Select(trackGroup =>
            {
                var firstTrack = trackGroup.First().Row.Track;
                var modules = trackGroup
                    .GroupBy(item => item.Row.Module.Id)
                    .Select(moduleGroup =>
                    {
                        var firstModule = moduleGroup.First().Row.Module;
                        return new VideoLessonProgramModuleDto(
                            firstModule.Id,
                            firstModule.Title,
                            firstModule.Description,
                            firstModule.EstimatedDurationMinutes,
                            moduleGroup.Select(item => item.Item).ToList());
                    })
                    .ToList();

                return new VideoLessonProgramTrackDto(
                    firstTrack.Id,
                    firstTrack.Title,
                    firstTrack.Description,
                    firstTrack.SubtestCode,
                    modules);
            })
            .ToList();

        var programAccessible = access.IncludedPrograms.Contains(program.Id) ||
            listItems.Any(item => item.Item.IsAccessible);

        return new VideoLessonProgramDto(
            program.Id,
            program.Title,
            program.Description,
            program.ExamTypeCode,
            program.ThumbnailUrl,
            programAccessible,
            tracks);
    }

    public async Task<VideoLessonProgressUpdateResponse?> UpdateProgressAsync(
        string userId,
        string lessonId,
        int watchedSeconds,
        CancellationToken ct)
    {
        var durationSeconds = await GetDurationSecondsAsync(lessonId, ct);
        if (durationSeconds is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var clampedWatchedSeconds = Math.Clamp(watchedSeconds, 0, Math.Max(durationSeconds.Value, 0));
        var progress = await db.LearnerVideoProgress
            .FirstOrDefaultAsync(p => p.UserId == userId && p.VideoLessonId == lessonId, ct);

        if (progress is null)
        {
            progress = new LearnerVideoProgress
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                VideoLessonId = lessonId
            };
            db.LearnerVideoProgress.Add(progress);
        }

        progress.WatchedSeconds = Math.Max(progress.WatchedSeconds, clampedWatchedSeconds);
        progress.Completed = progress.Completed || IsComplete(progress.WatchedSeconds, durationSeconds.Value);
        progress.LastWatchedAt = now;

        await db.SaveChangesAsync(ct);

        return new VideoLessonProgressUpdateResponse(
            progress.Completed,
            progress.WatchedSeconds,
            PercentComplete(progress.WatchedSeconds, durationSeconds.Value),
            now);
    }

    private IQueryable<HierarchyLessonRow> QueryHierarchyLessons(
        string? examTypeCode = null,
        string? subtestCode = null,
        string? category = null,
        string? programId = null,
        string? lessonId = null,
        string? preferredLessonId = null,
        HierarchyLessonSort sort = HierarchyLessonSort.None)
    {
        examTypeCode = NullIfWhiteSpace(examTypeCode);
        subtestCode = NullIfWhiteSpace(subtestCode);
        category = NullIfWhiteSpace(category);
        programId = NullIfWhiteSpace(programId);
        lessonId = NullIfWhiteSpace(lessonId);
        preferredLessonId = NullIfWhiteSpace(preferredLessonId);

        var query =
            from lesson in db.ContentLessons.AsNoTracking()
            join module in db.ContentModules.AsNoTracking() on lesson.ModuleId equals module.Id
            join track in db.ContentTracks.AsNoTracking() on module.TrackId equals track.Id
            join program in db.ContentPrograms.AsNoTracking() on track.ProgramId equals program.Id
            join item in db.ContentItems.AsNoTracking() on lesson.ContentItemId equals item.Id into itemJoin
            from item in itemJoin.DefaultIfEmpty()
            join media in db.MediaAssets.AsNoTracking() on lesson.MediaAssetId equals media.Id into mediaJoin
            from media in mediaJoin.DefaultIfEmpty()
            where lesson.LessonType == LessonType
                && lesson.Status == ContentStatus.Published
                && module.Status == ContentStatus.Published
                && track.Status == ContentStatus.Published
                && program.Status == ContentStatus.Published
                && (examTypeCode == null || program.ExamTypeCode == examTypeCode)
                && (subtestCode == null || track.SubtestCode == subtestCode)
                && (category == null || (item != null && item.ContentType == category) || lesson.LessonType == category)
                && (programId == null || program.Id == programId)
                && (lessonId == null || lesson.Id == lessonId || lesson.ContentItemId == lessonId)
            select new HierarchyLessonQueryRow
            {
                Lesson = lesson,
                Module = module,
                Track = track,
                Program = program,
                Item = item,
                Media = media
            };

        if (preferredLessonId is not null)
        {
            query = query.OrderBy(row => row.Lesson.Id == preferredLessonId ? 0 : 1);
        }
        else
        {
            query = sort switch
            {
                HierarchyLessonSort.FullOutline => query
                    .OrderBy(row => row.Program.DisplayOrder)
                    .ThenBy(row => row.Track.DisplayOrder)
                    .ThenBy(row => row.Module.DisplayOrder)
                    .ThenBy(row => row.Lesson.DisplayOrder),
                HierarchyLessonSort.ProgramOutline => query
                    .OrderBy(row => row.Track.DisplayOrder)
                    .ThenBy(row => row.Module.DisplayOrder)
                    .ThenBy(row => row.Lesson.DisplayOrder),
                _ => query
            };
        }

        return query.Select(row => new HierarchyLessonRow(
            row.Lesson,
            row.Module,
            row.Track,
            row.Program,
            row.Item,
            row.Media));
    }

    private IQueryable<VideoLesson> QueryLegacyLessons(string? examTypeCode, string? subtestCode, string? category)
    {
        var query = db.VideoLessons.AsNoTracking().Where(lesson => lesson.Status == "active");
        if (!string.IsNullOrWhiteSpace(examTypeCode)) query = query.Where(lesson => lesson.ExamTypeCode == examTypeCode);
        if (!string.IsNullOrWhiteSpace(subtestCode)) query = query.Where(lesson => lesson.SubtestCode == subtestCode);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(lesson => lesson.Category == category);
        return query;
    }

    private async Task<AccessScope> LoadAccessScopeAsync(string userId, CancellationToken ct)
    {
        var activePlanIds = await db.Subscriptions.AsNoTracking()
            .Where(subscription => subscription.UserId == userId
                && (subscription.Status == SubscriptionStatus.Active || subscription.Status == SubscriptionStatus.Trial))
            .Select(subscription => subscription.PlanId)
            .ToListAsync(ct);

        var packageIds = activePlanIds.Count == 0
            ? []
            : await db.ContentPackages.AsNoTracking()
                .Where(package => package.BillingPlanId != null
                    && activePlanIds.Contains(package.BillingPlanId)
                    && package.Status == ContentStatus.Published)
                .Select(package => package.Id)
                .ToListAsync(ct);

        var rules = packageIds.Count == 0
            ? []
            : await db.PackageContentRules.AsNoTracking()
                .Where(rule => packageIds.Contains(rule.PackageId))
                .ToListAsync(ct);

        var previews = await db.FreePreviewAssets.AsNoTracking()
            .Where(asset => asset.Status == ContentStatus.Published)
            .ToListAsync(ct);

        var scope = new AccessScope(
            previews.Where(asset => asset.ContentItemId is not null).Select(asset => asset.ContentItemId!).ToHashSet(StringComparer.OrdinalIgnoreCase),
            previews.Where(asset => asset.MediaAssetId is not null).Select(asset => asset.MediaAssetId!).ToHashSet(StringComparer.OrdinalIgnoreCase));

        foreach (var rule in rules)
        {
            var include = rule.RuleType.StartsWith("include_", StringComparison.OrdinalIgnoreCase);
            var exclude = rule.RuleType.StartsWith("exclude_", StringComparison.OrdinalIgnoreCase);
            if (!include && !exclude)
            {
                continue;
            }

            var target = RuleTargetFor(scope, rule.TargetType, include);
            target?.Add(rule.TargetId);

            if (exclude)
            {
                RuleTargetFor(scope, rule.TargetType, include: false)?.Add(rule.TargetId);
            }
        }

        return scope;
    }

    private static HashSet<string>? RuleTargetFor(AccessScope scope, string targetType, bool include)
    {
        return targetType.ToLowerInvariant() switch
        {
            "program" => include ? scope.IncludedPrograms : scope.ExcludedPrograms,
            "track" => include ? scope.IncludedTracks : scope.ExcludedTracks,
            "module" => include ? scope.IncludedModules : scope.ExcludedModules,
            "content_item" => include ? scope.IncludedContentItems : scope.ExcludedContentItems,
            "lesson" => include ? scope.IncludedLessons : scope.ExcludedLessons,
            _ => null
        };
    }

    private static LessonAccess ResolveAccess(HierarchyLessonRow row, AccessScope scope)
    {
        var isPreview = row.Item?.IsPreviewEligible == true
            || row.Lesson.ContentItemId is not null && scope.FreePreviewContentItems.Contains(row.Lesson.ContentItemId)
            || row.Lesson.MediaAssetId is not null && scope.FreePreviewMediaAssets.Contains(row.Lesson.MediaAssetId);

        var included = scope.IncludedPrograms.Contains(row.Program.Id)
            || scope.IncludedTracks.Contains(row.Track.Id)
            || scope.IncludedModules.Contains(row.Module.Id)
            || scope.IncludedLessons.Contains(row.Lesson.Id)
            || row.Lesson.ContentItemId is not null && scope.IncludedContentItems.Contains(row.Lesson.ContentItemId);

        var excluded = scope.ExcludedPrograms.Contains(row.Program.Id)
            || scope.ExcludedTracks.Contains(row.Track.Id)
            || scope.ExcludedModules.Contains(row.Module.Id)
            || scope.ExcludedLessons.Contains(row.Lesson.Id)
            || row.Lesson.ContentItemId is not null && scope.ExcludedContentItems.Contains(row.Lesson.ContentItemId);

        var accessible = !excluded && (included || isPreview);
        var reason = accessible
            ? included ? "entitled" : "preview"
            : excluded ? "excluded" : "locked";

        return new LessonAccess(accessible, isPreview, !accessible, reason);
    }

    private async Task<Dictionary<string, LearnerVideoProgress>> LoadProgressAsync(
        string userId,
        IEnumerable<string> lessonIds,
        CancellationToken ct)
    {
        var ids = lessonIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.LearnerVideoProgress.AsNoTracking()
            .Where(progress => progress.UserId == userId && ids.Contains(progress.VideoLessonId))
            .ToDictionaryAsync(progress => progress.VideoLessonId, StringComparer.OrdinalIgnoreCase, ct);
    }

    private static VideoLessonProgressDto GetProgress(
        IReadOnlyDictionary<string, LearnerVideoProgress> progress,
        string lessonId,
        int durationSeconds)
    {
        if (!progress.TryGetValue(lessonId, out var item))
        {
            return new VideoLessonProgressDto(0, false, 0, null);
        }

        return new VideoLessonProgressDto(
            item.WatchedSeconds,
            item.Completed,
            PercentComplete(item.WatchedSeconds, durationSeconds),
            item.LastWatchedAt);
    }

    private VideoLessonListItemDto ToListItem(
        HierarchyLessonRow row,
        AccessScope scope,
        VideoLessonProgressDto progress)
    {
        var access = ResolveAccess(row, scope);
        return new VideoLessonListItemDto(
            row.Lesson.Id,
            "content_hierarchy",
            row.Program.ExamTypeCode,
            row.Track.SubtestCode,
            row.Lesson.Title,
            DescriptionFor(row),
            DurationFor(row),
            ThumbnailFor(row),
            CategoryFor(row),
            null,
            DifficultyFor(row),
            access.IsAccessible,
            access.IsPreviewEligible,
            access.RequiresUpgrade,
            progress,
            row.Program.Id,
            row.Module.Id,
            row.Lesson.DisplayOrder);
    }

    private static VideoLessonListItemDto ToLegacyListItem(VideoLesson lesson, VideoLessonProgressDto progress)
    {
        return new VideoLessonListItemDto(
            lesson.Id,
            "legacy_video_lesson",
            lesson.ExamTypeCode,
            lesson.SubtestCode,
            lesson.Title,
            lesson.Description,
            lesson.DurationSeconds,
            lesson.ThumbnailUrl,
            lesson.Category,
            lesson.InstructorName,
            "standard",
            true,
            false,
            false,
            progress,
            null,
            null,
            lesson.SortOrder);
    }

    private static int DurationFor(HierarchyLessonRow row)
    {
        if (row.Media?.DurationSeconds is > 0) return row.Media.DurationSeconds.Value;
        if (row.Item?.EstimatedDurationMinutes is > 0) return row.Item.EstimatedDurationMinutes * 60;
        if (row.Module.EstimatedDurationMinutes > 0) return row.Module.EstimatedDurationMinutes * 60;
        return 0;
    }

    private static string? DescriptionFor(HierarchyLessonRow row)
        => FirstNonBlank(row.Item?.CaseNotes, row.Module.Description, row.Program.Description);

    private static string CategoryFor(HierarchyLessonRow row)
        => FirstNonBlank(row.Item?.ContentType, row.Track.SubtestCode, LessonType) ?? LessonType;

    private static string DifficultyFor(HierarchyLessonRow row)
        => FirstNonBlank(row.Item?.Difficulty, row.Module.EstimatedDurationMinutes > 0 ? "guided" : null, "standard") ?? "standard";

    private static string? ThumbnailFor(HierarchyLessonRow row)
        => FirstNonBlank(row.Media?.ThumbnailPath, row.Program.ThumbnailUrl);

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private async Task<(string? PreviousLessonId, string? NextLessonId)> GetAdjacentLessonIdsAsync(ContentLesson current, CancellationToken ct)
    {
        var lessons = await db.ContentLessons.AsNoTracking()
            .Where(lesson => lesson.ModuleId == current.ModuleId
                && lesson.LessonType == LessonType
                && lesson.Status == ContentStatus.Published)
            .OrderBy(lesson => lesson.DisplayOrder)
            .ThenBy(lesson => lesson.Title)
            .Select(lesson => new { lesson.Id, lesson.DisplayOrder, lesson.Title })
            .ToListAsync(ct);

        var index = lessons.FindIndex(lesson => lesson.Id == current.Id);
        return (
            index > 0 ? lessons[index - 1].Id : null,
            index >= 0 && index < lessons.Count - 1 ? lessons[index + 1].Id : null);
    }

    private async Task<IReadOnlyList<VideoLessonResourceDto>> GetModuleResourcesAsync(string moduleId, CancellationToken ct)
    {
        return await db.ContentReferences.AsNoTracking()
            .Where(reference => reference.ModuleId == moduleId && reference.Status == ContentStatus.Published)
            .OrderBy(reference => reference.DisplayOrder)
            .Select(reference => new VideoLessonResourceDto(
                reference.Title,
                reference.ExternalUrl,
                reference.ReferenceType))
            .ToListAsync(ct);
    }

    private async Task<int?> GetDurationSecondsAsync(string lessonId, CancellationToken ct)
    {
        var hierarchy = await QueryHierarchyLessons(lessonId: lessonId)
            .FirstOrDefaultAsync(ct);
        if (hierarchy is not null)
        {
            return DurationFor(hierarchy);
        }

        return await db.VideoLessons.AsNoTracking()
            .Where(lesson => lesson.Id == lessonId && lesson.Status == "active")
            .Select(lesson => (int?)lesson.DurationSeconds)
            .FirstOrDefaultAsync(ct);
    }

    private static bool IsComplete(int watchedSeconds, int durationSeconds)
        => durationSeconds <= 0 || watchedSeconds >= Math.Ceiling(durationSeconds * 0.9);

    private static int PercentComplete(int watchedSeconds, int durationSeconds)
        => durationSeconds <= 0 ? 0 : Math.Clamp((int)Math.Round(watchedSeconds * 100d / durationSeconds), 0, 100);

    private static IReadOnlyList<VideoLessonChapterDto> ParseChapters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return doc.RootElement.EnumerateArray()
                .Select(item => new VideoLessonChapterDto(
                    ReadInt(item, "timeSeconds") ?? ReadInt(item, "time") ?? 0,
                    ReadString(item, "title") ?? "Chapter"))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<VideoLessonResourceDto> ParseResources(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return doc.RootElement.EnumerateArray()
                .Select(item => new VideoLessonResourceDto(
                    ReadString(item, "title") ?? "Resource",
                    ReadString(item, "url"),
                    ReadString(item, "type")))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)
            ? number
            : null;
    }

    private sealed record HierarchyLessonRow(
        ContentLesson Lesson,
        ContentModule Module,
        ContentTrack Track,
        ContentProgram Program,
        ContentItem? Item,
        MediaAsset? Media);

    private sealed class HierarchyLessonQueryRow
    {
        public ContentLesson Lesson { get; init; } = default!;
        public ContentModule Module { get; init; } = default!;
        public ContentTrack Track { get; init; } = default!;
        public ContentProgram Program { get; init; } = default!;
        public ContentItem? Item { get; init; }
        public MediaAsset? Media { get; init; }
    }

    private enum HierarchyLessonSort
    {
        None,
        FullOutline,
        ProgramOutline
    }

    private sealed record LessonAccess(
        bool IsAccessible,
        bool IsPreviewEligible,
        bool RequiresUpgrade,
        string Reason);

    private sealed class AccessScope(HashSet<string> freePreviewContentItems, HashSet<string> freePreviewMediaAssets)
    {
        public HashSet<string> FreePreviewContentItems { get; } = freePreviewContentItems;
        public HashSet<string> FreePreviewMediaAssets { get; } = freePreviewMediaAssets;
        public HashSet<string> IncludedPrograms { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> IncludedTracks { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> IncludedModules { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> IncludedLessons { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> IncludedContentItems { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExcludedPrograms { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExcludedTracks { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExcludedModules { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExcludedLessons { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExcludedContentItems { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
