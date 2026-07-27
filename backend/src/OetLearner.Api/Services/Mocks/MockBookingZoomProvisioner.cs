using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Mocks;

/// <summary>
/// Provisioning states for <see cref="MockBooking.ZoomStatus"/>.
/// </summary>
public static class MockBookingZoomStatuses
{
    public const string Pending = "pending";
    public const string Creating = "creating";
    public const string Created = "created";
    public const string Failed = "failed";
}

/// <summary>
/// Shared presentation rules for mock-booking join links. The learner's
/// <c>joinUrl</c> is ALWAYS the in-app speaking-room route (consent gate,
/// candidate card, chunked recording); the real Zoom URLs are surfaced
/// separately and only once provisioning succeeded — legacy rows stored the
/// internal room route in <see cref="MockBooking.ZoomJoinUrl"/>, so the
/// status + scheme guard keeps those from leaking as "Zoom" links.
/// </summary>
public static class MockBookingPresentation
{
    public static string RoomRoute(MockBooking booking)
        => $"/mocks/speaking-room/{Uri.EscapeDataString(booking.Id)}";

    public static string? LearnerZoomJoinUrl(MockBooking booking)
        => booking.ZoomStatus == MockBookingZoomStatuses.Created
           && booking.ZoomJoinUrl is not null
           && booking.ZoomJoinUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? booking.ZoomJoinUrl
            : null;

    public static string? ExpertZoomStartUrl(MockBooking booking)
        => booking.ZoomStatus == MockBookingZoomStatuses.Created
           && booking.ZoomStartUrl is not null
           && booking.ZoomStartUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? booking.ZoomStartUrl
            : null;
}

/// <summary>
/// Creates real Zoom meetings for Full Mock speaking bookings (replacing the
/// old sandbox-id stamping). Mirrors the Private Speaking pattern: queued as a
/// <see cref="JobType.MockBookingZoomCreate"/> background job at booking
/// creation / reschedule, idempotent on re-run, up to 3 retries via the job
/// processor, stale meeting deleted before re-provisioning.
/// </summary>
public sealed class MockBookingZoomProvisioner(
    LearnerDbContext db,
    ZoomMeetingService zoomService,
    ILogger<MockBookingZoomProvisioner> logger)
{
    private const int MaxZoomRetries = 3;
    private const int DefaultMeetingMinutes = 30;

    /// <summary>
    /// Adds the provisioning job row to the SAME change-tracker as the booking
    /// so both commit atomically with the caller's SaveChanges.
    /// </summary>
    public static void QueueZoomCreateJob(LearnerDbContext db, string bookingId)
    {
        var now = DateTimeOffset.UtcNow;
        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"bgj-{Guid.NewGuid():N}",
            Type = JobType.MockBookingZoomCreate,
            ResourceId = bookingId,
            State = AsyncState.Queued,
            AvailableAt = now,
            CreatedAt = now,
            LastTransitionAt = now
        });
    }

    public async Task CreateZoomMeetingForMockBookingAsync(string bookingId, CancellationToken ct)
    {
        var booking = await db.MockBookings
            .Include(b => b.MockBundle)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null)
        {
            logger.LogWarning("Cannot create Zoom meeting for mock booking {BookingId}: not found", bookingId);
            return;
        }

        if (booking.Status is MockBookingStatuses.Cancelled
            or MockBookingStatuses.Completed
            or MockBookingStatuses.LearnerNoShow
            or MockBookingStatuses.TutorNoShow)
        {
            return;
        }

        if (booking.ZoomStatus == MockBookingZoomStatuses.Created
            && MockBookingPresentation.LearnerZoomJoinUrl(booking) is not null)
        {
            return; // Idempotent re-run
        }

        if (!await zoomService.IsEnabledAsync(ct))
        {
            logger.LogInformation("Zoom integration disabled; skipping meeting provisioning for mock booking {BookingId}", bookingId);
            return;
        }

        // Reschedule path: drop the stale meeting before creating the new one.
        await DeleteZoomMeetingBestEffortAsync(booking, ct);

        booking.ZoomStatus = MockBookingZoomStatuses.Creating;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        try
        {
            var minutes = booking.MockBundle is { EstimatedDurationMinutes: > 0 } bundle
                ? bundle.EstimatedDurationMinutes
                : DefaultMeetingMinutes;
            var result = await zoomService.CreateMeetingAsync(
                topic: $"OET Speaking Mock — {booking.MockBundle?.Title ?? "Scheduled mock"}",
                startTime: booking.ScheduledStartAt,
                durationMinutes: minutes,
                timezone: booking.TimezoneIana,
                ct);

            booking.ZoomMeetingId = result.MeetingId.ToString(CultureInfo.InvariantCulture);
            booking.ZoomJoinUrl = result.JoinUrl;
            booking.ZoomStartUrl = result.StartUrl;
            booking.ZoomMeetingPassword = result.Password;
            booking.ZoomStatus = MockBookingZoomStatuses.Created;
            booking.ZoomError = null;
            booking.UpdatedAt = DateTimeOffset.UtcNow;

            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = booking.UpdatedAt,
                ActorId = "system",
                ActorName = "system",
                Action = "mock_booking_zoom_created",
                ResourceType = "MockBooking",
                ResourceId = booking.Id,
                Details = JsonSupport.Serialize(new { meetingId = booking.ZoomMeetingId }),
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            booking.ZoomStatus = MockBookingZoomStatuses.Failed;
            booking.ZoomError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            booking.ZoomRetryCount++;
            booking.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);

            logger.LogError(ex, "Failed to create Zoom meeting for mock booking {BookingId} (attempt {Attempt})",
                bookingId, booking.ZoomRetryCount);

            if (booking.ZoomRetryCount < MaxZoomRetries)
            {
                throw; // Let the background job processor retry.
            }
        }
    }

    /// <summary>
    /// Deletes the booking's Zoom meeting if a real one exists. Non-fatal by
    /// design (cancellation should never fail because Zoom is unreachable);
    /// legacy sandbox ids don't parse as meeting numbers and are skipped.
    /// </summary>
    public async Task DeleteZoomMeetingBestEffortAsync(MockBooking booking, CancellationToken ct)
    {
        if (!long.TryParse(booking.ZoomMeetingId, NumberStyles.None, CultureInfo.InvariantCulture, out var meetingId))
        {
            return;
        }

        try
        {
            await zoomService.DeleteMeetingAsync(meetingId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete Zoom meeting {MeetingId} for mock booking {BookingId}",
                meetingId, booking.Id);
        }
    }
}
