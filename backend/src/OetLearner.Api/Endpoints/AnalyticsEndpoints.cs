using System.Security.Claims;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var analytics = app.MapGroup("/v1/analytics")
            .RequireAuthorization()
            .RequireRateLimiting("PerUserWrite");

        analytics.MapPost("/events", async (HttpContext http, AnalyticsTrackRequest request, AnalyticsIngestionService service, CancellationToken ct) =>
        {
            await service.RecordAsync(http.UserId(), request, ct);
            return Results.NoContent();
        });

        return app;
    }

    private static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}