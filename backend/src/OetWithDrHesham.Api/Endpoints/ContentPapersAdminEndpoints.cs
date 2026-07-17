using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Security;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Content;
using OetWithDrHesham.Api.Services.Writing;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// Admin endpoints for the Content Upload subsystem.
/// All routes require <c>AdminContentWrite</c> except publish, which
/// requires <c>AdminContentPublish</c> (multi-stage approval preserved).
/// </summary>
public static class ContentPapersAdminEndpoints
{
    private const long BulkZipRequestLimitBytes = 1024L * 1024 * 1024;

    public static IEndpointRouteBuilder MapContentPapersAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/papers")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUser");

        // ── List / get ─────────────────────────────────────────────────────
        group.MapGet("", async (
            IContentPaperService svc, HttpContext http, CancellationToken ct,
            string? subtest, string? profession, string? status,
            string? cardType, string? letterType, string? search,
            int? page, int? pageSize) =>
        {
            var query = new ContentPaperQuery(
                subtest, profession, status, cardType, letterType, search,
                page ?? 1, pageSize ?? 50);
            var rows = await svc.ListAsync(query, ct);
            // Backward-compatible paging: the body is still the bare paper array
            // existing callers expect; the unfiltered-by-page total is exposed
            // via the X-Total-Count header so the admin UI can paginate server-side.
            var total = await svc.CountAsync(query, ct);
            http.Response.Headers["X-Total-Count"] = total.ToString();
            return Results.Ok(rows.Select(ProjectPaper));
        });

        group.MapGet("/{id}", async (string id, IContentPaperService svc, CancellationToken ct) =>
        {
            var paper = await svc.GetAsync(id, ct);
            return paper is null ? Results.NotFound() : Results.Ok(ProjectPaper(paper));
        });

        // ── Create / update / archive ──────────────────────────────────────
        group.MapPost("", async (
            ContentPaperCreate dto, IContentPaperService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var paper = await svc.CreateAsync(dto, adminId, ct);
            return Results.Created($"/v1/admin/papers/{paper.Id}", ProjectPaper(paper));
        })
        .RequireAuthorization("AdminContentWrite")
        .RequireRateLimiting("PerUserWrite");

        // Writing-specific authoring endpoint (spec §1C / §19). Enforces the
        // integrity acknowledgement gate. The generic POST above is left
        // unchanged so the ZIP-import path and seeders keep working.
        group.MapPost("/writing-task", async (
            WritingTaskCreate dto, IContentPaperService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try
            {
                var paper = await svc.CreateWritingTaskAsync(dto, adminId, ct);
                return Results.Created($"/v1/admin/papers/{paper.Id}", ProjectPaper(paper));
            }
            catch (ContentIntegrityAcknowledgementRequiredException ex)
            {
                return Results.BadRequest(new
                {
                    error = ex.Message,
                    code = "integrity_acknowledgement_required",
                });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .RequireAuthorization("AdminContentWrite")
        .RequireRateLimiting("PerUserWrite");

        group.MapPut("/{id}", async (
            string id, ContentPaperUpdate dto, IContentPaperService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var paper = await svc.UpdateAsync(id, dto, adminId, ct);
            return Results.Ok(ProjectPaper(paper));
        })
        .RequireAuthorization("AdminContentWrite")
        .RequireRateLimiting("PerUserWrite");

        group.MapDelete("/{id}", async (
            string id, IContentPaperService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            await svc.ArchiveAsync(id, adminId, ct);
            return Results.NoContent();
        })
        .RequireAuthorization("AdminContentWrite")
        .RequireRateLimiting("PerUserWrite");

        // ── Publish ────────────────────────────────────────────────────────
        group.MapPost("/{id}/publish", async (
            string id, IContentPaperService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try { await svc.PublishAsync(id, adminId, ct); }
            catch (InvalidOperationException ex)
            {
                // Return `message` (in addition to `error`) so the client api
                // helper surfaces the real reason (e.g. "SourceProvenance is
                // required …") instead of a generic failure toast.
                return Results.BadRequest(new { error = ex.Message, message = ex.Message });
            }
            return Results.NoContent();
        })
        .RequireAuthorization("AdminContentPublish")
        .RequireRateLimiting("PerUserWrite");

        // ── Unpublish (revert to Draft) ───────────────────────────────────
        group.MapPost("/{id}/unpublish", async (
            string id, IContentPaperService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try { await svc.UnpublishAsync(id, adminId, ct); }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            return Results.NoContent();
        })
        .RequireAuthorization("AdminContentPublish")
        .RequireRateLimiting("PerUserWrite");

        // ── Draft → InReview → Published workflow (spec §1E) ──────────────
        group.MapPost("/{id}/submit-for-review", async (
            string id, IContentPaperService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try { await svc.SubmitForReviewAsync(id, adminId, ct); }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            return Results.NoContent();
        })
        .RequireAuthorization("AdminContentWrite")
        .RequireRateLimiting("PerUserWrite");

        group.MapPost("/{id}/approve-publish", async (
            string id, IContentPaperService svc, LearnerDbContext db,
            IWritingTaskProjectionService projection,
            ILoggerFactory loggerFactory, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try { await svc.ApproveAndPublishAsync(id, adminId, ct); }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            // WS-B2 bridge: publishing a writing paper idempotently projects it into
            // the authoritative WritingScenario (keyed on SourceContentPaperId). Failure
            // here must not undo the publish, so it is logged and swallowed.
            var paper = await db.ContentPapers.Include(p => p.Assets).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (paper is not null && string.Equals(paper.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await projection.ProjectFromContentPaperAsync(paper, ct);
                }
                catch (Exception ex)
                {
                    loggerFactory
                        .CreateLogger(typeof(ContentPapersAdminEndpoints).FullName!)
                        .LogError(ex, "Failed to project writing ContentPaper {PaperId} into a scenario", id);
                }
            }

            return Results.NoContent();
        })
        .RequireAuthorization("AdminContentPublish")
        .RequireRateLimiting("PerUserWrite");

        group.MapPost("/{id}/reject", async (
            string id, ContentPaperRejectDto dto, IContentPaperService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            if (string.IsNullOrWhiteSpace(dto.Reason))
            {
                return Results.BadRequest(new { error = "Rejection reason is required.", code = "rejection_reason_required" });
            }
            try { await svc.RejectAsync(id, adminId, dto.Reason, ct); }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            return Results.NoContent();
        })
        .RequireAuthorization("AdminContentPublish")
        .RequireRateLimiting("PerUserWrite");

        // ── Bulk workflow actions (T1) ─────────────────────────────────────
        // ONE transaction + ONE summary audit row, delegating to the per-item
        // service methods. Permission is split per action (see below) because a
        // single route can't carry two RequireAuthorization policies.
        group.MapPost("/bulk", async (
            ContentPaperBulkRequest req, IContentPaperService svc, HttpContext http, CancellationToken ct) =>
        {
            var action = (req.Action ?? string.Empty).Trim().ToLowerInvariant();

            // delete / force-delete (permanent) require system_admin — the most
            // destructive actions (force-delete also purges learner attempts);
            // publish/unpublish/approve-publish/reject require content:publish;
            // archive/submit-for-review require content:write.
            var requiresSystemAdmin = action is "delete" or "force-delete";
            var requiresPublish = action is "publish" or "unpublish" or "approve-publish" or "reject";
            var requiresWrite = action is "archive" or "submit-for-review";
            if (!requiresSystemAdmin && !requiresPublish && !requiresWrite)
            {
                return Results.BadRequest(new { error = $"Unknown bulk action '{req.Action}'." });
            }

            var perms = http.User.FindFirstValue(AuthTokenService.AdminPermissionsClaimType);
            var allowed = requiresSystemAdmin
                ? AdminPermissionEvaluator.HasAny(perms, "system_admin")
                : requiresPublish
                    ? AdminPermissionEvaluator.HasAny(perms, "content:publish", "system_admin")
                    : AdminPermissionEvaluator.HasAny(perms, "content:write", "system_admin");
            if (!allowed)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (action == "reject" && string.IsNullOrWhiteSpace(req.Reason))
            {
                return Results.BadRequest(new { error = "Rejection reason is required.", code = "rejection_reason_required" });
            }

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try
            {
                var result = await svc.BulkAsync(action, req.Ids ?? Array.Empty<string>(), adminId, req.Reason, ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .RequireRateLimiting("PerUserWrite");

        // Speaking-specific structure editor. Hidden interlocutor data stays
        // behind the admin gate and is never exposed through learner routes.
        group.MapGet("/{id}/speaking-structure", async (
            string id, LearnerDbContext db, CancellationToken ct) =>
        {
            var paper = await db.ContentPapers
                .Include(p => p.Assets)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (paper is null) return Results.NotFound();
            if (!string.Equals(paper.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "This paper is not a speaking paper." });
            }

            return Results.Ok(new
            {
                paperId = paper.Id,
                structure = SpeakingContentStructure.ExtractStructure(paper.ExtractedTextJson),
                validation = SpeakingContentStructure.Validate(paper),
            });
        })
        .RequireAuthorization("AdminContentWrite")
        .RequireRateLimiting("PerUserWrite");

        group.MapPut("/{id}/speaking-structure", async (
            string id,
            SpeakingStructureSaveDto dto,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var paper = await db.ContentPapers
                .Include(p => p.Assets)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (paper is null) return Results.NotFound();
            if (!string.Equals(paper.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "This paper is not a speaking paper." });
            }

            if (dto.Structure.ValueKind != JsonValueKind.Object)
            {
                return Results.BadRequest(new { error = "speaking structure object is required." });
            }

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            paper.ExtractedTextJson = SpeakingContentStructure.ReplaceStructure(paper.ExtractedTextJson, dto.Structure);
            paper.UpdatedAt = DateTimeOffset.UtcNow;
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = DateTimeOffset.UtcNow,
                ActorId = adminId,
                ActorName = adminId,
                Action = "ContentPaperSpeakingStructureUpdated",
                ResourceType = "ContentPaper",
                ResourceId = paper.Id,
                Details = paper.Title,
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                paperId = paper.Id,
                structure = SpeakingContentStructure.ExtractStructure(paper.ExtractedTextJson),
                validation = SpeakingContentStructure.Validate(paper),
                updatedAt = paper.UpdatedAt,
            });
        })
        .RequireAuthorization("AdminContentWrite")
        .RequireRateLimiting("PerUserWrite");

        // Writing-specific structure editor. Model answers remain behind the
        // admin gate here and are projected only to the post-submit study flow.
        group.MapGet("/{id}/writing-structure", async (
            string id, LearnerDbContext db, CancellationToken ct) =>
        {
            var paper = await db.ContentPapers
                .Include(p => p.Assets)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (paper is null) return Results.NotFound();
            if (!string.Equals(paper.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "This paper is not a writing paper." });
            }

            return Results.Ok(new
            {
                paperId = paper.Id,
                structure = WritingContentStructure.ExtractStructure(paper.ExtractedTextJson),
                validation = WritingContentStructure.Validate(paper),
            });
        })
        .RequireAuthorization("AdminContentWrite")
        .RequireRateLimiting("PerUserWrite");

        group.MapPut("/{id}/writing-structure", async (
            string id,
            WritingStructureSaveDto dto,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var paper = await db.ContentPapers
                .Include(p => p.Assets)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (paper is null) return Results.NotFound();
            if (!string.Equals(paper.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "This paper is not a writing paper." });
            }

            if (dto.Structure.ValueKind != JsonValueKind.Object)
            {
                return Results.BadRequest(new { error = "writing structure object is required." });
            }

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            paper.ExtractedTextJson = WritingContentStructure.ReplaceStructure(paper.ExtractedTextJson, dto.Structure);
            paper.UpdatedAt = DateTimeOffset.UtcNow;
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = DateTimeOffset.UtcNow,
                ActorId = adminId,
                ActorName = adminId,
                Action = "ContentPaperWritingStructureUpdated",
                ResourceType = "ContentPaper",
                ResourceId = paper.Id,
                Details = paper.Title,
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                paperId = paper.Id,
                structure = WritingContentStructure.ExtractStructure(paper.ExtractedTextJson),
                validation = WritingContentStructure.Validate(paper),
                updatedAt = paper.UpdatedAt,
            });
        })
        .RequireAuthorization("AdminContentWrite")
        .RequireRateLimiting("PerUserWrite");

        // ── Asset attach / remove ──────────────────────────────────────────
        group.MapPost("/{id}/assets", async (
            string id, ContentPaperAssetAttach dto,
            IContentPaperService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var asset = await svc.AttachAssetAsync(id, dto, adminId, ct);
            return Results.Created($"/v1/admin/papers/{id}/assets/{asset.Id}", ProjectAsset(asset));
        })
        .RequireAuthorization("AdminContentWrite")
        .RequireRateLimiting("PerUserWrite");

        group.MapDelete("/{id}/assets/{assetId}", async (
            string id, string assetId,
            IContentPaperService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var removed = await svc.RemoveAssetAsync(id, assetId, adminId, ct);
            return removed ? Results.NoContent() : Results.NotFound();
        })
        .RequireAuthorization("AdminContentWrite")
        .RequireRateLimiting("PerUserWrite");

        // ── Required roles (helper for the editor UI) ──────────────────────
        group.MapGet("/required-roles/{subtest}", (string subtest, IContentPaperService svc) =>
        {
            var roles = svc.RequiredRolesFor(subtest).Select(r => r.ToString()).ToArray();
            return Results.Ok(new { subtest, required = roles });
        });

        // ── Chunked upload endpoints ───────────────────────────────────────
        var uploads = app.MapGroup("/v1/admin/uploads")
            .RequireAuthorization("AdminContentWrite")
            .RequireRateLimiting("PerUserWrite");

        uploads.MapPost("", async (
            ChunkedUploadStartDto dto, IChunkedUploadService svc, IOptions<StorageOptions> options, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("admin id required");
            var session = await svc.StartAsync(new ChunkedUploadStart(
                adminId, dto.OriginalFilename, dto.DeclaredMimeType,
                dto.DeclaredSizeBytes, dto.IntendedRole ?? "Supplementary"), ct);
            return Results.Ok(new { uploadId = session.Id, chunkSizeBytes = options.Value.ContentUpload.ChunkSizeBytes, expiresAt = session.ExpiresAt });
        });

        uploads.MapPut("/{uploadId}/parts/{partNumber:int}", async (
            string uploadId, int partNumber, HttpContext http,
            IChunkedUploadService svc, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("admin id required");
            var session = await svc.UploadPartAsync(adminId, uploadId, partNumber, http.Request.Body, ct);
            return Results.Ok(new { session.PartsReceived, session.ReceivedBytes, session.State });
        })
        .DisableAntiforgery();

        uploads.MapPost("/{uploadId}/complete", async (
            string uploadId, HttpContext http, IChunkedUploadService svc, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("admin id required");
            var result = await svc.CompleteAsync(adminId, uploadId, ct);
            return Results.Ok(result);
        });

        uploads.MapDelete("/{uploadId}", async (
            string uploadId, HttpContext http, IChunkedUploadService svc, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("admin id required");
            await svc.AbortAsync(adminId, uploadId, ct);
            return Results.NoContent();
        });

        // ── Bulk ZIP import (Slice 5) ──────────────────────────────────────
        var imports = app.MapGroup("/v1/admin/imports")
            .RequireAuthorization("AdminContentWrite")
            .RequireRateLimiting("PerUserWrite");

        imports.MapPost("/zip", async (
            HttpContext http, IContentBulkImportService svc, IFormFile file, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("admin id required");
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "file required" });
            await using var stream = file.OpenReadStream();
            var session = await svc.StagePayloadAsync(adminId, stream, file.FileName, ct);
            return Results.Ok(new
            {
                sessionId = session.SessionId,
                expiresAt = session.ExpiresAt,
                papers = session.Manifest.Papers.Select(p => new
                {
                    p.ProposalId, p.SubtestCode, p.Title,
                    p.ProfessionId, p.AppliesToAllProfessions,
                    p.CardType, p.LetterType, p.SourceProvenance,
                    p.DeliveryModes,
                    p.OfficialShape,
                    readinessIssues = p.ReadinessIssues.Select(i => new { i.Code, i.Severity, i.Message }),
                    assets = p.Assets.Select(a => new
                    {
                        a.SourceRelativePath, role = a.Role.ToString(),
                        a.Part, a.SuggestedTitle,
                    }),
                }),
                references = session.Manifest.References.Select(r => new
                {
                    r.ProposalId,
                    r.Target,
                    r.Title,
                    r.SourceRelativePath,
                    r.Kind,
                    r.ProfessionId,
                    r.SharedResourceKind,
                    r.TemplateKey,
                    r.SortOrder,
                    r.SourceProvenance,
                    readinessIssues = r.ReadinessIssues.Select(i => new { i.Code, i.Severity, i.Message }),
                }),
                inventory = session.Manifest.Inventory,
                readinessIssues = session.Manifest.ReadinessIssues.Select(i => new { i.Code, i.Severity, i.Message }),
                issues = session.Manifest.Issues,
            });
        })
        .DisableAntiforgery()
        .WithMetadata(
            new RequestSizeLimitAttribute(BulkZipRequestLimitBytes),
            new RequestFormLimitsAttribute { MultipartBodyLengthLimit = BulkZipRequestLimitBytes });

        imports.MapPost("/zip/{sessionId}/commit", async (
            string sessionId,
            IReadOnlyList<BulkImportApproval> approvals,
            IContentBulkImportService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("admin id required");
            var result = await svc.CommitAsync(adminId, sessionId, approvals, ct);
            return Results.Ok(result);
        });

        return app;
    }

    private static object ProjectPaper(ContentPaper p) => new
    {
        p.Id, p.SubtestCode, p.Title, p.Slug, p.ProfessionId, p.AppliesToAllProfessions,
        p.Difficulty, p.EstimatedDurationMinutes, status = p.Status.ToString(),
        p.PublishedRevisionId, p.CardType, p.LetterType, p.Priority, p.TagsCsv,
        p.SourceProvenance, p.CreatedAt, p.UpdatedAt, p.PublishedAt, p.ArchivedAt,
        p.IntegrityAcknowledgedByAdminId, p.IntegrityAcknowledgedAt,
        assets = p.Assets.Select(ProjectAsset),
    };

    private static object ProjectAsset(ContentPaperAsset a) => new
    {
        a.Id,
        role = a.Role.ToString(),
        a.Part,
        a.MediaAssetId,
        a.Title,
        a.DisplayOrder,
        a.IsPrimary,
        a.CreatedAt,
        media = a.MediaAsset is null ? null : new
        {
            a.MediaAsset.Id,
            a.MediaAsset.OriginalFilename,
            a.MediaAsset.MimeType,
            a.MediaAsset.Format,
            a.MediaAsset.SizeBytes,
            a.MediaAsset.DurationSeconds,
            a.MediaAsset.Sha256,
            a.MediaAsset.MediaKind,
            a.MediaAsset.UploadedAt,
        },
    };
}

public sealed record ChunkedUploadStartDto(
    string OriginalFilename,
    string DeclaredMimeType,
    long DeclaredSizeBytes,
    string? IntendedRole);

public sealed record SpeakingStructureSaveDto(JsonElement Structure);
public sealed record WritingStructureSaveDto(JsonElement Structure);
public sealed record ContentPaperRejectDto(string Reason);
