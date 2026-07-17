using System.Security.Claims;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Writing;

namespace OetWithDrHesham.Api.Endpoints;

public static class WritingCoachEndpoints
{
    public static IEndpointRouteBuilder MapWritingCoachEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");
        var coach = v1.MapGroup("/writing");

        coach.MapPost("/attempts/{attemptId}/coach-check", async (string attemptId, HttpContext http, WritingCoachCheckRequest request, WritingCoachService svc, CancellationToken ct) =>
            Results.Ok(await svc.CheckTextAsync(http.UserId(), attemptId, request, ct)));

        coach.MapPost("/coach-suggestions/{id}/resolve", async (Guid id, HttpContext http, WritingCoachResolveRequest request, WritingCoachService svc, CancellationToken ct) =>
            Results.Ok(await svc.ResolveSuggestionAsync(http.UserId(), id, request.Resolution, ct)));

        coach.MapGet("/attempts/{attemptId}/coach-stats", async (string attemptId, HttpContext http, WritingCoachService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetStatsAsync(http.UserId(), attemptId, ct)));

        // Spec §14 — learner weakness analytics. Returns the pre-aggregated
        // WeaknessSummary shape consumed by app/writing/analytics/page.tsx.
        coach.MapGet("/analytics/weaknesses", async (
            HttpContext http,
            WritingWeaknessAnalyticsService svc,
            CancellationToken ct,
            int? days) =>
            Results.Ok(await svc.ComputeForLearnerAsync(http.UserId(), days ?? 14, ct)));

        // Spec §12.E — dual AI + Tutor assessment. AI track is always present
        // once the evaluation completes; tutor track populates once an expert
        // submits a review draft. Returns 404 if the evaluation is missing or
        // the caller doesn't own the underlying attempt (IDOR guard).
        coach.MapGet("/evaluations/{evaluationId}/dual-assessment", async (
            string evaluationId,
            HttpContext http,
            WritingDualAssessmentService svc,
            CancellationToken ct) =>
        {
            var result = await svc.GetAsync(http.UserId(), evaluationId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        return app;
    }
}

public record WritingCoachResolveRequest(string Resolution);

file static class WritingCoachHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
