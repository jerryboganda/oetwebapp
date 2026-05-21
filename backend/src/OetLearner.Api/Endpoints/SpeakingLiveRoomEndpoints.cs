using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// HTTP surface for the Phase 3 live-tutor room workflow.
///
/// All routes require an authenticated learner-or-tutor user (the
/// <c>RulebookReader</c> policy is reused because it allows learners,
/// experts, and admins). Per-route business logic in
/// <see cref="SpeakingLiveRoomService"/> performs the finer-grained
/// owner / assigned-tutor checks.
///
/// Wire-up: <c>Program.cs</c> (owned by the wiring agent) calls
/// <see cref="MapSpeakingLiveRoomEndpoints"/>. This file deliberately
/// avoids touching Program.cs per the Phase 3 plan.
/// </summary>
public static class SpeakingLiveRoomEndpoints
{
    public static IEndpointRouteBuilder MapSpeakingLiveRoomEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/speaking/live-rooms")
            .RequireAuthorization("RulebookReader")
            .WithTags("Speaking live rooms");

        group.MapPost("/", CreateRoomAsync)
            .WithSummary("Create a LiveKit room for a Speaking session (learner-initiated).")
            .Produces<SpeakingLiveRoomCreatePayload>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id}/tokens", IssueTokenAsync)
            .WithSummary("Mint a short-lived access token for the calling user against this room.")
            .Produces<SpeakingLiveRoomTokenPayload>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id}/start-recording", StartRecordingAsync)
            .WithSummary("Start track-composite egress on the room.")
            .Produces<SpeakingLiveRoomRecordingPayload>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id}/stop-recording", StopRecordingAsync)
            .WithSummary("Stop the active egress on the room.")
            .Produces<SpeakingLiveRoomStopRecordingPayload>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id}/end", EndRoomAsync)
            .WithSummary("End the room (owner or assigned tutor only).")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    // ─────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────

    private static async Task<IResult> CreateRoomAsync(
        HttpContext http,
        [FromBody] SpeakingLiveRoomCreateRequest? request,
        SpeakingLiveRoomService service,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.SpeakingSessionId))
        {
            return Results.BadRequest(new
            {
                errorCode = "speaking_session_id_required",
                message = "speakingSessionId is required.",
            });
        }

        var userId = http.LiveRoomUserId();

        try
        {
            var result = await service.CreateRoomForSessionAsync(userId, request.SpeakingSessionId, ct);
            var payload = new SpeakingLiveRoomCreatePayload(
                LiveRoomId: result.LiveRoomId,
                LivekitWssUrl: result.LivekitWssUrl,
                RoomName: result.RoomName);
            return Results.Created($"/v1/speaking/live-rooms/{result.LiveRoomId}", payload);
        }
        catch (SpeakingLiveRoomNotFoundException ex)
        {
            return Results.NotFound(new { errorCode = "session_not_found", message = ex.Message });
        }
        catch (SpeakingLiveRoomForbiddenException ex)
        {
            return Results.Json(
                new { errorCode = "forbidden", message = ex.Message },
                statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> IssueTokenAsync(
        string id,
        HttpContext http,
        [FromBody] SpeakingLiveRoomTokenRequest? request,
        SpeakingLiveRoomService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Results.BadRequest(new { errorCode = "live_room_id_required", message = "live room id is required." });
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Role))
        {
            return Results.BadRequest(new { errorCode = "role_required", message = "role is required." });
        }

        var userId = http.LiveRoomUserId();

        try
        {
            var result = await service.IssueTokenAsync(userId, id, request.Role, ct);
            var capPayload = new SpeakingLiveRoomCapabilitiesPayload(
                result.Capabilities.CanPublishAudio,
                result.Capabilities.CanPublishVideo,
                result.Capabilities.CanSubscribe);
            return Results.Ok(new SpeakingLiveRoomTokenPayload(
                Token: result.Token,
                ExpiresAt: result.ExpiresAt,
                Capabilities: capPayload));
        }
        catch (SpeakingLiveRoomNotFoundException ex)
        {
            return Results.NotFound(new { errorCode = "live_room_not_found", message = ex.Message });
        }
        catch (SpeakingLiveRoomForbiddenException ex)
        {
            return Results.Json(
                new { errorCode = "forbidden", message = ex.Message },
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (SpeakingLiveRoomInvalidStateException ex)
        {
            return Results.BadRequest(new { errorCode = "invalid_state", message = ex.Message });
        }
    }

    private static async Task<IResult> StartRecordingAsync(
        string id,
        SpeakingLiveRoomService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Results.BadRequest(new { errorCode = "live_room_id_required", message = "live room id is required." });
        }

        try
        {
            var result = await service.StartRecordingAsync(id, ct);
            return Results.Ok(new SpeakingLiveRoomRecordingPayload(result.EgressId, result.OutputUrl));
        }
        catch (SpeakingLiveRoomNotFoundException ex)
        {
            return Results.NotFound(new { errorCode = "live_room_not_found", message = ex.Message });
        }
    }

    private static async Task<IResult> StopRecordingAsync(
        string id,
        SpeakingLiveRoomService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Results.BadRequest(new { errorCode = "live_room_id_required", message = "live room id is required." });
        }

        try
        {
            var stopped = await service.StopRecordingAsync(id, ct);
            return Results.Ok(new SpeakingLiveRoomStopRecordingPayload(stopped));
        }
        catch (SpeakingLiveRoomNotFoundException ex)
        {
            return Results.NotFound(new { errorCode = "live_room_not_found", message = ex.Message });
        }
    }

    private static async Task<IResult> EndRoomAsync(
        string id,
        HttpContext http,
        SpeakingLiveRoomService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Results.BadRequest(new { errorCode = "live_room_id_required", message = "live room id is required." });
        }

        var userId = http.LiveRoomUserId();

        try
        {
            await service.EndRoomAsync(userId, id, ct);
            return Results.NoContent();
        }
        catch (SpeakingLiveRoomNotFoundException ex)
        {
            return Results.NotFound(new { errorCode = "live_room_not_found", message = ex.Message });
        }
        catch (SpeakingLiveRoomForbiddenException ex)
        {
            return Results.Json(
                new { errorCode = "forbidden", message = ex.Message },
                statusCode: StatusCodes.Status403Forbidden);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private static string LiveRoomUserId(this HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? http.User.FindFirstValue("sub")
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}

// ─────────────────────────────────────────────────────────────────────
// DTOs
// ─────────────────────────────────────────────────────────────────────

public sealed record SpeakingLiveRoomCreateRequest(string SpeakingSessionId);
public sealed record SpeakingLiveRoomCreatePayload(string LiveRoomId, string LivekitWssUrl, string RoomName);

public sealed record SpeakingLiveRoomTokenRequest(string Role);
public sealed record SpeakingLiveRoomTokenPayload(
    string Token,
    DateTimeOffset ExpiresAt,
    SpeakingLiveRoomCapabilitiesPayload Capabilities);

public sealed record SpeakingLiveRoomCapabilitiesPayload(
    bool CanPublishAudio,
    bool CanPublishVideo,
    bool CanSubscribe);

public sealed record SpeakingLiveRoomRecordingPayload(string EgressId, string OutputUrl);
public sealed record SpeakingLiveRoomStopRecordingPayload(bool Stopped);
