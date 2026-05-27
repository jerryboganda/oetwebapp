using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
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

        group.MapGet("/{id}", GetRoomAsync)
            .WithSummary("Get live-room details for a participant or admin observer.")
            .Produces<SpeakingLiveRoomDetailPayload>(StatusCodes.Status200OK)
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

        // P6 — convenience GET for the frontend `getLiveKitToken(roomId)`
        // helper. Internally re-uses IssueTokenAsync; the role is inferred
        // from the caller's relationship with the session (owner →
        // learner, assigned interlocutor → tutor).
        group.MapGet("/{id}/token", GetTokenAsync)
            .WithSummary("Get a short-lived LiveKit access token for the calling user.")
            .Produces<SpeakingLiveRoomTokenWithWssPayload>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // P6 — inbound LiveKit webhook receiver. Public route (no auth
        // policy) — instead, the Authorization header carries the
        // LiveKit-minted JWT/HMAC signature, verified inside
        // ILiveKitGateway.VerifyWebhookSignature.
        app.MapPost("/v1/speaking/live-rooms/webhooks/livekit", HandleLiveKitWebhookAsync)
            .AllowAnonymous()
            .WithTags("Speaking live rooms")
            .WithSummary("Inbound LiveKit webhook (HMAC-signed) for speaking live rooms.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status400BadRequest);

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

        if (string.Equals(request.Role, "observer", StringComparison.OrdinalIgnoreCase) && !IsAdmin(http.User))
        {
            return Results.Json(
                new { errorCode = "forbidden", message = "Observer tokens require admin access." },
                statusCode: StatusCodes.Status403Forbidden);
        }

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
        HttpContext http,
        LearnerDbContext db,
        SpeakingLiveRoomService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Results.BadRequest(new { errorCode = "live_room_id_required", message = "live room id is required." });
        }

        try
        {
            var access = await LoadRoomWithSessionAsync(id, db, ct);
            if (access is null)
            {
                return Results.NotFound(new { errorCode = "live_room_not_found", message = $"Live room '{id}' was not found." });
            }
            if (!IsAssignedTutor(http.User, access.Session))
            {
                return Results.Json(
                    new { errorCode = "forbidden", message = "Only the assigned tutor may start room recording." },
                    statusCode: StatusCodes.Status403Forbidden);
            }
            var result = await service.StartRecordingAsync(id, ct);
            return Results.Ok(new SpeakingLiveRoomRecordingPayload(result.EgressId, result.OutputUrl));
        }
        catch (SpeakingLiveRoomNotFoundException ex)
        {
            return Results.NotFound(new { errorCode = "live_room_not_found", message = ex.Message });
        }
        catch (SpeakingLiveRoomInvalidStateException ex)
        {
            return Results.BadRequest(new { errorCode = "invalid_state", message = ex.Message });
        }
    }

    private static async Task<IResult> StopRecordingAsync(
        string id,
        HttpContext http,
        LearnerDbContext db,
        SpeakingLiveRoomService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Results.BadRequest(new { errorCode = "live_room_id_required", message = "live room id is required." });
        }

        try
        {
            var access = await LoadRoomWithSessionAsync(id, db, ct);
            if (access is null)
            {
                return Results.NotFound(new { errorCode = "live_room_not_found", message = $"Live room '{id}' was not found." });
            }
            if (!IsAssignedTutor(http.User, access.Session))
            {
                return Results.Json(
                    new { errorCode = "forbidden", message = "Only the assigned tutor may stop room recording." },
                    statusCode: StatusCodes.Status403Forbidden);
            }
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
    // GET /{id}/token — convenience helper for the frontend client.
    // Infers role from the caller's relationship to the session.
    // ─────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetRoomAsync(
        string id,
        HttpContext http,
        LearnerDbContext db,
        IOptions<LiveKitOptions> liveKitOptions,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Results.BadRequest(new { errorCode = "live_room_id_required", message = "live room id is required." });
        }

        var access = await LoadRoomWithSessionAsync(id, db, ct);
        if (access is null)
        {
            return Results.NotFound(new { errorCode = "live_room_not_found", message = $"Live room '{id}' was not found." });
        }

        if (!IsRoomParticipantOrAdmin(http.User, access.Session))
        {
            return Results.Json(
                new { errorCode = "forbidden", message = "User is not a participant of this live room." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Ok(ToDetailPayload(access.Room, access.Card, liveKitOptions.Value.WssUrl));
    }

    private static async Task<IResult> GetTokenAsync(
        string id,
        HttpContext http,
        SpeakingLiveRoomService service,
        LearnerDbContext db,
        Microsoft.Extensions.Options.IOptions<OetLearner.Api.Configuration.LiveKitOptions> liveKitOptions,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Results.BadRequest(new { errorCode = "live_room_id_required", message = "live room id is required." });
        }

        var userId = http.LiveRoomUserId();

        // Resolve role from session ownership / interlocutor assignment.
        // We look up the room → session join lazily so the caller does
        // not have to choose between learner/tutor explicitly.
        var room = await db.SpeakingLiveRooms
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (room is null)
        {
            return Results.NotFound(new { errorCode = "live_room_not_found", message = $"Live room '{id}' was not found." });
        }

        var session = await db.SpeakingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == room.SpeakingSessionId, ct);
        if (session is null)
        {
            return Results.NotFound(new { errorCode = "session_not_found", message = "Speaking session not found." });
        }

        string role;
        if (string.Equals(session.UserId, userId, StringComparison.Ordinal))
        {
            role = "learner";
        }
        else if (!string.IsNullOrWhiteSpace(session.InterlocutorActorId)
            && string.Equals(session.InterlocutorActorId, userId, StringComparison.Ordinal))
        {
            role = "tutor";
        }
        else if (IsAdmin(http.User))
        {
            role = "observer";
        }
        else
        {
            return Results.Json(
                new { errorCode = "forbidden", message = "User is not a participant of this live room." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            var result = await service.IssueTokenAsync(userId, id, role, ct);
            var capPayload = new SpeakingLiveRoomCapabilitiesPayload(
                result.Capabilities.CanPublishAudio,
                result.Capabilities.CanPublishVideo,
                result.Capabilities.CanSubscribe);
            return Results.Ok(new SpeakingLiveRoomTokenWithWssPayload(
                Token: result.Token,
                WssUrl: liveKitOptions.Value.WssUrl,
                RoomName: room.RoomName,
                ExpiresAt: result.ExpiresAt,
                Role: role,
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

    // ─────────────────────────────────────────────────────────────────
    // POST /v1/speaking/live-rooms/webhooks/livekit
    //
    // Receives signed LiveKit webhook events and reacts to:
    //   - room_finished     → State = Ended, ActualEndUtc = now
    //   - egress_ended      → persist SpeakingRecording row + schedule
    //                          retention via SpeakingAudioRetentionWorker
    //   - all events        → appended to SpeakingLiveRoom.WebhookEventsJson
    //
    // Webhook signature is verified against LiveKitOptions.WebhookSigningSecret
    // BEFORE any side-effects are taken. Idempotency is enforced via the
    // shared IdempotencyRecord table to absorb provider retries.
    // ─────────────────────────────────────────────────────────────────

    private const string LiveKitWebhookIdempotencyScope = "speaking_livekit_webhook";

    private static async Task<IResult> HandleLiveKitWebhookAsync(
        HttpContext http,
        ILiveKitGateway gateway,
        SpeakingLiveRoomService service,
        LearnerDbContext db,
        CancellationToken ct)
    {
        // Read raw request body — must match the bytes the provider
        // signed. EnableBuffering so an upstream signature verifier
        // could re-read if needed.
        http.Request.EnableBuffering();
        http.Request.Body.Position = 0;
        string payload;
        using (var reader = new StreamReader(http.Request.Body, leaveOpen: true))
        {
            payload = await reader.ReadToEndAsync(ct);
        }
        http.Request.Body.Position = 0;

        var signature = http.Request.Headers.TryGetValue("Authorization", out var headerValue)
            ? headerValue.ToString()
            : null;

        if (string.IsNullOrWhiteSpace(signature))
        {
            return Results.Unauthorized();
        }

        if (!gateway.VerifyWebhookSignature(payload, signature))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return Results.BadRequest(new { errorCode = "empty_payload", message = "Webhook payload is empty." });
        }

        string eventType;
        string? webhookEventId;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            // LiveKit Cloud uses `event` (string); older test fixtures
            // use `eventType`. Accept either.
            eventType = TryGetString(root, "event")
                ?? TryGetString(root, "eventType")
                ?? string.Empty;
            webhookEventId = TryGetString(root, "id");

            if (string.IsNullOrWhiteSpace(eventType))
            {
                return Results.BadRequest(new { errorCode = "event_required", message = "event field is required." });
            }
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { errorCode = "invalid_json", message = "Webhook payload is not valid JSON." });
        }

        // Idempotency dedupe — providers retry on 5xx + transient
        // network failures, so a unique (Scope, Key) on IdempotencyRecord
        // prevents double-processing.
        if (!string.IsNullOrWhiteSpace(webhookEventId))
        {
            var existing = await db.IdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.Scope == LiveKitWebhookIdempotencyScope && r.Key == webhookEventId,
                    ct);
            if (existing is not null)
            {
                return Results.Accepted(value: new { status = "duplicate" });
            }

            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Id = $"sp_lk_{Guid.NewGuid():N}",
                Scope = LiveKitWebhookIdempotencyScope,
                Key = webhookEventId,
                ResponseJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                return Results.Accepted(value: new { status = "duplicate" });
            }
        }

        // SpeakingLiveRoomService.HandleWebhookAsync centralises the
        // dispatch by event type — recording_finished / egress_ended /
        // room_finished are all handled there. We keep this endpoint
        // thin to mirror the existing /v1/webhooks/livekit handler.
        await service.HandleWebhookAsync(eventType, payload, ct);

        return Results.Ok(new { status = "ok" });
    }

    private static string? TryGetString(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    private static async Task<LiveRoomAccess?> LoadRoomWithSessionAsync(
        string liveRoomId,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var room = await db.SpeakingLiveRooms
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == liveRoomId, ct);
        if (room is null)
        {
            return null;
        }

        var session = await db.SpeakingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == room.SpeakingSessionId, ct);
        if (session is null)
        {
            return null;
        }

        var card = await db.RolePlayCards.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == session.RolePlayCardId, ct);

        return card is null ? null : new LiveRoomAccess(room, session, card);
    }

    private static bool IsRoomParticipantOrAdmin(ClaimsPrincipal user, SpeakingSession session)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return string.Equals(session.UserId, userId, StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(session.InterlocutorActorId)
                && string.Equals(session.InterlocutorActorId, userId, StringComparison.Ordinal))
            || IsAdmin(user);
    }

    private static bool IsAssignedTutor(ClaimsPrincipal user, SpeakingSession session)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return !string.IsNullOrWhiteSpace(session.InterlocutorActorId)
            && string.Equals(session.InterlocutorActorId, userId, StringComparison.Ordinal);
    }

    private static bool IsAdmin(ClaimsPrincipal user)
        => user.IsInRole(ApplicationUserRoles.Admin)
           || user.IsInRole("Admin")
           || user.IsInRole("system_admin");

    private static SpeakingLiveRoomDetailPayload ToDetailPayload(SpeakingLiveRoom room, RolePlayCard card, string wssUrl) => new(
        LiveRoomId: room.Id,
        SpeakingSessionId: room.SpeakingSessionId,
        RoomName: room.RoomName,
        LivekitWssUrl: wssUrl,
        State: room.State.ToString(),
        RecordingEgressId: room.EgressId,
        CreatedAt: room.CreatedAt,
        EndedAt: room.ActualEndUtc,
        Card: new SpeakingLiveRoomCardPayload(
            CardId: card.Id,
            ScenarioTitle: card.ScenarioTitle,
            Setting: card.Setting,
            CandidateRole: card.CandidateRole,
            RolePlayTimeSeconds: card.RolePlayTimeSeconds));

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private static string LiveRoomUserId(this HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? http.User.FindFirstValue("sub")
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}

internal sealed record LiveRoomAccess(SpeakingLiveRoom Room, SpeakingSession Session, RolePlayCard Card);

// ─────────────────────────────────────────────────────────────────────
// DTOs
// ─────────────────────────────────────────────────────────────────────

public sealed record SpeakingLiveRoomCreateRequest(string SpeakingSessionId);
public sealed record SpeakingLiveRoomCreatePayload(string LiveRoomId, string LivekitWssUrl, string RoomName);

public sealed record SpeakingLiveRoomDetailPayload(
    string LiveRoomId,
    string SpeakingSessionId,
    string RoomName,
    string LivekitWssUrl,
    string State,
    string? RecordingEgressId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EndedAt,
    SpeakingLiveRoomCardPayload Card);

public sealed record SpeakingLiveRoomCardPayload(
    string CardId,
    string? ScenarioTitle,
    string? Setting,
    string? CandidateRole,
    int RolePlayTimeSeconds);

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

/// <summary>
/// Response shape for GET /v1/speaking/live-rooms/{id}/token. Bundles the
/// minted JWT together with the WebSocket URL + room name so the
/// frontend doesn't need a second round-trip to connect.
/// </summary>
public sealed record SpeakingLiveRoomTokenWithWssPayload(
    string Token,
    string WssUrl,
    string RoomName,
    DateTimeOffset ExpiresAt,
    string Role,
    SpeakingLiveRoomCapabilitiesPayload Capabilities);
