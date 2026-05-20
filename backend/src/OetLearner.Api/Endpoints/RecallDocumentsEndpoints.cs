using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin CRUD + learner read for the Recall PDF library. Each document is
/// one uploaded PDF (e.g. "2026 Q1 Listening Recalls") plus per-document
/// metadata. Storage uses the existing <see cref="IFileStorage"/> +
/// <see cref="MediaAsset"/> + content-addressed sha256 layout; same as
/// ContentPaperAsset uploads.
/// </summary>
public static class RecallDocumentsEndpoints
{
    private const long MaxPdfBytes = 50L * 1024 * 1024; // 50 MB

    public static IEndpointRouteBuilder MapRecallDocumentsEndpoints(this IEndpointRouteBuilder app)
    {
        // Admin group.
        var admin = app.MapGroup("/v1/admin/recall-documents")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUser");

        admin.MapGet("", async (LearnerDbContext db, string? subtest, string? status, int? page, int? pageSize, CancellationToken ct) =>
        {
            var q = db.RecallDocuments.AsNoTracking().Include(x => x.MediaAsset).AsQueryable();
            if (!string.IsNullOrWhiteSpace(subtest)) q = q.Where(x => x.SubtestCode == subtest);
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ContentStatus>(status, true, out var st))
                q = q.Where(x => x.Status == st);
            var p = Math.Max(1, page ?? 1);
            var ps = Math.Clamp(pageSize ?? 50, 1, 200);
            var total = await q.CountAsync(ct);
            var rows = await q.OrderBy(x => x.SortOrder).ThenByDescending(x => x.UpdatedAt)
                .Skip((p - 1) * ps).Take(ps).ToListAsync(ct);
            return Results.Ok(new { total, page = p, pageSize = ps, items = rows.Select(Project) });
        });

        admin.MapGet("/{id}", async (string id, LearnerDbContext db, CancellationToken ct) =>
        {
            var row = await db.RecallDocuments.AsNoTracking().Include(x => x.MediaAsset)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            return row is null ? Results.NotFound() : Results.Ok(Project(row));
        });

        admin.MapPost("", async (
            HttpContext http,
            LearnerDbContext db,
            IFileStorage storage,
            IOptions<StorageOptions> storageOptions,
            IUploadContentValidator validator,
            IUploadScanner scanner,
            IFormFile file,
            [FromForm] string title,
            [FromForm] string subtestCode,
            [FromForm] string periodLabel,
            [FromForm] string? professionId,
            [FromForm] string? descriptionMarkdown,
            [FromForm] int? sortOrder,
            CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "file required" });
            if (file.Length > MaxPdfBytes) return Results.BadRequest(new { error = $"file too large (max {MaxPdfBytes} bytes)" });

            var normalizedSubtest = (subtestCode ?? "").ToLowerInvariant();
            if (!AcceptableSubtests.Contains(normalizedSubtest))
                return Results.BadRequest(new { error = "subtestCode must be one of: listening, reading, writing, speaking, cross" });
            if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
                return Results.BadRequest(new { error = "title required (max 200 chars)" });
            if (string.IsNullOrWhiteSpace(periodLabel) || periodLabel.Length > 64)
                return Results.BadRequest(new { error = "periodLabel required (max 64 chars)" });

            // Stream-hash and stage to a temp key under the standard staging
            // root, then content-address-promote to the published shard.
            var originalFileName = Path.GetFileName(file.FileName ?? "recall-document.pdf");
            if (string.IsNullOrWhiteSpace(originalFileName)) originalFileName = "recall-document.pdf";
            var ext = string.IsNullOrWhiteSpace(Path.GetExtension(originalFileName))
                ? "pdf"
                : Path.GetExtension(originalFileName).TrimStart('.').ToLowerInvariant();
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

            var stagingKey = $"staging/recall-uploads/{adminId}/{Guid.NewGuid():N}.pdf";
            long bytes;
            string sha;
            buffer.Position = 0;
            await using (var dest = await storage.OpenWriteAsync(stagingKey, ct))
            {
                (bytes, sha) = await StreamingSha256.ComputeAsync(new[] { buffer }, dest, ct);
            }

            var publishedKey = ContentAddressed.PublishedKey(
                storageOptions.Value.ContentUpload.PublishedSubpath,
                sha,
                ext);
            if (!storage.Exists(publishedKey))
            {
                storage.Move(stagingKey, publishedKey, overwrite: false);
            }
            else
            {
                storage.Delete(stagingKey);
            }

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
            var doc = new RecallDocument
            {
                Id = $"rcl_{Guid.NewGuid():N}",
                Title = title.Trim(),
                SubtestCode = normalizedSubtest,
                PeriodLabel = periodLabel.Trim(),
                ProfessionId = NormalizeProfessionId(professionId),
                MediaAssetId = mediaId,
                DescriptionMarkdown = descriptionMarkdown,
                SortOrder = sortOrder ?? 0,
                Status = ContentStatus.Draft,
                UploadedByUserId = adminId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.RecallDocuments.Add(doc);
            AddAuditEvent(db, http, "RecallDocumentUploaded", "RecallDocument", doc.Id, $"title={doc.Title};subtest={doc.SubtestCode};media={mediaId}");
            await db.SaveChangesAsync(ct);

            await db.Entry(doc).Reference(x => x.MediaAsset!).LoadAsync(ct);
            return Results.Ok(Project(doc));
        })
        .DisableAntiforgery()
        .WithAdminWrite("AdminContentWrite");

        admin.MapPut("/{id}", async (
            string id,
            HttpContext http,
            LearnerDbContext db,
            RecallDocumentUpdate dto,
            CancellationToken ct) =>
        {
            var doc = await db.RecallDocuments.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (doc is null) return Results.NotFound();
            if (!string.IsNullOrWhiteSpace(dto.Title)) doc.Title = dto.Title.Trim();
            if (!string.IsNullOrWhiteSpace(dto.SubtestCode) && AcceptableSubtests.Contains(dto.SubtestCode.ToLowerInvariant()))
                doc.SubtestCode = dto.SubtestCode.ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(dto.PeriodLabel)) doc.PeriodLabel = dto.PeriodLabel.Trim();
            if (dto.ProfessionId is not null) doc.ProfessionId = NormalizeProfessionId(dto.ProfessionId);
            if (dto.DescriptionMarkdown is not null) doc.DescriptionMarkdown = dto.DescriptionMarkdown;
            if (dto.SortOrder is not null) doc.SortOrder = dto.SortOrder.Value;
            doc.UpdatedAt = DateTimeOffset.UtcNow;
            AddAuditEvent(db, http, "RecallDocumentUpdated", "RecallDocument", doc.Id, $"title={doc.Title};status={doc.Status}");
            await db.SaveChangesAsync(ct);
            await db.Entry(doc).Reference(x => x.MediaAsset!).LoadAsync(ct);
            return Results.Ok(Project(doc));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/{id}/publish", async (string id, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var doc = await db.RecallDocuments.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (doc is null) return Results.NotFound();
            doc.Status = ContentStatus.Published;
            doc.PublishedAt = DateTimeOffset.UtcNow;
            doc.UpdatedAt = DateTimeOffset.UtcNow;
            doc.ArchivedAt = null;
            AddAuditEvent(db, http, "RecallDocumentPublished", "RecallDocument", doc.Id, doc.Title);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = doc.Id, status = doc.Status.ToString() });
        })
        .WithAdminWrite("AdminContentPublish");

        admin.MapPost("/{id}/archive", async (string id, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var doc = await db.RecallDocuments.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (doc is null) return Results.NotFound();
            doc.Status = ContentStatus.Archived;
            doc.ArchivedAt = DateTimeOffset.UtcNow;
            doc.UpdatedAt = DateTimeOffset.UtcNow;
            AddAuditEvent(db, http, "RecallDocumentArchived", "RecallDocument", doc.Id, doc.Title);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = doc.Id, status = doc.Status.ToString() });
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/{id}/unarchive", async (string id, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var doc = await db.RecallDocuments.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (doc is null) return Results.NotFound();
            doc.Status = ContentStatus.Draft;
            doc.ArchivedAt = null;
            doc.UpdatedAt = DateTimeOffset.UtcNow;
            AddAuditEvent(db, http, "RecallDocumentUnarchived", "RecallDocument", doc.Id, doc.Title);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = doc.Id, status = doc.Status.ToString() });
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapDelete("/{id}", async (string id, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            // Soft delete (archive). Hard delete would orphan referenced MediaAssets.
            var doc = await db.RecallDocuments.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (doc is null) return Results.NotFound();
            doc.Status = ContentStatus.Archived;
            doc.ArchivedAt = DateTimeOffset.UtcNow;
            doc.UpdatedAt = DateTimeOffset.UtcNow;
            AddAuditEvent(db, http, "RecallDocumentDeleted", "RecallDocument", doc.Id, doc.Title);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithAdminWrite("AdminContentWrite");

        // Learner group.
        var learner = app.MapGroup("/v1/recall-documents")
            .RequireAuthorization()
            .RequireRateLimiting("PerUser");

        learner.MapGet("", async (LearnerDbContext db, HttpContext http, string? subtest, CancellationToken ct) =>
        {
            // Learner profession scoping: show docs with null ProfessionId (all)
            // OR matching the learner's active profession.
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            string? activeProfession = null;
            if (!string.IsNullOrEmpty(userId))
            {
                var learnerRow = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
                activeProfession = learnerRow?.ActiveProfessionId;
            }

            var q = db.RecallDocuments.AsNoTracking().Include(x => x.MediaAsset)
                .Where(x => x.Status == ContentStatus.Published)
                .Where(x => x.ProfessionId == null
                            || (activeProfession != null && x.ProfessionId == activeProfession));
            if (!string.IsNullOrWhiteSpace(subtest)) q = q.Where(x => x.SubtestCode == subtest);

            var rows = await q.OrderBy(x => x.SortOrder).ThenByDescending(x => x.PublishedAt).Take(500).ToListAsync(ct);
            return Results.Ok(rows.Select(ProjectLearner));
        });

        return app;
    }

    private static readonly HashSet<string> AcceptableSubtests =
        new(StringComparer.OrdinalIgnoreCase) { "listening", "reading", "writing", "speaking", "cross" };

    private static string? NormalizeProfessionId(string? professionId)
        => string.IsNullOrWhiteSpace(professionId) ? null : professionId.Trim().ToLowerInvariant();

    private static object Project(RecallDocument d) => new
    {
        d.Id,
        d.Title,
        d.SubtestCode,
        d.PeriodLabel,
        d.ProfessionId,
        d.MediaAssetId,
        d.DescriptionMarkdown,
        d.SortOrder,
        status = d.Status.ToString(),
        d.PublishedAt,
        d.UploadedByUserId,
        d.CreatedAt,
        d.UpdatedAt,
        d.ArchivedAt,
        media = d.MediaAsset is null ? null : new
        {
            d.MediaAsset.Id,
            d.MediaAsset.OriginalFilename,
            d.MediaAsset.MimeType,
            d.MediaAsset.Format,
            d.MediaAsset.SizeBytes,
            d.MediaAsset.Sha256,
        },
    };

    private static object ProjectLearner(RecallDocument d) => new
    {
        d.Id,
        d.Title,
        d.SubtestCode,
        d.PeriodLabel,
        d.ProfessionId,
        d.DescriptionMarkdown,
        d.PublishedAt,
        media = d.MediaAsset is null ? null : new
        {
            d.MediaAsset.Id,
            d.MediaAsset.OriginalFilename,
            d.MediaAsset.SizeBytes,
            // Download URL is computed via the existing /v1/media/{id} authenticated endpoint.
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

public sealed record RecallDocumentUpdate(
    string? Title,
    string? SubtestCode,
    string? PeriodLabel,
    string? ProfessionId,
    string? DescriptionMarkdown,
    int? SortOrder);
