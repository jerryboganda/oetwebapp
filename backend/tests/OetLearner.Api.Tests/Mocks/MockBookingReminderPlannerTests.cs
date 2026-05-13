using OetLearner.Api.Domain;
using OetLearner.Api.Services.Mocks;

namespace OetLearner.Api.Tests.Mocks;

/// <summary>
/// Mocks Wave 5 closure (May 2026). Locks the offset firing logic of
/// <see cref="MockBookingReminderPlanner"/>. The planner is the pure
/// half of <see cref="MockBookingReminderWorker"/> — same inputs always
/// yield the same plan, regardless of database state or notification
/// dedupe history (dedupe is enforced downstream by NotificationService).
///
/// The planner is the single source of truth for:
///   • Which booking statuses are eligible.
///   • Which offsets fire (24 h / 2 h / 30 min).
///   • The horizon used by the worker's DB query.
/// </summary>
public class MockBookingReminderPlannerTests
{
    private static MockBooking BookingAt(DateTimeOffset start, string status = MockBookingStatuses.Scheduled) => new()
    {
        Id = "booking-test",
        UserId = "user-test",
        MockBundleId = "bundle-test",
        ScheduledStartAt = start,
        Status = status,
        TimezoneIana = "UTC",
        DeliveryMode = MockDeliveryModes.Computer,
        LiveRoomState = MockLiveRoomStates.Waiting,
    };

    private static readonly DateTimeOffset Now = new(2026, 5, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Offsets_ArePresent_InDescendingOrder_24h_2h_30m()
    {
        // Locking the order so worker iteration "larger first" is stable.
        Assert.Equal(3, MockBookingReminderPlanner.Offsets.Length);
        Assert.Equal(TimeSpan.FromHours(24), MockBookingReminderPlanner.Offsets[0].Offset);
        Assert.Equal(TimeSpan.FromHours(2), MockBookingReminderPlanner.Offsets[1].Offset);
        Assert.Equal(TimeSpan.FromMinutes(30), MockBookingReminderPlanner.Offsets[2].Offset);

        Assert.Equal(NotificationEventKey.LearnerMockReminder24h, MockBookingReminderPlanner.Offsets[0].EventKey);
        Assert.Equal(NotificationEventKey.LearnerMockReminder2h, MockBookingReminderPlanner.Offsets[1].EventKey);
        Assert.Equal(NotificationEventKey.LearnerMockReminder30m, MockBookingReminderPlanner.Offsets[2].EventKey);

        Assert.Equal("reminder-24h", MockBookingReminderPlanner.Offsets[0].Bucket);
        Assert.Equal("reminder-2h", MockBookingReminderPlanner.Offsets[1].Bucket);
        Assert.Equal("reminder-30m", MockBookingReminderPlanner.Offsets[2].Bucket);
    }

    [Fact]
    public void EligibleStatuses_AreOnly_Scheduled_And_Confirmed()
    {
        Assert.True(MockBookingReminderPlanner.EligibleStatuses.Contains(MockBookingStatuses.Scheduled));
        Assert.True(MockBookingReminderPlanner.EligibleStatuses.Contains(MockBookingStatuses.Confirmed));
        Assert.False(MockBookingReminderPlanner.EligibleStatuses.Contains(MockBookingStatuses.InProgress));
        Assert.False(MockBookingReminderPlanner.EligibleStatuses.Contains(MockBookingStatuses.Completed));
        Assert.False(MockBookingReminderPlanner.EligibleStatuses.Contains(MockBookingStatuses.Cancelled));
        Assert.False(MockBookingReminderPlanner.EligibleStatuses.Contains(MockBookingStatuses.LearnerNoShow));
        Assert.False(MockBookingReminderPlanner.EligibleStatuses.Contains(MockBookingStatuses.TutorNoShow));
    }

    [Fact]
    public void IsInHorizon_True_For_BookingWithin24Hours()
    {
        Assert.True(MockBookingReminderPlanner.IsInHorizon(BookingAt(Now.AddHours(23)), Now));
        Assert.True(MockBookingReminderPlanner.IsInHorizon(BookingAt(Now.AddHours(1)), Now));
        Assert.True(MockBookingReminderPlanner.IsInHorizon(BookingAt(Now.AddMinutes(15)), Now));
    }

    [Fact]
    public void IsInHorizon_False_For_BookingPastStart_OrBeyondHorizon()
    {
        Assert.False(MockBookingReminderPlanner.IsInHorizon(BookingAt(Now), Now)); // already started
        Assert.False(MockBookingReminderPlanner.IsInHorizon(BookingAt(Now.AddMinutes(-5)), Now));
        Assert.False(MockBookingReminderPlanner.IsInHorizon(BookingAt(Now.AddDays(2)), Now));
    }

    [Fact]
    public void IsInHorizon_False_For_IneligibleStatus()
    {
        var booking = BookingAt(Now.AddHours(10), MockBookingStatuses.Cancelled);
        Assert.False(MockBookingReminderPlanner.IsInHorizon(booking, Now));
    }

    [Fact]
    public void Plan_FarFutureBooking_Yields_NoReminders()
    {
        var booking = BookingAt(Now.AddDays(3));
        Assert.Empty(MockBookingReminderPlanner.Plan(booking, Now));
    }

    [Fact]
    public void Plan_BookingWithin24h_Yields_OnlyThe_24hReminder()
    {
        var booking = BookingAt(Now.AddHours(23));
        var plan = MockBookingReminderPlanner.Plan(booking, Now).ToArray();
        Assert.Single(plan);
        Assert.Equal(NotificationEventKey.LearnerMockReminder24h, plan[0].EventKey);
        Assert.Equal("reminder-24h", plan[0].Bucket);
    }

    [Fact]
    public void Plan_BookingWithin2h_Yields_24h_AND_2h_Reminders()
    {
        var booking = BookingAt(Now.AddMinutes(90));
        var plan = MockBookingReminderPlanner.Plan(booking, Now).ToArray();
        Assert.Equal(2, plan.Length);
        Assert.Equal(NotificationEventKey.LearnerMockReminder24h, plan[0].EventKey);
        Assert.Equal(NotificationEventKey.LearnerMockReminder2h, plan[1].EventKey);
    }

    [Fact]
    public void Plan_BookingWithin30m_Yields_All_Three_Reminders()
    {
        var booking = BookingAt(Now.AddMinutes(20));
        var plan = MockBookingReminderPlanner.Plan(booking, Now).ToArray();
        Assert.Equal(3, plan.Length);
        Assert.Equal(NotificationEventKey.LearnerMockReminder24h, plan[0].EventKey);
        Assert.Equal(NotificationEventKey.LearnerMockReminder2h, plan[1].EventKey);
        Assert.Equal(NotificationEventKey.LearnerMockReminder30m, plan[2].EventKey);
    }

    [Fact]
    public void Plan_PastBooking_Yields_NoReminders()
    {
        var booking = BookingAt(Now.AddMinutes(-10));
        Assert.Empty(MockBookingReminderPlanner.Plan(booking, Now));
    }

    [Fact]
    public void Plan_CancelledBooking_Yields_NoReminders_EvenIfWithin30m()
    {
        var booking = BookingAt(Now.AddMinutes(15), MockBookingStatuses.Cancelled);
        Assert.Empty(MockBookingReminderPlanner.Plan(booking, Now));
    }

    [Fact]
    public void Plan_InProgressBooking_Yields_NoReminders()
    {
        var booking = BookingAt(Now.AddMinutes(15), MockBookingStatuses.InProgress);
        Assert.Empty(MockBookingReminderPlanner.Plan(booking, Now));
    }

    [Fact]
    public void Plan_ConfirmedBooking_IsTreatedSameAs_Scheduled()
    {
        var scheduled = BookingAt(Now.AddMinutes(20), MockBookingStatuses.Scheduled);
        var confirmed = BookingAt(Now.AddMinutes(20), MockBookingStatuses.Confirmed);
        var planA = MockBookingReminderPlanner.Plan(scheduled, Now).ToArray();
        var planB = MockBookingReminderPlanner.Plan(confirmed, Now).ToArray();
        Assert.Equal(planA.Length, planB.Length);
    }

    [Theory]
    [InlineData(60 * 24)]   // exactly 24h → still inside (≤ 24h)
    [InlineData(60 * 2)]    // exactly 2h
    [InlineData(30)]        // exactly 30 min
    public void Plan_FiresAtBoundary_Inclusive(int minutesUntilStart)
    {
        // Boundary semantics: until <= offset means the offset has been
        // crossed. 24h-out should already be in the 24h window.
        var booking = BookingAt(Now.AddMinutes(minutesUntilStart));
        var plan = MockBookingReminderPlanner.Plan(booking, Now).ToArray();
        Assert.NotEmpty(plan);
        // All offsets >= remaining time should fire.
        foreach (var entry in plan)
        {
            Assert.True(entry.TimeUntilStart.TotalMinutes <= minutesUntilStart + 0.001);
        }
    }

    [Fact]
    public void Plan_PreservesBooking_And_User_Identity()
    {
        var booking = BookingAt(Now.AddMinutes(20));
        booking.Id = "booking-xyz";
        booking.UserId = "user-xyz";

        var plan = MockBookingReminderPlanner.Plan(booking, Now).ToArray();
        Assert.NotEmpty(plan);
        foreach (var entry in plan)
        {
            Assert.Equal("booking-xyz", entry.BookingId);
            Assert.Equal("user-xyz", entry.UserId);
        }
    }
}
