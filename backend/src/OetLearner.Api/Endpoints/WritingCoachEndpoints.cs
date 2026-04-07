using System.Security.Claims;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

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
