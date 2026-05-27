using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Hubs;

/// <summary>
/// SignalR hub for streaming Coach hints to the active writing session.
/// Clients subscribe to a per-session group and call <see cref="RequestHints"/>
/// when the editor wants fresh hints (debounced + rate-limited server-side
/// per the writing-coach policy).
///
/// Wire-up: mapped at <c>/hubs/writing-coach</c> in <c>Program.cs</c>.
///
/// Client → Server methods:
///   JoinSession(sessionId) — subscribe to coach events for a session.
///   LeaveSession(sessionId) — unsubscribe.
///   RequestHints(payload) — ask the server for hints; replies via HintGenerated.
///
/// Server → Client events:
///   HintGenerated(hint) — new Coach hint produced.
///   HintError(code, message) — Coach service failure (cooldown, quota, etc.).
/// </summary>
[Authorize(Policy = "LearnerOnly")]
public sealed class WritingCoachHub : Hub
{
    public const string HintGeneratedEvent = "HintGenerated";
    public const string HintErrorEvent = "HintError";

    public static string SessionGroup(string userId, string sessionId) => $"writing-coach:{userId}:{sessionId}";

    private readonly IWritingCoachServiceV2 _coach;
    private readonly ILogger<WritingCoachHub> _logger;

    public WritingCoachHub(IWritingCoachServiceV2 coach, ILogger<WritingCoachHub> logger)
    {
        _coach = coach;
        _logger = logger;
    }

    public async Task JoinSession(string sessionId)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new HubException("user_id_required");
        }
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new HubException("session_id_required");
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            SessionGroup(userId, sessionId),
            Context.ConnectionAborted);
    }

    public async Task LeaveSession(string sessionId)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return;
        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            SessionGroup(userId, sessionId),
            Context.ConnectionAborted);
    }

    public async Task RequestHints(WritingCoachHintRequest payload)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new HubException("user_id_required");
        }

        try
        {
            var hints = await _coach.RequestHintsAsync(userId, payload, Context.ConnectionAborted);
            foreach (var hint in hints)
            {
                await Clients.Group(SessionGroup(userId, payload.SessionId))
                    .SendAsync(HintGeneratedEvent, hint, Context.ConnectionAborted);
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected — normal flow
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Writing coach hint generation failed for user {UserId} session {SessionId}", userId, payload.SessionId);
            await Clients.Caller.SendAsync(HintErrorEvent, "COACH_FAILED", "Coach is unavailable right now.", Context.ConnectionAborted);
        }
    }
}
