using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin CRUD for the result-template image gallery. After a learner finishes
/// a mock, the active template's image is composited onto the result page.
/// </summary>
public static class ResultTemplatesEndpoints
{
    private const long MaxImageBytes = 10L * 1024 * 1024; // 10 MB
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png", "webp" };

    public static IEndpointRouteBuilder MapResultTemplatesEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin/result-templates")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUser");

        admin.MapGet("", async (LearnerDbContext db, string? profession, CancellationToken ct) =>
        {
            var normalizedProfession = NormalizeProfessionId(profession);
            var q = db.ResultTemplateAssets.AsNoTracking().Include(x => x.MediaAsset).AsQueryable();
            if (!string.IsNullOrWhiteSpace(normalizedProfession)) q = q.Where(x => x.ProfessionId != null && x.ProfessionId.ToLower() == normalizedProfession);
            var rows = await q.OrderBy(x => x.SortOrder).ThenByDescending(x => x.UpdatedAt).ToListAsync(ct);
            return Results.Ok(rows.Select(Project));
        });

        admin.MapGet("/{id}", async (string id, LearnerDbContext db, CancellationToken ct) =>
        {
            var row = await db.ResultTemplateAssets.AsNoTracking().Include(x => x.MediaAsset)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            return row is null ? Results.NotFound() : Results.Ok(Project(row));
        });

        // Permanent delete — removes the template row outright. No rows reference it
        // (it points AT a MediaAsset, which is left intact). system_admin only.
        admin.MapPost("/{id}/force-delete", async (string id, LearnerDbContext db, CancellationToken ct) =>
        {
            var row = await db.ResultTemplateAssets.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            db.ResultTemplateAssets.Remove(row);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithAdminWrite("AdminSystemAdmin");

        admin.MapPost("", async (
            HttpContext http,
            LearnerDbContext db,
            IFileStorage storage,
            IOptions<StorageOptions> storageOptions,
            IUploadContentValidator validator,
            IUploadScanner scanner,
            IFormFile file,
            [FromForm] string templateKey,
            [FromForm] string title,
            [FromForm] string? description,
            [FromForm] string? professionId,
            [FromForm] int? sortOrder,
            CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "file required" });
            if (file.Length > MaxImageBytes) return Results.BadRequest(new { error = $"file too large (max {MaxImageBytes} bytes)" });
            if (string.IsNullOrWhiteSpace(templateKey) || templateKey.Length > 128)
                return Results.BadRequest(new { error = "templateKey required (max 128 chars)" });
            if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
                return Results.BadRequest(new { error = "title required (max 200 chars)" });

            var originalFileName = Path.GetFileName(file.FileName ?? "result-template.jpg");
            if (string.IsNullOrWhiteSpace(originalFileName)) originalFileName = "result-template.jpg";
            var ext = (Path.GetExtension(originalFileName)?.TrimStart('.') ?? "").ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
                return Results.BadRequest(new { error = "only jpg / jpeg / png / webp accepted" });

            await using var buffer = new MemoryStream((int)Math.Min(file.Length, MaxImageBytes));
            await file.CopyToAsync(buffer, ct);
            buffer.Position = 0;

            var validation = await validator.ValidateAsync(buffer, ext, ct);
            if (!validation.Accepted
                || string.IsNullOrWhiteSpace(validation.DetectedMime)
                || string.IsNullOrWhiteSpace(validation.DetectedExtension)
                || !validation.DetectedMime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || !AllowedExtensions.Contains(validation.DetectedExtension))
            {
                return Results.BadRequest(new
                {
                    code = "invalid_file_content",
                    message = validation.Reason ?? "The uploaded file content does not match a supported image type.",
                });
            }

            buffer.Position = 0;
            var scanResult = await scanner.ScanAsync(buffer, originalFileName, ct);
            if (!scanResult.clean)
            {
                return Results.BadRequest(new
                {
                    code = "file_failed_security_scan",
                    message = scanResult.reason ?? "The uploaded file failed security scanning.",
                });
            }

            var detectedExt = validation.DetectedExtension.TrimStart('.').ToLowerInvariant();
            if (detectedExt == "jpeg") detectedExt = "jpg";

            // Unique templateKey check
            var normalizedKey = templateKey.Trim();
            var keyExists = await db.ResultTemplateAssets.AnyAsync(x => x.TemplateKey == normalizedKey, ct);
            if (keyExists) return Results.Conflict(new { error = "templateKey already used" });

            var stagingKey = $"staging/result-template/{adminId}/{Guid.NewGuid():N}.{detectedExt}";
            long bytes;
            string sha;
            buffer.Position = 0;
            await using (var dest = await storage.OpenWriteAsync(stagingKey, ct))
            {
                (bytes, sha) = await StreamingSha256.ComputeAsync(new[] { buffer }, dest, ct);
            }
            var publishedKey = ContentAddressed.PublishedKey(
                storageOptions.Value.ContentUpload.PublishedSubpath, sha, detectedExt);
            if (!await storage.ExistsAsync(publishedKey, ct))
                await storage.MoveAsync(stagingKey, publishedKey, overwrite: false, ct);
            else
                await storage.DeleteAsync(stagingKey, ct);

            var existingMedia = await db.MediaAssets.FirstOrDefaultAsync(m => m.Sha256 == sha && m.Format == detectedExt, ct);
            string mediaId;
            if (existingMedia is null)
            {
                mediaId = $"med_{Guid.NewGuid():N}";
                db.MediaAssets.Add(new MediaAsset
                {
                    Id = mediaId,
                    OriginalFilename = originalFileName,
                    MimeType = validation.DetectedMime,
                    Format = detectedExt,
                    SizeBytes = bytes,
                    StoragePath = publishedKey,
                    Status = MediaAssetStatus.Ready,
                    Sha256 = sha,
                    MediaKind = "image",
                    UploadedBy = adminId,
                    UploadedAt = DateTimeOffset.UtcNow,
                    ProcessedAt = DateTimeOffset.UtcNow,
                });
            }
            else
            {
                mediaId = existingMedia.Id;
            }

            var now = DateTimeOffset.UtcNow;
            var row = new ResultTemplateAsset
            {
                Id = $"rtpl_{Guid.NewGuid():N}",
                TemplateKey = normalizedKey,
                Title = title.Trim(),
                Description = description,
                ProfessionId = NormalizeProfessionId(professionId),
                MediaAssetId = mediaId,
                IsActive = false,
                SortOrder = sortOrder ?? 0,
                UploadedByUserId = adminId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.ResultTemplateAssets.Add(row);
            AddAuditEvent(db, http, "ResultTemplateUploaded", "ResultTemplateAsset", row.Id, $"templateKey={row.TemplateKey};media={mediaId}");
            await db.SaveChangesAsync(ct);
            await db.Entry(row).Reference(x => x.MediaAsset!).LoadAsync(ct);
            return Results.Ok(Project(row));
        })
        .DisableAntiforgery()
        .WithAdminWrite("AdminContentWrite");

        admin.MapPut("/{id}", async (
            string id,
            HttpContext http,
            LearnerDbContext db,
            ResultTemplateUpdate dto,
            CancellationToken ct) =>
        {
            var row = await db.ResultTemplateAssets.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            if (!string.IsNullOrWhiteSpace(dto.Title)) row.Title = dto.Title.Trim();
            if (dto.Description is not null) row.Description = dto.Description;
            if (dto.ProfessionId is not null) row.ProfessionId = NormalizeProfessionId(dto.ProfessionId);
            if (dto.SortOrder is not null) row.SortOrder = dto.SortOrder.Value;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            AddAuditEvent(db, http, "ResultTemplateUpdated", "ResultTemplateAsset", row.Id, row.TemplateKey);
            await db.SaveChangesAsync(ct);
            await db.Entry(row).Reference(x => x.MediaAsset!).LoadAsync(ct);
            return Results.Ok(Project(row));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/{id}/activate", async (string id, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var row = await db.ResultTemplateAssets.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            // Multiple templates can be active; admin chooses which one renders on result pages.
            row.IsActive = true;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            AddAuditEvent(db, http, "ResultTemplateActivated", "ResultTemplateAsset", row.Id, row.TemplateKey);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { row.Id, row.IsActive });
        })
        .WithAdminWrite("AdminContentPublish");

        admin.MapPost("/{id}/deactivate", async (string id, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var row = await db.ResultTemplateAssets.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            row.IsActive = false;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            AddAuditEvent(db, http, "ResultTemplateDeactivated", "ResultTemplateAsset", row.Id, row.TemplateKey);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { row.Id, row.IsActive });
        })
        .WithAdminWrite("AdminContentPublish");

        admin.MapDelete("/{id}", async (string id, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var row = await db.ResultTemplateAssets.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            db.ResultTemplateAssets.Remove(row);
            AddAuditEvent(db, http, "ResultTemplateDeleted", "ResultTemplateAsset", row.Id, row.TemplateKey);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithAdminWrite("AdminContentWrite");

        var learner = app.MapGroup("/v1/result-templates")
            .RequireAuthorization()
            .RequireRateLimiting("PerUser");

        learner.MapGet("/active", async (LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            string? activeProfession = null;
            if (!string.IsNullOrEmpty(userId))
            {
                activeProfession = NormalizeProfessionId(await db.Users.AsNoTracking()
                    .Where(user => user.Id == userId)
                    .Select(user => user.ActiveProfessionId)
                    .SingleOrDefaultAsync(ct));
            }

            var candidates = await db.ResultTemplateAssets.AsNoTracking().Include(x => x.MediaAsset)
                .Where(x => x.IsActive)
                .Where(x => x.ProfessionId == null
                    || (activeProfession != null && x.ProfessionId != null && x.ProfessionId.ToLower() == activeProfession))
                .ToListAsync(ct);

            var selected = candidates
                .OrderByDescending(x => activeProfession != null && x.ProfessionId == activeProfession)
                .ThenBy(x => x.SortOrder)
                .ThenByDescending(x => x.UpdatedAt)
                .FirstOrDefault();

            return selected is null ? Results.NotFound() : Results.Ok(ProjectLearner(selected));
        });

        return app;
    }

    private static object Project(ResultTemplateAsset r) => new
    {
        r.Id,
        r.TemplateKey,
        r.Title,
        r.Description,
        r.ProfessionId,
        r.MediaAssetId,
        r.IsActive,
        r.SortOrder,
        r.UploadedByUserId,
        r.CreatedAt,
        r.UpdatedAt,
        media = r.MediaAsset is null ? null : new
        {
            r.MediaAsset.Id,
            r.MediaAsset.OriginalFilename,
            r.MediaAsset.MimeType,
            r.MediaAsset.Format,
            r.MediaAsset.SizeBytes,
            r.MediaAsset.Sha256,
        },
    };

    private static object ProjectLearner(ResultTemplateAsset r) => new
    {
        r.Id,
        r.TemplateKey,
        r.Title,
        r.Description,
        r.ProfessionId,
        r.MediaAssetId,
        r.SortOrder,
        r.UpdatedAt,
        media = r.MediaAsset is null ? null : new
        {
            r.MediaAsset.Id,
            r.MediaAsset.OriginalFilename,
            r.MediaAsset.MimeType,
            r.MediaAsset.SizeBytes,
        },
    };

    private static void AddAuditEvent(
        LearnerDbContext db,
        HttpContext? http,
        string action,
        string resourceType,
        string? resourceId,
        string? details)
    {
        var actorId = http?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorName = http?.User.Identity?.Name ?? actorId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
        });
    }

    private static string? NormalizeProfessionId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}

public sealed record ResultTemplateUpdate(
    string? Title,
    string? Description,
    string? ProfessionId,
    int? SortOrder);
