using System.Security.Claims;
using OetLearner.Api.Services.Mocks;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Learner-facing endpoints exposing aggregate mock readiness signals.
///
/// Phase 3 of the OET Mocks Module — surfaces the "Green across 2+ mocks"
/// trend computed by <see cref="MockReadinessTrendService"/>.
/// </summary>
public static class MockReadinessEndpoints
{
    public static IEndpointRouteBuilder MapMockReadinessEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/learner/me/readiness")
            .RequireAuthorization("LearnerOnly")
            .WithTags("Learner Mock Readiness");

        group.MapGet("/trend", async (
            HttpContext http,
            MockReadinessTrendService service,
            CancellationToken ct) =>
        {
            var result = await service.ComputeAsync(UserId(http), ct);
            return Results.Ok(new
            {
                attemptsConsidered = result.AttemptsConsidered,
                overallTrend = result.OverallTrend,
                consistentGreen = result.ConsistentGreen,
                message = result.Message
            });
        });

        return app;
    }

    private static string UserId(HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
