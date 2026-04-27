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
/// All routes require <c>AdminContentWrite</c>. Until the relational
/// <c>ListeningPart</c>/<c>ListeningQuestion</c> tables ship (Phase 2), the
/// authored question list is persisted under <c>ContentPaper.ExtractedTextJson["listeningQuestions"]</c> —
/// which is exactly what <c>ListeningLearnerService.ExtractQuestions</c> reads
/// at runtime, so authoring through this endpoint immediately drives the
/// learner player + grader.
/// </summary>
public static class ListeningAuthoringAdminEndpoints
{
    public sealed record ReplaceStructureBody(IReadOnlyList<ListeningAuthoredQuestion> Questions);

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

        return app;
    }
}
