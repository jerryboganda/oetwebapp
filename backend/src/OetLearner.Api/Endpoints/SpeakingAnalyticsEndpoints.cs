using System.Security.Claims;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Endpoints;

// Phase 6 of the OET Speaking module roadmap.
//
// Surfaces SpeakingAnalyticsService for the three dashboards:
//   * Learner:  GET /v1/speaking/analytics/me
//   * Teacher:  GET /v1/expert/speaking/analytics/class
//               GET /v1/expert/speaking/analytics/tutor-consistency
//   * Admin:    GET /v1/admin/speaking/analytics/content-difficulty
//
// All four routes are read-only and rely on the service's in-memory
// 5-minute cache to keep the dashboards responsive.
public static class SpeakingAnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapSpeakingAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Learner ────────────────────────────────────────────────────────
        var learner = app.MapGroup("/v1/speaking/analytics")
            .RequireAuthorization()
            .RequireRateLimiting("PerUser");

        learner.MapGet("/me", async (
            HttpContext http,
            SpeakingAnalyticsService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.GetLearnerAnalyticsAsync(http.UserId(), ct)));

        // ── Teacher / Expert ───────────────────────────────────────────────
        var teacher = app.MapGroup("/v1/expert/speaking/analytics")
            .RequireAuthorization("TeachingStaffOnly")
            .RequireRateLimiting("PerUser");

        teacher.MapGet("/class", async (
            string? cohortId,
            string? professionId,
            SpeakingAnalyticsService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.GetClassAnalyticsAsync(cohortId, professionId, ct)));

        teacher.MapGet("/tutor-consistency", async (
            HttpContext http,
            string? tutorId,
            SpeakingAnalyticsService svc,
            CancellationToken ct) =>
        {
            // If no tutorId is supplied, fall back to the caller's own id
            // so tutors can see their own consistency report.
            var effectiveTutorId = string.IsNullOrWhiteSpace(tutorId)
                ? http.UserId()
                : tutorId;
            return Results.Ok(await svc.GetTutorConsistencyAsync(effectiveTutorId, ct));
        });

        // ── Admin ──────────────────────────────────────────────────────────
        var admin = app.MapGroup("/v1/admin/speaking/analytics")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUser");

        admin.MapGet("/content-difficulty", async (
            string? professionId,
            SpeakingAnalyticsService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.GetContentDifficultyAsync(professionId, ct)));

        return app;
    }
}

file static class SpeakingAnalyticsHttpContextExtensions
{
    internal static string UserId(this HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
