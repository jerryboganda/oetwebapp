using System.Security.Claims;
using OetLearner.Api.Services.VideoLibrary;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Capture-protection / tamper telemetry ingest (Course Platform Security
/// Requirements §2). Deliberately separate from the video analytics events
/// endpoint (<c>POST /v1/video-library/events</c>) — see
/// <see cref="VideoProtectionEventService"/>'s class doc.
/// </summary>
public static class VideoProtectionEndpoints
{
    public static IEndpointRouteBuilder MapVideoProtectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/video-library")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapPost("/protection-events", async (
            HttpContext http,
            VideoProtectionEventBatchRequest request,
            VideoProtectionEventService service,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("Authenticated user id is required.");
            var platform = http.Request.Headers["X-OET-Client-Platform"].ToString();
            var ip = http.Connection.RemoteIpAddress?.ToString();

            var result = await service.RecordAsync(
                userId,
                string.IsNullOrWhiteSpace(platform) ? null : platform,
                ip,
                request.Events?.Select(e => new VideoProtectionEventInput(
                    e.VideoId, e.SessionId, e.Kind ?? string.Empty, e.OccurredAt, e.Metadata)).ToList(),
                ct);
            return Results.Ok(result);
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("RecordVideoProtectionEvents");

        return app;
    }
}

public sealed record VideoProtectionEventItemRequest(
    string? VideoId,
    string? SessionId,
    string? Kind,
    DateTimeOffset? OccurredAt,
    Dictionary<string, object?>? Metadata);

public sealed record VideoProtectionEventBatchRequest(List<VideoProtectionEventItemRequest>? Events);
