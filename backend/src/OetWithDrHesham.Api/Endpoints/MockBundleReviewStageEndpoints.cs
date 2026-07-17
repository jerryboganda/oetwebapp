using System.Security.Claims;
using OetWithDrHesham.Api.Services.Mocks;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// Mocks Module Phase 3.3 — admin endpoints driving the multi-stage editorial
/// review state machine. Routes live under <c>/v1/admin/mocks/bundles/{bundleId}/review-stage</c>
/// and are gated by the <c>AdminContentPublish</c> policy (same gate used by
/// every other publish-related admin operation).
/// </summary>
public static class MockBundleReviewStageEndpoints
{
    public static IEndpointRouteBuilder MapMockBundleReviewStageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/mocks/bundles/{bundleId}/review-stage")
            .RequireAuthorization("AdminContentPublish")
            .RequireRateLimiting("PerUser")
            .WithTags("Admin Mock Review Stage");

        group.MapPost("/advance", async (
            string bundleId,
            MockBundleReviewStageAdvanceRequest request,
            MockBundleReviewStageService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            var adminId = AdminId(http);
            await service.AdvanceStageAsync(bundleId, request.NextStage, adminId, request.Notes, ct);
            var summary = await service.GetSummaryAsync(bundleId, ct);
            return Results.Ok(summary);
        }).RequireRateLimiting("PerUserWrite");

        group.MapGet("/summary", async (
            string bundleId,
            MockBundleReviewStageService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetSummaryAsync(bundleId, ct)));

        return app;
    }

    private static string AdminId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.FindFirstValue("sub")
            ?? "system";
}

/// <summary>
/// Request body for <c>POST /v1/admin/mocks/bundles/{bundleId}/review-stage/advance</c>.
/// </summary>
public sealed record MockBundleReviewStageAdvanceRequest(string NextStage, string? Notes);
