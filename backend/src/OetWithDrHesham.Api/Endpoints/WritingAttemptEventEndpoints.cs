using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using OetWithDrHesham.Api.Services.Writing;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// Batched Writing attempt-event ingestion (spec §17.7).
/// <c>POST /v1/writing/attempt-events</c> → <c>{ accepted: number }</c>.
/// Mirrors the learner auth + rate-limit policy used across Writing V2
/// endpoints (LearnerOnly + PerUser + http.WritingV2UserId()).
/// </summary>
public static class WritingAttemptEventEndpoints
{
    public static IEndpointRouteBuilder MapWritingAttemptEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing/attempt-events")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapPost("", async (
            WritingAttemptEventBatchRequest request,
            HttpContext http,
            IWritingAttemptEventService service,
            CancellationToken ct) =>
        {
            var inputs = (request.Events ?? new List<WritingAttemptEventDto>())
                .Select(e => new WritingAttemptEventInput(
                    e.EventType ?? string.Empty,
                    e.Timestamp,
                    e.Mode ?? "computer",
                    e.SessionId,
                    e.ScenarioId,
                    e.SubmissionId,
                    e.Payload.HasValue ? e.Payload.Value.GetRawText() : null))
                .ToList();

            var accepted = await service.RecordAsync(http.WritingV2UserId(), inputs, ct);
            return Results.Ok(new { accepted });
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("RecordWritingAttemptEvents");

        return app;
    }
}

/// <summary>Batch request body for Writing attempt events.</summary>
public sealed record WritingAttemptEventBatchRequest(List<WritingAttemptEventDto> Events);

/// <summary>Wire DTO for a single Writing attempt event (camelCase JSON).</summary>
public sealed record WritingAttemptEventDto(
    [property: Required] string? EventType,
    DateTimeOffset? Timestamp,
    string? Mode,
    string? SessionId,
    Guid? ScenarioId,
    Guid? SubmissionId,
    JsonElement? Payload);
