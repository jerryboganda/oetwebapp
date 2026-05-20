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
/// Admin CRUD + learner read for Speaking shared resources (Warm-up
/// questions + Assessment Criteria PDFs).
/// </summary>
public static class SpeakingSharedResourcesEndpoints
{
    private const long MaxPdfBytes = 50L * 1024 * 1024;
    private static readonly HashSet<string> AllowedKinds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            SpeakingSharedResourceKinds.WarmUpQuestions,
            SpeakingSharedResourceKinds.AssessmentCriteria,
        };

    public static IEndpointRouteBuilder MapSpeakingSharedResourcesEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Admin ──────────────────────────────────────────────────────────
        var admin = app.MapGroup("/v1/admin/speaking/shared-resources")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUser");

        admin.MapGet("", async (LearnerDbContext db, string? kind, string? profession, CancellationToken ct) =>
        {
            var q = db.SpeakingSharedResources.AsNoTracking().Include(x => x.MediaAsset).AsQueryable();
            if (!string.IsNullOrWhiteSpace(kind)) q = q.Where(x => x.Kind == kind);
            if (!string.IsNullOrWhiteSpace(profession)) q = q.Where(x => x.ProfessionId == profession);
            var rows = await q.OrderByDescending(x => x.UpdatedAt).ToListAsync(ct);
            return Results.Ok(rows.Select(Project));
        });

        admin.MapPost("", async (
            HttpContext http,
            LearnerDbContext db,
            IFileStorage storage,
            IOptions<StorageOptions> storageOptions,
            IUploadContentValidator validator,
            IUploadScanner scanner,
            IFormFile file,
            [FromForm] string kind,
            [FromForm] string title,
            [FromForm] string? professionId,
            CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "file required" });
            if (file.Length > MaxPdfBytes) return Results.BadRequest(new { error = $"file too large (max {MaxPdfBytes} bytes)" });
            if (string.IsNullOrWhiteSpace(kind) || !AllowedKinds.Contains(kind))
                return Results.BadRequest(new { error = "kind must be WarmUpQuestions or AssessmentCriteria" });
            if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
                return Results.BadRequest(new { error = "title required (max 200 chars)" });

            var originalFileName = Path.GetFileName(file.FileName ?? "speaking-shared-resource.pdf");
            if (string.IsNullOrWhiteSpace(originalFileName)) originalFileName = "speaking-shared-resource.pdf";
            var ext = (Path.GetExtension(originalFileName)?.TrimStart('.') ?? "pdf").ToLowerInvariant();
            if (ext != "pdf") return Results.BadRequest(new { error = "only .pdf accepted" });

            await using var buffer = new MemoryStream((int)Math.Min(file.Length, MaxPdfBytes));
            await file.CopyToAsync(buffer, ct);
            buffer.Position = 0;

            var validation = await validator.ValidateAsync(buffer, ext, ct);
            if (!validation.Accepted
                || !string.Equals(validation.DetectedMime, "application/pdf", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(validation.DetectedExtension, "pdf", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new
                {
                    code = "invalid_file_content",
                    message = validation.Reason ?? "The uploaded file content does not match a PDF.",
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

            var stagingKey = $"staging/speaking-shared/{adminId}/{Guid.NewGuid():N}.pdf";
            long bytes;
            string sha;
            buffer.Position = 0;
            await using (var dest = await storage.OpenWriteAsync(stagingKey, ct))
            {
                (bytes, sha) = await StreamingSha256.ComputeAsync(new[] { buffer }, dest, ct);
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
                    OriginalFilename = originalFileName,
                    MimeType = "application/pdf",
                    Format = "pdf",
                    SizeBytes = bytes,
                    StoragePath = publishedKey,
                    Status = MediaAssetStatus.Ready,
                    Sha256 = sha,
                    MediaKind = "document",
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
            var row = new SpeakingSharedResource
            {
                Id = $"sss_{Guid.NewGuid():N}",
                Kind = kind,
                Title = title.Trim(),
                ProfessionId = string.IsNullOrWhiteSpace(professionId) ? null : professionId,
                MediaAssetId = mediaId,
                Status = ContentStatus.Draft,
                UploadedByUserId = adminId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.SpeakingSharedResources.Add(row);
            AddAuditEvent(db, http, "SpeakingSharedResourceUploaded", "SpeakingSharedResource", row.Id, $"kind={row.Kind};media={mediaId}");
            await db.SaveChangesAsync(ct);
            await db.Entry(row).Reference(x => x.MediaAsset!).LoadAsync(ct);
            return Results.Ok(Project(row));
        })
        .DisableAntiforgery()
        .RequireAuthorization("AdminContentWrite");

        admin.MapPost("/{id}/publish", async (string id, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var row = await db.SpeakingSharedResources.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            row.Status = ContentStatus.Published;
            row.PublishedAt = DateTimeOffset.UtcNow;
            row.EffectiveFrom ??= DateTimeOffset.UtcNow;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            AddAuditEvent(db, http, "SpeakingSharedResourcePublished", "SpeakingSharedResource", row.Id, row.Title);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { row.Id, status = row.Status.ToString() });
        })
        .RequireAuthorization("AdminContentPublish");

        admin.MapPost("/{id}/archive", async (string id, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var row = await db.SpeakingSharedResources.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            row.Status = ContentStatus.Archived;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            AddAuditEvent(db, http, "SpeakingSharedResourceArchived", "SpeakingSharedResource", row.Id, row.Title);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { row.Id, status = row.Status.ToString() });
        })
        .RequireAuthorization("AdminContentWrite");

        admin.MapDelete("/{id}", async (string id, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var row = await db.SpeakingSharedResources.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            row.Status = ContentStatus.Archived;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            AddAuditEvent(db, http, "SpeakingSharedResourceDeleted", "SpeakingSharedResource", row.Id, row.Title);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization("AdminContentWrite");

        // ── Learner ────────────────────────────────────────────────────────
        var learner = app.MapGroup("/v1/speaking/shared-resources")
            .RequireAuthorization()
            .RequireRateLimiting("PerUser");

        learner.MapGet("", async (LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            string? activeProfession = null;
            if (!string.IsNullOrEmpty(userId))
            {
                var learnerRow = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
                activeProfession = learnerRow?.ActiveProfessionId;
            }
            var rows = await db.SpeakingSharedResources.AsNoTracking().Include(x => x.MediaAsset)
                .Where(x => x.Status == ContentStatus.Published)
                .Where(x => x.ProfessionId == null
                            || (activeProfession != null && x.ProfessionId == activeProfession))
                .OrderByDescending(x => x.EffectiveFrom)
                .ToListAsync(ct);
            return Results.Ok(rows.Select(ProjectLearner));
        });

        return app;
    }

    private static object Project(SpeakingSharedResource r) => new
    {
        r.Id,
        r.Kind,
        r.Title,
        r.ProfessionId,
        r.MediaAssetId,
        status = r.Status.ToString(),
        r.PublishedAt,
        r.EffectiveFrom,
        r.UploadedByUserId,
        r.CreatedAt,
        r.UpdatedAt,
        media = r.MediaAsset is null ? null : new
        {
            r.MediaAsset.Id,
            r.MediaAsset.OriginalFilename,
            r.MediaAsset.MimeType,
            r.MediaAsset.SizeBytes,
            r.MediaAsset.Sha256,
        },
    };

    private static object ProjectLearner(SpeakingSharedResource r) => new
    {
        r.Id,
        r.Kind,
        r.Title,
        r.ProfessionId,
        r.PublishedAt,
        media = r.MediaAsset is null ? null : new
        {
            r.MediaAsset.Id,
            r.MediaAsset.OriginalFilename,
            r.MediaAsset.SizeBytes,
        },
    };

    private static void AddAuditEvent(
        LearnerDbContext db,
        HttpContext http,
        string action,
        string resourceType,
        string? resourceId,
        string? details)
    {
        var actorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorName = http.User.Identity?.Name ?? actorId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
        });
    }
}
