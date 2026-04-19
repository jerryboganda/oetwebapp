using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OetLearner.Api.Services.Progress;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Learner-facing endpoints for the Progress v2 dashboard. The v1
/// <c>/v1/progress</c> route is kept for backward compatibility and now
/// delegates to the same service (see <c>LearnerEndpoints</c> wiring).
/// Everything new lives under <c>/v1/progress/v2</c>.
/// </summary>
public static class ProgressLearnerEndpoints
{
    public static IEndpointRouteBuilder MapProgressLearnerEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/progress")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        g.MapGet("/v2", async (
            IProgressService svc, HttpContext http, string? range, CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var payload = await svc.GetProgressAsync(userId, range ?? "90d", ct);

            var etag = payload.Freshness.ETag;
            http.Response.Headers.ETag = etag;

            if (http.Request.Headers.IfNoneMatch.Contains(etag))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }
            return Results.Ok(payload);
        });

        g.MapGet("/v2/comparative", async (
            IProgressService svc, HttpContext http, CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var comparative = await svc.GetComparativeAsync(userId, ct);
            return Results.Ok(comparative);
        });

        g.MapGet("/v2/export.pdf", async (
            IProgressService svc, HttpContext http, CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var policy = await svc.GetPolicyAsync("oet", ct);
            if (!policy.ExportPdfEnabled)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var payload = await svc.GetProgressAsync(userId, "90d", ct);
            var bytes = ProgressPdfRenderer.Render(payload);
            return Results.File(bytes, "application/pdf", fileDownloadName: "progress.pdf");
        });

        return app;
    }

    private static string RequireUserId(HttpContext http)
    {
        var id = http.User.FindFirstValue("user_id")
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(id))
            throw new UnauthorizedAccessException("learner context required");
        return id;
    }
}
