using System.Security.Claims;
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

        // Phase 2 follow-up: bulk JSON→relational backfill across every
        // Listening paper. Gated separately under AdminContentWrite +
        // PerUserWrite because it mutates the relational tables.
        var write = app.MapGroup("/v1/admin/listening")
            .RequireAuthorization("AdminContentWrite")
            .RequireRateLimiting("PerUserWrite");

        write.MapPost("/backfill", async (
            IListeningBackfillService svc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var reports = await svc.BackfillAllAsync(adminId, ct);
            return Results.Ok(new
            {
                count = reports.Count,
                successCount = reports.Count(r => r.Success),
                reports,
            });
        })
            .WithName("BackfillListeningRelationalAll")
            .WithSummary("Project the JSON blob into ListeningPart/Extract/Question/Option for every Listening paper");

        return app;
    }
}
