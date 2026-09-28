using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Services;

public sealed partial class MockService
{
    public async Task<object> UpdateMockBookingAsync(string userId, string bookingId, MockBookingUpdateRequest request, CancellationToken ct)
    {
        var booking = await db.MockBookings
            .Include(x => x.MockBundle)
            .FirstOrDefaultAsync(x => x.Id == bookingId && x.UserId == userId, ct)
            ?? throw ApiException.NotFound("mock_booking_not_found", "Mock booking not found.");

        if (await IsSpeakingBundleAsync(booking.MockBundleId, ct))
        {
            throw ApiException.Conflict(
                "canonical_speaking_booking_required",
                "Speaking booking changes must use the canonical tutor-calendar workflow.");
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            throw ApiException.Validation(
                "booking_status_readonly",
                "Learners cannot change booking status through this endpoint. Use reschedule or cancel actions instead.",
                [new ApiFieldError("status", "readonly", "Booking status is controlled by the booking lifecycle service.")]);
        }
        if (booking.Status is MockBookingStatuses.Completed or MockBookingStatuses.Cancelled or MockBookingStatuses.LearnerNoShow or MockBookingStatuses.TutorNoShow)
        {
            throw ApiException.Validation("booking_finalized", "This booking can no longer be changed.");
        }
        if (request.ScheduledStartAt.HasValue && request.ScheduledStartAt.Value != booking.ScheduledStartAt)
        {
            if (booking.RescheduleCount >= MockBookingService.MaxReschedulesPerBooking)
            {
                throw ApiException.Validation("reschedule_cap_reached",
                    $"Bookings can be rescheduled up to {MockBookingService.MaxReschedulesPerBooking} times.");
            }
            if (request.ScheduledStartAt.Value <= DateTimeOffset.UtcNow.AddHours(MockBookingService.MinLeadTimeHours))
            {
                throw ApiException.Validation("lead_time_too_short",
                    $"Reschedule must land at least {MockBookingService.MinLeadTimeHours} hours in the future.");
            }
            booking.ScheduledStartAt = request.ScheduledStartAt.Value.ToUniversalTime();
            booking.RescheduleCount += 1;
            // Re-provision the Zoom meeting for the new time (the job deletes
            // the stale meeting before creating the replacement).
            if (booking.ZoomStatus is not null)
            {
                booking.ZoomStatus = Mocks.MockBookingZoomStatuses.Pending;
                Mocks.MockBookingZoomProvisioner.QueueZoomCreateJob(db, booking.Id);
            }
        }
        if (request.TimezoneIana is not null)
        {
            booking.TimezoneIana = string.IsNullOrWhiteSpace(request.TimezoneIana) ? booking.TimezoneIana : request.TimezoneIana.Trim();
        }
        if (request.ConsentToRecording.HasValue) booking.ConsentToRecording = request.ConsentToRecording.Value;
        if (request.LearnerNotes is not null) booking.LearnerNotes = request.LearnerNotes;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        RecordEvent(userId, "mock_booking_updated", new { bookingId = booking.Id, booking.Status, booking.ScheduledStartAt });
        await db.SaveChangesAsync(ct);
        return ProjectBookingLearner(booking, booking.MockBundle);
    }

    public async Task<object> ListExpertMockBookingsAsync(string expertId, CancellationToken ct)
    {
        var rows = await db.MockBookings.AsNoTracking()
            .Include(x => x.MockBundle)
            .Where(x => x.AssignedTutorId == null || x.AssignedTutorId == expertId || x.AssignedInterlocutorId == expertId)
            .OrderBy(x => x.ScheduledStartAt)
            .Take(100)
            .ToListAsync(ct);
        return new { items = rows.Select(ProjectBookingExpert).ToArray() };
    }

    private async Task<bool> IsSpeakingBundleAsync(string bundleId, CancellationToken ct)
    {
        if (await db.MockBundles.AsNoTracking()
                .AnyAsync(bundle => bundle.Id == bundleId && bundle.SubtestCode == "speaking", ct))
        {
            return true;
        }

        return await db.MockBundleSections.AsNoTracking()
            .AnyAsync(section => section.MockBundleId == bundleId && section.SubtestCode == "speaking", ct);
    }

    private static object ProjectBookingLearner(MockBooking booking, MockBundle? bundle) => new
    {
        id = booking.Id,
        bookingId = booking.Id,
        mockBundleId = booking.MockBundleId,
        mockAttemptId = booking.MockAttemptId,
        mockSectionId = booking.MockSectionId,
        title = bundle?.Title ?? "Scheduled mock",
        scheduledStartAt = booking.ScheduledStartAt,
        timezoneIana = booking.TimezoneIana,
        status = booking.Status,
        deliveryMode = booking.DeliveryMode,
        liveRoomState = booking.LiveRoomState,
        consentToRecording = booking.ConsentToRecording,
        rescheduleCount = booking.RescheduleCount,
        joinUrl = Mocks.MockBookingPresentation.RoomRoute(booking),
        zoomJoinUrl = Mocks.MockBookingPresentation.LearnerZoomJoinUrl(booking),
        learnerNotes = booking.LearnerNotes,
        releasePolicy = bundle?.ReleasePolicy ?? MockReleasePolicies.Instant,
        candidateCardVisible = true,
        interlocutorCardVisible = false
    };

    private static object ProjectBookingExpert(MockBooking booking) => new
    {
        id = booking.Id,
        bookingId = booking.Id,
        learnerId = booking.UserId,
        mockBundleId = booking.MockBundleId,
        mockBundleTitle = booking.MockBundle?.Title,
        scheduledStartAt = booking.ScheduledStartAt,
        timezoneIana = booking.TimezoneIana,
        status = booking.Status,
        liveRoomState = booking.LiveRoomState,
        startUrl = (string?)null,
        zoomStartUrl = Mocks.MockBookingPresentation.ExpertZoomStartUrl(booking),
        joinUrl = Mocks.MockBookingPresentation.LearnerZoomJoinUrl(booking),
        zoomJoinUrl = Mocks.MockBookingPresentation.LearnerZoomJoinUrl(booking),
        consentToRecording = booking.ConsentToRecording,
        candidateCardVisible = true,
        interlocutorCardVisible = true,
        learnerNotes = booking.LearnerNotes
    };
}
