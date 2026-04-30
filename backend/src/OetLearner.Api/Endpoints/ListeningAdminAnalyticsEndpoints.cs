using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Phase 7 of LISTENING-MODULE-PLAN.md — class-wide Listening analytics for
/// admins. Mirrors the Reading admin analytics surface and is gated by the
/// <c>AdminContentRead</c> policy. Returns per-part class averages, hardest
/// questions, distractor heat, and common misspellings over the requested
/// rolling window (default 30 days, max 365).
/// </summary>
public static class ListeningAdminAnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapListeningAdminAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/listening")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUser");

        group.MapGet("/analytics", async (
            int? days,
            IListeningAnalyticsService svc,
            CancellationToken ct) =>
        {
            var data = await svc.GetAdminAnalyticsAsync(days ?? 30, ct);
            return Results.Ok(data);
        })
            .WithName("GetListeningAdminAnalytics")
            .WithSummary("Class-wide Listening analytics over a rolling window");

        return app;
    }
}
