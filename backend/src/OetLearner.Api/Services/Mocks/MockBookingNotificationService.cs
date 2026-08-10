using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Mocks;

/// <summary>Delivers the single automated Full Mock Speaking booking confirmation.</summary>
public sealed class MockBookingNotificationService(
    LearnerDbContext db,
    NotificationService notifications)
{
    public async Task SendConfirmationAsync(string bookingId, CancellationToken ct)
    {
        var booking = await db.MockBookings
            .Include(item => item.MockBundle)
            .FirstOrDefaultAsync(item => item.Id == bookingId, ct);
        if (booking is null
            || booking.Status == MockBookingStatuses.Cancelled
            || MockBookingPresentation.LearnerZoomJoinUrl(booking) is null)
        {
            // A confirmation is valid only after the real Zoom meeting and
            // learner join URL exist. Never emit a fallback confirmation.
            return;
        }

        var payload = new Dictionary<string, object?>
        {
            ["bookingId"] = booking.Id,
            ["bundleTitle"] = booking.MockBundle?.Title ?? "Full Mock Speaking",
            ["scheduledStartAt"] = booking.ScheduledStartAt.ToString("O"),
            ["timezoneIana"] = booking.TimezoneIana,
            ["joinUrl"] = MockBookingPresentation.RoomRoute(booking),
            ["zoomJoinUrl"] = MockBookingPresentation.LearnerZoomJoinUrl(booking),
            ["tutorId"] = booking.AssignedTutorId,
        };

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerMockScheduled,
            booking.UserId,
            "mock_booking",
            booking.Id,
            "booking-confirmation",
            payload,
            ct);

        if (!string.IsNullOrWhiteSpace(booking.AssignedTutorId))
        {
            await notifications.CreateForExpertAsync(
                NotificationEventKey.ExpertSpeakingSessionAssigned,
                booking.AssignedTutorId,
                "mock_booking",
                booking.Id,
                "booking-confirmation",
                payload,
                ct);
        }
    }
}
