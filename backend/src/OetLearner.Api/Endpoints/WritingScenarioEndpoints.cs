using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

public static class WritingScenarioEndpoints
{
    public static IEndpointRouteBuilder MapWritingScenarioEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing/scenarios")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapGet("/", async (
            [FromQuery] string? profession,
            [FromQuery] string? letterType,
            [FromQuery] int? difficulty,
            [FromQuery] bool? isDiagnostic,
            [FromQuery] string? search,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            HttpContext http,
            IWritingScenarioService service,
            CancellationToken ct)
            => Results.Ok(await service.ListScenariosAsync(
                http.WritingV2UserId(),
                profession,
                letterType,
                difficulty,
                isDiagnostic,
                search,
                page ?? 1,
                pageSize ?? 20,
                ct)))
            .WithName("ListWritingScenarios");

        group.MapGet("/random", async (
            [FromQuery] string? profession,
            [FromQuery] string? letterType,
            HttpContext http,
            IWritingScenarioService service,
            CancellationToken ct) =>
        {
            var scenario = await service.GetRandomScenarioAsync(http.WritingV2UserId(), profession, letterType, ct);
            return scenario is null ? Results.NotFound() : Results.Ok(scenario);
        })
        .WithName("GetRandomWritingScenario");

        group.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingScenarioService service,
            CancellationToken ct) =>
        {
            var scenario = await service.GetScenarioAsync(http.WritingV2UserId(), id, ct);
            return scenario is null ? Results.NotFound() : Results.Ok(scenario);
        })
        .WithName("GetWritingScenario");

        return app;
    }
}
