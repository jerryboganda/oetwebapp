using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Listening;

// `ListeningExtractionDraft` collides between the in-memory AI-result record
// in `OetLearner.Api.Services.Listening` and the persisted entity in
// `OetLearner.Api.Domain`. Endpoint code only ever needs the entity here.
using DraftEntity = OetLearner.Api.Domain.ListeningExtractionDraft;
using DraftStatus = OetLearner.Api.Domain.ListeningExtractionDraftStatus;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin endpoints for the Listening authoring + publish-gate surface.
///
/// Mirrors the Reading pattern (<c>ReadingAuthoringAdminEndpoints</c>):
///
///   GET  /v1/admin/papers/{id}/listening/structure   — load authored 42-item map
///   PUT  /v1/admin/papers/{id}/listening/structure   — replace authored 42-item map
///   GET  /v1/admin/papers/{id}/listening/validate    — publish-gate report
///
/// All routes require <c>AdminContentWrite</c>. Structure/extract writes remain
/// JSON-compatible for admin editing and can be projected into the relational
/// Listening tables through the backfill endpoint. Authored learner attempts
/// prefer relational rows when present and keep JSON fallback for migration.
/// </summary>
public static class ListeningAuthoringAdminEndpoints
{
    public sealed record ReplaceStructureBody(IReadOnlyList<ListeningAuthoredQuestion> Questions);
    public sealed record ReplaceExtractsBody(IReadOnlyList<ListeningAuthoredExtract> Extracts);

    /// <summary>WS5: import body — a spec §19 manifest plus the replace toggle.</summary>
    public sealed record ImportManifestBody(bool ReplaceExisting, ListeningStructureManifest Manifest);

    public sealed record ApproveDraftBody(string? Reason);
    public sealed record RejectDraftBody(string? Reason);
    public sealed record BackfillRequestBody(string? ConfirmationToken);
    public sealed record TranscriptSegmentDto(int StartMs, int EndMs, string SpeakerId, string Text);
    public sealed record ReplaceTranscriptBody(IReadOnlyList<TranscriptSegmentDto> Segments);

    /// <summary>
    /// Validates the If-Match header against the paper's RowVersion.
    /// Returns a 412 Precondition Failed result if they don't match, or null to proceed.
    /// </summary>
    private static IResult? CheckIfMatch(HttpContext http, ContentPaper paper)
    {
        var ifMatch = http.Request.Headers.IfMatch.FirstOrDefault();
        if (string.IsNullOrEmpty(ifMatch)) return null;
        if (!int.TryParse(ifMatch.Trim('"'), out var clientVersion)
            || clientVersion != paper.RowVersion)
        {
            return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
        }
        return null;
    }

    /// <summary>Sets the ETag response header to the paper's current RowVersion.</summary>
    private static void SetETag(HttpContext http, ContentPaper paper)
    {
        http.Response.Headers.ETag = $"\"{paper.RowVersion}\"";
    }

    /// <summary>
    /// H2: Published papers require AdminContentPublish (or system_admin) for any mutation.
    /// Returns a Forbid result when the paper is Published and the caller lacks permission.
    /// Returns null when the caller is allowed to proceed.
    /// </summary>
    private static IResult? EnforcePublishGate(ContentPaper paper, HttpContext http)
    {
        if (paper.Status != ContentStatus.Published) return null;
        var perms = http.User.FindFirstValue(AuthTokenService.AdminPermissionsClaimType);
        if (AdminPermissionEvaluator.HasAny(perms, AdminPermissions.ContentPublish, AdminPermissions.SystemAdmin))
            return null;
        return Results.Forbid();
    }

    public static IEndpointRouteBuilder MapListeningAuthoringAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/papers/{paperId}/listening")
            .RequireAuthorization("AdminContentWrite")
            .RequireRateLimiting("PerUserWrite");

        group.MapGet("/validate", async (
            string paperId,
            IListeningStructureService svc,
            CancellationToken ct) =>
        {
            var report = await svc.ValidatePaperAsync(paperId, ct);
            return Results.Ok(report);
        });

        group.MapGet("/structure", async (
            string paperId,
            IListeningAuthoringService svc,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var paper = await db.ContentPapers.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == paperId, ct);
            if (paper is not null) SetETag(http, paper);
            var doc = await svc.GetStructureAsync(paperId, ct);
            return Results.Ok(doc);
        });

        group.MapPut("/structure", async (
            string paperId,
            ReplaceStructureBody body,
            IListeningAuthoringService svc,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var paper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct);
            if (paper is null) return Results.NotFound();
            var forbidden = EnforcePublishGate(paper, http);
            if (forbidden is not null) return forbidden;
            var conflict = CheckIfMatch(http, paper);
            if (conflict is not null) return conflict;

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var doc = await svc.ReplaceStructureAsync(paperId, body.Questions ?? [], adminId, ct);
            await db.Entry(paper).ReloadAsync(ct);
            SetETag(http, paper);
            return Results.Ok(doc);
        });

        // Gap B6: per-question PATCH. Mutates only fields explicitly supplied
        // in the body; null fields are left untouched. Returns the full
        // re-tallied structure so the admin UI can refresh in one round trip.
        group.MapPatch("/structure/{questionId}", async (
            string paperId,
            string questionId,
            ListeningQuestionPatch body,
            IListeningAuthoringService svc,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var paper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct);
            if (paper is null) return Results.NotFound();
            var forbidden = EnforcePublishGate(paper, http);
            if (forbidden is not null) return forbidden;
            var conflict = CheckIfMatch(http, paper);
            if (conflict is not null) return conflict;

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var doc = await svc.PatchQuestionAsync(paperId, questionId, body ?? new(), adminId, ct);
            await db.Entry(paper).ReloadAsync(ct);
            SetETag(http, paper);
            return Results.Ok(doc);
        });

        // Phase 8: AI-assisted structure proposal. Persists the AI gateway
        // result as a Pending ListeningExtractionDraft and returns the same
        // payload shape the admin UI already consumes plus draftId/status so
        // the new approve/reject flow can take over.
        group.MapPost("/extract", async (
            string paperId,
            IListeningExtractionDraftService drafts,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var paper = await db.ContentPapers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paperId, ct);
            if (paper is null) return Results.NotFound();
            var forbidden = EnforcePublishGate(paper, http);
            if (forbidden is not null) return forbidden;

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var draft = await drafts.ProposeAsync(paperId, adminId, ct);
            var questions = System.Text.Json.JsonSerializer
                .Deserialize<List<ListeningAuthoredQuestion>>(
                    draft.ProposedQuestionsJson,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    }) ?? [];
            return Results.Ok(new
            {
                draftId = draft.Id,
                status = draft.Status.ToString(),
                summary = draft.Summary,
                isStub = draft.IsStub,
                stubReason = draft.StubReason,
                questions,
            });
        });

        // Phase 5 tail: admin CRUD for paper-level extract metadata
        // (accent / speakers / audio window / extract title). Persisted under
        // ContentPaper.ExtractedTextJson["listeningExtracts"] so it ships
        // additively next to the questions blob.
        group.MapGet("/extracts", async (
            string paperId,
            IListeningAuthoringService svc,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var paper = await db.ContentPapers.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == paperId, ct);
            if (paper is not null) SetETag(http, paper);
            var doc = await svc.GetExtractsAsync(paperId, ct);
            return Results.Ok(new { extracts = doc });
        });

        group.MapPut("/extracts", async (
            string paperId,
            ReplaceExtractsBody body,
            IListeningAuthoringService svc,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var paper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct);
            if (paper is null) return Results.NotFound();
            var forbidden = EnforcePublishGate(paper, http);
            if (forbidden is not null) return forbidden;
            var conflict = CheckIfMatch(http, paper);
            if (conflict is not null) return conflict;
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var doc = await svc.ReplaceExtractsAsync(paperId, body.Extracts ?? [], adminId, ct);
            await db.Entry(paper).ReloadAsync(ct);
            SetETag(http, paper);
            return Results.Ok(new { extracts = doc });
        });

        // Gap B6: per-extract PATCH. The extractCode segment is one of the
        // canonical part codes (A1 | A2 | B | C1 | C2). Mutates only fields
        // explicitly supplied in the body.
        group.MapPatch("/extracts/{extractCode}", async (
            string paperId,
            string extractCode,
            ListeningExtractPatch body,
            IListeningAuthoringService svc,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var paper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct);
            if (paper is null) return Results.NotFound();
            var forbidden = EnforcePublishGate(paper, http);
            if (forbidden is not null) return forbidden;
            var conflict = CheckIfMatch(http, paper);
            if (conflict is not null) return conflict;
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var doc = await svc.PatchExtractAsync(paperId, extractCode, body ?? new(), adminId, ct);
            await db.Entry(paper).ReloadAsync(ct);
            SetETag(http, paper);
            return Results.Ok(new { extracts = doc });
        });

        // ─── WS5: spec §19 manifest import / export ────────────────────────
        //
        // GET  /v1/admin/papers/{id}/listening/manifest — export the current
        //      authored structure + extracts as a §19 manifest (round-trips).
        // POST /v1/admin/papers/{id}/listening/manifest — import a complete
        //      Listening test from a §19 manifest. Reuses the same auth
        //      (AdminContentWrite) + rate-limit (PerUserWrite) + publish-gate +
        //      If-Match helpers as the structure/extracts routes. Validation
        //      failures map to 400; learner-attempt / already-authored conflicts
        //      map to 409. Mirrors ReadingAuthoringAdminEndpoints' /manifest pair.
        group.MapGet("/manifest", async (
            string paperId,
            IListeningAuthoringService svc,
            CancellationToken ct) =>
        {
            try
            {
                var manifest = await svc.ExportManifestAsync(paperId, ct);
                return Results.Ok(manifest);
            }
            catch (ApiException ex)
            {
                return Results.Json(new { error = ex.Message, errorCode = ex.ErrorCode }, statusCode: ex.StatusCode);
            }
        });

        group.MapPost("/manifest", async (
            string paperId,
            ImportManifestBody body,
            IListeningAuthoringService svc,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (body?.Manifest is null)
                return Results.BadRequest(new { error = "Request body must contain a manifest.", errorCode = "listening_manifest_required" });

            var paper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct);
            if (paper is null) return Results.NotFound();
            var forbidden = EnforcePublishGate(paper, http);
            if (forbidden is not null) return forbidden;
            var conflict = CheckIfMatch(http, paper);
            if (conflict is not null) return conflict;

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try
            {
                var result = await svc.ImportManifestAsync(paperId, body.Manifest, body.ReplaceExisting, adminId, ct);
                await db.Entry(paper).ReloadAsync(ct);
                SetETag(http, paper);
                return Results.Ok(new { structure = result.Structure, report = result.Report });
            }
            catch (ApiException ex)
            {
                return Results.Json(new { error = ex.Message, errorCode = ex.ErrorCode }, statusCode: ex.StatusCode);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Gap B7 — AI extraction draft lifecycle. Reads gated to
        // AdminContentRead, mutations stay on the AdminContentWrite group.
        var readGroup = app.MapGroup("/v1/admin/papers/{paperId}/listening")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUserWrite");

        readGroup.MapGet("/extractions", async (
            string paperId,
            string? status,
            IListeningExtractionDraftService svc,
            CancellationToken ct) =>
        {
            ListeningExtractionDraftStatus? filter = null;
            if (!string.IsNullOrWhiteSpace(status)
                && Enum.TryParse<DraftStatus>(status, ignoreCase: true, out var parsed))
            {
                filter = parsed;
            }
            var drafts = await svc.ListAsync(paperId, filter, ct);
            return Results.Ok(drafts);
        });

        readGroup.MapGet("/extractions/{draftId}", async (
            string paperId,
            string draftId,
            IListeningExtractionDraftService svc,
            CancellationToken ct) =>
        {
            var draft = await svc.GetAsync(draftId, ct);
            if (draft is null || draft.PaperId != paperId) return Results.NotFound();
            return Results.Ok(draft);
        });

        group.MapPost("/extractions/{draftId}/approve", async (
            string paperId,
            string draftId,
            ApproveDraftBody? body,
            IListeningExtractionDraftService svc,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var existing = await svc.GetAsync(draftId, ct);
            if (existing is null || existing.PaperId != paperId) return Results.NotFound();
            var paper = await db.ContentPapers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paperId, ct);
            if (paper is null) return Results.NotFound();
            var forbidden = EnforcePublishGate(paper, http);
            if (forbidden is not null) return forbidden;

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var draft = await svc.ApproveAsync(draftId, adminId, body?.Reason, ct);
            return Results.Ok(draft);
        });

        group.MapPost("/extractions/{draftId}/reject", async (
            string paperId,
            string draftId,
            RejectDraftBody body,
            IListeningExtractionDraftService svc,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var existing = await svc.GetAsync(draftId, ct);
            if (existing is null || existing.PaperId != paperId) return Results.NotFound();
            var paper = await db.ContentPapers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paperId, ct);
            if (paper is null) return Results.NotFound();
            var forbidden = EnforcePublishGate(paper, http);
            if (forbidden is not null) return forbidden;

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var draft = await svc.RejectAsync(draftId, adminId, body?.Reason ?? string.Empty, ct);
            return Results.Ok(draft);
        });

        // Phase 2 follow-up: project the JSON-blob authored shape into the
        // relational ListeningPart / Extract / Question / Option entities.
        // Idempotent — wipes existing relational rows for the paper before
        // re-inserting. Authored learner attempts read those relational rows
        // when present, with JSON fallback for not-yet-backfilled content.
        //
        // H3: Backfill is destructive — requires explicit confirmation token.
        // If paper has existing attempts, only system_admin may proceed.
        group.MapPost("/backfill", async (
            string paperId,
            BackfillRequestBody? body,
            IListeningBackfillService svc,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            // H3: Require confirmation token to prevent accidental invocation
            var confirmToken = body?.ConfirmationToken;
            if (string.IsNullOrWhiteSpace(confirmToken) || confirmToken != "CONFIRM_BACKFILL")
            {
                return Results.BadRequest(new
                {
                    errorCode = "listening_backfill_requires_confirmation",
                    message = "Backfill overwrites hand-edited relational data. Pass confirmationToken='CONFIRM_BACKFILL' to proceed.",
                });
            }

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";

            // H2: Published papers require AdminContentPublish
            var paper = await db.ContentPapers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paperId, ct);
            if (paper is null) return Results.NotFound();
            var forbidden = EnforcePublishGate(paper, http);
            if (forbidden is not null) return forbidden;

            // H3: If paper has existing attempts, require system_admin
            var hasAttempts = await db.ListeningAttempts.AsNoTracking()
                .AnyAsync(a => a.PaperId == paperId, ct);
            if (hasAttempts)
            {
                var perms = http.User.FindFirstValue(AuthTokenService.AdminPermissionsClaimType);
                if (!AdminPermissionEvaluator.HasAny(perms, AdminPermissions.SystemAdmin))
                {
                    return Results.Json(new
                    {
                        errorCode = "listening_backfill_requires_system_admin",
                        message = "Only system_admin can backfill papers that learners have already attempted.",
                    }, statusCode: 403);
                }
            }

            // H3: Audit event for every backfill invocation
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = DateTimeOffset.UtcNow,
                ActorId = adminId,
                ActorName = adminId,
                Action = "listening.backfill.executed",
                ResourceType = "ContentPaper",
                ResourceId = paperId,
                Details = $"hasExistingAttempts={hasAttempts}; destructive=true",
            });
            await db.SaveChangesAsync(ct);

            var report = await svc.BackfillPaperAsync(paperId, adminId, hasAttempts, ct);
            return Results.Ok(report);
        });

        // ─── Transcript Segment CRUD ───────────────────────────────────────

        group.MapGet("/extracts/{extractId}/transcript", async (
            string paperId,
            string extractId,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var extract = await db.ListeningExtracts
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == extractId, ct);
            if (extract is null)
                return Results.NotFound(new { errorCode = "listening_extract_not_found", message = $"Extract {extractId} not found." });

            // Validate extract belongs to this paper via part → paper linkage.
            var part = await db.ListeningParts
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == extract.ListeningPartId, ct);
            if (part is null || part.PaperId != paperId)
                return Results.NotFound(new { errorCode = "listening_extract_not_found", message = $"Extract {extractId} does not belong to paper {paperId}." });

            var segments = System.Text.Json.JsonSerializer
                .Deserialize<List<TranscriptSegmentDto>>(
                    extract.TranscriptSegmentsJson,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    }) ?? [];

            return Results.Ok(new { extractId, segments });
        })
        .WithName("GetListeningExtractTranscript")
        .WithSummary("Return deserialized transcript segments for a listening extract.");

        group.MapPut("/extracts/{extractId}/transcript", async (
            string paperId,
            string extractId,
            ReplaceTranscriptBody body,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            // Validate paper exists and enforce publish gate (tracked for RowVersion).
            var paper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct);
            if (paper is null) return Results.NotFound();
            var forbidden = EnforcePublishGate(paper, http);
            if (forbidden is not null) return forbidden;

            // Concurrency check via If-Match header.
            var conflict = CheckIfMatch(http, paper);
            if (conflict is not null) return conflict;

            var extract = await db.ListeningExtracts
                .FirstOrDefaultAsync(e => e.Id == extractId, ct);
            if (extract is null)
                return Results.NotFound(new { errorCode = "listening_extract_not_found", message = $"Extract {extractId} not found." });

            // Validate extract belongs to this paper via part → paper linkage.
            var part = await db.ListeningParts
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == extract.ListeningPartId, ct);
            if (part is null || part.PaperId != paperId)
                return Results.NotFound(new { errorCode = "listening_extract_not_found", message = $"Extract {extractId} does not belong to paper {paperId}." });

            // Validate each segment.
            if (body.Segments is null || body.Segments.Count == 0)
                return Results.BadRequest(new { errorCode = "invalid_segments", message = "Segments array must contain at least one entry." });

            for (var i = 0; i < body.Segments.Count; i++)
            {
                var seg = body.Segments[i];
                if (seg.StartMs < 0)
                    return Results.BadRequest(new { errorCode = "invalid_segment", message = $"Segment[{i}]: startMs must be >= 0." });
                if (seg.StartMs >= seg.EndMs)
                    return Results.BadRequest(new { errorCode = "invalid_segment", message = $"Segment[{i}]: startMs must be less than endMs." });
                if (string.IsNullOrWhiteSpace(seg.Text))
                    return Results.BadRequest(new { errorCode = "invalid_segment", message = $"Segment[{i}]: text must not be empty." });
                if (string.IsNullOrWhiteSpace(seg.SpeakerId))
                    return Results.BadRequest(new { errorCode = "invalid_segment", message = $"Segment[{i}]: speakerId must not be empty." });
            }

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";

            extract.TranscriptSegmentsJson = System.Text.Json.JsonSerializer.Serialize(
                body.Segments,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                });

            paper.RowVersion++;

            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = DateTimeOffset.UtcNow,
                ActorId = adminId,
                ActorName = adminId,
                Action = "listening.transcript.updated",
                ResourceType = "ListeningExtract",
                ResourceId = extractId,
                Details = $"segmentCount={body.Segments.Count}",
            });

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { errorCode = "listening_paper_concurrent_update", message = "Another user modified this paper. Reload and retry." });
            }

            await db.Entry(paper).ReloadAsync(ct);
            SetETag(http, paper);

            var saved = System.Text.Json.JsonSerializer
                .Deserialize<List<TranscriptSegmentDto>>(
                    extract.TranscriptSegmentsJson,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    }) ?? [];

            return Results.Ok(new { extractId, segments = saved });
        })
        .WithName("ReplaceListeningExtractTranscript")
        .WithSummary("Replace transcript segments for a listening extract.");

        // TTS synthesis. Enqueues a ListeningTtsJob so the background worker
        // (ListeningTtsJobWorker) reads the extract's transcript segments and
        // synthesises audio asynchronously via whichever provider is registered
        // in Program.cs. The endpoint returns 202 Accepted with the job ID
        // immediately; the client polls
        // GET /v1/admin/papers/{id}/listening/extracts/{eid}/synthesize/{jobId}.
        group.MapPost("/extracts/{extractId}/synthesize", async (
            string paperId,
            string extractId,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            // H2: Published papers require AdminContentPublish for mutations
            var paper = await db.ContentPapers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paperId, ct);
            if (paper is null) return Results.NotFound();
            var forbidden = EnforcePublishGate(paper, http);
            if (forbidden is not null) return forbidden;

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";

            // Validate extract belongs to this paper via part → paper linkage.
            var extract = await db.ListeningExtracts
                .FirstOrDefaultAsync(e => e.Id == extractId, ct);
            if (extract is null)
                return Results.Json(
                    new { errorCode = "listening_extract_not_found", message = $"Extract {extractId} not found." },
                    statusCode: 404);

            var ttsPart = await db.ListeningParts.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == extract.ListeningPartId, ct);
            if (ttsPart is null || ttsPart.PaperId != paperId)
                return Results.NotFound(new { errorCode = "listening_extract_not_found", message = $"Extract {extractId} does not belong to paper {paperId}." });

            var job = new ListeningTtsJob
            {
                Id = Guid.NewGuid().ToString("N"),
                ExtractId = extractId,
                RequestedBy = adminId,
                Status = ListeningTtsJobStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.ListeningTtsJobs.Add(job);
            await db.SaveChangesAsync(ct);

            return Results.Accepted($"/v1/admin/papers/{paperId}/listening/extracts/{extractId}/synthesize/{job.Id}",
                new { jobId = job.Id, status = "queued", extractId });
        })
        .WithName("SynthesizeListeningExtract")
        .WithSummary("Enqueue background TTS synthesis for an extract. Returns 202 Accepted with jobId.");

        // Poll a TTS job status.
        group.MapGet("/extracts/{extractId}/synthesize/{jobId}", async (
            string paperId,
            string extractId,
            string jobId,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var job = await db.ListeningTtsJobs
                .FirstOrDefaultAsync(j => j.Id == jobId && j.ExtractId == extractId, ct);
            if (job is null)
                return Results.NotFound(new { errorCode = "tts_job_not_found" });

            return Results.Ok(new
            {
                jobId = job.Id,
                status = job.Status.ToString().ToLowerInvariant(),
                retryCount = job.RetryCount,
                error = job.ErrorMessage,
                updatedAt = job.UpdatedAt,
            });
        })
        .WithName("GetListeningTtsJobStatus")
        .WithSummary("Poll TTS synthesis job status.");

        // ─── Bulk validate (not scoped to a single paperId) ───────────────
        app.MapPost("/v1/admin/papers/listening/bulk-validate", async (
            BulkValidateRequest body,
            IListeningStructureService svc,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            if (body.PaperIds is null || body.PaperIds.Count == 0)
                return Results.BadRequest(new { errorCode = "empty_paper_ids", message = "At least one paperId is required." });
            if (body.PaperIds.Count > 50)
                return Results.BadRequest(new { errorCode = "too_many_paper_ids", message = "Maximum 50 paper IDs per request." });

            var papers = await db.ContentPapers.AsNoTracking()
                .Where(p => body.PaperIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);

            var results = new List<BulkValidateResult>(body.PaperIds.Count);
            foreach (var paperId in body.PaperIds)
            {
                if (!papers.TryGetValue(paperId, out var paper))
                {
                    results.Add(new BulkValidateResult(paperId, null, "Unknown", false, ["Paper not found"]));
                    continue;
                }

                try
                {
                    var report = await svc.ValidatePaperAsync(paperId, ct);
                    results.Add(new BulkValidateResult(
                        paperId,
                        paper.Title,
                        paper.Status.ToString(),
                        report.IsPublishReady,
                        report.Issues.Select(i => i.Message).ToList()));
                }
                catch
                {
                    results.Add(new BulkValidateResult(paperId, paper.Title, paper.Status.ToString(), false, ["Validation failed unexpectedly"]));
                }
            }

            var ready = results.Count(r => r.IsPublishReady);
            var response = new BulkValidateResponse(
                results,
                new BulkValidateSummary(results.Count, ready, results.Count - ready));
            return Results.Ok(response);
        })
        .RequireAuthorization("AdminContentRead")
        .WithName("BulkValidateListeningPapers")
        .WithSummary("Validate multiple papers for publish readiness.");

        // ─── WS4: Admin Sequence Builder ───────────────────────────────────
        //
        // Optional explicit exam-sequence (ordered FSM phases + per-phase
        // window durations) authored per paper. Consumed by the session FSM
        // when present; absent ⇒ derived canonical fallback (byte-identical to
        // the prior policy-only timing). Reuses the same auth / rate-limit /
        // publish-gate / If-Match helpers as the structure routes.
        var seqGroup = app.MapGroup("/v1/admin/papers/{paperId}/listening/sequence")
            .RequireAuthorization("AdminContentWrite")
            .RequireRateLimiting("PerUserWrite");

        seqGroup.MapGet("", async (
            string paperId,
            ListeningSequenceService svc,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var paper = await db.ContentPapers.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == paperId, ct);
            if (paper is null) return Results.NotFound();
            SetETag(http, paper);
            var sequence = await svc.GetAsync(paperId, ct);
            return Results.Ok(new { sequence, isAuthored = sequence is not null });
        });

        seqGroup.MapPut("", async (
            string paperId,
            ListeningSequence body,
            ListeningSequenceService svc,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (body?.Items is null)
                return Results.BadRequest(new { errorCode = "listening_sequence_missing_body", message = "Request body must contain an items array." });
            var paper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct);
            if (paper is null) return Results.NotFound();
            var forbidden = EnforcePublishGate(paper, http);
            if (forbidden is not null) return forbidden;
            var conflict = CheckIfMatch(http, paper);
            if (conflict is not null) return conflict;

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var report = await svc.ReplaceAsync(paperId, body, adminId, ct);
            await db.Entry(paper).ReloadAsync(ct);
            SetETag(http, paper);
            var sequence = await svc.GetAsync(paperId, ct);
            return Results.Ok(new { sequence, report });
        });

        // Returns the canonical sequence derived from the effective policy for
        // the requested mode (default Exam). Powers the "Reset to canonical"
        // affordance in the builder UI. Read-only — does not persist.
        seqGroup.MapPost("/derive", async (
            string paperId,
            string? mode,
            ListeningSequenceService svc,
            ListeningModePolicyResolver modes,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var paper = await db.ContentPapers.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == paperId, ct);
            if (paper is null) return Results.NotFound();

            var policyRow = await db.ListeningPolicies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == "global", ct);
            var policy = ListeningPolicyResolver.Resolve(policyRow, null);
            var attemptMode = Enum.TryParse<ListeningAttemptMode>(mode, ignoreCase: true, out var m)
                ? m
                : ListeningAttemptMode.Exam;
            var modePolicy = modes.For(attemptMode);

            var sequence = svc.DeriveFromPolicy(policy, modePolicy);
            return Results.Ok(new { sequence, mode = modePolicy.Mode });
        });

        // Validates a candidate sequence against the paper's authored structure
        // (1:1 phase mapping, audio-extract resolution, 42-question coverage)
        // without persisting it. Powers the live validation banner.
        seqGroup.MapPost("/validate", async (
            string paperId,
            ListeningSequence body,
            ListeningSequenceService svc,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            if (body?.Items is null)
                return Results.BadRequest(new { errorCode = "listening_sequence_missing_body", message = "Request body must contain an items array." });
            var paper = await db.ContentPapers.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == paperId, ct);
            if (paper is null) return Results.NotFound();
            var report = await svc.ValidateForPaperAsync(paperId, body, ct);
            return Results.Ok(report);
        });

        return app;
    }

    // ─── Bulk validate DTOs ──────────────────────────────────────────────────
    public sealed record BulkValidateRequest(IReadOnlyList<string> PaperIds);
    public sealed record BulkValidateResult(string PaperId, string? Title, string Status, bool IsPublishReady, IReadOnlyList<string> Issues);
    public sealed record BulkValidateSummary(int Total, int Ready, int NotReady);
    public sealed record BulkValidateResponse(IReadOnlyList<BulkValidateResult> Results, BulkValidateSummary Summary);
}
