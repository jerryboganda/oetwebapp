using System.Security.Claims;
using OetLearner.Api.Services.Listening;

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

        // Phase 8: AI-assisted structure proposal. Today this delegates to the
        // deterministic `StubListeningExtractionAi` so admins always get a
        // 24/6/12 placeholder. A grounded gateway impl plugs in via DI.
        group.MapPost("/extract", async (
            string paperId,
            IListeningExtractionService svc,
            CancellationToken ct) =>
        {
            var draft = await svc.ProposeStructureAsync(paperId, ct);
            return Results.Ok(draft);
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
