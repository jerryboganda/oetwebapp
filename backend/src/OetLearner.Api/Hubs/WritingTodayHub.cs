using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OetLearner.Api.Contracts;

namespace OetLearner.Api.Hubs;

/// <summary>
/// SignalR hub that pushes <c>TodayPlanUpdated</c> events to the learner
/// after a pathway recompute or daily-plan regeneration. The learner connects
/// and is auto-joined to a per-user group on <see cref="OnConnectedAsync"/>;
/// service code broadcasts to <see cref="UserGroup(string)"/>.
///
/// Wire-up: mapped at <c>/hubs/writing-today</c> in <c>Program.cs</c>.
///
/// Server → Client events:
///   TodayPlanUpdated(plan) — full WritingTodayPlanResponseV2 payload.
///   PathwayRecalculated(pathway) — full WritingPathwayResponseV2 payload.
/// </summary>
[Authorize(Policy = "LearnerOnly")]
public sealed class WritingTodayHub : Hub
{
    public const string TodayPlanUpdatedEvent = "TodayPlanUpdated";
    public const string PathwayRecalculatedEvent = "PathwayRecalculated";

    public static string UserGroup(string userId) => $"writing-today:{userId}";

    private readonly ILogger<WritingTodayHub> _logger;

    public WritingTodayHub(ILogger<WritingTodayHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
}
