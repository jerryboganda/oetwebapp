using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services;
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
        // Routes through WritingEntitlementService.AuthorizeStartAsync — the
        // SAME canonical entitlement decision as GET /v1/writing/entitlement
        // (Dashboard) and Submit-for-Grading — instead of calling
        // AiPackageCreditService directly, so a learner whose valid entitlement
        // comes from the free-tier window (not an AI package/subscription) is
        // never blocked here while every other surface shows them as allowed
        // (Writing Rule Enforcement Addendum Rev5, 10 Sep 2026, §12). Debit
        // (when one applies) happens once at session start, idempotent on the
        // scenario id, so a refresh cannot charge again. Submit must not debit.
        group.MapGet("/{id:guid}/eligibility", async (
            Guid id,
            HttpContext http,
            IWritingEntitlementService writingEntitlement,
            CancellationToken ct) =>
        {
            var userId = http.WritingV2UserId();
            // §12.4: the reference id must advance once this attempt's
            // submission is actually graded, so "Practice this again" is
            // billed as the genuinely new attempt it is — see
            // BuildScenarioStartReferenceIdAsync.
            var referenceId = await writingEntitlement.BuildScenarioStartReferenceIdAsync(userId, id, ct);
            var result = await writingEntitlement.AuthorizeStartAsync(
                userId,
                referenceId,
                id.ToString("D"),
                ct);
            if (!result.Allowed)
            {
                throw ApiException.PaymentRequired(
                    result.ErrorCode ?? "no_ai_package_credits",
                    result.ErrorMessage ?? "You do not have enough credits to start this activity. Please purchase another package or upgrade your plan.");
            }
            return Results.Ok(new { feedbackMessage = result.FeedbackMessage });
        })
        .WithName("CheckWritingScenarioEligibility");

        return app;
    }
}
