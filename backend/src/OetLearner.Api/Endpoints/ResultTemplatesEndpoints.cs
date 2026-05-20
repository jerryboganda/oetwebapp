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
            var q = db.ResultTemplateAssets.AsNoTracking().Include(x => x.MediaAsset).AsQueryable();
            if (!string.IsNullOrWhiteSpace(profession)) q = q.Where(x => x.ProfessionId == profession);
            var rows = await q.OrderBy(x => x.SortOrder).ThenByDescending(x => x.UpdatedAt).ToListAsync(ct);
            return Results.Ok(rows.Select(Project));
        });

        admin.MapGet("/{id}", async (string id, LearnerDbContext db, CancellationToken ct) =>
        {
            var row = await db.ResultTemplateAssets.AsNoTracking().Include(x => x.MediaAsset)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            return row is null ? Results.NotFound() : Results.Ok(Project(row));
        });

        admin.MapPost("", async (
            HttpContext http,
            LearnerDbContext db,
            IFileStorage storage,
            IOptions<StorageOptions> storageOptions,
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

            var ext = (Path.GetExtension(file.FileName)?.TrimStart('.') ?? "").ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
                return Results.BadRequest(new { error = "only jpg / jpeg / png / webp accepted" });

            // Unique templateKey check
            var keyExists = await db.ResultTemplateAssets.AnyAsync(x => x.TemplateKey == templateKey, ct);
            if (keyExists) return Results.Conflict(new { error = "templateKey already used" });

            var stagingKey = $"staging/result-template/{adminId}/{Guid.NewGuid():N}.{ext}";
            long bytes;
            string sha;
            await using (var src = file.OpenReadStream())
            await using (var dest = await storage.OpenWriteAsync(stagingKey, ct))
            {
                (bytes, sha) = await StreamingSha256.ComputeAsync(new[] { src }, dest, ct);
            }
            var publishedKey = ContentAddressed.PublishedKey(
                storageOptions.Value.ContentUpload.PublishedSubpath, sha, ext);
            if (!storage.Exists(publishedKey))
                storage.Move(stagingKey, publishedKey, overwrite: false);
            else
                storage.Delete(stagingKey);

            var existingMedia = await db.MediaAssets.FirstOrDefaultAsync(m => m.Sha256 == sha && m.Format == ext, ct);
            string mediaId;
            if (existingMedia is null)
            {
                mediaId = $"med_{Guid.NewGuid():N}";
                db.MediaAssets.Add(new MediaAsset
                {
                    Id = mediaId,
                    OriginalFilename = file.FileName,
                    MimeType = ext == "png" ? "image/png" : ext == "webp" ? "image/webp" : "image/jpeg",
                    Format = ext,
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
                TemplateKey = templateKey.Trim(),
                Title = title.Trim(),
                Description = description,
                ProfessionId = string.IsNullOrWhiteSpace(professionId) ? null : professionId,
                MediaAssetId = mediaId,
                IsActive = false,
                SortOrder = sortOrder ?? 0,
                UploadedByUserId = adminId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.ResultTemplateAssets.Add(row);
            await db.SaveChangesAsync(ct);
            await db.Entry(row).Reference(x => x.MediaAsset!).LoadAsync(ct);
            return Results.Ok(Project(row));
        })
        .DisableAntiforgery()
        .RequireAuthorization("AdminContentWrite");

        admin.MapPut("/{id}", async (
            string id,
            LearnerDbContext db,
            ResultTemplateUpdate dto,
            CancellationToken ct) =>
        {
            var row = await db.ResultTemplateAssets.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            if (!string.IsNullOrWhiteSpace(dto.Title)) row.Title = dto.Title.Trim();
            if (dto.Description is not null) row.Description = dto.Description;
            if (dto.ProfessionId is not null) row.ProfessionId = string.IsNullOrWhiteSpace(dto.ProfessionId) ? null : dto.ProfessionId;
            if (dto.SortOrder is not null) row.SortOrder = dto.SortOrder.Value;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await db.Entry(row).Reference(x => x.MediaAsset!).LoadAsync(ct);
            return Results.Ok(Project(row));
        })
        .RequireAuthorization("AdminContentWrite");

        admin.MapPost("/{id}/activate", async (string id, LearnerDbContext db, CancellationToken ct) =>
        {
            var row = await db.ResultTemplateAssets.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            // Multiple templates can be active; admin chooses which one renders on result pages.
            row.IsActive = true;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { row.Id, row.IsActive });
        })
        .RequireAuthorization("AdminContentWrite");

        admin.MapPost("/{id}/deactivate", async (string id, LearnerDbContext db, CancellationToken ct) =>
        {
            var row = await db.ResultTemplateAssets.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            row.IsActive = false;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { row.Id, row.IsActive });
        })
        .RequireAuthorization("AdminContentWrite");

        admin.MapDelete("/{id}", async (string id, LearnerDbContext db, CancellationToken ct) =>
        {
            var row = await db.ResultTemplateAssets.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            db.ResultTemplateAssets.Remove(row);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization("AdminContentWrite");

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
}

public sealed record ResultTemplateUpdate(
    string? Title,
    string? Description,
    string? ProfessionId,
    int? SortOrder);
