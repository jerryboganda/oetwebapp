using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.VideoLibrary;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin CRUD + lifecycle for the Video Library (Bunny Stream backed).
/// Uploads go browser→Bunny via presigned TUS; this API only mints the
/// authorization and tracks encode state.
/// </summary>
public static class VideoLibraryAdminEndpoints
{
    public static IEndpointRouteBuilder MapVideoLibraryAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin/video-library")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        // ── Videos: list / create / detail / patch ─────────────────────────

        admin.MapGet("/videos", async (
            HttpContext http,
            LearnerDbContext db,
            [FromQuery] string? q,
            [FromQuery] string? status,
            [FromQuery] string? accessTier,
            [FromQuery] string? encodeStatus,
            [FromQuery] string? categoryId,
            [FromQuery] string? subtestCode,
            [FromQuery] string? profession,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            CancellationToken ct) =>
        {
            var pageNumber = Math.Max(1, page ?? 1);
            var size = pageSize is null or < 1 or > 200 ? 50 : pageSize.Value;

            var query = db.LibraryVideos.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var needle = q.Trim().ToLower();
                query = query.Where(v => v.Title.ToLower().Contains(needle)
                    || (v.Description != null && v.Description.ToLower().Contains(needle)));
            }
            if (!string.IsNullOrWhiteSpace(status)
                && Enum.TryParse<ContentStatus>(status.Trim().Replace("_", ""), ignoreCase: true, out var parsedStatus))
            {
                query = query.Where(v => v.Status == parsedStatus);
            }
            if (!string.IsNullOrWhiteSpace(accessTier))
            {
                var tier = accessTier.Trim().ToLower();
                query = query.Where(v => v.AccessTier == tier);
            }
            if (VideoLibraryAdminService.ParseEncodeStatusLabel(encodeStatus) is { } parsedEncode)
            {
                query = query.Where(v => v.EncodeStatus == parsedEncode);
            }
            if (!string.IsNullOrWhiteSpace(subtestCode))
            {
                var code = subtestCode.Trim().ToLower();
                query = query.Where(v => v.SubtestCode == code);
            }
            if (!string.IsNullOrWhiteSpace(categoryId))
            {
                var inCategory = db.VideoCategoryItems.AsNoTracking()
                    .Where(i => i.CategoryId == categoryId)
                    .Select(i => i.VideoId);
                query = query.Where(v => inCategory.Contains(v.Id));
            }

            var candidates = await query
                .OrderByDescending(v => v.UpdatedAt)
                .ToListAsync(ct);

            // ProfessionIdsJson is a JSON column — filter client-side.
            if (!string.IsNullOrWhiteSpace(profession))
            {
                var normalized = profession.Trim().ToLowerInvariant();
                candidates = candidates
                    .Where(v => VideoLibraryAdminService.ParseProfessionIds(v.ProfessionIdsJson)
                        .Any(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            var total = candidates.Count;
            var items = candidates.Skip((pageNumber - 1) * size).Take(size).ToList();

            var videoIds = items.Select(v => v.Id).ToList();
            var names = await (
                from item in db.VideoCategoryItems.AsNoTracking()
                join category in db.VideoCategories.AsNoTracking() on item.CategoryId equals category.Id
                where videoIds.Contains(item.VideoId)
                select new { item.VideoId, category.Title }).ToListAsync(ct);
            var namesByVideo = names
                .GroupBy(n => n.VideoId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(n => n.Title).Distinct().ToList(), StringComparer.Ordinal);

            http.Response.Headers["X-Total-Count"] = total.ToString();
            return Results.Ok(items.Select(v => new AdminVideoSummaryDto(
                VideoId: v.Id,
                Title: v.Title,
                Status: VideoLibraryAdminService.StatusLabel(v.Status),
                EncodeStatus: VideoLibraryAdminService.EncodeStatusLabel(v.EncodeStatus),
                AccessTier: v.AccessTier,
                CategoryNames: namesByVideo.TryGetValue(v.Id, out var n) ? n : [],
                DurationSeconds: v.DurationSeconds,
                ThumbnailUrl: !string.IsNullOrWhiteSpace(v.CustomThumbnailMediaAssetId)
                    ? $"/v1/media/{v.CustomThumbnailMediaAssetId}/content"
                    : v.BunnyThumbnailUrl,
                IsFeatured: v.IsFeatured,
                ViewCount: v.ViewCount,
                UpdatedAt: v.UpdatedAt,
                PublishAt: v.PublishAt)));
        })
        .WithAdminRead("AdminContentRead");

        admin.MapPost("/videos", async (
            HttpContext http,
            AdminVideoCreateRequest request,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            var video = await service.CreateDraftAsync(http.AdminId(), request.Title, ct);
            return Results.Ok(await service.BuildDetailAsync(video, ct));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapGet("/videos/{videoId}", async (
            string videoId,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            var video = await service.RequireVideoAsync(videoId, ct);
            return Results.Ok(await service.BuildDetailAsync(video, ct));
        })
        .WithAdminRead("AdminContentRead");

        admin.MapPatch("/videos/{videoId}", async (
            HttpContext http,
            string videoId,
            AdminVideoPatchRequest request,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            var video = await service.RequireVideoAsync(videoId, ct);
            await service.ApplyPatchAsync(video, http.AdminId(), request, ct);
            return Results.Ok(await service.BuildDetailAsync(video, ct));
        })
        .WithAdminWrite("AdminContentWrite");

        // ── Bunny upload lifecycle ─────────────────────────────────────────

        admin.MapPost("/videos/{videoId}/upload-authorization", async (
            HttpContext http,
            string videoId,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            var video = await service.RequireVideoAsync(videoId, ct);
            try
            {
                var auth = await service.CreateUploadAuthorizationAsync(video, http.AdminId(), ct);
                return Results.Ok(ToUploadAuthorizationResponse(auth));
            }
            catch (BunnyNotConfiguredException)
            {
                return BunnyNotConfigured();
            }
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/videos/{videoId}/reset-upload", async (
            HttpContext http,
            string videoId,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            var video = await service.RequireVideoAsync(videoId, ct);
            try
            {
                var auth = await service.ResetUploadAsync(video, http.AdminId(), ct);
                return Results.Ok(ToUploadAuthorizationResponse(auth));
            }
            catch (BunnyNotConfiguredException)
            {
                return BunnyNotConfigured();
            }
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/videos/{videoId}/refresh-status", async (
            HttpContext http,
            string videoId,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            var video = await service.RequireVideoAsync(videoId, ct);
            try
            {
                await service.RefreshStatusAsync(video, http.AdminId(), ct);
            }
            catch (BunnyNotConfiguredException)
            {
                return BunnyNotConfigured();
            }
            return Results.Ok(await service.BuildDetailAsync(video, ct));
        })
        .WithAdminWrite("AdminContentWrite");

        // ── Chapters / captions / attachments / thumbnail ──────────────────

        admin.MapPut("/videos/{videoId}/chapters", async (
            HttpContext http,
            string videoId,
            AdminVideoChaptersRequest request,
            LearnerDbContext db,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            var video = await service.RequireVideoAsync(videoId, ct);
            var chapters = (request.Chapters ?? [])
                .Where(c => !string.IsNullOrWhiteSpace(c.Title))
                .OrderBy(c => c.TimeSeconds)
                .Select(c => new { timeSeconds = Math.Max(0, c.TimeSeconds), title = c.Title.Trim() })
                .ToList();
            video.ChaptersJson = System.Text.Json.JsonSerializer.Serialize(chapters);
            video.UpdatedAt = DateTimeOffset.UtcNow;
            video.UpdatedByAdminId = http.AdminId();
            await db.SaveChangesAsync(ct);
            return Results.Ok(await service.BuildDetailAsync(video, ct));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/videos/{videoId}/captions", async (
            HttpContext http,
            string videoId,
            AdminVideoCaptionRequest request,
            LearnerDbContext db,
            IFileStorage storage,
            IBunnyStreamClient bunny,
            VideoLibraryAdminService service,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var video = await service.RequireVideoAsync(videoId, ct);
            var languageCode = request.LanguageCode?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(languageCode) || languageCode.Length > 16)
            {
                return Results.BadRequest(new { code = "invalid_language_code", message = "languageCode is required (max 16 chars)." });
            }
            if (string.IsNullOrWhiteSpace(request.Label))
            {
                return Results.BadRequest(new { code = "label_required", message = "label is required." });
            }

            var media = await db.MediaAssets.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == request.MediaAssetId, ct);
            if (media is null)
            {
                return Results.NotFound(new { code = "media_asset_not_found", message = "Caption media asset not found." });
            }

            if (await db.VideoCaptionTracks.AnyAsync(c => c.VideoId == videoId && c.LanguageCode == languageCode, ct))
            {
                return Results.Conflict(new { code = "caption_language_exists", message = "A caption for this language already exists." });
            }

            var nextSort = await db.VideoCaptionTracks.AsNoTracking()
                .Where(c => c.VideoId == videoId)
                .Select(c => (int?)c.SortOrder)
                .MaxAsync(ct) ?? -1;
            var track = new VideoCaptionTrack
            {
                Id = Guid.NewGuid(),
                VideoId = videoId,
                LanguageCode = languageCode,
                Label = request.Label.Trim(),
                MediaAssetId = media.Id,
                SortOrder = nextSort + 1,
            };

            // Best-effort Bunny push — the local track row is authoritative;
            // sync status is surfaced to the admin via syncedToBunny.
            if (!string.IsNullOrWhiteSpace(video.BunnyVideoId))
            {
                try
                {
                    await using var stream = await storage.OpenReadAsync(media.StoragePath, ct);
                    using var buffer = new MemoryStream();
                    await stream.CopyToAsync(buffer, ct);
                    await bunny.UploadCaptionAsync(video.BunnyVideoId, languageCode, track.Label, buffer.ToArray(), ct);
                    track.SyncedToBunnyAt = DateTimeOffset.UtcNow;
                }
                catch (Exception ex) when (ex is BunnyNotConfiguredException or ApiException or IOException)
                {
                    loggerFactory.CreateLogger("VideoLibraryAdminEndpoints").LogWarning(ex,
                        "Caption {Language} for video {VideoId} saved locally but Bunny sync failed.", languageCode, videoId);
                }
            }

            db.VideoCaptionTracks.Add(track);
            await db.SaveChangesAsync(ct);
            return Results.Ok(await service.BuildDetailAsync(video, ct));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapDelete("/videos/{videoId}/captions/{captionId:guid}", async (
            string videoId,
            Guid captionId,
            LearnerDbContext db,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            var video = await service.RequireVideoAsync(videoId, ct);
            var track = await db.VideoCaptionTracks
                .FirstOrDefaultAsync(c => c.Id == captionId && c.VideoId == videoId, ct);
            if (track is null)
            {
                return Results.NotFound(new { code = "caption_not_found", message = "Caption not found." });
            }
            db.VideoCaptionTracks.Remove(track);
            await db.SaveChangesAsync(ct);
            return Results.Ok(await service.BuildDetailAsync(video, ct));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/videos/{videoId}/attachments", async (
            string videoId,
            AdminVideoAttachmentRequest request,
            LearnerDbContext db,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            var video = await service.RequireVideoAsync(videoId, ct);
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return Results.BadRequest(new { code = "title_required", message = "Attachment title is required." });
            }
            var mediaExists = await db.MediaAssets.AsNoTracking().AnyAsync(m => m.Id == request.MediaAssetId, ct);
            if (!mediaExists)
            {
                return Results.NotFound(new { code = "media_asset_not_found", message = "Attachment media asset not found." });
            }

            var nextSort = await db.VideoAttachments.AsNoTracking()
                .Where(a => a.VideoId == videoId)
                .Select(a => (int?)a.SortOrder)
                .MaxAsync(ct) ?? -1;
            db.VideoAttachments.Add(new VideoAttachment
            {
                Id = Guid.NewGuid(),
                VideoId = videoId,
                Title = request.Title.Trim(),
                MediaAssetId = request.MediaAssetId!,
                SortOrder = nextSort + 1,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(await service.BuildDetailAsync(video, ct));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapDelete("/videos/{videoId}/attachments/{attachmentId:guid}", async (
            string videoId,
            Guid attachmentId,
            LearnerDbContext db,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            var video = await service.RequireVideoAsync(videoId, ct);
            var attachment = await db.VideoAttachments
                .FirstOrDefaultAsync(a => a.Id == attachmentId && a.VideoId == videoId, ct);
            if (attachment is null)
            {
                return Results.NotFound(new { code = "attachment_not_found", message = "Attachment not found." });
            }
            db.VideoAttachments.Remove(attachment);
            await db.SaveChangesAsync(ct);
            return Results.Ok(await service.BuildDetailAsync(video, ct));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPut("/videos/{videoId}/attachments/order", async (
            string videoId,
            AdminOrderedIdsRequest request,
            LearnerDbContext db,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            var video = await service.RequireVideoAsync(videoId, ct);
            var attachments = await db.VideoAttachments.Where(a => a.VideoId == videoId).ToListAsync(ct);
            var order = (request.OrderedIds ?? []).ToList();
            foreach (var attachment in attachments)
            {
                var index = order.FindIndex(id => Guid.TryParse(id, out var g) && g == attachment.Id);
                attachment.SortOrder = index >= 0 ? index : order.Count + attachment.SortOrder;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(await service.BuildDetailAsync(video, ct));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/videos/{videoId}/thumbnail", async (
            HttpContext http,
            string videoId,
            AdminVideoThumbnailRequest request,
            LearnerDbContext db,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            var video = await service.RequireVideoAsync(videoId, ct);
            var mediaExists = await db.MediaAssets.AsNoTracking().AnyAsync(m => m.Id == request.MediaAssetId, ct);
            if (!mediaExists)
            {
                return Results.NotFound(new { code = "media_asset_not_found", message = "Thumbnail media asset not found." });
            }
            video.CustomThumbnailMediaAssetId = request.MediaAssetId;
            video.UpdatedAt = DateTimeOffset.UtcNow;
            video.UpdatedByAdminId = http.AdminId();
            await db.SaveChangesAsync(ct);
            return Results.Ok(await service.BuildDetailAsync(video, ct));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapDelete("/videos/{videoId}/thumbnail", async (
            HttpContext http,
            string videoId,
            LearnerDbContext db,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            var video = await service.RequireVideoAsync(videoId, ct);
            video.CustomThumbnailMediaAssetId = null;
            video.UpdatedAt = DateTimeOffset.UtcNow;
            video.UpdatedByAdminId = http.AdminId();
            await db.SaveChangesAsync(ct);
            return Results.Ok(await service.BuildDetailAsync(video, ct));
        })
        .WithAdminWrite("AdminContentWrite");

        // ── Publish gate + lifecycle ───────────────────────────────────────

        admin.MapGet("/videos/{videoId}/publish-gate", async (
            string videoId,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            var video = await service.RequireVideoAsync(videoId, ct);
            return Results.Ok(await service.EvaluatePublishGateAsync(video, ct));
        })
        .WithAdminRead("AdminContentRead");

        admin.MapPost("/videos/{videoId}/publish", async (
            HttpContext http,
            string videoId,
            AdminVideoPublishRequest? request,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            var video = await service.RequireVideoAsync(videoId, ct);
            var gate = await service.PublishAsync(video, http.AdminId(), request?.PublishAt, ct);
            return gate.CanPublish
                ? Results.Ok(new { published = true, status = "Published", errors = Array.Empty<string>() })
                : Results.Json(new { published = false, errors = gate.Errors }, statusCode: 422);
        })
        .WithAdminWrite("AdminContentPublish");

        admin.MapPost("/videos/{videoId}/unpublish", async (
            HttpContext http,
            string videoId,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            var video = await service.RequireVideoAsync(videoId, ct);
            await service.UnpublishAsync(video, http.AdminId(), ct);
            return Results.Ok(await service.BuildDetailAsync(video, ct));
        })
        .WithAdminWrite("AdminContentPublish");

        admin.MapPost("/videos/{videoId}/archive", async (
            HttpContext http,
            string videoId,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            var video = await service.RequireVideoAsync(videoId, ct);
            await service.ArchiveAsync(video, http.AdminId(), ct);
            return Results.Ok(await service.BuildDetailAsync(video, ct));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/videos/{videoId}/restore", async (
            HttpContext http,
            string videoId,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            var video = await service.RequireVideoAsync(videoId, ct);
            await service.RestoreAsync(video, http.AdminId(), ct);
            return Results.Ok(await service.BuildDetailAsync(video, ct));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/videos/{videoId}/force-delete", async (
            HttpContext http,
            string videoId,
            AdminVideoForceDeleteRequest request,
            LearnerDbContext db,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            if (!request.Force)
            {
                return Results.BadRequest(new { code = "force_required", message = "Set force=true to permanently delete this video." });
            }
            var video = await service.RequireVideoAsync(videoId, ct);
            var report = await service.ForceDeleteAsync(video, http.AdminId(), ct);

            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                ActorId = http.AdminId(),
                ActorName = http.AdminName(),
                Action = "VideoForceDeleted",
                ResourceType = "library_video",
                ResourceId = videoId,
                Details = JsonSupport.Serialize(new { reason = request.Reason, purged = report }),
                OccurredAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { deleted = true, purged = report });
        })
        .WithAdminWrite("AdminSystemAdmin");

        admin.MapPost("/videos/bulk-lifecycle", async (
            HttpContext http,
            AdminVideoBulkLifecycleRequest request,
            VideoLibraryAdminService service,
            CancellationToken ct) =>
        {
            var result = await service.BulkLifecycleAsync(http.AdminId(), request.Action, request.VideoIds ?? [], ct);
            return Results.Ok(result);
        })
        .WithAdminWrite("AdminContentPublish");

        admin.MapVideoLibraryCategoryEndpoints();
        admin.MapVideoLibraryAnalyticsEndpoints();

        return app;
    }

    private static object ToUploadAuthorizationResponse(BunnyTusAuthorization auth) => new
    {
        bunnyVideoId = auth.BunnyVideoId,
        libraryId = auth.LibraryId,
        tusEndpoint = auth.TusEndpoint,
        authorizationSignature = auth.AuthorizationSignature,
        authorizationExpire = auth.AuthorizationExpire,
    };

    private static IResult BunnyNotConfigured()
        => Results.Json(
            new { code = "bunny_not_configured", message = "Configure Bunny Stream in Admin → Settings first." },
            statusCode: StatusCodes.Status503ServiceUnavailable);

    // Private (not internal) to match the sibling endpoint classes — a wider
    // accessibility would make every http.AdminId() call in this namespace
    // ambiguous against the per-class private copies.
    private static string AdminId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");

    private static string AdminName(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Admin";
}

public sealed record AdminVideoCreateRequest(string Title);
public sealed record AdminVideoChapterInput(int TimeSeconds, string Title);
public sealed record AdminVideoChaptersRequest(AdminVideoChapterInput[]? Chapters);
public sealed record AdminVideoCaptionRequest(string? MediaAssetId, string? LanguageCode, string? Label);
public sealed record AdminVideoAttachmentRequest(string? MediaAssetId, string? Title);
public sealed record AdminOrderedIdsRequest(string[]? OrderedIds);
public sealed record AdminVideoThumbnailRequest(string? MediaAssetId);
public sealed record AdminVideoPublishRequest(DateTimeOffset? PublishAt);
public sealed record AdminVideoForceDeleteRequest(bool Force, string? Reason);
public sealed record AdminVideoBulkLifecycleRequest(string Action, string[]? VideoIds);
