using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class LearnerActionsEndpoints
{
    public static IEndpointRouteBuilder MapLearnerActionsEndpoints(this IEndpointRouteBuilder app)
    {
        var actions = app.MapGroup("/v1/learner/actions")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        // Next-action engine
        actions.MapGet("/next", async (HttpContext http, LearnerActionsService service, CancellationToken ct)
            => Results.Ok(await service.GetNextActionsAsync(http.UserId(), ct)));

        // Readiness blockers
        actions.MapGet("/readiness/blockers", async (HttpContext http, LearnerActionsService service, CancellationToken ct)
            => Results.Ok(await service.GetReadinessBlockersAsync(http.UserId(), ct)));

        // Progress trend
        actions.MapGet("/progress/trend", async (HttpContext http, LearnerActionsService service, CancellationToken ct)
            => Results.Ok(await service.GetProgressTrendAsync(http.UserId(), ct)));

        return app;
    }

    private static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
