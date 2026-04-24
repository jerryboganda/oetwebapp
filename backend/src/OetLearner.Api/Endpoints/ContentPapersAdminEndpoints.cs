using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin endpoints for the Content Upload subsystem.
/// All routes require <c>AdminContentWrite</c> except publish, which
/// requires <c>AdminContentPublish</c> (multi-stage approval preserved).
/// </summary>
public static class ContentPapersAdminEndpoints
{
    public static IEndpointRouteBuilder MapContentPapersAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/papers")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUser");

        // ── List / get ─────────────────────────────────────────────────────
        group.MapGet("", async (
            IContentPaperService svc, CancellationToken ct,
            string? subtest, string? profession, string? status,
            string? cardType, string? letterType, string? search,
            int? page, int? pageSize) =>
        {
            var rows = await svc.ListAsync(new ContentPaperQuery(
                subtest, profession, status, cardType, letterType, search,
                page ?? 1, pageSize ?? 50), ct);
            return Results.Ok(rows);
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
            return Results.Created($"/v1/admin/papers/{paper.Id}", paper);
        })
        .RequireAuthorization("AdminContentWrite")
        .RequireRateLimiting("PerUserWrite");

        group.MapPut("/{id}", async (
            string id, ContentPaperUpdate dto, IContentPaperService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var paper = await svc.UpdateAsync(id, dto, adminId, ct);
            return Results.Ok(paper);
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
                return Results.BadRequest(new { error = ex.Message });
            }
            return Results.NoContent();
        })
        .RequireAuthorization("AdminContentPublish")
        .RequireRateLimiting("PerUserWrite");

        // ── Asset attach / remove ──────────────────────────────────────────
        group.MapPost("/{id}/assets", async (
            string id, ContentPaperAssetAttach dto,
            IContentPaperService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var asset = await svc.AttachAssetAsync(id, dto, adminId, ct);
            return Results.Created($"/v1/admin/papers/{id}/assets/{asset.Id}", asset);
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
            var session = await svc.UploadPartAsync(uploadId, partNumber, http.Request.Body, ct);
            return Results.Ok(new { session.PartsReceived, session.ReceivedBytes, session.State });
        })
        .DisableAntiforgery();

        uploads.MapPost("/{uploadId}/complete", async (
            string uploadId, IChunkedUploadService svc, CancellationToken ct) =>
        {
            var result = await svc.CompleteAsync(uploadId, ct);
            return Results.Ok(result);
        });

        uploads.MapDelete("/{uploadId}", async (
            string uploadId, IChunkedUploadService svc, CancellationToken ct) =>
        {
            await svc.AbortAsync(uploadId, ct);
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
                    assets = p.Assets.Select(a => new
                    {
                        a.SourceRelativePath, role = a.Role.ToString(),
                        a.Part, a.SuggestedTitle,
                    }),
                }),
                issues = session.Manifest.Issues,
            });
        })
        .DisableAntiforgery();

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
        assets = p.Assets.Select(a => new
        {
            a.Id, role = a.Role.ToString(), a.Part, a.MediaAssetId, a.Title,
            a.DisplayOrder, a.IsPrimary, a.CreatedAt,
            media = a.MediaAsset is null ? null : new
            {
                a.MediaAsset.Id, a.MediaAsset.OriginalFilename, a.MediaAsset.MimeType,
                a.MediaAsset.Format, a.MediaAsset.SizeBytes, a.MediaAsset.DurationSeconds,
                a.MediaAsset.Sha256, a.MediaAsset.MediaKind, a.MediaAsset.UploadedAt,
            },
        }),
    };
}

public sealed record ChunkedUploadStartDto(
    string OriginalFilename,
    string DeclaredMimeType,
    long DeclaredSizeBytes,
    string? IntendedRole);
