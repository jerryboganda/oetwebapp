using OetLearner.Api.Contracts;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// V2 pathway + today endpoints. GET /v1/writing/pathway and GET
/// /v1/writing/today collide with the legacy registrations in
/// WritingPathwayEndpoints. To preserve back-compat per the plan, V2 surfaces
/// those at the v2/ sub-prefix. Other V2-only routes
/// (/pathway/recalculate, /today/regenerate, /today/items/.../complete) keep
/// their natural paths since they don't conflict.
/// </summary>
public static class WritingPathwayV2Endpoints
{
    public static IEndpointRouteBuilder MapWritingPathwayV2Endpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapGet("/v2/pathway", async (
            HttpContext http,
            IWritingPathwayServiceV2 service,
            CancellationToken ct)
            => Results.Ok(await service.GetPathwayAsync(http.WritingV2UserId(), ct)))
            .WithName("GetWritingPathwayV2");

        group.MapPost("/pathway/recalculate", async (
            HttpContext http,
            IWritingPathwayServiceV2 service,
            CancellationToken ct)
            => Results.Ok(await service.RecalculatePathwayAsync(http.WritingV2UserId(), ct)))
            .RequireRateLimiting("PerUserWrite")
            .WithName("RecalculateWritingPathwayV2");

        group.MapGet("/v2/today", async (
            HttpContext http,
            IWritingDailyPlanServiceV2 service,
            CancellationToken ct)
            => Results.Ok(await service.GetTodayPlanAsync(http.WritingV2UserId(), ct)))
            .WithName("GetWritingTodayPlanV2");

        group.MapPost("/today/items/{id:guid}/complete", async (
            Guid id,
            HttpContext http,
            IWritingDailyPlanServiceV2 service,
            CancellationToken ct)
            => Results.Ok(await service.MarkItemCompleteAsync(http.WritingV2UserId(), id, ct)))
            .RequireRateLimiting("PerUserWrite")
            .WithName("CompleteWritingTodayItemV2");

        group.MapPost("/today/regenerate", async (
            HttpContext http,
            IWritingDailyPlanServiceV2 service,
            CancellationToken ct)
            => Results.Ok(await service.RegenerateTodayPlanAsync(http.WritingV2UserId(), ct)))
            .RequireRateLimiting("PerUserWrite")
            .WithName("RegenerateWritingTodayPlanV2");

        return app;
    }
}
