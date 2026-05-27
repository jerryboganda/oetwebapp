using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

public static class WritingExemplarEndpoints
{
    public static IEndpointRouteBuilder MapWritingExemplarEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing/exemplars")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapGet("/", async (
            [FromQuery] string? profession,
            [FromQuery] string? letterType,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            HttpContext http,
            IWritingExemplarService service,
            CancellationToken ct)
            => Results.Ok(await service.ListExemplarsAsync(
                http.WritingV2UserId(),
                profession,
                letterType,
                page ?? 1,
                pageSize ?? 20,
                ct)))
            .WithName("ListWritingExemplars");

        group.MapGet("/closest-to/{scenarioId:guid}", async (
            Guid scenarioId,
            HttpContext http,
            IWritingExemplarService service,
            CancellationToken ct) =>
        {
            var exemplar = await service.GetClosestExemplarForScenarioAsync(http.WritingV2UserId(), scenarioId, ct);
            return exemplar is null ? Results.NotFound() : Results.Ok(exemplar);
        })
        .WithName("GetClosestWritingExemplar");

        group.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingExemplarService service,
            CancellationToken ct) =>
        {
            var exemplar = await service.GetExemplarAsync(http.WritingV2UserId(), id, ct);
            return exemplar is null ? Results.NotFound() : Results.Ok(exemplar);
        })
        .WithName("GetWritingExemplar");

        return app;
    }
}
