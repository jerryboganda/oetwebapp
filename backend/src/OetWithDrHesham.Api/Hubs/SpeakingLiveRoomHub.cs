using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Speaking;

namespace OetWithDrHesham.Api.Hubs;

/// <summary>
/// Realtime control-plane hub layered on top of the LiveKit media room
/// (Phase 3 of the OET Speaking module). LiveKit carries audio/video;
/// this hub carries the metadata events the UI needs to render:
/// tutor cue raises, room-ended announcements, and presence updates.
///
/// Wire-up: <c>Program.cs</c> (owned by the wiring agent) maps this hub
/// at <c>/v1/speaking/live-rooms/hub</c>.
///
/// Auth + group pattern mirrors
/// <see cref="OetWithDrHesham.Api.Services.Mocks.MockLiveRoomHub"/>.
/// </summary>
[Authorize]
public sealed class SpeakingLiveRoomHub : Hub
{
    public const string CueRaisedEvent = "CueRaised";
    public const string LiveRoomEndedEvent = "LiveRoomEnded";
    public const string SnapshotEvent = "LiveRoomSnapshot";

    private readonly LearnerDbContext _db;
    private readonly SpeakingLiveRoomService _service;
    private readonly ILogger<SpeakingLiveRoomHub> _logger;

    public SpeakingLiveRoomHub(
        LearnerDbContext db,
        SpeakingLiveRoomService service,
        ILogger<SpeakingLiveRoomHub> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static string LiveRoomGroup(string liveRoomId) => $"live-room:{liveRoomId}";

    // ─────────────────────────────────────────────────────────────────
    // Client → server: join a room as an authenticated participant.
    // ─────────────────────────────────────────────────────────────────
    public async Task JoinRoom(string liveRoomId)
    {
        if (string.IsNullOrWhiteSpace(liveRoomId))
        {
            throw new HubException("live_room_id_required");
        }

        var userId = ResolveUserId()
            ?? throw new HubException("user_id_required");

        var room = await _db.SpeakingLiveRooms
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == liveRoomId, Context.ConnectionAborted)
            ?? throw new HubException("live_room_not_found");

        var session = await _db.SpeakingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == room.SpeakingSessionId, Context.ConnectionAborted)
            ?? throw new HubException("speaking_session_not_found");

        if (!IsParticipant(session, userId))
        {
            throw new HubException("forbidden");
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            LiveRoomGroup(liveRoomId),
            Context.ConnectionAborted);

        await Clients.Caller.SendAsync(
            SnapshotEvent,
            SpeakingLiveRoomSnapshot.From(room),
            Context.ConnectionAborted);

        _logger.LogInformation(
            "SpeakingLiveRoomHub.JoinRoom userId={UserId} liveRoomId={LiveRoomId}",
            userId,
            liveRoomId);
    }

    public async Task LeaveRoom(string liveRoomId)
    {
        if (string.IsNullOrWhiteSpace(liveRoomId))
        {
            return;
        }

        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            LiveRoomGroup(liveRoomId),
            Context.ConnectionAborted);
    }

    // ─────────────────────────────────────────────────────────────────
    // Tutor-only: raise a cue from the interlocutor script.
    // ─────────────────────────────────────────────────────────────────
    public async Task BroadcastCue(string liveRoomId, string cueIndex)
    {
        if (string.IsNullOrWhiteSpace(liveRoomId))
        {
            throw new HubException("live_room_id_required");
        }
        if (string.IsNullOrWhiteSpace(cueIndex))
        {
            throw new HubException("cue_index_required");
        }

        var userId = ResolveUserId()
            ?? throw new HubException("user_id_required");

        var room = await _db.SpeakingLiveRooms
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == liveRoomId, Context.ConnectionAborted)
            ?? throw new HubException("live_room_not_found");

        var session = await _db.SpeakingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == room.SpeakingSessionId, Context.ConnectionAborted)
            ?? throw new HubException("speaking_session_not_found");

        if (!IsTutor(session, userId))
        {
            throw new HubException("forbidden");
        }

        var payload = new SpeakingLiveRoomCueRaised(
            LiveRoomId: liveRoomId,
            CueIndex: cueIndex,
            RaisedBy: userId,
            Timestamp: DateTimeOffset.UtcNow);

        await Clients.Group(LiveRoomGroup(liveRoomId))
            .SendAsync(CueRaisedEvent, payload, Context.ConnectionAborted);

        _logger.LogInformation(
            "SpeakingLiveRoomHub.BroadcastCue liveRoomId={LiveRoomId} cueIndex={CueIndex}",
            liveRoomId,
            cueIndex);
    }

    // ─────────────────────────────────────────────────────────────────
    // Owner or tutor: end the room.
    // ─────────────────────────────────────────────────────────────────
    public async Task EndRoom(string liveRoomId)
    {
        if (string.IsNullOrWhiteSpace(liveRoomId))
        {
            throw new HubException("live_room_id_required");
        }

        var userId = ResolveUserId()
            ?? throw new HubException("user_id_required");

        try
        {
            await _service.EndRoomAsync(userId, liveRoomId, Context.ConnectionAborted);
        }
        catch (SpeakingLiveRoomNotFoundException ex)
        {
            throw new HubException(ex.Message);
        }
        catch (SpeakingLiveRoomForbiddenException ex)
        {
            throw new HubException(ex.Message);
        }

        var payload = new SpeakingLiveRoomEnded(
            LiveRoomId: liveRoomId,
            EndedBy: userId,
            Timestamp: DateTimeOffset.UtcNow);

        await Clients.Group(LiveRoomGroup(liveRoomId))
            .SendAsync(LiveRoomEndedEvent, payload, Context.ConnectionAborted);

        _logger.LogInformation(
            "SpeakingLiveRoomHub.EndRoom liveRoomId={LiveRoomId} userId={UserId}",
            liveRoomId,
            userId);
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private bool IsParticipant(SpeakingSession session, string userId)
    {
        if (Context.User?.IsInRole(ApplicationUserRoles.Admin) == true) return true;
        if (string.Equals(session.UserId, userId, StringComparison.Ordinal)) return true;
        if (!string.IsNullOrWhiteSpace(session.InterlocutorActorId)
            && string.Equals(session.InterlocutorActorId, userId, StringComparison.Ordinal))
        {
            return true;
        }
        return false;
    }

    private static bool IsTutor(SpeakingSession session, string userId)
        => !string.IsNullOrWhiteSpace(session.InterlocutorActorId)
           && string.Equals(session.InterlocutorActorId, userId, StringComparison.Ordinal);

    private string? ResolveUserId()
        => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? Context.User?.FindFirstValue("sub");
}

// ─────────────────────────────────────────────────────────────────────
// Wire-format records — keep field names in lock-step with the
// frontend SignalR client.
// ─────────────────────────────────────────────────────────────────────

public sealed record SpeakingLiveRoomSnapshot(
    string LiveRoomId,
    string SpeakingSessionId,
    string RoomName,
    string Provider,
    string LivekitRoomSid,
    string State,
    DateTimeOffset? ActualStartUtc,
    DateTimeOffset? ActualEndUtc,
    DateTimeOffset UpdatedAt)
{
    public static SpeakingLiveRoomSnapshot From(SpeakingLiveRoom room) => new(
        room.Id,
        room.SpeakingSessionId,
        room.RoomName,
        room.Provider,
        room.LiveKitRoomSid ?? string.Empty,
        room.State.ToString().ToLowerInvariant(),
        room.ActualStartUtc,
        room.ActualEndUtc,
        room.UpdatedAt);
}

public sealed record SpeakingLiveRoomCueRaised(
    string LiveRoomId,
    string CueIndex,
    string RaisedBy,
    DateTimeOffset Timestamp);

public sealed record SpeakingLiveRoomEnded(
    string LiveRoomId,
    string EndedBy,
    DateTimeOffset Timestamp);
