using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Writing Module V2 — Buddy System endpoints (spec §23.5).
/// Seven routes under <c>/v1/writing/buddy</c>; opt-in, match,
/// fetch the active pair, chat, weekly check-in, end pair.
/// </summary>
public static class WritingBuddyEndpoints
{
    public static IEndpointRouteBuilder MapWritingBuddyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing/buddy")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapPost("/opt-in", async (
            HttpContext http,
            IWritingBuddyService service,
            CancellationToken ct)
            => Results.Ok(await service.OptInAsync(http.WritingV2UserId(), ct)))
            .WithName("WritingBuddyOptIn");

        group.MapPost("/match", async (
            HttpContext http,
            IWritingBuddyService service,
            CancellationToken ct)
            => Results.Ok(await service.RequestMatchAsync(http.WritingV2UserId(), ct)))
            .WithName("WritingBuddyRequestMatch");

        group.MapGet("/pair", async (
            HttpContext http,
            IWritingBuddyService service,
            CancellationToken ct) =>
        {
            var pair = await service.GetActivePairAsync(http.WritingV2UserId(), ct);
            return pair is null ? Results.NoContent() : Results.Ok(pair);
        })
        .WithName("WritingBuddyGetActivePair");

        group.MapPost("/pair/{pairId:guid}/messages", async (
            Guid pairId,
            [FromBody] WritingBuddyMessageCreateRequest request,
            HttpContext http,
            IWritingBuddyService service,
            CancellationToken ct)
            => Results.Ok(await service.SendMessageAsync(http.WritingV2UserId(), pairId, request?.Body ?? string.Empty, ct)))
            .WithName("WritingBuddySendMessage");

        group.MapGet("/pair/{pairId:guid}/messages", async (
            Guid pairId,
            [FromQuery] int? take,
            HttpContext http,
            IWritingBuddyService service,
            CancellationToken ct)
            => Results.Ok(await service.GetMessagesAsync(http.WritingV2UserId(), pairId, take ?? 50, ct)))
            .WithName("WritingBuddyListMessages");

        group.MapPost("/pair/{pairId:guid}/check-in", async (
            Guid pairId,
            [FromBody] WritingBuddyCheckInRequest request,
            HttpContext http,
            IWritingBuddyService service,
            CancellationToken ct)
            => Results.Ok(await service.SubmitWeeklyCheckInAsync(http.WritingV2UserId(), pairId, request?.Report ?? "{}", ct)))
            .WithName("WritingBuddyCheckIn");

        group.MapPost("/pair/{pairId:guid}/end", async (
            Guid pairId,
            [FromBody] WritingBuddyEndPairRequest request,
            HttpContext http,
            IWritingBuddyService service,
            CancellationToken ct)
            => Results.Ok(new { ended = await service.EndPairAsync(http.WritingV2UserId(), pairId, request?.Reason ?? "user_ended", ct) }))
            .WithName("WritingBuddyEndPair");

        return app;
    }
}

public sealed record WritingBuddyMessageCreateRequest(string Body);
public sealed record WritingBuddyCheckInRequest(string Report);
public sealed record WritingBuddyEndPairRequest(string Reason);
