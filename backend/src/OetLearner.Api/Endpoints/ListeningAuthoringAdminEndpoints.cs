using System.Security.Claims;
using OetLearner.Api.Domain;
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
    public sealed record ApproveDraftBody(string? Reason);
    public sealed record RejectDraftBody(string? Reason);

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
            CancellationToken ct) =>
        {
            var doc = await svc.GetStructureAsync(paperId, ct);
            return Results.Ok(doc);
        });

        group.MapPut("/structure", async (
            string paperId,
            ReplaceStructureBody body,
            IListeningAuthoringService svc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var doc = await svc.ReplaceStructureAsync(paperId, body.Questions ?? [], adminId, ct);
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
            HttpContext http,
            CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var doc = await svc.PatchQuestionAsync(paperId, questionId, body ?? new(), adminId, ct);
            return Results.Ok(doc);
        });

        // Phase 8: AI-assisted structure proposal. Persists the AI gateway
        // result as a Pending ListeningExtractionDraft and returns the same
        // payload shape the admin UI already consumes plus draftId/status so
        // the new approve/reject flow can take over.
        group.MapPost("/extract", async (
            string paperId,
            IListeningExtractionDraftService drafts,
            HttpContext http,
            CancellationToken ct) =>
        {
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
            CancellationToken ct) =>
        {
            var doc = await svc.GetExtractsAsync(paperId, ct);
            return Results.Ok(new { extracts = doc });
        });

        group.MapPut("/extracts", async (
            string paperId,
            ReplaceExtractsBody body,
            IListeningAuthoringService svc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var doc = await svc.ReplaceExtractsAsync(paperId, body.Extracts ?? [], adminId, ct);
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
            HttpContext http,
            CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var doc = await svc.PatchExtractAsync(paperId, extractCode, body ?? new(), adminId, ct);
            return Results.Ok(new { extracts = doc });
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
            HttpContext http,
            CancellationToken ct) =>
        {
            var existing = await svc.GetAsync(draftId, ct);
            if (existing is null || existing.PaperId != paperId) return Results.NotFound();
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var draft = await svc.ApproveAsync(draftId, adminId, body?.Reason, ct);
            return Results.Ok(draft);
        });

        group.MapPost("/extractions/{draftId}/reject", async (
            string paperId,
            string draftId,
            RejectDraftBody body,
            IListeningExtractionDraftService svc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var existing = await svc.GetAsync(draftId, ct);
            if (existing is null || existing.PaperId != paperId) return Results.NotFound();
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var draft = await svc.RejectAsync(draftId, adminId, body?.Reason ?? string.Empty, ct);
            return Results.Ok(draft);
        });

        // Phase 2 follow-up: project the JSON-blob authored shape into the
        // relational ListeningPart / Extract / Question / Option entities.
        // Idempotent — wipes existing relational rows for the paper before
        // re-inserting. Authored learner attempts read those relational rows
        // when present, with JSON fallback for not-yet-backfilled content.
        group.MapPost("/backfill", async (
            string paperId,
            IListeningBackfillService svc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var report = await svc.BackfillPaperAsync(paperId, adminId, ct);
            return Results.Ok(report);
        });

        return app;
    }
}
