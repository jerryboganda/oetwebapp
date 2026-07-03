using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.VideoLibrary;

// ── Learner-facing contracts (pinned cross-team; do not rename fields) ──────

public sealed record VideoProgressDto(int PositionSeconds, int PercentComplete, bool Completed);

public sealed record VideoSummaryDto(
    string Id,
    string Title,
    string? Description,
    int DurationSeconds,
    string? ThumbnailUrl,
    string AccessTier,
    bool IsAccessible,
    bool RequiresUpgrade,
    string? LockReason,
    string? SubtestCode,
    string? Difficulty,
    IReadOnlyList<string> Tags,
    bool IsFeatured,
    DateTimeOffset? PublishedAt,
    long ViewCount,
    VideoProgressDto? Progress,
    bool Bookmarked,
    IReadOnlyList<string> CategoryIds);

public sealed record VideoChapterDto(int TimeSeconds, string Title);
public sealed record VideoCaptionDto(string LanguageCode, string Label);
public sealed record VideoAttachmentDto(Guid Id, string Title, string Url);

public sealed record VideoDetailDto(
    VideoSummaryDto Summary,
    IReadOnlyList<VideoChapterDto> Chapters,
    IReadOnlyList<VideoCaptionDto> Captions,
    IReadOnlyList<VideoAttachmentDto> Attachments,
    string? PreviousVideoId,
    string? NextVideoId);

public sealed record VideoCategoryShelfDto(
    string Id,
    string Title,
    string Slug,
    string? Description,
    IReadOnlyList<VideoSummaryDto> Videos);

public sealed record VideoLibraryHomeDto(
    IReadOnlyList<VideoSummaryDto> Featured,
    IReadOnlyList<VideoSummaryDto> ContinueWatching,
    IReadOnlyList<VideoCategoryShelfDto> Categories,
    IReadOnlyList<VideoSummaryDto> Uncategorized);

public sealed record VideoProgressUpdateResultDto(
    int PositionSeconds,
    int WatchedSeconds,
    int PercentComplete,
    bool Completed);

/// <summary>
/// Learner surface of the Video Library: home shelves, video detail (never a
/// playback URL), watch progress, and bookmarks. All reads are scoped to
/// Published + release-dated + profession-visible videos.
/// </summary>
public sealed class VideoLibraryLearnerService(
    LearnerDbContext db,
    IVideoEntitlementService entitlements)
{
    private const string FeatureFlagKey = "video_library";
    private const double CompletionThreshold = 0.9;

    /// <summary>Learner release gate — mirrors the retired video_lessons flag semantics (default ON).</summary>
    public async Task<bool> IsEnabledAsync(CancellationToken ct)
    {
        var flags = await db.FeatureFlags.AsNoTracking()
            .Where(f => f.Key == FeatureFlagKey || f.Key == "video-library")
            .ToListAsync(ct);
        var flag = flags.OrderByDescending(f => f.UpdatedAt).FirstOrDefault();
        return flag?.Enabled ?? true;
    }

    public async Task<VideoLibraryHomeDto> GetHomeAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var videos = await LoadVisibleVideosAsync(userId, now, ct);
        var videoIds = videos.Select(v => v.Id).ToList();

        var memberships = await db.VideoCategoryItems.AsNoTracking()
            .Where(i => videoIds.Contains(i.VideoId))
            .ToListAsync(ct);
        var membershipsByVideo = memberships
            .GroupBy(i => i.VideoId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var context = await entitlements.ResolveContextAsync(userId, isAdmin: false, ct);
        var progressByVideo = await LoadProgressAsync(userId, videoIds, ct);
        var bookmarked = await LoadBookmarksAsync(userId, videoIds, ct);

        var summariesById = videos.ToDictionary(
            v => v.Id,
            v => ToSummary(v, context, progressByVideo, bookmarked, membershipsByVideo),
            StringComparer.Ordinal);

        var featured = videos
            .Where(v => v.IsFeatured)
            .OrderBy(v => v.SortOrder)
            .ThenBy(v => v.Title, StringComparer.OrdinalIgnoreCase)
            .Select(v => summariesById[v.Id])
            .ToList();

        var continueWatching = videos
            .Where(v => progressByVideo.TryGetValue(v.Id, out var p) && p.PositionSeconds > 0 && !p.Completed)
            .OrderByDescending(v => progressByVideo[v.Id].LastWatchedAt)
            .Take(10)
            .Select(v => summariesById[v.Id])
            .ToList();

        var categories = await db.VideoCategories.AsNoTracking()
            .Where(c => c.Status == ContentStatus.Published)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Title)
            .ToListAsync(ct);

        var shelves = new List<VideoCategoryShelfDto>();
        foreach (var category in categories)
        {
            var shelfVideos = memberships
                .Where(m => m.CategoryId == category.Id && summariesById.ContainsKey(m.VideoId))
                .OrderBy(m => m.SortOrder)
                .Select(m => summariesById[m.VideoId])
                .ToList();
            if (shelfVideos.Count == 0) continue;
            shelves.Add(new VideoCategoryShelfDto(
                category.Id, category.Title, category.Slug, category.Description, shelfVideos));
        }

        var categorizedIds = memberships.Select(m => m.VideoId).ToHashSet(StringComparer.Ordinal);
        var uncategorized = videos
            .Where(v => !categorizedIds.Contains(v.Id))
            .OrderBy(v => v.SortOrder)
            .ThenBy(v => v.Title, StringComparer.OrdinalIgnoreCase)
            .Select(v => summariesById[v.Id])
            .ToList();

        return new VideoLibraryHomeDto(featured, continueWatching, shelves, uncategorized);
    }

    /// <summary>Returns null when the video is not published / not visible to this learner (endpoint 404s).</summary>
    public async Task<VideoDetailDto?> GetDetailAsync(string userId, string videoId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var video = await FindVisibleVideoAsync(userId, videoId, now, ct);
        if (video is null) return null;

        var memberships = await db.VideoCategoryItems.AsNoTracking()
            .Where(i => i.VideoId == videoId)
            .ToListAsync(ct);
        var membershipsByVideo = new Dictionary<string, List<VideoCategoryItem>>(StringComparer.Ordinal)
        {
            [videoId] = memberships,
        };

        var context = await entitlements.ResolveContextAsync(userId, isAdmin: false, ct);
        var progress = await LoadProgressAsync(userId, [videoId], ct);
        var bookmarks = await LoadBookmarksAsync(userId, [videoId], ct);
        var summary = ToSummary(video, context, progress, bookmarks, membershipsByVideo);

        var captions = await db.VideoCaptionTracks.AsNoTracking()
            .Where(c => c.VideoId == videoId)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.LanguageCode)
            .Select(c => new VideoCaptionDto(c.LanguageCode, c.Label))
            .ToListAsync(ct);

        var attachments = await db.VideoAttachments.AsNoTracking()
            .Where(a => a.VideoId == videoId)
            .OrderBy(a => a.SortOrder)
            .ToListAsync(ct);

        var (previousId, nextId) = await ResolveAdjacentAsync(userId, video, memberships, now, ct);

        return new VideoDetailDto(
            summary,
            ParseChapters(video.ChaptersJson),
            captions,
            attachments
                .Select(a => new VideoAttachmentDto(a.Id, a.Title, $"/v1/media/{a.MediaAssetId}/content"))
                .ToList(),
            previousId,
            nextId);
    }

    public async Task<VideoProgressUpdateResultDto?> UpdateProgressAsync(
        string userId, string videoId, int positionSeconds, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var video = await FindVisibleVideoAsync(userId, videoId, now, ct);
        if (video is null) return null;

        var duration = Math.Max(0, video.DurationSeconds);
        var clampedPosition = Math.Clamp(positionSeconds, 0, duration > 0 ? duration : int.MaxValue);

        var progress = await db.LearnerVideoLibraryProgress
            .FirstOrDefaultAsync(p => p.UserId == userId && p.VideoId == videoId, ct);
        if (progress is null)
        {
            progress = new LearnerVideoLibraryProgress
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                VideoId = videoId,
            };
            db.LearnerVideoLibraryProgress.Add(progress);
        }

        progress.PositionSeconds = clampedPosition;
        progress.WatchedSeconds = Math.Max(progress.WatchedSeconds, clampedPosition);
        progress.LastWatchedAt = now;
        if (!progress.Completed && duration > 0
            && progress.WatchedSeconds >= Math.Ceiling(duration * CompletionThreshold))
        {
            progress.Completed = true;
            progress.CompletedAt = now;
        }
        await db.SaveChangesAsync(ct);

        return new VideoProgressUpdateResultDto(
            progress.PositionSeconds,
            progress.WatchedSeconds,
            PercentComplete(progress.WatchedSeconds, duration),
            progress.Completed);
    }

    public async Task<bool?> ToggleBookmarkAsync(string userId, string videoId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var video = await FindVisibleVideoAsync(userId, videoId, now, ct);
        if (video is null) return null;

        var existing = await db.LearnerVideoBookmarks
            .FirstOrDefaultAsync(b => b.UserId == userId && b.VideoId == videoId, ct);
        if (existing is not null)
        {
            db.LearnerVideoBookmarks.Remove(existing);
            await db.SaveChangesAsync(ct);
            return false;
        }

        db.LearnerVideoBookmarks.Add(new LearnerVideoBookmark
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            VideoId = videoId,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RecordEventAsync(
        string userId, string videoId, string? sessionId, string eventType, int positionSeconds, CancellationToken ct)
    {
        var exists = await db.LibraryVideos.AsNoTracking().AnyAsync(v => v.Id == videoId, ct);
        if (!exists) return false;

        db.VideoPlaybackEvents.Add(new VideoPlaybackEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            VideoId = videoId,
            SessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId,
            EventType = eventType,
            PositionSeconds = Math.Max(0, positionSeconds),
            OccurredAt = DateTimeOffset.UtcNow,
            PayloadJson = "{}",
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Published + release-dated + profession-visible lookup used by the playback path too.</summary>
    public async Task<LibraryVideo?> FindVisibleVideoAsync(
        string userId, string videoId, DateTimeOffset now, CancellationToken ct)
    {
        var video = await db.LibraryVideos.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == videoId
                && v.Status == ContentStatus.Published
                && (v.PublishAt == null || v.PublishAt <= now), ct);
        if (video is null) return null;

        var profession = await ResolveProfessionAsync(userId, ct);
        return IsProfessionVisible(video.ProfessionIdsJson, profession) ? video : null;
    }

    // ── Internals ──────────────────────────────────────────────────────────

    private async Task<List<LibraryVideo>> LoadVisibleVideosAsync(
        string userId, DateTimeOffset now, CancellationToken ct)
    {
        var profession = await ResolveProfessionAsync(userId, ct);
        var published = await db.LibraryVideos.AsNoTracking()
            .Where(v => v.Status == ContentStatus.Published
                && (v.PublishAt == null || v.PublishAt <= now))
            .ToListAsync(ct);
        // ProfessionIdsJson is a JSON column — filter client-side (never LINQ into JSON).
        return published.Where(v => IsProfessionVisible(v.ProfessionIdsJson, profession)).ToList();
    }

    private async Task<string?> ResolveProfessionAsync(string userId, CancellationToken ct)
    {
        var profession = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.ActiveProfessionId)
            .FirstOrDefaultAsync(ct);
        return profession?.Trim().ToLowerInvariant();
    }

    public static bool IsProfessionVisible(string? professionIdsJson, string? normalizedProfession)
    {
        if (string.IsNullOrWhiteSpace(professionIdsJson)) return true;
        try
        {
            using var doc = JsonDocument.Parse(professionIdsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return true;
            if (doc.RootElement.GetArrayLength() == 0) return true;
            if (string.IsNullOrWhiteSpace(normalizedProfession)) return false;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String
                    && string.Equals(el.GetString()?.Trim(), normalizedProfession, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        catch (JsonException)
        {
            return true; // malformed targeting must not hide content platform-wide
        }
    }

    private async Task<Dictionary<string, LearnerVideoLibraryProgress>> LoadProgressAsync(
        string userId, IReadOnlyCollection<string> videoIds, CancellationToken ct)
    {
        if (videoIds.Count == 0) return new(StringComparer.Ordinal);
        return await db.LearnerVideoLibraryProgress.AsNoTracking()
            .Where(p => p.UserId == userId && videoIds.Contains(p.VideoId))
            .ToDictionaryAsync(p => p.VideoId, StringComparer.Ordinal, ct);
    }

    private async Task<HashSet<string>> LoadBookmarksAsync(
        string userId, IReadOnlyCollection<string> videoIds, CancellationToken ct)
    {
        if (videoIds.Count == 0) return new(StringComparer.Ordinal);
        var ids = await db.LearnerVideoBookmarks.AsNoTracking()
            .Where(b => b.UserId == userId && videoIds.Contains(b.VideoId))
            .Select(b => b.VideoId)
            .ToListAsync(ct);
        return ids.ToHashSet(StringComparer.Ordinal);
    }

    private VideoSummaryDto ToSummary(
        LibraryVideo video,
        VideoAccessContext context,
        IReadOnlyDictionary<string, LearnerVideoLibraryProgress> progressByVideo,
        IReadOnlySet<string> bookmarked,
        IReadOnlyDictionary<string, List<VideoCategoryItem>> membershipsByVideo)
    {
        var entitlement = entitlements.Evaluate(context, video);
        var progress = progressByVideo.TryGetValue(video.Id, out var p)
            ? new VideoProgressDto(p.PositionSeconds, PercentComplete(p.WatchedSeconds, video.DurationSeconds), p.Completed)
            : null;
        var categoryIds = membershipsByVideo.TryGetValue(video.Id, out var m)
            ? m.OrderBy(x => x.SortOrder).Select(x => x.CategoryId).Distinct().ToList()
            : [];

        return new VideoSummaryDto(
            Id: video.Id,
            Title: video.Title,
            Description: video.Description,
            DurationSeconds: video.DurationSeconds,
            ThumbnailUrl: ResolveThumbnailUrl(video),
            AccessTier: video.AccessTier,
            IsAccessible: entitlement.Allowed,
            RequiresUpgrade: !entitlement.Allowed,
            LockReason: entitlement.Allowed ? null : "subscription_required",
            SubtestCode: video.SubtestCode,
            Difficulty: video.Difficulty,
            Tags: SplitTags(video.TagsCsv),
            IsFeatured: video.IsFeatured,
            PublishedAt: video.PublishedAt,
            ViewCount: video.ViewCount,
            Progress: progress,
            Bookmarked: bookmarked.Contains(video.Id),
            CategoryIds: categoryIds);
    }

    private async Task<(string? PreviousId, string? NextId)> ResolveAdjacentAsync(
        string userId, LibraryVideo video, List<VideoCategoryItem> memberships, DateTimeOffset now, CancellationToken ct)
    {
        if (memberships.Count == 0) return (null, null);

        // Primary category = the membership whose category has the lowest
        // DisplayOrder (ties by category id for determinism).
        var categoryIds = memberships.Select(m => m.CategoryId).Distinct().ToList();
        var categoryOrder = await db.VideoCategories.AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .Select(c => new { c.Id, c.DisplayOrder })
            .ToListAsync(ct);
        var primaryCategoryId = memberships
            .OrderBy(m => categoryOrder.FirstOrDefault(c => c.Id == m.CategoryId)?.DisplayOrder ?? int.MaxValue)
            .ThenBy(m => m.CategoryId, StringComparer.Ordinal)
            .Select(m => m.CategoryId)
            .First();

        var siblingItems = await db.VideoCategoryItems.AsNoTracking()
            .Where(i => i.CategoryId == primaryCategoryId)
            .OrderBy(i => i.SortOrder)
            .ToListAsync(ct);

        var siblingIds = siblingItems.Select(i => i.VideoId).ToList();
        var visibleSiblings = await db.LibraryVideos.AsNoTracking()
            .Where(v => siblingIds.Contains(v.Id)
                && v.Status == ContentStatus.Published
                && (v.PublishAt == null || v.PublishAt <= now))
            .Select(v => new { v.Id, v.ProfessionIdsJson })
            .ToListAsync(ct);
        var profession = await ResolveProfessionAsync(userId, ct);
        var visibleIds = visibleSiblings
            .Where(v => IsProfessionVisible(v.ProfessionIdsJson, profession))
            .Select(v => v.Id)
            .ToHashSet(StringComparer.Ordinal);

        var ordered = siblingItems.Where(i => visibleIds.Contains(i.VideoId)).Select(i => i.VideoId).ToList();
        var index = ordered.FindIndex(id => string.Equals(id, video.Id, StringComparison.Ordinal));
        return (
            index > 0 ? ordered[index - 1] : null,
            index >= 0 && index < ordered.Count - 1 ? ordered[index + 1] : null);
    }

    private static string? ResolveThumbnailUrl(LibraryVideo video)
        => !string.IsNullOrWhiteSpace(video.CustomThumbnailMediaAssetId)
            ? $"/v1/media/{video.CustomThumbnailMediaAssetId}/content"
            : video.BunnyThumbnailUrl;

    private static IReadOnlyList<string> SplitTags(string? tagsCsv)
        => string.IsNullOrWhiteSpace(tagsCsv)
            ? []
            : tagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal static int PercentComplete(int watchedSeconds, int durationSeconds)
        => durationSeconds <= 0
            ? 0
            : Math.Clamp((int)Math.Round(watchedSeconds * 100d / durationSeconds), 0, 100);

    public static IReadOnlyList<VideoChapterDto> ParseChapters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            var chapters = new List<VideoChapterDto>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var time = el.TryGetProperty("timeSeconds", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out var ti)
                    ? ti
                    : 0;
                var title = el.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String
                    ? titleEl.GetString() ?? "Chapter"
                    : "Chapter";
                chapters.Add(new VideoChapterDto(Math.Max(0, time), title));
            }
            return chapters;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
