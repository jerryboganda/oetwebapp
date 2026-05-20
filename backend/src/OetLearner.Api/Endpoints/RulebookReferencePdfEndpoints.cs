using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin attach/detach a human-readable PDF onto a <see cref="RulebookVersion"/>.
/// Grading still uses the JSON rule rows; the PDF is for humans only.
/// Mounted as a small surface alongside the existing rulebook admin routes.
/// </summary>
public static class RulebookReferencePdfEndpoints
{
    private const long MaxPdfBytes = 50L * 1024 * 1024; // 50 MB

    public static IEndpointRouteBuilder MapRulebookReferencePdfEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/rulebooks")
            .RequireAuthorization("AdminContentWrite")
            .RequireRateLimiting("PerUser");

        group.MapPost("/{id}/reference-pdf", async (
            string id,
            HttpContext http,
            LearnerDbContext db,
            IFileStorage storage,
            IOptions<StorageOptions> storageOptions,
            IAuthorizationService authorization,
            IUploadContentValidator validator,
            IUploadScanner scanner,
            IFormFile file,
            CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var rb = await db.Set<RulebookVersion>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (rb is null) return Results.NotFound();
            if (rb.Status == RulebookStatus.Published
                && !(await authorization.AuthorizeAsync(http.User, "AdminContentPublish")).Succeeded)
            {
                return Results.Forbid();
            }
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "file required" });
            if (file.Length > MaxPdfBytes) return Results.BadRequest(new { error = $"file too large (max {MaxPdfBytes} bytes)" });

            var originalFileName = Path.GetFileName(file.FileName ?? "rulebook-reference.pdf");
            if (string.IsNullOrWhiteSpace(originalFileName)) originalFileName = "rulebook-reference.pdf";
            var extValue = Path.GetExtension(originalFileName);
            var ext = string.IsNullOrWhiteSpace(extValue)
                ? "pdf"
                : extValue.TrimStart('.').ToLowerInvariant();
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

            var stagingKey = $"staging/rulebook-pdf/{adminId}/{Guid.NewGuid():N}.pdf";
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

            var existing = await db.MediaAssets.FirstOrDefaultAsync(m => m.Sha256 == sha && m.Format == "pdf", ct);
            string mediaId;
            if (existing is null)
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
                mediaId = existing.Id;
            }

            rb.ReferencePdfAssetId = mediaId;
            rb.UpdatedAt = DateTimeOffset.UtcNow;
            rb.UpdatedByUserId = adminId;
            AddAuditEvent(db, http, "RulebookReferencePdfAttached", "RulebookVersion", rb.Id, $"media={mediaId};file={originalFileName}");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { rb.Id, referencePdfAssetId = mediaId, originalFilename = originalFileName, sizeBytes = bytes });
        })
        .DisableAntiforgery();

        group.MapDelete("/{id}/reference-pdf", async (
            string id,
            HttpContext http,
            LearnerDbContext db,
            IAuthorizationService authorization,
            CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var rb = await db.Set<RulebookVersion>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (rb is null) return Results.NotFound();
            if (rb.Status == RulebookStatus.Published
                && !(await authorization.AuthorizeAsync(http.User, "AdminContentPublish")).Succeeded)
            {
                return Results.Forbid();
            }
            rb.ReferencePdfAssetId = null;
            rb.UpdatedAt = DateTimeOffset.UtcNow;
            rb.UpdatedByUserId = adminId;
            AddAuditEvent(db, http, "RulebookReferencePdfDetached", "RulebookVersion", rb.Id, null);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Learner-visible probe: returns the asset id of the active published version's PDF (if any).
        var learner = app.MapGroup("/v1/rulebooks")
            .RequireAuthorization()
            .RequireRateLimiting("PerUser");

        learner.MapGet("/{kind}/{profession}/reference-pdf", async (
            string kind,
            string profession,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var rb = await db.Set<RulebookVersion>().AsNoTracking()
                .Where(x => x.Kind == kind.ToLowerInvariant() && x.Profession == profession.ToLowerInvariant() && x.Status == RulebookStatus.Published)
                .OrderByDescending(x => x.PublishedAt)
                .FirstOrDefaultAsync(ct);
            if (rb is null || rb.ReferencePdfAssetId is null) return Results.NotFound();
            return Results.Ok(new { rulebookId = rb.Id, referencePdfAssetId = rb.ReferencePdfAssetId });
        });

        return app;
    }

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
