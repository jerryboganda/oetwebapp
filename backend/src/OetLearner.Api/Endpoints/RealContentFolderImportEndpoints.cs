using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin endpoints for the Real Content folder importer. Uploads a ZIP
/// mirroring the user's `Project Real Content/` folder, parses it into a
/// list of proposals (Listening/Reading/Writing/Speaking papers, Recalls,
/// Result templates, Speaking shared resources, Rulebook PDFs, Scoring
/// policy markdown), and lets admin review + commit.
///
/// Session state is in-memory only (single-process). For multi-instance
/// scaling a Redis/EF-backed staging table would be needed; not required
/// for current single-API-replica deploys.
/// </summary>
public static class RealContentFolderImportEndpoints
{
    private const long MaxZipBytes = 1024L * 1024 * 1024; // 1 GB
    private static readonly ConcurrentDictionary<string, RealContentImportStageResult> _sessions = new();

    public static IEndpointRouteBuilder MapRealContentFolderImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/imports/real-content-folder")
            .RequireAuthorization("AdminContentWrite")
            .RequireRateLimiting("PerUserWrite");

        group.MapPost("/stage", async (
            HttpContext http,
            RealContentFolderImporter importer,
            IUploadContentValidator validator,
            IUploadScanner scanner,
            IFormFile file,
            CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "file required" });
            if (file.Length > MaxZipBytes) return Results.BadRequest(new { error = $"file too large (max {MaxZipBytes} bytes)" });
            var ext = (Path.GetExtension(file.FileName)?.TrimStart('.') ?? "").ToLowerInvariant();
            if (ext != "zip") return Results.BadRequest(new { error = "only .zip accepted (zip up your Project Real Content folder first)" });

            await using var stream = file.OpenReadStream();
            var validation = await validator.ValidateAsync(stream, ext, ct);
            if (!validation.Accepted
                || !string.Equals(validation.DetectedMime, "application/zip", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(validation.DetectedExtension, "zip", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new
                {
                    code = "invalid_file_content",
                    message = validation.Reason ?? "The uploaded file content does not match a ZIP archive.",
                });
            }

            if (stream.CanSeek) stream.Position = 0;
            var scanResult = await scanner.ScanAsync(stream, Path.GetFileName(file.FileName), ct);
            if (!scanResult.clean)
            {
                return Results.BadRequest(new
                {
                    code = "file_failed_security_scan",
                    message = scanResult.reason ?? "The uploaded ZIP failed security scanning.",
                });
            }

            if (stream.CanSeek) stream.Position = 0;
            var result = await importer.StageAsync(adminId, stream, file.FileName, ct);
            _sessions[result.SessionId] = result;
            return Results.Ok(new
            {
                sessionId = result.SessionId,
                uploadedFilename = result.UploadedFilename,
                stagedAt = result.StagedAt,
                proposals = result.Proposals.Select(p => new
                {
                    target = p.Target.ToString(),
                    title = p.Title,
                    subtest = p.Subtest,
                    professionId = p.ProfessionId,
                    cardType = p.CardType,
                    letterType = p.LetterType,
                    periodLabel = p.PeriodLabel,
                    templateKey = p.TemplateKey,
                    sharedResourceKind = p.SharedResourceKind,
                    rulebookKind = p.RulebookKind,
                    rulebookProfession = p.RulebookProfession,
                    sourcePath = p.SourcePath,
                    assets = p.Assets.Select(a => new { role = a.Role, part = a.Part, sourcePath = a.SourcePath, originalFilename = a.OriginalFilename }),
                }),
                issues = result.Issues,
            });
        })
        .DisableAntiforgery();

        group.MapPost("/{sessionId}/commit", async (
            string sessionId,
            HttpContext http,
            RealContentFolderImporter importer,
            IAuthorizationService authorization,
            CommitRequest body,
            CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            if (!_sessions.TryGetValue(sessionId, out var staged))
                return Results.NotFound(new { error = "session not found or expired" });

            var approvedSet = new HashSet<string>(body?.ApprovedSourcePaths ?? Array.Empty<string>());
            var approved = approvedSet.Count == 0
                ? staged.Proposals
                : staged.Proposals.Where(p => approvedSet.Contains(p.SourcePath)).ToList();

            // Wire staged storage keys onto asset entries (needed by the
            // commit path because we stored the file once per source path).
            foreach (var p in approved)
            {
                foreach (var a in p.Assets)
                {
                    var match = staged.Proposals
                        .SelectMany(x => x.Assets)
                        .FirstOrDefault(y => y.SourcePath == a.SourcePath);
                    if (match != null) a.StagedStorageKey = match.StagedStorageKey ?? a.StagedStorageKey;
                }
            }
            // Top-level (non-asset) proposals have their storage key on the proposal itself.
            // It was already populated by StageAsync.

            var canPublishContent = (await authorization.AuthorizeAsync(http.User, "AdminContentPublish")).Succeeded;
            var result = await importer.CommitAsync(adminId, approved, canPublishContent, ct);
            _sessions.TryRemove(sessionId, out _);
            return Results.Ok(new
            {
                created = result.Created.Select(c => new { target = c.Target.ToString(), id = c.Id, title = c.Title }),
                errors = result.Errors,
            });
        });

        return app;
    }

    public sealed record CommitRequest(IReadOnlyList<string>? ApprovedSourcePaths);
}
