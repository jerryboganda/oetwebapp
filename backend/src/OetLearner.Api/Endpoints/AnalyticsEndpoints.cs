using System.Security.Claims;
using System.Text.Json;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class AnalyticsEndpoints
{
    private static readonly JsonSerializerOptions AnalyticsJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var analytics = app.MapGroup("/v1/analytics")
            .RequireAuthorization()
            .RequireRateLimiting("PerUserWrite");

        analytics.MapPost("/events", async (
            HttpContext http,
            AnalyticsIngestionService service,
            CancellationToken ct) =>
        {
            string body;
            using (var reader = new StreamReader(http.Request.Body))
            {
                body = await reader.ReadToEndAsync(ct);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return Results.NoContent();
            }

            try
            {
                var request = JsonSerializer.Deserialize<AnalyticsTrackRequest>(body, AnalyticsJsonOptions);
                if (request is null)
                {
                    return Results.NoContent();
                }

                await service.RecordAsync(http.UserId(), request, ct);
                return Results.NoContent();
            }
            catch (JsonException)
            {
                return Results.NoContent();
            }
        });

        return app;
    }

    private static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
