using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services.Billing;
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

        // Gate for AI-graded practice/paper sessions (NOT mock sessions —
        // mocks never touch the AI grading credit pool, see WritingMockService).
        // Read-only: does not consume a credit. The real debit still happens
        // once, at submit, via AiPackageCreditService.DeductGradingCreditAsync.
        group.MapGet("/{id:guid}/eligibility", async (
            Guid id,
            HttpContext http,
            IAiPackageCreditService aiPackageCreditService,
            CancellationToken ct) =>
        {
            // A Writing exam costs two credits (AiGradingCreditCost.WritingExam),
            // so the start gate must confirm both are available — otherwise a
            // learner with exactly one credit passes here and fails at submit.
            var result = await aiPackageCreditService.CheckGradingCreditAsync(
                http.WritingV2UserId(), "writing", AiGradingCreditCost.WritingExam, ct);
            result.EnsureDebited();
            return Results.NoContent();
        })
        .WithName("CheckWritingScenarioEligibility");

        return app;
    }
}
