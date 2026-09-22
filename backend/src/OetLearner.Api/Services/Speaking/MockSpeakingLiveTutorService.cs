using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

public sealed class MockSpeakingLiveTutorService(
    LearnerDbContext db,
    SpeakingExamService exams)
{
    public async Task<PrivateSpeakingBooking> EnsureCanonicalBookingAsync(
        MockBooking booking,
        MockBundle bundle,
        PrivateSpeakingTutorProfile tutor,
        CancellationToken ct)
    {
        await ValidateBundlePublishedAsync(bundle, ct);
        ValidateBooking(booking, bundle, tutor);

        var now = DateTimeOffset.UtcNow;
        var durationMinutes = bundle.EstimatedDurationMinutes > 0
            ? bundle.EstimatedDurationMinutes
            : 30;
        var currency = await db.PrivateSpeakingConfigs.AsNoTracking()
            .Select(config => config.Currency)
            .FirstOrDefaultAsync(ct) ?? "GBP";
        var profession = string.IsNullOrWhiteSpace(bundle.ProfessionId)
            ? "medicine"
            : bundle.ProfessionId.Trim().ToLowerInvariant();

        var canonical = await db.PrivateSpeakingBookings
            .FirstOrDefaultAsync(item => item.Id == booking.Id, ct);

        if (canonical is null)
        {
            canonical = new PrivateSpeakingBooking
            {
                Id = booking.Id,
                LearnerUserId = booking.UserId,
                TutorProfileId = tutor.Id,
                Status = PrivateSpeakingBookingStatus.Confirmed,
                SessionStartUtc = booking.ScheduledStartAt,
                DurationMinutes = durationMinutes,
                SessionFormat = "exam",
                TutorTimezone = tutor.Timezone,
                LearnerTimezone = string.IsNullOrWhiteSpace(booking.TimezoneIana)
                    ? "UTC"
                    : booking.TimezoneIana,
                PriceMinorUnits = 0,
                Currency = currency,
                PaymentStatus = PrivateSpeakingPaymentStatus.Succeeded,
                PaymentConfirmedAt = now,
                ProfessionTrack = profession,
                IdempotencyKey = $"mock-booking:{booking.Id}",
                LearnerNotes = booking.LearnerNotes,
                CreatedAt = booking.CreatedAt == default ? now : booking.CreatedAt,
                UpdatedAt = now,
            };
            db.PrivateSpeakingBookings.Add(canonical);
            return canonical;
        }

        if (canonical.LearnerUserId != booking.UserId
            || canonical.TutorProfileId != tutor.Id)
        {
            throw ApiException.Conflict(
                "livekit_booking_identity_conflict",
                "This Speaking booking is already linked to a different learner or tutor.");
        }

        if (canonical.Status is PrivateSpeakingBookingStatus.Cancelled
            or PrivateSpeakingBookingStatus.Completed
            or PrivateSpeakingBookingStatus.Refunded
            or PrivateSpeakingBookingStatus.NoShow)
        {
            throw ApiException.Conflict(
                "livekit_booking_finalized",
                "This Speaking booking is already finalized.");
        }

        canonical.Status = PrivateSpeakingBookingStatus.Confirmed;
        canonical.SessionStartUtc = booking.ScheduledStartAt;
        canonical.DurationMinutes = durationMinutes;
        canonical.SessionFormat = "exam";
        canonical.TutorTimezone = tutor.Timezone;
        canonical.LearnerTimezone = string.IsNullOrWhiteSpace(booking.TimezoneIana)
            ? "UTC"
            : booking.TimezoneIana;
        canonical.PaymentStatus = PrivateSpeakingPaymentStatus.Succeeded;
        canonical.PaymentConfirmedAt ??= now;
        canonical.ProfessionTrack = profession;
        canonical.LearnerNotes = booking.LearnerNotes;
        canonical.UpdatedAt = now;
        return canonical;
    }

    public async Task<SpeakingExamDetail> CreateLearnerExamAsync(
        string userId,
        string bookingId,
        CancellationToken ct)
    {
        var booking = await LoadBookingAsync(bookingId, ct);
        if (!string.Equals(booking.UserId, userId, StringComparison.Ordinal))
        {
            throw ApiException.NotFound("mock_booking_not_found", "Mock booking not found.");
        }

        var tutor = await LoadTutorAsync(booking, ct);
        await EnsureCanonicalBookingAsync(booking, booking.MockBundle!, tutor, ct);
        await db.SaveChangesAsync(ct);
        return await exams.CreateExamForBookingAsync(userId, booking.Id, ct);
    }

    public async Task<SpeakingExamDetail> CreateTutorExamAsync(
        string expertUserId,
        string bookingId,
        CancellationToken ct)
    {
        var booking = await LoadBookingAsync(bookingId, ct);
        var tutor = await LoadTutorAsync(booking, ct);
        if (!string.Equals(tutor.ExpertUserId, expertUserId, StringComparison.Ordinal))
        {
            throw ApiException.NotFound("mock_booking_not_found", "Mock booking not found.");
        }

        await EnsureCanonicalBookingAsync(booking, booking.MockBundle!, tutor, ct);
        await db.SaveChangesAsync(ct);
        return await exams.CreateExamForTutorFromBookingAsync(expertUserId, booking.Id, ct);
    }

    public async Task SyncRescheduleAsync(MockBooking booking, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(booking.TutorProfileId)) return;

        var bundle = await db.MockBundles
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == booking.MockBundleId, ct)
            ?? throw ApiException.NotFound("bundle_not_found", "Mock bundle not found.");
        var tutor = await LoadTutorAsync(booking, ct);
        var canonical = await EnsureCanonicalBookingAsync(booking, bundle, tutor, ct);
        canonical.SessionStartUtc = booking.ScheduledStartAt;
        canonical.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public async Task SyncCancellationAsync(MockBooking booking, string actorId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(booking.TutorProfileId)) return;

        var bundle = await db.MockBundles
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == booking.MockBundleId, ct)
            ?? throw ApiException.NotFound("bundle_not_found", "Mock bundle not found.");
        var tutor = await LoadTutorAsync(booking, ct);
        var canonical = await EnsureCanonicalBookingAsync(booking, bundle, tutor, ct);
        var now = DateTimeOffset.UtcNow;
        if (canonical.Status is not (PrivateSpeakingBookingStatus.Cancelled
            or PrivateSpeakingBookingStatus.Completed
            or PrivateSpeakingBookingStatus.Refunded
            or PrivateSpeakingBookingStatus.NoShow))
        {
            canonical.Status = PrivateSpeakingBookingStatus.Cancelled;
            canonical.CancelledBy = actorId;
            canonical.CancelledAt = now;
            canonical.CancellationReason = "mock_booking_cancelled";
            canonical.UpdatedAt = now;
        }
    }

    private async Task<MockBooking> LoadBookingAsync(string bookingId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bookingId))
        {
            throw ApiException.Validation("mock_booking_id_required", "Mock booking id is required.");
        }

        var booking = await db.MockBookings
            .Include(item => item.MockBundle)
            .FirstOrDefaultAsync(item => item.Id == bookingId, ct)
            ?? throw ApiException.NotFound("mock_booking_not_found", "Mock booking not found.");

        if (booking.MockBundle is null)
        {
            throw ApiException.NotFound("bundle_not_found", "Mock bundle not found.");
        }

        return booking;
    }

    private async Task<PrivateSpeakingTutorProfile> LoadTutorAsync(
        MockBooking booking,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(booking.TutorProfileId))
        {
            throw ApiException.Conflict(
                "livekit_tutor_required",
                "A canonical tutor assignment is required for a live Speaking room.");
        }

        return await db.PrivateSpeakingTutorProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(profile => profile.Id == booking.TutorProfileId && profile.IsActive, ct)
            ?? throw ApiException.Conflict("tutor_unavailable", "The assigned tutor is no longer available.");
    }

    private async Task ValidateBundlePublishedAsync(MockBundle bundle, CancellationToken ct)
    {
        var isSpeaking = string.Equals(bundle.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase)
            || await db.MockBundleSections.AsNoTracking().AnyAsync(section =>
                section.MockBundleId == bundle.Id
                && section.SubtestCode == "speaking", ct);
        if (!isSpeaking || bundle.Status != ContentStatus.Published)
        {
            throw ApiException.Conflict(
                "published_speaking_bundle_required",
                "Only a published Speaking mock can open a live tutor room.");
        }
    }

    private void ValidateBooking(
        MockBooking booking,
        MockBundle bundle,
        PrivateSpeakingTutorProfile tutor)
    {
        if (booking.Status is MockBookingStatuses.Cancelled
            or MockBookingStatuses.Completed
            or MockBookingStatuses.LearnerNoShow
            or MockBookingStatuses.TutorNoShow)
        {
            throw ApiException.Conflict("mock_booking_finalized", "This mock booking is already finalized.");
        }

        if (!string.Equals(booking.TutorProfileId, tutor.Id, StringComparison.Ordinal))
        {
            throw ApiException.Conflict("livekit_booking_identity_conflict", "The tutor assignment is invalid.");
        }
    }
}
