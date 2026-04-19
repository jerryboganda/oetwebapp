using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public sealed class StrategyGuideService(LearnerDbContext db)
{
    private const string FeatureFlagKey = "strategy_guides";
    private const string LessonType = "strategy_guide";

    public async Task<bool> IsEnabledAsync(CancellationToken ct)
    {
        var flag = await db.FeatureFlags
            .AsNoTracking()
            .Where(f => f.Key == FeatureFlagKey || f.Key == "strategy-guides")
            .OrderByDescending(f => f.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        return flag?.Enabled ?? true;
    }

    public async Task<StrategyGuideLibraryDto> ListGuidesAsync(
        string userId,
        string? examTypeCode,
        string? subtestCode,
        string? category,
        string? q,
        bool recommendedOnly,
        CancellationToken ct)
    {
        var query = QueryActiveGuides(examTypeCode ?? "oet", subtestCode, category, q);
        var guides = await query
            .OrderBy(guide => guide.SortOrder)
            .ThenBy(guide => guide.Title)
            .ToListAsync(ct);

        var progress = await LoadProgressAsync(userId, guides.Select(guide => guide.Id), ct);
        var hierarchy = await LoadHierarchyRowsAsync(guides.Select(guide => guide.ContentLessonId), ct);
        var access = await LoadAccessScopeAsync(userId, ct);
        var weakSubtests = await LoadWeakSubtestsAsync(userId, examTypeCode ?? "oet", ct);

        var allItems = guides
            .Select(guide => ToListItem(guide, progress, hierarchy, access, weakSubtests))
            .ToList();

        var recommended = allItems
            .Where(item => item.RecommendedReason is not null)
            .Take(6)
            .ToList();

        if (recommended.Count == 0)
        {
            recommended = allItems
                .Take(3)
                .Select(item => item with { RecommendedReason = "High-impact OET strategy." })
                .ToList();
        }

        var items = recommendedOnly
            ? allItems.Where(item => item.RecommendedReason is not null).ToList()
            : allItems;

        if (recommendedOnly && items.Count == 0)
        {
            items = recommended;
        }

        return new StrategyGuideLibraryDto(
            items,
            recommended,
            allItems.Where(item => item.Progress.ReadPercent > 0 && !item.Progress.Completed).Take(6).ToList(),
            allItems.Where(item => item.Bookmarked).Take(6).ToList(),
            BuildCategories(allItems));
    }

    public async Task<StrategyGuideDetailDto?> GetGuideAsync(string userId, string guideId, CancellationToken ct)
    {
        var guide = await db.StrategyGuides.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == guideId && item.Status == "active", ct);
        if (guide is null)
        {
            return null;
        }

        var progress = await LoadProgressAsync(userId, [guide.Id], ct);
        var hierarchy = await LoadHierarchyRowsAsync([guide.ContentLessonId], ct);
        var access = await LoadAccessScopeAsync(userId, ct);
        var weakSubtests = await LoadWeakSubtestsAsync(userId, guide.ExamTypeCode, ct);
        var accessState = ResolveAccess(guide, hierarchy, access);
        var hierarchyRow = TryGetHierarchyRow(guide, hierarchy);

        var (previousGuideId, nextGuideId) = await GetAdjacentGuideIdsAsync(guide, ct);
        var related = await GetRelatedGuidesAsync(guide, userId, weakSubtests, ct);

        var itemProgress = GetProgress(progress, guide.Id);

        return new StrategyGuideDetailDto(
            guide.Id,
            guide.Slug,
            SourceFor(guide),
            guide.ExamTypeCode,
            guide.SubtestCode,
            guide.Title,
            guide.Summary,
            guide.Category,
            guide.ReadingTimeMinutes,
            accessState.IsAccessible || guide.IsPreviewEligible ? guide.ContentJson : null,
            accessState.IsAccessible || guide.IsPreviewEligible ? SanitizeLegacyHtml(guide.ContentHtml) : null,
            guide.SourceProvenance,
            accessState.IsAccessible,
            accessState.IsPreviewEligible,
            accessState.RequiresUpgrade,
            accessState.Reason,
            itemProgress,
            itemProgress.Bookmarked,
            RecommendationReason(guide, weakSubtests),
            hierarchyRow?.Program.Id,
            hierarchyRow?.Program.Title,
            hierarchyRow?.Track.Id,
            hierarchyRow?.Track.Title,
            hierarchyRow?.Module.Id,
            hierarchyRow?.Module.Title,
            guide.ContentLessonId,
            previousGuideId,
            nextGuideId,
            related,
            PublishedAtOrNull(guide));
    }

    public async Task<StrategyGuideProgressUpdateResponse?> UpdateProgressAsync(
        string userId,
        string guideId,
        int readPercent,
        CancellationToken ct)
    {
        var exists = await db.StrategyGuides.AsNoTracking()
            .AnyAsync(guide => guide.Id == guideId && guide.Status == "active", ct);
        if (!exists)
        {
            return null;
        }

        var progress = await GetOrCreateProgressAsync(userId, guideId, ct);
        var now = DateTimeOffset.UtcNow;
        var clampedPercent = Math.Clamp(readPercent, 0, 100);

        progress.ReadPercent = Math.Max(progress.ReadPercent, clampedPercent);
        progress.Completed = progress.Completed || progress.ReadPercent >= 100;
        progress.LastReadAt = now;
        if (progress.Completed && progress.CompletedAt is null)
        {
            progress.CompletedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return new StrategyGuideProgressUpdateResponse(ToProgressDto(progress));
    }

    public async Task<StrategyGuideBookmarkUpdateResponse?> SetBookmarkAsync(
        string userId,
        string guideId,
        bool bookmarked,
        CancellationToken ct)
    {
        var exists = await db.StrategyGuides.AsNoTracking()
            .AnyAsync(guide => guide.Id == guideId && guide.Status == "active", ct);
        if (!exists)
        {
            return null;
        }

        var progress = await GetOrCreateProgressAsync(userId, guideId, ct);
        progress.Bookmarked = bookmarked;
        progress.BookmarkedAt = bookmarked ? progress.BookmarkedAt ?? DateTimeOffset.UtcNow : null;
        progress.LastReadAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return new StrategyGuideBookmarkUpdateResponse(ToProgressDto(progress));
    }

    public async Task<IReadOnlyList<StrategyGuideAdminDto>> ListAdminGuidesAsync(
        string? status,
        string? examTypeCode,
        string? q,
        CancellationToken ct)
    {
        var query = db.StrategyGuides.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(guide => guide.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(examTypeCode))
        {
            query = query.Where(guide => guide.ExamTypeCode == examTypeCode);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLowerInvariant();
            query = query.Where(guide =>
                guide.Title.ToLower().Contains(needle) ||
                guide.Summary.ToLower().Contains(needle) ||
                guide.Category.ToLower().Contains(needle));
        }

        return await query
            .OrderBy(guide => guide.SortOrder)
            .ThenBy(guide => guide.Title)
            .Select(guide => ToAdminDto(guide))
            .ToListAsync(ct);
    }

    public async Task<StrategyGuideAdminDto?> GetAdminGuideAsync(string guideId, CancellationToken ct)
    {
        var guide = await db.StrategyGuides.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == guideId, ct);
        return guide is null ? null : ToAdminDto(guide);
    }

    public async Task<StrategyGuideAdminDto> CreateGuideAsync(
        string adminId,
        string adminName,
        StrategyGuideUpsertRequest request,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var guide = new StrategyGuide
        {
            Id = $"strategy-{Guid.NewGuid():N}"[..32],
            Status = "draft",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = default
        };

        ApplyUpsert(guide, request, now);
        db.StrategyGuides.Add(guide);
        AddAudit(adminId, adminName, "Created", guide.Id, $"Created strategy guide: {guide.Title}");
        await db.SaveChangesAsync(ct);
        return ToAdminDto(guide);
    }

    public async Task<StrategyGuideAdminDto?> UpdateGuideAsync(
        string adminId,
        string adminName,
        string guideId,
        StrategyGuideUpsertRequest request,
        CancellationToken ct)
    {
        var guide = await db.StrategyGuides.FirstOrDefaultAsync(item => item.Id == guideId, ct);
        if (guide is null)
        {
            return null;
        }

        ApplyUpsert(guide, request, DateTimeOffset.UtcNow);
        AddAudit(adminId, adminName, "Updated", guide.Id, $"Updated strategy guide: {guide.Title}");
        await db.SaveChangesAsync(ct);
        return ToAdminDto(guide);
    }

    public async Task<StrategyGuidePublishValidationDto?> ValidateGuideForPublishAsync(string guideId, CancellationToken ct)
    {
        var guide = await db.StrategyGuides.AsNoTracking().FirstOrDefaultAsync(item => item.Id == guideId, ct);
        return guide is null ? null : await ValidateGuideForPublishAsync(guide, ct);
    }

    public async Task<StrategyGuidePublishResult> PublishGuideAsync(
        string adminId,
        string adminName,
        string guideId,
        CancellationToken ct)
    {
        var guide = await db.StrategyGuides.FirstOrDefaultAsync(item => item.Id == guideId, ct);
        if (guide is null)
        {
            return new StrategyGuidePublishResult(
                false,
                new StrategyGuidePublishValidationDto(false, [new StrategyGuidePublishValidationErrorDto("id", "Strategy guide was not found.")]),
                null);
        }

        var validation = await ValidateGuideForPublishAsync(guide, ct);
        if (!validation.CanPublish)
        {
            return new StrategyGuidePublishResult(false, validation, ToAdminDto(guide));
        }

        var now = DateTimeOffset.UtcNow;
        guide.Status = "active";
        guide.PublishedAt = now;
        guide.ArchivedAt = null;
        guide.UpdatedAt = now;
        AddAudit(adminId, adminName, "Published", guide.Id, $"Published strategy guide: {guide.Title}");
        await db.SaveChangesAsync(ct);

        return new StrategyGuidePublishResult(true, validation, ToAdminDto(guide));
    }

    public async Task<StrategyGuideAdminDto?> ArchiveGuideAsync(
        string adminId,
        string adminName,
        string guideId,
        CancellationToken ct)
    {
        var guide = await db.StrategyGuides.FirstOrDefaultAsync(item => item.Id == guideId, ct);
        if (guide is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        guide.Status = "archived";
        guide.ArchivedAt = now;
        guide.UpdatedAt = now;
        AddAudit(adminId, adminName, "Archived", guide.Id, $"Archived strategy guide: {guide.Title}");
        await db.SaveChangesAsync(ct);
        return ToAdminDto(guide);
    }

    private IQueryable<StrategyGuide> QueryActiveGuides(
        string? examTypeCode,
        string? subtestCode,
        string? category,
        string? q)
    {
        var query = db.StrategyGuides.AsNoTracking().Where(guide => guide.Status == "active");
        if (!string.IsNullOrWhiteSpace(examTypeCode))
        {
            query = query.Where(guide => guide.ExamTypeCode == examTypeCode);
        }

        if (!string.IsNullOrWhiteSpace(subtestCode))
        {
            query = query.Where(guide => guide.SubtestCode == subtestCode);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(guide => guide.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLowerInvariant();
            query = query.Where(guide =>
                guide.Title.ToLower().Contains(needle) ||
                guide.Summary.ToLower().Contains(needle) ||
                guide.Category.ToLower().Contains(needle) ||
                (guide.SubtestCode != null && guide.SubtestCode.ToLower().Contains(needle)));
        }

        return query;
    }

    private async Task<Dictionary<string, LearnerStrategyProgress>> LoadProgressAsync(
        string userId,
        IEnumerable<string> guideIds,
        CancellationToken ct)
    {
        var ids = guideIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.LearnerStrategyProgress.AsNoTracking()
            .Where(progress => progress.UserId == userId && ids.Contains(progress.StrategyGuideId))
            .ToDictionaryAsync(progress => progress.StrategyGuideId, StringComparer.OrdinalIgnoreCase, ct);
    }

    private async Task<LearnerStrategyProgress> GetOrCreateProgressAsync(string userId, string guideId, CancellationToken ct)
    {
        var progress = await db.LearnerStrategyProgress
            .FirstOrDefaultAsync(item => item.UserId == userId && item.StrategyGuideId == guideId, ct);

        if (progress is not null)
        {
            return progress;
        }

        var now = DateTimeOffset.UtcNow;
        progress = new LearnerStrategyProgress
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StrategyGuideId = guideId,
            StartedAt = now,
            LastReadAt = now
        };
        db.LearnerStrategyProgress.Add(progress);
        return progress;
    }

    private StrategyGuideListItemDto ToListItem(
        StrategyGuide guide,
        IReadOnlyDictionary<string, LearnerStrategyProgress> progress,
        IReadOnlyDictionary<string, StrategyHierarchyRow> hierarchy,
        AccessScope access,
        IReadOnlySet<string> weakSubtests)
    {
        var itemProgress = GetProgress(progress, guide.Id);
        var accessState = ResolveAccess(guide, hierarchy, access);
        var row = TryGetHierarchyRow(guide, hierarchy);

        return new StrategyGuideListItemDto(
            guide.Id,
            guide.Slug,
            SourceFor(guide),
            guide.ExamTypeCode,
            guide.SubtestCode,
            guide.Title,
            guide.Summary,
            guide.Category,
            guide.ReadingTimeMinutes,
            accessState.IsAccessible,
            accessState.IsPreviewEligible,
            accessState.RequiresUpgrade,
            accessState.Reason,
            itemProgress,
            itemProgress.Bookmarked,
            RecommendationReason(guide, weakSubtests),
            row?.Program.Id,
            row?.Module.Id,
            guide.ContentLessonId,
            guide.SortOrder,
            PublishedAtOrNull(guide));
    }

    private static StrategyGuideProgressDto GetProgress(
        IReadOnlyDictionary<string, LearnerStrategyProgress> progress,
        string guideId)
    {
        return progress.TryGetValue(guideId, out var item)
            ? ToProgressDto(item)
            : new StrategyGuideProgressDto(0, false, null, null, null, false, null);
    }

    private static StrategyGuideProgressDto ToProgressDto(LearnerStrategyProgress progress)
    {
        return new StrategyGuideProgressDto(
            Math.Clamp(progress.ReadPercent, 0, 100),
            progress.Completed,
            progress.StartedAt == default ? null : progress.StartedAt,
            progress.LastReadAt == default ? null : progress.LastReadAt,
            progress.CompletedAt,
            progress.Bookmarked,
            progress.BookmarkedAt);
    }

    private static string? RecommendationReason(StrategyGuide guide, IReadOnlySet<string> weakSubtests)
    {
        if (guide.SubtestCode is not null && weakSubtests.Contains(guide.SubtestCode))
        {
            return $"Matches your {FormatSubtest(guide.SubtestCode)} focus.";
        }

        return guide.Category is "exam_day" or "time_management" or "common_mistakes"
            ? "High-impact OET strategy."
            : null;
    }

    private async Task<IReadOnlySet<string>> LoadWeakSubtestsAsync(string userId, string examTypeCode, CancellationToken ct)
    {
        var json = await db.Goals.AsNoTracking()
            .Where(goal => goal.UserId == userId && goal.ExamTypeCode == examTypeCode)
            .OrderByDescending(goal => goal.UpdatedAt)
            .Select(goal => goal.WeakSubtestsJson)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(json))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return doc.RootElement
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!.Trim().ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<Dictionary<string, StrategyHierarchyRow>> LoadHierarchyRowsAsync(IEnumerable<string?> lessonIds, CancellationToken ct)
    {
        var ids = lessonIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await (
            from lesson in db.ContentLessons.AsNoTracking()
            join module in db.ContentModules.AsNoTracking() on lesson.ModuleId equals module.Id
            join track in db.ContentTracks.AsNoTracking() on module.TrackId equals track.Id
            join program in db.ContentPrograms.AsNoTracking() on track.ProgramId equals program.Id
            where ids.Contains(lesson.Id)
                && lesson.LessonType == LessonType
                && lesson.Status == ContentStatus.Published
                && module.Status == ContentStatus.Published
                && track.Status == ContentStatus.Published
                && program.Status == ContentStatus.Published
            select new StrategyHierarchyRow(lesson, module, track, program))
            .ToListAsync(ct);

        return rows.ToDictionary(row => row.Lesson.Id, StringComparer.OrdinalIgnoreCase);
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

        var scope = new AccessScope();
        foreach (var rule in rules)
        {
            var include = rule.RuleType.StartsWith("include_", StringComparison.OrdinalIgnoreCase);
            var exclude = rule.RuleType.StartsWith("exclude_", StringComparison.OrdinalIgnoreCase);
            if (!include && !exclude)
            {
                continue;
            }

            RuleTargetFor(scope, rule.TargetType, include)?.Add(rule.TargetId);
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

    private static GuideAccess ResolveAccess(
        StrategyGuide guide,
        IReadOnlyDictionary<string, StrategyHierarchyRow> hierarchy,
        AccessScope scope)
    {
        var row = TryGetHierarchyRow(guide, hierarchy);
        if (row is null)
        {
            return new GuideAccess(true, guide.IsPreviewEligible, false, "legacy_access");
        }

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

        var accessible = !excluded && (included || guide.IsPreviewEligible);
        var reason = accessible
            ? included ? "entitled" : "preview"
            : excluded ? "excluded" : "locked";

        return new GuideAccess(accessible, guide.IsPreviewEligible, !accessible, reason);
    }

    private static StrategyHierarchyRow? TryGetHierarchyRow(
        StrategyGuide guide,
        IReadOnlyDictionary<string, StrategyHierarchyRow> hierarchy)
        => guide.ContentLessonId is not null && hierarchy.TryGetValue(guide.ContentLessonId, out var row)
            ? row
            : null;

    private async Task<(string? PreviousGuideId, string? NextGuideId)> GetAdjacentGuideIdsAsync(StrategyGuide current, CancellationToken ct)
    {
        var guides = await db.StrategyGuides.AsNoTracking()
            .Where(guide => guide.Status == "active" && guide.ExamTypeCode == current.ExamTypeCode)
            .OrderBy(guide => guide.SortOrder)
            .ThenBy(guide => guide.Title)
            .Select(guide => new { guide.Id, guide.SortOrder, guide.Title })
            .ToListAsync(ct);

        var index = guides.FindIndex(guide => guide.Id == current.Id);
        return (
            index > 0 ? guides[index - 1].Id : null,
            index >= 0 && index < guides.Count - 1 ? guides[index + 1].Id : null);
    }

    private async Task<IReadOnlyList<StrategyGuideListItemDto>> GetRelatedGuidesAsync(
        StrategyGuide current,
        string userId,
        IReadOnlySet<string> weakSubtests,
        CancellationToken ct)
    {
        var guides = await db.StrategyGuides.AsNoTracking()
            .Where(guide => guide.Status == "active"
                && guide.Id != current.Id
                && guide.ExamTypeCode == current.ExamTypeCode
                && (guide.Category == current.Category || guide.SubtestCode == current.SubtestCode))
            .OrderBy(guide => guide.SortOrder)
            .ThenBy(guide => guide.Title)
            .Take(3)
            .ToListAsync(ct);

        var progress = await LoadProgressAsync(userId, guides.Select(guide => guide.Id), ct);
        var hierarchy = await LoadHierarchyRowsAsync(guides.Select(guide => guide.ContentLessonId), ct);
        var access = await LoadAccessScopeAsync(userId, ct);
        return guides.Select(guide => ToListItem(guide, progress, hierarchy, access, weakSubtests)).ToList();
    }

    private async Task<StrategyGuidePublishValidationDto> ValidateGuideForPublishAsync(StrategyGuide guide, CancellationToken ct)
    {
        var errors = new List<StrategyGuidePublishValidationErrorDto>();
        if (string.IsNullOrWhiteSpace(guide.Title))
        {
            errors.Add(new StrategyGuidePublishValidationErrorDto("title", "Title is required before publishing."));
        }

        if (string.IsNullOrWhiteSpace(guide.Summary))
        {
            errors.Add(new StrategyGuidePublishValidationErrorDto("summary", "Summary is required before publishing."));
        }

        if (string.IsNullOrWhiteSpace(guide.Category))
        {
            errors.Add(new StrategyGuidePublishValidationErrorDto("category", "Category is required before publishing."));
        }

        if (guide.ReadingTimeMinutes <= 0)
        {
            errors.Add(new StrategyGuidePublishValidationErrorDto("readingTimeMinutes", "Reading time must be greater than zero."));
        }

        if (string.IsNullOrWhiteSpace(guide.SourceProvenance))
        {
            errors.Add(new StrategyGuidePublishValidationErrorDto("sourceProvenance", "Source provenance is required before publishing."));
        }

        if (string.IsNullOrWhiteSpace(guide.ContentJson) && string.IsNullOrWhiteSpace(guide.ContentHtml))
        {
            errors.Add(new StrategyGuidePublishValidationErrorDto("content", "Structured content or sanitized HTML is required before publishing."));
        }

        if (!string.IsNullOrWhiteSpace(guide.ContentLessonId))
        {
            var lessonExists = await db.ContentLessons.AsNoTracking()
                .AnyAsync(lesson => lesson.Id == guide.ContentLessonId && lesson.LessonType == LessonType, ct);
            if (!lessonExists)
            {
                errors.Add(new StrategyGuidePublishValidationErrorDto("contentLessonId", "Linked content lesson must exist and use strategy_guide lesson type."));
            }
        }

        return new StrategyGuidePublishValidationDto(errors.Count == 0, errors);
    }

    private static StrategyGuideAdminDto ToAdminDto(StrategyGuide guide)
    {
        return new StrategyGuideAdminDto(
            guide.Id,
            guide.Slug,
            guide.ExamTypeCode,
            guide.SubtestCode,
            guide.Title,
            guide.Summary,
            guide.Category,
            guide.ReadingTimeMinutes,
            guide.SortOrder,
            guide.Status,
            guide.IsPreviewEligible,
            guide.ContentLessonId,
            guide.ContentJson,
            guide.ContentHtml,
            guide.SourceProvenance,
            guide.RightsStatus,
            guide.FreshnessConfidence,
            guide.CreatedAt,
            guide.UpdatedAt,
            PublishedAtOrNull(guide),
            guide.ArchivedAt);
    }

    private static void ApplyUpsert(StrategyGuide guide, StrategyGuideUpsertRequest request, DateTimeOffset now)
    {
        guide.Slug = string.IsNullOrWhiteSpace(request.Slug) ? Slugify(request.Title) : Slugify(request.Slug);
        guide.ExamTypeCode = NullIfWhiteSpace(request.ExamTypeCode) ?? "oet";
        guide.SubtestCode = NullIfWhiteSpace(request.SubtestCode)?.ToLowerInvariant();
        guide.Title = request.Title.Trim();
        guide.Summary = request.Summary.Trim();
        guide.Category = request.Category.Trim().ToLowerInvariant();
        guide.ReadingTimeMinutes = Math.Max(0, request.ReadingTimeMinutes);
        guide.SortOrder = request.SortOrder;
        guide.IsPreviewEligible = request.IsPreviewEligible;
        guide.ContentLessonId = NullIfWhiteSpace(request.ContentLessonId);
        guide.ContentJson = NullIfWhiteSpace(request.ContentJson);
        guide.ContentHtml = NullIfWhiteSpace(request.ContentHtml) ?? "";
        guide.SourceProvenance = NullIfWhiteSpace(request.SourceProvenance);
        guide.RightsStatus = NullIfWhiteSpace(request.RightsStatus);
        guide.FreshnessConfidence = NullIfWhiteSpace(request.FreshnessConfidence);
        guide.UpdatedAt = now;
    }

    private void AddAudit(string adminId, string adminName, string action, string guideId, string details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"aud-strategy-{Guid.NewGuid():N}"[..32],
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorName = adminName,
            Action = action,
            ResourceType = "StrategyGuide",
            ResourceId = guideId,
            Details = details
        });
    }

    private static IReadOnlyList<StrategyGuideCategoryDto> BuildCategories(IEnumerable<StrategyGuideListItemDto> items)
    {
        return items
            .GroupBy(item => item.Category)
            .OrderBy(group => group.Key)
            .Select(group => new StrategyGuideCategoryDto(group.Key, CategoryLabel(group.Key), group.Count()))
            .ToList();
    }

    private static string SourceFor(StrategyGuide guide)
        => string.IsNullOrWhiteSpace(guide.ContentLessonId) ? "legacy_strategy_guide" : "content_hierarchy";

    private static DateTimeOffset? PublishedAtOrNull(StrategyGuide guide)
        => guide.PublishedAt == default ? null : guide.PublishedAt;

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatSubtest(string subtest)
        => subtest.Length == 0 ? subtest : char.ToUpperInvariant(subtest[0]) + subtest[1..].ToLowerInvariant();

    private static string CategoryLabel(string category)
        => string.Join(' ', category.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(FormatSubtest));

    private static string Slugify(string? value)
    {
        var input = NullIfWhiteSpace(value) ?? $"strategy-{Guid.NewGuid():N}";
        var slug = Regex.Replace(input.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? $"strategy-{Guid.NewGuid():N}"[..32] : slug;
    }

    private static string? SanitizeLegacyHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var sanitized = Regex.Replace(html, "<script[^>]*>.*?</script>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        sanitized = Regex.Replace(sanitized, "<style[^>]*>.*?</style>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        sanitized = Regex.Replace(sanitized, "\\son[a-z]+\\s*=\\s*(\"[^\"]*\"|'[^']*'|[^\\s>]+)", "", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, "javascript:", "", RegexOptions.IgnoreCase);
        return WebUtility.HtmlDecode(sanitized);
    }

    private sealed record StrategyHierarchyRow(
        ContentLesson Lesson,
        ContentModule Module,
        ContentTrack Track,
        ContentProgram Program);

    private sealed record GuideAccess(
        bool IsAccessible,
        bool IsPreviewEligible,
        bool RequiresUpgrade,
        string Reason);

    private sealed class AccessScope
    {
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
