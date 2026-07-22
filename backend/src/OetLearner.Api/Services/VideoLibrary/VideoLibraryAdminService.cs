using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.VideoLibrary;

// ── Admin contracts ──────────────────────────────────────────────────────────

public sealed record AdminVideoSummaryDto(
    string VideoId,
    string Title,
    string Status,
    string EncodeStatus,
    string AccessTier,
    IReadOnlyList<string> CategoryNames,
    int DurationSeconds,
    string? ThumbnailUrl,
    bool IsFeatured,
    long ViewCount,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishAt);

/// <summary>Lightweight video row for the per-user allocation / per-plan override pickers: the
/// axes the grouped tree needs (section = SubtestCode, language, profession) plus the curated
/// shelf/category name(s) — many videos share no distinguishing words in their own title (e.g.
/// "Writing Session 3"), so the category ("... New Medicine Crash Course ...", "... December
/// Batch ...") is often the only thing an admin can search or select by.</summary>
public sealed record AdminAllocatableVideoDto(
    string Id,
    string Title,
    string? SubtestCode,
    string? Language,
    IReadOnlyList<string> ProfessionIds,
    IReadOnlyList<string> CategoryNames);

public sealed record AdminVideoCaptionDto(Guid Id, string LanguageCode, string Label, bool SyncedToBunny);
public sealed record AdminVideoAttachmentDto(Guid Id, string Title, string MediaAssetId, int SortOrder);

public sealed record AdminVideoDetailDto(
    string VideoId,
    string Title,
    string? Description,
    string? SubtestCode,
    string? TagsCsv,
    string? Difficulty,
    IReadOnlyList<string> CategoryIds,
    IReadOnlyList<string> CategoryNames,
    string AccessTier,
    IReadOnlyList<string> TargetProfessionIds,
    string? BunnyVideoId,
    string? BunnyCollectionId,
    string EncodeStatus,
    int EncodeProgress,
    string? EncodeError,
    int DurationSeconds,
    int? Width,
    int? Height,
    string? ThumbnailUrl,
    string ThumbnailMode,
    string? CustomThumbnailAssetId,
    IReadOnlyList<AdminVideoCaptionDto> Captions,
    IReadOnlyList<VideoChapterDto> Chapters,
    IReadOnlyList<AdminVideoAttachmentDto> Attachments,
    bool IsFeatured,
    int SortOrder,
    string Status,
    DateTimeOffset? PublishAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ArchivedAt,
    long ViewCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Language = null);

public sealed record VideoPublishGateResult(bool CanPublish, string[] Errors, string[] Warnings);

/// <summary>
/// Admin CRUD + lifecycle for Video Library videos. Endpoint-facing; every
/// mutation stamps UpdatedAt/UpdatedByAdminId.
/// </summary>
public sealed class VideoLibraryAdminService(
    LearnerDbContext db,
    IBunnyStreamClient bunny,
    Settings.IRuntimeSettingsProvider settingsProvider,
    ILogger<VideoLibraryAdminService> logger)
{
    private static readonly string[] ValidAccessTiers = ["free", "premium"];
    private static readonly string[] ValidDifficulties = ["foundation", "core", "advanced"];

    public async Task<LibraryVideo> CreateDraftAsync(string adminId, string title, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw ApiException.Validation("title_required", "A video title is required.");
        }

        var now = DateTimeOffset.UtcNow;
        var video = new LibraryVideo
        {
            Id = $"vid_{Guid.NewGuid():N}",
            Title = title.Trim(),
            Status = ContentStatus.Draft,
            EncodeStatus = VideoEncodeStatus.NotUploaded,
            AccessTier = "premium",
            ProfessionIdsJson = "[]",
            ChaptersJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByAdminId = adminId,
            UpdatedByAdminId = adminId,
        };
        db.LibraryVideos.Add(video);
        await db.SaveChangesAsync(ct);
        return video;
    }

    public async Task<LibraryVideo> RequireVideoAsync(string videoId, CancellationToken ct)
        => await db.LibraryVideos.FirstOrDefaultAsync(v => v.Id == videoId, ct)
           ?? throw ApiException.NotFound("video_not_found", "Video not found.");

    public async Task ApplyPatchAsync(LibraryVideo video, string adminId, AdminVideoPatchRequest patch, CancellationToken ct)
    {
        if (patch.Title is not null)
        {
            if (string.IsNullOrWhiteSpace(patch.Title))
                throw ApiException.Validation("title_required", "Title cannot be blank.");
            video.Title = patch.Title.Trim();
        }
        if (patch.Description is not null)
            video.Description = string.IsNullOrWhiteSpace(patch.Description) ? null : patch.Description.Trim();
        if (patch.TagsCsv is not null)
            video.TagsCsv = string.IsNullOrWhiteSpace(patch.TagsCsv) ? null : patch.TagsCsv.Trim();
        if (patch.SubtestCode is not null)
            video.SubtestCode = string.IsNullOrWhiteSpace(patch.SubtestCode) ? null : patch.SubtestCode.Trim().ToLowerInvariant();
        if (patch.Difficulty is not null)
        {
            var difficulty = patch.Difficulty.Trim().ToLowerInvariant();
            if (difficulty.Length == 0)
            {
                video.Difficulty = null;
            }
            else if (!ValidDifficulties.Contains(difficulty, StringComparer.Ordinal))
            {
                throw ApiException.Validation("invalid_difficulty", "Difficulty must be foundation, core, or advanced.");
            }
            else
            {
                video.Difficulty = difficulty;
            }
        }
        if (patch.AccessTier is not null)
        {
            var tier = patch.AccessTier.Trim().ToLowerInvariant();
            if (!ValidAccessTiers.Contains(tier, StringComparer.Ordinal))
                throw ApiException.Validation("invalid_access_tier", "Access tier must be 'free' or 'premium'.");
            video.AccessTier = tier;
        }
        if (patch.TargetProfessionIds is not null)
        {
            video.ProfessionIdsJson = JsonSerializer.Serialize(patch.TargetProfessionIds
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim().ToLowerInvariant())
                .Distinct()
                .ToArray());
        }
        if (patch.Language is not null)
        {
            var language = patch.Language.Trim().ToLowerInvariant();
            if (language.Length == 0)
            {
                video.Language = null;
            }
            else if (language is not ("en" or "ar"))
            {
                throw ApiException.Validation("invalid_language", "Language must be 'en' or 'ar'.");
            }
            else
            {
                video.Language = language;
            }
        }
        if (patch.Language is not null || patch.SubtestCode is not null || patch.TargetProfessionIds is not null)
        {
            var targets = ParseProfessionIds(video.ProfessionIdsJson);
            if (!string.IsNullOrWhiteSpace(video.Language) && !string.IsNullOrWhiteSpace(video.SubtestCode)
                && !CourseContentMatrix.TryValidateVideo(video.Language, video.SubtestCode, targets, out var matrixError))
                throw ApiException.Validation("invalid_course_video_scope", matrixError);
        }
        if (patch.IsFeatured is not null) video.IsFeatured = patch.IsFeatured.Value;
        if (patch.SortOrder is not null) video.SortOrder = patch.SortOrder.Value;
        if (patch.BunnyCollectionId is not null)
            video.BunnyCollectionId = string.IsNullOrWhiteSpace(patch.BunnyCollectionId) ? null : patch.BunnyCollectionId.Trim();
        if (patch.PublishAt is { } publishAtEl)
        {
            video.PublishAt = publishAtEl.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String when string.IsNullOrWhiteSpace(publishAtEl.GetString()) => null,
                JsonValueKind.String when publishAtEl.TryGetDateTimeOffset(out var dt) => dt,
                _ => throw ApiException.Validation("invalid_publish_at", "publishAt must be an ISO timestamp or null."),
            };
        }

        if (patch.CategoryIds is not null)
        {
            await ReplaceCategoriesAsync(video.Id, patch.CategoryIds, ct);
        }

        Touch(video, adminId);
        await db.SaveChangesAsync(ct);
    }

    // ── Bunny upload lifecycle ──────────────────────────────────────────────

    public async Task<BunnyTusAuthorization> CreateUploadAuthorizationAsync(
        LibraryVideo video, string adminId, CancellationToken ct)
    {
        if (video.EncodeStatus == VideoEncodeStatus.Ready)
        {
            throw ApiException.Conflict("video_already_uploaded",
                "This video already has a ready encode. Use reset-upload to replace it.");
        }

        var settings = (await settingsProvider.GetAsync(ct)).BunnyStream;
        if (video.BunnyVideoId is null)
        {
            var collectionId = video.BunnyCollectionId ?? settings.CollectionId;
            var bunnyVideoId = await bunny.CreateVideoAsync(video.Title, collectionId, ct);
            video.BunnyVideoId = bunnyVideoId;
            video.BunnyLibraryId = settings.LibraryId;
            video.EncodeStatus = VideoEncodeStatus.Uploading;
            video.EncodeProgress = 0;
            video.EncodeError = null;
            Touch(video, adminId);
            await db.SaveChangesAsync(ct);
        }

        var expires = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds();
        return await bunny.CreateTusUploadAuthorizationAsync(video.BunnyVideoId!, expires, ct);
    }

    public async Task<BunnyTusAuthorization> ResetUploadAsync(LibraryVideo video, string adminId, CancellationToken ct)
    {
        var settings = (await settingsProvider.GetAsync(ct)).BunnyStream;
        if (!string.IsNullOrWhiteSpace(video.BunnyVideoId))
        {
            try
            {
                await bunny.DeleteVideoAsync(video.BunnyVideoId, ct);
            }
            catch (ApiException ex)
            {
                // Best-effort remote cleanup — a stale Bunny row must not block re-upload.
                logger.LogWarning("Reset-upload could not delete old Bunny video {BunnyVideoId}: {Code}",
                    video.BunnyVideoId, ex.ErrorCode);
            }
        }

        var bunnyVideoId = await bunny.CreateVideoAsync(video.Title, video.BunnyCollectionId ?? settings.CollectionId, ct);
        video.BunnyVideoId = bunnyVideoId;
        video.BunnyLibraryId = settings.LibraryId;
        video.EncodeStatus = VideoEncodeStatus.Uploading;
        video.EncodeProgress = 0;
        video.EncodeError = null;
        video.DurationSeconds = 0;
        video.Width = null;
        video.Height = null;
        video.BunnyThumbnailUrl = null;
        Touch(video, adminId);
        await db.SaveChangesAsync(ct);

        var expires = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds();
        return await bunny.CreateTusUploadAuthorizationAsync(bunnyVideoId, expires, ct);
    }

    public async Task RefreshStatusAsync(LibraryVideo video, string adminId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(video.BunnyVideoId))
        {
            throw ApiException.Validation("video_not_uploaded", "This video has no Bunny upload to refresh.");
        }
        var info = await bunny.GetVideoAsync(video.BunnyVideoId, ct);
        ApplyBunnyInfo(video, info);
        Touch(video, adminId);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Bunny status int → local encode status. 3 (Finished) and 4 (Resolution finished) → Ready.</summary>
    // Bunny Stream video status enum: 0 Created (object exists, bytes NOT yet
    // received) · 1 Uploaded · 2 Processing · 3 Transcoding · 4 Finished ·
    // 5 Error · 6 UploadFailed. Status 0 is the "awaiting upload" state — map it
    // to Uploading (not Queued) so an interrupted/never-completed upload stays
    // recoverable in the admin card (which re-offers the upload; tus resumes)
    // instead of polling a phantom encode forever.
    public static VideoEncodeStatus MapBunnyStatus(int bunnyStatus) => bunnyStatus switch
    {
        0 => VideoEncodeStatus.Uploading,
        1 => VideoEncodeStatus.Processing,
        2 => VideoEncodeStatus.Encoding,
        3 or 4 => VideoEncodeStatus.Ready,
        5 or 6 => VideoEncodeStatus.Failed,
        _ => VideoEncodeStatus.Processing,
    };

    public static void ApplyBunnyInfo(LibraryVideo video, BunnyVideoInfo info)
    {
        video.EncodeStatus = MapBunnyStatus(info.Status);
        // Robust guard: a video with no stored bytes was never actually uploaded
        // (early/mid interruption). Keep it in the uploadable "Uploading" state
        // regardless of the raw Bunny status so the card re-offers the upload.
        if (info.StorageSizeBytes <= 0 && video.EncodeStatus != VideoEncodeStatus.Failed)
            video.EncodeStatus = VideoEncodeStatus.Uploading;
        video.EncodeProgress = Math.Clamp(info.EncodeProgress, 0, 100);
        if (info.LengthSeconds > 0) video.DurationSeconds = info.LengthSeconds;
        if (!string.IsNullOrWhiteSpace(info.ThumbnailUrl)) video.BunnyThumbnailUrl = info.ThumbnailUrl;
        if (info.Width is > 0) video.Width = info.Width;
        if (info.Height is > 0) video.Height = info.Height;
        video.EncodeError = video.EncodeStatus == VideoEncodeStatus.Failed
            ? "Bunny Stream reported the encode as failed."
            : null;
    }

    // ── Publish gate + lifecycle ────────────────────────────────────────────

    public async Task<VideoPublishGateResult> EvaluatePublishGateAsync(LibraryVideo video, CancellationToken ct)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (video.EncodeStatus != VideoEncodeStatus.Ready)
            errors.Add("Video must finish encoding on Bunny Stream before publishing.");
        if (video.DurationSeconds <= 0)
            errors.Add("Video duration is unknown — refresh the encode status first.");
        if (string.IsNullOrWhiteSpace(video.Title))
            errors.Add("Video title is required.");
        if (!ValidAccessTiers.Contains(video.AccessTier?.Trim().ToLowerInvariant() ?? string.Empty, StringComparer.Ordinal))
            errors.Add("Access tier must be 'free' or 'premium'.");
        if (!CourseContentMatrix.TryValidateVideo(video.Language, video.SubtestCode, ParseProfessionIds(video.ProfessionIdsJson), out var matrixError))
            errors.Add(matrixError);

        var settings = (await settingsProvider.GetAsync(ct)).BunnyStream;
        if (!settings.IsConfigured)
            errors.Add("Bunny Stream is not configured — playback would be impossible.");

        if (string.IsNullOrWhiteSpace(video.CustomThumbnailMediaAssetId) && string.IsNullOrWhiteSpace(video.BunnyThumbnailUrl))
            warnings.Add("No thumbnail is set.");
        if (!await db.VideoCaptionTracks.AsNoTracking().AnyAsync(c => c.VideoId == video.Id, ct))
            warnings.Add("No caption tracks are attached.");
        if (!await db.VideoCategoryItems.AsNoTracking().AnyAsync(i => i.VideoId == video.Id, ct))
            warnings.Add("Video is not in any category.");
        if (string.IsNullOrWhiteSpace(video.Description))
            warnings.Add("Description is empty.");

        return new VideoPublishGateResult(errors.Count == 0, [.. errors], [.. warnings]);
    }

    public async Task<VideoPublishGateResult> PublishAsync(
        LibraryVideo video, string adminId, DateTimeOffset? publishAt, CancellationToken ct)
    {
        var gate = await EvaluatePublishGateAsync(video, ct);
        if (!gate.CanPublish) return gate;

        video.Status = ContentStatus.Published;
        video.PublishedAt = DateTimeOffset.UtcNow;
        video.PublishAt = publishAt;
        video.ArchivedAt = null;
        Touch(video, adminId);
        await db.SaveChangesAsync(ct);
        return gate;
    }

    public async Task UnpublishAsync(LibraryVideo video, string adminId, CancellationToken ct)
    {
        video.Status = ContentStatus.Draft;
        video.PublishAt = null;
        Touch(video, adminId);
        await db.SaveChangesAsync(ct);
    }

    public async Task ArchiveAsync(LibraryVideo video, string adminId, CancellationToken ct)
    {
        video.Status = ContentStatus.Archived;
        video.ArchivedAt = DateTimeOffset.UtcNow;
        video.PublishAt = null;
        Touch(video, adminId);
        await db.SaveChangesAsync(ct);
    }

    public async Task RestoreAsync(LibraryVideo video, string adminId, CancellationToken ct)
    {
        video.Status = ContentStatus.Draft;
        video.ArchivedAt = null;
        Touch(video, adminId);
        await db.SaveChangesAsync(ct);
    }

    public async Task<BulkActionResult> BulkLifecycleAsync(
        string adminId, string action, IReadOnlyList<string> videoIds, CancellationToken ct)
    {
        var normalized = action?.Trim().ToLowerInvariant();
        if (normalized is not ("publish" or "unpublish" or "archive"))
        {
            throw ApiException.Validation("invalid_bulk_action", "Action must be publish, unpublish, or archive.");
        }

        var ids = videoIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        int succeeded = 0, skipped = 0, failed = 0;
        var errors = new List<string>();

        foreach (var id in ids)
        {
            var video = await db.LibraryVideos.FirstOrDefaultAsync(v => v.Id == id, ct);
            if (video is null)
            {
                failed++;
                if (errors.Count < 20) errors.Add($"{id}: not found");
                continue;
            }

            switch (normalized)
            {
                case "publish":
                    if (video.Status == ContentStatus.Published) { skipped++; break; }
                    var gate = await PublishAsync(video, adminId, publishAt: null, ct);
                    if (gate.CanPublish) succeeded++;
                    else
                    {
                        failed++;
                        if (errors.Count < 20) errors.Add($"{id}: {string.Join("; ", gate.Errors)}");
                    }
                    break;
                case "unpublish":
                    if (video.Status != ContentStatus.Published) { skipped++; break; }
                    await UnpublishAsync(video, adminId, ct);
                    succeeded++;
                    break;
                case "archive":
                    if (video.Status == ContentStatus.Archived) { skipped++; break; }
                    await ArchiveAsync(video, adminId, ct);
                    succeeded++;
                    break;
            }
        }

        return new BulkActionResult(videoIds.Count, succeeded, skipped, failed, [.. errors]);
    }

    /// <summary>
    /// Irreversible permanent delete: deletes the Bunny video then removes
    /// dependents-first — events → sessions → progress → bookmarks → captions →
    /// attachments → category items → the video row. Callable from ANY status
    /// (the system-admin policy + an explicit confirm are the safety); a video
    /// need not be archived first.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> ForceDeleteAsync(
        LibraryVideo video, string adminId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(video.BunnyVideoId))
        {
            try
            {
                await bunny.DeleteVideoAsync(video.BunnyVideoId, ct);
            }
            catch (BunnyNotConfiguredException)
            {
                logger.LogWarning(
                    "Force-delete of {VideoId}: Bunny is not configured; remote video {BunnyVideoId} was not deleted.",
                    video.Id, video.BunnyVideoId);
            }
        }

        var report = new Dictionary<string, int>
        {
            ["VideoPlaybackEvents"] = await DeleteWhereAsync(db.VideoPlaybackEvents, e => e.VideoId == video.Id, ct),
            ["VideoPlaybackSessions"] = await DeleteWhereAsync(db.VideoPlaybackSessions, s => s.VideoId == video.Id, ct),
            ["LearnerVideoLibraryProgress"] = await DeleteWhereAsync(db.LearnerVideoLibraryProgress, p => p.VideoId == video.Id, ct),
            ["LearnerVideoBookmarks"] = await DeleteWhereAsync(db.LearnerVideoBookmarks, b => b.VideoId == video.Id, ct),
            ["VideoCaptionTracks"] = await DeleteWhereAsync(db.VideoCaptionTracks, c => c.VideoId == video.Id, ct),
            ["VideoAttachments"] = await DeleteWhereAsync(db.VideoAttachments, a => a.VideoId == video.Id, ct),
            ["VideoCategoryItems"] = await DeleteWhereAsync(db.VideoCategoryItems, i => i.VideoId == video.Id, ct),
        };

        db.LibraryVideos.Remove(
            await db.LibraryVideos.FirstAsync(v => v.Id == video.Id, ct));
        await db.SaveChangesAsync(ct);
        report["LibraryVideos"] = 1;

        logger.LogWarning("Force-deleted library video {VideoId} ({Title}) by admin {AdminId}.",
            video.Id, video.Title, adminId);
        return report;
    }

    // ── Projections ────────────────────────────────────────────────────────

    public async Task<AdminVideoDetailDto> BuildDetailAsync(LibraryVideo video, CancellationToken ct)
    {
        var memberships = await db.VideoCategoryItems.AsNoTracking()
            .Where(i => i.VideoId == video.Id)
            .OrderBy(i => i.SortOrder)
            .ToListAsync(ct);
        var categoryIds = memberships.Select(m => m.CategoryId).Distinct().ToList();
        var categoryNames = await db.VideoCategories.AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .Select(c => c.Title)
            .ToListAsync(ct);

        var captions = await db.VideoCaptionTracks.AsNoTracking()
            .Where(c => c.VideoId == video.Id)
            .OrderBy(c => c.SortOrder)
            .Select(c => new AdminVideoCaptionDto(c.Id, c.LanguageCode, c.Label, c.SyncedToBunnyAt != null))
            .ToListAsync(ct);

        var attachments = await db.VideoAttachments.AsNoTracking()
            .Where(a => a.VideoId == video.Id)
            .OrderBy(a => a.SortOrder)
            .Select(a => new AdminVideoAttachmentDto(a.Id, a.Title, a.MediaAssetId, a.SortOrder))
            .ToListAsync(ct);

        var hasCustomThumbnail = !string.IsNullOrWhiteSpace(video.CustomThumbnailMediaAssetId);
        var bunny = (await settingsProvider.GetAsync(ct)).BunnyStream;
        return new AdminVideoDetailDto(
            VideoId: video.Id,
            Title: video.Title,
            Description: video.Description,
            SubtestCode: video.SubtestCode,
            TagsCsv: video.TagsCsv,
            Difficulty: video.Difficulty,
            CategoryIds: categoryIds,
            CategoryNames: categoryNames,
            AccessTier: video.AccessTier,
            TargetProfessionIds: ParseProfessionIds(video.ProfessionIdsJson),
            BunnyVideoId: video.BunnyVideoId,
            BunnyCollectionId: video.BunnyCollectionId,
            EncodeStatus: EncodeStatusLabel(video.EncodeStatus),
            EncodeProgress: video.EncodeProgress,
            EncodeError: video.EncodeError,
            DurationSeconds: video.DurationSeconds,
            Width: video.Width,
            Height: video.Height,
            ThumbnailUrl: VideoThumbnailUrl.Resolve(video, bunny),
            ThumbnailMode: hasCustomThumbnail ? "custom" : "auto",
            CustomThumbnailAssetId: video.CustomThumbnailMediaAssetId,
            Captions: captions,
            Chapters: VideoLibraryLearnerService.ParseChapters(video.ChaptersJson),
            Attachments: attachments,
            IsFeatured: video.IsFeatured,
            SortOrder: video.SortOrder,
            Status: StatusLabel(video.Status),
            PublishAt: video.PublishAt,
            PublishedAt: video.PublishedAt,
            ArchivedAt: video.ArchivedAt,
            ViewCount: video.ViewCount,
            CreatedAt: video.CreatedAt,
            UpdatedAt: video.UpdatedAt,
            Language: video.Language);
    }

    public static string EncodeStatusLabel(VideoEncodeStatus status) => status switch
    {
        VideoEncodeStatus.NotUploaded => "not_uploaded",
        VideoEncodeStatus.Uploading => "uploading",
        VideoEncodeStatus.Queued => "queued",
        VideoEncodeStatus.Processing => "processing",
        VideoEncodeStatus.Encoding => "encoding",
        VideoEncodeStatus.Ready => "ready",
        _ => "failed",
    };

    public static VideoEncodeStatus? ParseEncodeStatusLabel(string? label) => label?.Trim().ToLowerInvariant() switch
    {
        "not_uploaded" => VideoEncodeStatus.NotUploaded,
        "uploading" => VideoEncodeStatus.Uploading,
        "queued" => VideoEncodeStatus.Queued,
        "processing" => VideoEncodeStatus.Processing,
        "encoding" => VideoEncodeStatus.Encoding,
        "ready" => VideoEncodeStatus.Ready,
        "failed" => VideoEncodeStatus.Failed,
        _ => null,
    };

    /// <summary>
    /// PascalCase enum name — the admin FE contract compares against
    /// 'Draft' | 'InReview' | 'Published' | 'Rejected' | 'Archived'
    /// (see app/admin/content/videos/page.tsx bulk-action eligibility).
    /// </summary>
    public static string StatusLabel(ContentStatus status) => status.ToString();

    public static IReadOnlyList<string> ParseProfessionIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json)?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ── Internals ──────────────────────────────────────────────────────────

    private async Task ReplaceCategoriesAsync(string videoId, IReadOnlyList<string> categoryIds, CancellationToken ct)
    {
        var wanted = categoryIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        var known = await db.VideoCategories.AsNoTracking()
            .Where(c => wanted.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct);
        var missing = wanted.Except(known, StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            throw ApiException.Validation("category_not_found", $"Unknown category id(s): {string.Join(", ", missing)}.");
        }

        var existing = await db.VideoCategoryItems.Where(i => i.VideoId == videoId).ToListAsync(ct);
        db.VideoCategoryItems.RemoveRange(existing.Where(i => !wanted.Contains(i.CategoryId)));
        var have = existing.Select(i => i.CategoryId).ToHashSet(StringComparer.Ordinal);
        foreach (var categoryId in wanted.Where(id => !have.Contains(id)))
        {
            var nextSort = await db.VideoCategoryItems.AsNoTracking()
                .Where(i => i.CategoryId == categoryId)
                .Select(i => (int?)i.SortOrder)
                .MaxAsync(ct) ?? -1;
            db.VideoCategoryItems.Add(new VideoCategoryItem
            {
                Id = Guid.NewGuid(),
                CategoryId = categoryId,
                VideoId = videoId,
                SortOrder = nextSort + 1,
            });
        }
    }

    private async Task<int> DeleteWhereAsync<T>(
        DbSet<T> set, System.Linq.Expressions.Expression<Func<T, bool>> predicate, CancellationToken ct)
        where T : class
    {
        if (db.Database.IsRelational())
        {
            return await set.Where(predicate).ExecuteDeleteAsync(ct);
        }
        var rows = await set.Where(predicate).ToListAsync(ct);
        set.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    private static void Touch(LibraryVideo video, string adminId)
    {
        video.UpdatedAt = DateTimeOffset.UtcNow;
        video.UpdatedByAdminId = adminId;
    }
}

/// <summary>PATCH body for /v1/admin/video-library/videos/{videoId}. Null = leave unchanged.</summary>
public sealed class AdminVideoPatchRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? TagsCsv { get; set; }
    public string? Difficulty { get; set; }
    public string? SubtestCode { get; set; }
    public string[]? CategoryIds { get; set; }
    public string? AccessTier { get; set; }
    public string[]? TargetProfessionIds { get; set; }
    /// <summary>Instruction language: "en" | "ar". Omitted = unchanged; "" = clear; else set.</summary>
    public string? Language { get; set; }
    public bool? IsFeatured { get; set; }
    public int? SortOrder { get; set; }
    /// <summary>Tri-state: omitted = unchanged; null/"" = clear; ISO string = set.</summary>
    public JsonElement? PublishAt { get; set; }
    /// <summary>Bunny collection membership mirror. Omitted = unchanged; "" = clear; guid = set.</summary>
    public string? BunnyCollectionId { get; set; }
}
