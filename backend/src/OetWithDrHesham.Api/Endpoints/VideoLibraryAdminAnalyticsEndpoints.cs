using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.VideoLibrary;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// Category CRUD and admin analytics for the Video Library. Mapped inside the
/// /v1/admin/video-library group by <see cref="VideoLibraryAdminEndpoints"/>.
/// Analytics routes use the AdminQualityAnalytics policy (inventory-pinned by
/// EndpointRegistrationTests.AnalyticsRoutes_RequireQualityAnalyticsPolicy).
/// </summary>
internal static class VideoLibraryAdminAnalyticsEndpoints
{
    // ── Categories ─────────────────────────────────────────────────────────

    internal static RouteGroupBuilder MapVideoLibraryCategoryEndpoints(this RouteGroupBuilder admin)
    {
        admin.MapGet("/categories", async (
            LearnerDbContext db,
            [FromQuery] bool? includeInactive,
            CancellationToken ct) =>
        {
            var query = db.VideoCategories.AsNoTracking().AsQueryable();
            if (includeInactive != true)
            {
                query = query.Where(c => c.Status == ContentStatus.Published);
            }
            var categories = await query
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Title)
                .ToListAsync(ct);
            var categoryIds = categories.Select(c => c.Id).ToList();
            var counts = await db.VideoCategoryItems.AsNoTracking()
                .Where(i => categoryIds.Contains(i.CategoryId))
                .GroupBy(i => i.CategoryId)
                .Select(g => new { CategoryId = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            var countsById = counts.ToDictionary(c => c.CategoryId, c => c.Count, StringComparer.Ordinal);

            return Results.Ok(categories.Select(c => new
            {
                id = c.Id,
                title = c.Title,
                slug = c.Slug,
                description = c.Description,
                displayOrder = c.DisplayOrder,
                // Categories are a simple on/off concept for admins — the FE
                // contract is 'active' | 'inactive' (Published maps to active).
                status = c.Status == ContentStatus.Published ? "active" : "inactive",
                videoCount = countsById.TryGetValue(c.Id, out var n) ? n : 0,
            }));
        })
        .WithAdminRead("AdminContentRead");

        admin.MapPost("/categories", async (
            AdminVideoCategoryRequest request,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return Results.BadRequest(new { code = "title_required", message = "Category title is required." });
            }

            var now = DateTimeOffset.UtcNow;
            var slug = await ResolveUniqueSlugAsync(db, Slugify(request.Title), ct);
            var maxOrder = await db.VideoCategories.AsNoTracking()
                .Select(c => (int?)c.DisplayOrder)
                .MaxAsync(ct) ?? -1;
            var category = new VideoCategory
            {
                Id = $"vcat_{Guid.NewGuid():N}",
                Title = request.Title.Trim(),
                Slug = slug,
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                DisplayOrder = maxOrder + 1,
                Status = ContentStatus.Published,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.VideoCategories.Add(category);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = category.Id, title = category.Title, slug = category.Slug });
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPatch("/categories/{categoryId}", async (
            string categoryId,
            AdminVideoCategoryPatchRequest request,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var category = await db.VideoCategories.FirstOrDefaultAsync(c => c.Id == categoryId, ct);
            if (category is null)
            {
                return Results.NotFound(new { code = "category_not_found", message = "Category not found." });
            }
            if (request.Title is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Title))
                {
                    return Results.BadRequest(new { code = "title_required", message = "Category title cannot be blank." });
                }
                category.Title = request.Title.Trim();
            }
            if (request.Description is not null)
            {
                category.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            }
            // The admin FE PATCHes { status: 'active' | 'inactive' }; IsActive is
            // kept as an equivalent boolean alias.
            var requestedActive = request.Status?.Trim().ToLowerInvariant() switch
            {
                "active" => (bool?)true,
                "inactive" => false,
                _ => request.IsActive,
            };
            if (requestedActive is not null)
            {
                category.Status = requestedActive.Value ? ContentStatus.Published : ContentStatus.Archived;
            }
            category.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = category.Id, title = category.Title, slug = category.Slug });
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapDelete("/categories/{categoryId}", async (
            string categoryId,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var category = await db.VideoCategories.FirstOrDefaultAsync(c => c.Id == categoryId, ct);
            if (category is null)
            {
                return Results.NotFound(new { code = "category_not_found", message = "Category not found." });
            }
            var videoCount = await db.VideoCategoryItems.AsNoTracking().CountAsync(i => i.CategoryId == categoryId, ct);
            if (videoCount > 0)
            {
                return Results.Conflict(new
                {
                    code = "category_not_empty",
                    message = $"Category still contains {videoCount} video(s). Remove them first.",
                });
            }
            db.VideoCategories.Remove(category);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { deleted = true });
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPut("/categories/order", async (
            AdminOrderedIdsRequest request,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var order = (request.OrderedIds ?? []).ToList();
            var categories = await db.VideoCategories.ToListAsync(ct);
            foreach (var category in categories)
            {
                var index = order.IndexOf(category.Id);
                if (index >= 0)
                {
                    category.DisplayOrder = index;
                    category.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { updated = true });
        })
        .WithAdminWrite("AdminContentWrite");

        return admin;
    }

    // ── Analytics (AdminQualityAnalytics) ──────────────────────────────────

    internal static RouteGroupBuilder MapVideoLibraryAnalyticsEndpoints(this RouteGroupBuilder admin)
    {
        admin.MapGet("/analytics/summary", async (
            LearnerDbContext db,
            [FromQuery] int? days,
            CancellationToken ct) =>
        {
            var windowDays = days is null or < 1 or > 365 ? 30 : days.Value;
            var since = DateTimeOffset.UtcNow.AddDays(-windowDays);

            var publishedVideos = await db.LibraryVideos.AsNoTracking()
                .CountAsync(v => v.Status == ContentStatus.Published, ct);

            var playEvents = await db.VideoPlaybackEvents.AsNoTracking()
                .Where(e => e.EventType == "play" && e.OccurredAt >= since)
                .Select(e => new { e.VideoId, e.OccurredAt })
                .ToListAsync(ct);

            var progress = await db.LearnerVideoLibraryProgress.AsNoTracking()
                .Select(p => new { p.VideoId, p.WatchedSeconds, p.Completed })
                .ToListAsync(ct);
            var durations = await db.LibraryVideos.AsNoTracking()
                .Select(v => new { v.Id, v.Title, v.DurationSeconds })
                .ToListAsync(ct);
            var durationById = durations.ToDictionary(v => v.Id, StringComparer.Ordinal);

            var completionPercents = progress
                .Where(p => durationById.TryGetValue(p.VideoId, out var v) && v.DurationSeconds > 0)
                .Select(p => VideoLibraryLearnerService.PercentComplete(p.WatchedSeconds, durationById[p.VideoId].DurationSeconds))
                .ToList();

            var viewsPerDay = playEvents
                .GroupBy(e => e.OccurredAt.UtcDateTime.Date)
                .OrderBy(g => g.Key)
                .Select(g => new { date = g.Key.ToString("yyyy-MM-dd"), views = g.Count() })
                .ToList();

            var viewsByVideo = playEvents
                .GroupBy(e => e.VideoId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            var topVideos = viewsByVideo
                .OrderByDescending(kv => kv.Value)
                .Take(10)
                .Select(kv =>
                {
                    durationById.TryGetValue(kv.Key, out var video);
                    var videoProgress = progress.Where(p => p.VideoId == kv.Key).ToList();
                    var watchHours = Math.Round(videoProgress.Sum(p => (double)p.WatchedSeconds) / 3600d, 2);
                    var completion = video is { DurationSeconds: > 0 } && videoProgress.Count > 0
                        ? (int)Math.Round(videoProgress.Average(p =>
                            VideoLibraryLearnerService.PercentComplete(p.WatchedSeconds, video.DurationSeconds)))
                        : 0;
                    return new
                    {
                        videoId = kv.Key,
                        title = video?.Title ?? kv.Key,
                        views = kv.Value,
                        watchHours,
                        completionPercent = completion,
                    };
                })
                .ToList();

            var memberships = await db.VideoCategoryItems.AsNoTracking()
                .Select(i => new { i.CategoryId, i.VideoId })
                .ToListAsync(ct);
            var categories = await db.VideoCategories.AsNoTracking()
                .Select(c => new { c.Id, c.Title })
                .ToListAsync(ct);
            var viewsByCategory = categories
                .Select(c => new
                {
                    categoryId = c.Id,
                    title = c.Title,
                    views = memberships
                        .Where(m => m.CategoryId == c.Id)
                        .Sum(m => viewsByVideo.TryGetValue(m.VideoId, out var n) ? n : 0),
                })
                .Where(c => c.views > 0)
                .OrderByDescending(c => c.views)
                .ToList();

            return Results.Ok(new
            {
                totals = new
                {
                    publishedVideos,
                    views = playEvents.Count,
                    watchHours = Math.Round(progress.Sum(p => (double)p.WatchedSeconds) / 3600d, 2),
                    avgCompletionPercent = completionPercents.Count > 0
                        ? (int)Math.Round(completionPercents.Average())
                        : 0,
                },
                viewsPerDay,
                topVideos,
                viewsByCategory,
            });
        })
        .WithAdminRead("AdminQualityAnalytics");

        admin.MapGet("/videos/{videoId}/analytics/summary", async (
            string videoId,
            LearnerDbContext db,
            [FromQuery] int? days,
            CancellationToken ct) =>
        {
            var video = await db.LibraryVideos.AsNoTracking().FirstOrDefaultAsync(v => v.Id == videoId, ct);
            if (video is null)
            {
                return Results.NotFound(new { code = "video_not_found", message = "Video not found." });
            }

            var windowDays = days is null or < 1 or > 365 ? 30 : days.Value;
            var since = DateTimeOffset.UtcNow.AddDays(-windowDays);
            var playEvents = await db.VideoPlaybackEvents.AsNoTracking()
                .Where(e => e.VideoId == videoId && e.EventType == "play" && e.OccurredAt >= since)
                .Select(e => new { e.UserId, e.OccurredAt })
                .ToListAsync(ct);

            var progress = await db.LearnerVideoLibraryProgress.AsNoTracking()
                .Where(p => p.VideoId == videoId)
                .Select(p => new { p.WatchedSeconds, p.PositionSeconds })
                .ToListAsync(ct);

            var retention = new int[10];
            if (video.DurationSeconds > 0)
            {
                foreach (var p in progress)
                {
                    var reached = Math.Clamp(
                        (int)Math.Floor(p.PositionSeconds * 10d / video.DurationSeconds), 0, 9);
                    for (var bucket = 0; bucket <= reached; bucket++)
                    {
                        retention[bucket]++;
                    }
                }
            }

            return Results.Ok(new
            {
                views = playEvents.Count,
                uniqueViewers = playEvents.Select(e => e.UserId).Distinct(StringComparer.Ordinal).Count(),
                watchHours = Math.Round(progress.Sum(p => (double)p.WatchedSeconds) / 3600d, 2),
                avgCompletionPercent = video.DurationSeconds > 0 && progress.Count > 0
                    ? (int)Math.Round(progress.Average(p =>
                        VideoLibraryLearnerService.PercentComplete(p.WatchedSeconds, video.DurationSeconds)))
                    : 0,
                viewsPerDay = playEvents
                    .GroupBy(e => e.OccurredAt.UtcDateTime.Date)
                    .OrderBy(g => g.Key)
                    .Select(g => new { date = g.Key.ToString("yyyy-MM-dd"), views = g.Count() }),
                retentionBuckets = retention,
            });
        })
        .WithAdminRead("AdminQualityAnalytics");

        admin.MapGet("/videos/{videoId}/analytics/viewers", async (
            HttpContext http,
            string videoId,
            LearnerDbContext db,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            CancellationToken ct) =>
        {
            var video = await db.LibraryVideos.AsNoTracking().FirstOrDefaultAsync(v => v.Id == videoId, ct);
            if (video is null)
            {
                return Results.NotFound(new { code = "video_not_found", message = "Video not found." });
            }

            var pageNumber = Math.Max(1, page ?? 1);
            var size = pageSize is null or < 1 or > 200 ? 50 : pageSize.Value;

            var query = db.LearnerVideoLibraryProgress.AsNoTracking()
                .Where(p => p.VideoId == videoId)
                .OrderByDescending(p => p.LastWatchedAt);
            var total = await query.CountAsync(ct);
            var pageRows = await query.Skip((pageNumber - 1) * size).Take(size).ToListAsync(ct);

            var userIds = pageRows.Select(p => p.UserId).Distinct().ToList();
            var users = await db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Email, u.DisplayName })
                .ToListAsync(ct);
            var usersById = users.ToDictionary(u => u.Id, StringComparer.Ordinal);

            http.Response.Headers["X-Total-Count"] = total.ToString();
            return Results.Ok(pageRows.Select(p => new
            {
                userId = p.UserId,
                email = usersById.TryGetValue(p.UserId, out var u) ? u.Email : null,
                name = usersById.TryGetValue(p.UserId, out var u2) ? u2.DisplayName : null,
                positionSeconds = p.PositionSeconds,
                percentComplete = VideoLibraryLearnerService.PercentComplete(p.WatchedSeconds, video.DurationSeconds),
                completed = p.Completed,
                lastWatchedAt = p.LastWatchedAt,
            }));
        })
        .WithAdminRead("AdminQualityAnalytics");

        return admin;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string Slugify(string title)
    {
        var slug = new string(title.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray());
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }
        slug = slug.Trim('-');
        return string.IsNullOrEmpty(slug) ? $"category-{Guid.NewGuid():N}"[..16] : slug;
    }

    private static async Task<string> ResolveUniqueSlugAsync(LearnerDbContext db, string baseSlug, CancellationToken ct)
    {
        var slug = baseSlug;
        var suffix = 2;
        while (await db.VideoCategories.AsNoTracking().AnyAsync(c => c.Slug == slug, ct))
        {
            slug = $"{baseSlug}-{suffix++}";
        }
        return slug;
    }
}

public sealed record AdminVideoCategoryRequest(string? Title, string? Description);
public sealed record AdminVideoCategoryPatchRequest(string? Title, string? Description, bool? IsActive, string? Status);
