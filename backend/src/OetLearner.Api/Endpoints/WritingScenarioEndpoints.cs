using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Services;
using OetLearner.Api.Services.FreeSamples;
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

        group.MapGet("/free", async (
            HttpContext http,
            LearnerDbContext db,
            IFreeTierContentResolver freeTierContent,
            IWritingScenarioService service,
            CancellationToken ct) =>
        {
            var userId = http.WritingV2UserId();
            var profession = await db.Users.AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => user.ActiveProfessionId)
                .FirstOrDefaultAsync(ct);
            var scenario = await freeTierContent.ResolveWritingScenarioAsync(profession, ct);
            if (scenario is null)
            {
                throw ApiException.Conflict(
                    "free_tier_content_unavailable",
                    "No eligible free Writing task is published for your profession yet.");
            }

            var response = await service.GetScenarioAsync(userId, scenario.Id, ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("GetFreeWritingScenario");

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
        group.MapGet("/{id:guid}/eligibility", CheckEligibilityAsync)
            .WithName("CheckWritingScenarioEligibility");

        return app;
    }

    internal static async Task<IResult> CheckEligibilityAsync(
        Guid id,
        HttpContext http,
        IWritingScenarioService scenarios,
        IWritingEntitlementService writingEntitlement,
        LearnerDbContext db,
        IFreeTierContentResolver freeTierContent,
        CancellationToken ct)
    {
        // Addendum Rev8 §16-§17: the task must exist, be published and be
        // able to render BEFORE any credit is debited — never charge a
        // learner for a task page that cannot load.
        await scenarios.EnsureCandidateStartableAsync(id, ct);

        var userId = http.WritingV2UserId();
        var entitlement = await writingEntitlement.CheckAsync(userId, ct);
        // The learner's own pinned free sample (FreeSampleService) is never
        // blocked by the free-tier featured-item rule — even if an admin moved
        // the designation after the learner claimed it.
        if (entitlement.Allowed && string.Equals(entitlement.Tier, "free", StringComparison.OrdinalIgnoreCase)
            && !await new FreeSampleService(db).IsOfferedAsync(userId, FreeSampleService.Writing, id.ToString("D"), ct))
        {
            var profession = await db.Users.AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => user.ActiveProfessionId)
                .FirstOrDefaultAsync(ct);
            var featured = await freeTierContent.ResolveWritingScenarioAsync(profession, ct);
            if (featured is null)
            {
                throw ApiException.Conflict(
                    "free_tier_content_unavailable",
                    "No eligible free Writing task is published for your profession yet.");
            }
            if (featured.Id != id)
            {
                throw ApiException.PaymentRequired(
                    "free_tier_featured_item_only",
                    "Free Writing access is limited to the featured case-note task for your profession.");
            }
        }
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
    }
}
