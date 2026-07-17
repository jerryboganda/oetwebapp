using System.Security.Claims;
using OetWithDrHesham.Api.Services.Speaking;

namespace OetWithDrHesham.Api.Endpoints;

// Phase 6 (16-stage pathway, plan section 16) of the OET Speaking
// module roadmap.
//
// Single learner-facing read endpoint that returns the 16 stages plus
// the learner's progress through them.
public static class SpeakingCoursePathwayEndpoints
{
    public static IEndpointRouteBuilder MapSpeakingCoursePathwayEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/v1/speaking/course-pathway")
            .RequireAuthorization()
            .RequireRateLimiting("PerUser");

        learner.MapGet("", async (
            HttpContext http,
            SpeakingCoursePathwayService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.GetForLearnerAsync(http.UserId(), ct)));

        return app;
    }
}

file static class SpeakingCoursePathwayHttpContextExtensions
{
    internal static string UserId(this HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
