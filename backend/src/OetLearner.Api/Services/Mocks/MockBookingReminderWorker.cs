using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Mocks;

/// <summary>
/// Mocks Wave 5 (May 2026 closure addendum).
///
/// Background service that dispatches the three learner-facing
/// pre-booking reminder notifications for upcoming
/// <see cref="MockBooking"/> rows whose <c>Status</c> is
/// <c>scheduled</c> or <c>confirmed</c>:
///
///   • <c>LearnerMockReminder24h</c> — fires once when the booking is
///     within 24 h of <c>ScheduledStartAt</c>.
///   • <c>LearnerMockReminder2h</c> — fires once when within 2 h.
///   • <c>LearnerMockReminder30m</c> — fires once when within 30 min.
///
/// Tick interval is 5 minutes — small enough that the 30-minute
/// reminder still lands inside the 30..25 min window. Each reminder is
/// dispatched through <see cref="NotificationService.CreateForLearnerAsync"/>
/// with a fixed bucket string per offset (<c>"reminder-24h"</c>, etc.);
/// the notification service's existing dedupe-key contract
/// (<c>NotificationScheduling.BuildDedupeKey</c>) guarantees that even
/// across worker restarts each reminder is delivered at most once per
/// (booking × offset). No new persistence schema is required.
///
/// Bookings further than 24 h in the future are skipped entirely; once
/// a booking transitions out of <c>scheduled</c>/<c>confirmed</c>
/// (in_progress, completed, cancelled, no-show, etc.) the worker
/// skips it.
/// </summary>
public sealed class MockBookingReminderWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<MockBookingReminderWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    /// <summary>Cap on bookings inspected per tick to keep query latency bounded.</summary>
    private const int MaxBookingsPerTick = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Random startup jitter so multi-instance deployments don't all race.
        try { await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(5, 30)), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = await RunOnceAsync(stoppingToken);
                if (dispatched > 0)
                {
                    logger.LogInformation(
                        "MockBookingReminderWorker dispatched {Count} reminder notifications.",
                        dispatched);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "MockBookingReminderWorker tick failed.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Internal entry point used by tests. Returns the number of NEW
    /// notification events created (re-evaluations that hit the
    /// dedupe-key short-circuit count as zero).
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

        var now = clock.GetUtcNow();
        var horizon = now + MockBookingReminderPlanner.Offsets[0].Offset + TimeSpan.FromHours(1);

        var bookings = await db.MockBookings
            .Include(b => b.MockBundle)
            .Where(b =>
                (b.Status == MockBookingStatuses.Scheduled || b.Status == MockBookingStatuses.Confirmed) &&
                b.ScheduledStartAt > now &&
                b.ScheduledStartAt <= horizon)
            .OrderBy(b => b.ScheduledStartAt)
            .Take(MaxBookingsPerTick)
            .ToListAsync(ct);

        var dispatched = 0;
        foreach (var booking in bookings)
        {
            foreach (var planned in MockBookingReminderPlanner.Plan(booking, now))
            {
                var payload = new Dictionary<string, object?>
                {
                    ["bookingId"] = booking.Id,
                    ["bundleId"] = booking.MockBundleId,
                    ["bundleTitle"] = booking.MockBundle?.Title,
                    ["scheduledStartAt"] = booking.ScheduledStartAt.ToString("O"),
                    ["timezoneIana"] = booking.TimezoneIana,
                    ["deliveryMode"] = booking.DeliveryMode,
                    ["zoomJoinUrl"] = booking.ZoomJoinUrl,
                    ["minutesUntilStart"] = (int)Math.Ceiling(planned.TimeUntilStart.TotalMinutes),
                };

                try
                {
                    var eventCountBefore = await db.NotificationEvents.CountAsync(ct);
                    var notificationId = string.Equals(planned.AudienceRole, ApplicationUserRoles.Expert, StringComparison.OrdinalIgnoreCase)
                        ? await notifications.CreateForExpertAsync(
                            planned.EventKey,
                            planned.RecipientId,
                            "mock_booking",
                            planned.BookingId,
                            planned.Bucket,
                            payload,
                            ct)
                        : await notifications.CreateForLearnerAsync(
                            planned.EventKey,
                            planned.RecipientId,
                            "mock_booking",
                            planned.BookingId,
                            planned.Bucket,
                            payload,
                            ct);

                    if (!string.IsNullOrEmpty(notificationId)
                        && await db.NotificationEvents.CountAsync(ct) > eventCountBefore)
                    {
                        dispatched++;
                    }
                }
                catch (Exception ex)
                {
                    // Continue with next reminder rather than aborting the
                    // whole tick; isolated failure should not starve other
                    // bookings of their reminders.
                    logger.LogWarning(ex,
                        "Failed to dispatch {EventKey} reminder for booking {BookingId}.",
                        planned.EventKey, planned.BookingId);
                }
            }
        }

        return dispatched;
    }
}
