using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class ContentStalenessEndpoints
{
    public static IEndpointRouteBuilder MapContentStalenessEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        admin.MapGet("/content/staleness", async ([FromServices] IContentStalenessService service, CancellationToken ct, int? thresholdDays)
            => Results.Ok(await service.ComputeAllAsync(ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapGet("/content/{contentId}/staleness", async (string contentId, [FromServices] IContentStalenessService service, CancellationToken ct)
            => Results.Ok(await service.ComputeForContentAsync(contentId, ct)))
            .WithAdminRead("AdminContentRead");

        return app;
    }
}
