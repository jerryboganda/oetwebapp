using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Mocks;

/// <summary>
/// Mocks Wave 5 — pure planner half of <see cref="MockBookingReminderWorker"/>.
///
/// Splitting the time-window decision out of the worker keeps the latter
/// concerned only with database I/O and notification dispatch (which need
/// integration tests), while the offset/firing logic gets fast unit-test
/// coverage. The planner is deterministic: same inputs ⇒ same outputs.
/// </summary>
public static class MockBookingReminderPlanner
{
    /// <summary>
    /// The reminder offsets in **descending** order. The worker iterates in
    /// this order so larger offsets are evaluated first; each one is
    /// dispatched through the dedupe-aware notification service so
    /// re-evaluation of the same offset is a cheap no-op.
    /// </summary>
    public static readonly (TimeSpan Offset, NotificationEventKey EventKey, string Bucket)[] Offsets =
    [
        (TimeSpan.FromHours(24),    NotificationEventKey.LearnerMockReminder24h, "reminder-24h"),
        (TimeSpan.FromHours(2),     NotificationEventKey.LearnerMockReminder2h,  "reminder-2h"),
        (TimeSpan.FromMinutes(30),  NotificationEventKey.LearnerMockReminder30m, "reminder-30m"),
    ];

    /// <summary>
    /// Status values a booking must hold for reminders to fire. Once a
    /// booking transitions out of these (in_progress, completed, cancelled,
    /// any no-show variant) reminders stop.
    /// </summary>
    public static readonly IReadOnlySet<string> EligibleStatuses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            MockBookingStatuses.Scheduled,
            MockBookingStatuses.Confirmed,
        };

    /// <summary>
    /// Returns true when <paramref name="booking"/> is inside the planning
    /// horizon (status eligible AND ScheduledStartAt is in the future AND
    /// within the largest offset + 1h buffer). Used by the worker to bound
    /// its DB query as well as by tests.
    /// </summary>
    public static bool IsInHorizon(MockBooking booking, DateTimeOffset now)
    {
        if (!EligibleStatuses.Contains(booking.Status)) return false;
        if (booking.ScheduledStartAt <= now) return false;
        var horizon = now + Offsets[0].Offset + TimeSpan.FromHours(1);
        return booking.ScheduledStartAt <= horizon;
    }

    /// <summary>
    /// Plan dispatches for a single booking at instant <paramref name="now"/>.
    /// Returns one entry per reminder offset whose window the booking has
    /// crossed (i.e. <c>(start - now) ≤ offset</c>). The worker delegates
    /// dedupe to <c>NotificationService</c>, so the same plan emitted on
    /// the next tick will simply hit the dedupe-key short-circuit.
    /// </summary>
    public static IEnumerable<PlannedReminder> Plan(MockBooking booking, DateTimeOffset now)
    {
        if (!IsInHorizon(booking, now)) yield break;

        var until = booking.ScheduledStartAt - now;
        foreach (var (offset, eventKey, bucket) in Offsets)
        {
            if (until > offset) continue;
            yield return new PlannedReminder(booking.Id, booking.UserId, eventKey, bucket, until);
        }
    }

    public readonly record struct PlannedReminder(
        string BookingId,
        string UserId,
        NotificationEventKey EventKey,
        string Bucket,
        TimeSpan TimeUntilStart);
}
