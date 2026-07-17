using System.Security.Claims;
using OetWithDrHesham.Api.Services.Content;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// Learner-facing read endpoint for the Materials library.
/// Returns the candidate's pruned visible tree, with download URLs pointing at
/// the existing authenticated media-download endpoint.
/// </summary>
public static class MaterialsLearnerEndpoints
{
    public static IEndpointRouteBuilder MapMaterialsLearnerEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/v1/materials")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        learner.MapGet("", async (
            HttpContext http,
            MaterialAccessService access,
            CancellationToken ct) =>
        {
            var tree = await access.GetVisibleTreeAsync(http.User, ct);
            return Results.Ok(new { folders = tree });
        });

        return app;
    }
}
