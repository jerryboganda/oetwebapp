using System.Security.Claims;
using System.Text.Json;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class AnalyticsEndpoints
{
    private const int MaxAnalyticsBodyBytes = 16 * 1024;
    // A batch carries several single-event bodies, so it gets a proportionally larger body cap.
    private const int MaxAnalyticsBatchBodyBytes = 64 * 1024;
    private const int MaxAnalyticsBatchEvents = 50;
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
            if (http.Request.ContentLength > MaxAnalyticsBodyBytes)
            {
                return Results.NoContent();
            }

            string body;
            using (var reader = new StreamReader(http.Request.Body))
            {
                body = await reader.ReadToEndAsync(ct);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return Results.NoContent();
            }
            if (System.Text.Encoding.UTF8.GetByteCount(body) > MaxAnalyticsBodyBytes)
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

        // Several events in one request. Same tolerance as the single-event route: an empty,
        // malformed or oversized body is acknowledged and ignored (telemetry must never fail the
        // page), and an invalid entry is skipped without costing the others.
        analytics.MapPost("/events/batch", async (
            HttpContext http,
            AnalyticsIngestionService service,
            CancellationToken ct) =>
        {
            if (http.Request.ContentLength > MaxAnalyticsBatchBodyBytes)
            {
                return Results.NoContent();
            }

            string body;
            using (var reader = new StreamReader(http.Request.Body))
            {
                body = await reader.ReadToEndAsync(ct);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return Results.NoContent();
            }
            if (System.Text.Encoding.UTF8.GetByteCount(body) > MaxAnalyticsBatchBodyBytes)
            {
                return Results.NoContent();
            }

            try
            {
                var request = JsonSerializer.Deserialize<AnalyticsTrackBatchRequest>(body, AnalyticsJsonOptions);
                var events = request?.Events;
                if (events is null || events.Count == 0)
                {
                    return Results.NoContent();
                }

                await service.RecordBatchAsync(http.UserId(), events.Take(MaxAnalyticsBatchEvents).ToList(), ct);
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
