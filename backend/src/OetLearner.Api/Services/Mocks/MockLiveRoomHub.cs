using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Mocks;

[Authorize]
public sealed class MockLiveRoomHub(LearnerDbContext dbContext) : Hub
{
    public const string LiveRoomStateChangedEvent = "LiveRoomStateChanged";

    public static string BookingGroup(string bookingId) => $"mock-booking:{bookingId}";

    public async Task JoinBooking(string bookingId)
    {
        var userId = ResolveUserId()
            ?? throw new HubException("user_id_required");

        var booking = await dbContext.MockBookings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == bookingId, Context.ConnectionAborted)
            ?? throw new HubException("booking_not_found");

        if (!CanAccess(booking, userId))
        {
            throw new HubException("forbidden");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, BookingGroup(booking.Id), Context.ConnectionAborted);
        await Clients.Caller.SendAsync(
            "LiveRoomSnapshot",
            MockLiveRoomSnapshot.From(booking),
            Context.ConnectionAborted);
    }

    public async Task LeaveBooking(string bookingId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, BookingGroup(bookingId), Context.ConnectionAborted);
    }

    private bool CanAccess(MockBooking booking, string userId)
    {
        if (Context.User?.IsInRole(ApplicationUserRoles.Admin) == true) return true;
        if (Context.User?.IsInRole(ApplicationUserRoles.Expert) == true)
        {
            return booking.AssignedTutorId == userId || booking.AssignedInterlocutorId == userId;
        }

        return booking.UserId == userId;
    }

    private string? ResolveUserId()
        => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Context.User?.FindFirstValue("sub");
}

// NOTE: Property names below are the source of truth for the SignalR
// payload contract consumed by `lib/mocks/live-room-hub.ts`. Adding,
// removing, or renaming a field here is a wire-format break — update
// the TS interface in lockstep and the contract test in
// `MockLiveRoomTransitionTests`.
public sealed record MockLiveRoomSnapshot(
    string BookingId,
    string LiveRoomState,
    string Status,
    int TransitionVersion,
    DateTimeOffset ScheduledStartAt,
    string TimezoneIana,
    DateTimeOffset UpdatedAt)
{
    public static MockLiveRoomSnapshot From(MockBooking booking) => new(
        booking.Id,
        booking.LiveRoomState,
        booking.Status,
        booking.LiveRoomTransitionVersion,
        booking.ScheduledStartAt,
        booking.TimezoneIana,
        booking.UpdatedAt);
}

public sealed record MockLiveRoomStateChanged(
    string BookingId,
    string FromState,
    string ToState,
    string LiveRoomState,
    string Status,
    string ActorRole,
    int TransitionVersion,
    DateTimeOffset ScheduledStartAt,
    string TimezoneIana,
    string? Reason,
    DateTimeOffset OccurredAt)
{
    public static MockLiveRoomStateChanged From(MockBooking booking, string fromState, string actorRole, string? reason, DateTimeOffset occurredAt) => new(
        booking.Id,
        fromState,
        booking.LiveRoomState,
        booking.LiveRoomState,
        booking.Status,
        actorRole,
        booking.LiveRoomTransitionVersion,
        booking.ScheduledStartAt,
        booking.TimezoneIana,
        reason,
        occurredAt);
}