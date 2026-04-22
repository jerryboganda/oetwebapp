using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin endpoints for the Listening publish-gate validator.
///
/// Mirrors the Reading pattern (<c>ReadingAuthoringAdminEndpoints</c>): lets
/// admins preview whether a Listening <c>ContentPaper</c> satisfies the
/// canonical OET shape (Part A = 24, Part B = 6, Part C = 12 → 42 items)
/// before attempting to publish. Publish itself runs the same validator from
/// <c>ContentPaperService.PublishAsync</c>.
/// </summary>
public static class ListeningAuthoringAdminEndpoints
{
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

        return app;
    }
}
