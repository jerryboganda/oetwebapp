using System.Globalization;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using IcalCalendar = Ical.Net.Calendar;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Services;

public sealed partial class PrivateSpeakingService
{
    // ── Learner Queries ─────────────────────────────────────────────────

    public async Task<List<PrivateSpeakingBooking>> GetLearnerBookingsAsync(
        string learnerUserId, string? statusFilter, CancellationToken ct)
    {
        var query = db.PrivateSpeakingBookings
            .Include(b => b.TutorProfile)
            .Where(b => b.LearnerUserId == learnerUserId);

        if (!string.IsNullOrEmpty(statusFilter))
        {
            if (Enum.TryParse<PrivateSpeakingBookingStatus>(statusFilter, true, out var status))
                query = query.Where(b => b.Status == status);
        }

        var bookings = await query.ToListAsync(ct);
        return bookings.OrderByDescending(b => b.SessionStartUtc).ToList();
    }

    public async Task<PrivateSpeakingBooking?> GetBookingAsync(
        string bookingId, CancellationToken ct)
        => await db.PrivateSpeakingBookings
            .Include(b => b.TutorProfile)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

    // ── Expert Queries ──────────────────────────────────────────────────

    public async Task<List<PrivateSpeakingBooking>> GetExpertBookingsAsync(
        string expertUserId, string? statusFilter, CancellationToken ct)
    {
        var profile = await db.PrivateSpeakingTutorProfiles
            .FirstOrDefaultAsync(p => p.ExpertUserId == expertUserId, ct);
        if (profile is null) return [];

        var query = db.PrivateSpeakingBookings
            .Include(b => b.TutorProfile)
            .Where(b => b.TutorProfileId == profile.Id);

        if (!string.IsNullOrEmpty(statusFilter))
        {
            if (Enum.TryParse<PrivateSpeakingBookingStatus>(statusFilter, true, out var status))
                query = query.Where(b => b.Status == status);
        }

        var bookings = await query.ToListAsync(ct);
        return bookings.OrderByDescending(b => b.SessionStartUtc).ToList();
    }

    public async Task<PrivateSpeakingCalendarInvite> BuildCalendarInviteAsync(
        string bookingId,
        string actorId,
        string actorRole,
        CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings
            .AsNoTracking()
            .Include(item => item.TutorProfile)
            .FirstOrDefaultAsync(item => item.Id == bookingId, ct)
            ?? throw ApiException.NotFound("private_speaking_booking_not_found", "Private speaking booking not found.");

        if (actorRole == "learner" && booking.LearnerUserId != actorId)
        {
            throw ApiException.NotFound("private_speaking_booking_not_found", "Private speaking booking not found.");
        }

        if (actorRole == "expert" && booking.TutorProfile?.ExpertUserId != actorId)
        {
            throw ApiException.NotFound("private_speaking_booking_not_found", "Private speaking booking not found.");
        }

        var calendar = new IcalCalendar { Method = "REQUEST" };
        var title = $"OET Private Speaking Session with {booking.TutorProfile?.DisplayName ?? "Tutor"}";
        var ev = new CalendarEvent
        {
            Uid = $"oet-private-speaking-{booking.Id}@oetlearner",
            Summary = title,
            Description = "OET private speaking session. Join from your OET dashboard when the room opens.",
            Start = new CalDateTime(booking.SessionStartUtc.UtcDateTime, "UTC"),
            End = new CalDateTime(booking.SessionStartUtc.AddMinutes(booking.DurationMinutes).UtcDateTime, "UTC"),
            Location = "OET dashboard",
            DtStamp = new CalDateTime(timeProvider.GetUtcNow().UtcDateTime, "UTC"),
        };

        if (!string.IsNullOrWhiteSpace(booking.LearnerTimezone) && !string.Equals(booking.LearnerTimezone, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                ev.Start = new CalDateTime(booking.SessionStartUtc.UtcDateTime, booking.LearnerTimezone);
                ev.End = new CalDateTime(booking.SessionStartUtc.AddMinutes(booking.DurationMinutes).UtcDateTime, booking.LearnerTimezone);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Falling back to UTC for private speaking ics TZID {Timezone}", booking.LearnerTimezone);
                ev.Start = new CalDateTime(booking.SessionStartUtc.UtcDateTime, "UTC");
                ev.End = new CalDateTime(booking.SessionStartUtc.AddMinutes(booking.DurationMinutes).UtcDateTime, "UTC");
            }
        }

        calendar.Events.Add(ev);
        var serializer = new CalendarSerializer();
        var ics = serializer.SerializeToString(calendar) ?? string.Empty;
        var fileName = $"oet-private-speaking-{booking.Id}.ics";
        return new PrivateSpeakingCalendarInvite(fileName, "text/calendar; method=REQUEST", ics);
    }

    // ── Admin Queries ───────────────────────────────────────────────────

    public async Task<List<PrivateSpeakingBooking>> GetAllBookingsAsync(
        string? tutorProfileId, string? statusFilter, string? learnerUserId,
        DateOnly? fromDate, DateOnly? toDate,
        int page, int pageSize, CancellationToken ct)
    {
        var query = db.PrivateSpeakingBookings
            .Include(b => b.TutorProfile)
            .AsQueryable();

        if (!string.IsNullOrEmpty(tutorProfileId))
            query = query.Where(b => b.TutorProfileId == tutorProfileId);
        if (!string.IsNullOrEmpty(learnerUserId))
            query = query.Where(b => b.LearnerUserId == learnerUserId);
        if (!string.IsNullOrEmpty(statusFilter) && Enum.TryParse<PrivateSpeakingBookingStatus>(statusFilter, true, out var status))
            query = query.Where(b => b.Status == status);
        var bookings = await query.ToListAsync(ct);
        if (fromDate.HasValue)
        {
            var fromUtc = fromDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            bookings = bookings.Where(b => b.SessionStartUtc >= fromUtc).ToList();
        }
        if (toDate.HasValue)
        {
            var toUtc = toDate.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            bookings = bookings.Where(b => b.SessionStartUtc <= toUtc).ToList();
        }

        return bookings
            .OrderByDescending(b => b.SessionStartUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
    }

    public async Task<int> GetBookingCountAsync(
        string? tutorProfileId, string? statusFilter, CancellationToken ct)
    {
        var query = db.PrivateSpeakingBookings.AsQueryable();
        if (!string.IsNullOrEmpty(tutorProfileId))
            query = query.Where(b => b.TutorProfileId == tutorProfileId);
        if (!string.IsNullOrEmpty(statusFilter) && Enum.TryParse<PrivateSpeakingBookingStatus>(statusFilter, true, out var status))
            query = query.Where(b => b.Status == status);
        return await query.CountAsync(ct);
    }

    public async Task<List<PrivateSpeakingAuditLog>> GetAuditLogsAsync(
        string? bookingId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.PrivateSpeakingAuditLogs.AsQueryable();
        if (!string.IsNullOrEmpty(bookingId))
            query = query.Where(l => l.BookingId == bookingId);
        return await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    // ── Session Completion ──────────────────────────────────────────────

    public async Task MarkSessionCompletedAsync(string bookingId, string actorId, CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings.FindAsync([bookingId], ct);
        if (booking is null) return;

        booking.Status = PrivateSpeakingBookingStatus.Completed;
        booking.CompletedAt = timeProvider.GetUtcNow();
        booking.UpdatedAt = timeProvider.GetUtcNow();

        if (booking.TutorProfile is null)
        {
            var profile = await db.PrivateSpeakingTutorProfiles.FindAsync([booking.TutorProfileId], ct);
            if (profile is not null) profile.TotalSessions++;
        }

        await db.SaveChangesAsync(ct);
        await AuditAsync(booking.Id, actorId, "system", "session_completed", null, ct);
    }

    // ── Admin / Tutor Booking Actions (PDF §7.1 / §7.3 / §11) ───────────

    /// <summary>
    /// Admin edits booking metadata only: scheduling time, duration, profession
    /// track, tutor notes. Only non-null fields are applied. This does NOT change
    /// Status or payment state. LiveKit room provisioning is owned by the
    /// speaking-room lifecycle worker.
    /// </summary>
    public async Task<PrivateSpeakingBooking?> AdminEditBookingAsync(
        string bookingId, string adminId,
        DateTimeOffset? sessionStartUtc, int? durationMinutes,
        string? professionTrack, string? tutorNotes, CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings
            .Include(b => b.TutorProfile)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null) return null;

        var now = timeProvider.GetUtcNow();
        var scheduleChanged = sessionStartUtc.HasValue && sessionStartUtc.Value != booking.SessionStartUtc;
        var durationChanged = durationMinutes.HasValue && durationMinutes.Value != booking.DurationMinutes;
        if (scheduleChanged || durationChanged)
        {
            if (booking.Status is PrivateSpeakingBookingStatus.Cancelled
                or PrivateSpeakingBookingStatus.Refunded
                or PrivateSpeakingBookingStatus.Completed
                or PrivateSpeakingBookingStatus.NoShow)
                throw ApiException.Conflict("booking_finalized", "This booking cannot be rescheduled in its current state.");
            if (!SpeakingBookingPolicy.RescheduleAllowed(booking.SessionStartUtc, now))
                throw ApiException.Conflict("booking_started", "Bookings cannot be rescheduled after the session has started.");
            if (booking.TutorProfile is null || !booking.TutorProfile.IsActive)
                throw ApiException.Conflict("tutor_unavailable", "The assigned tutor is no longer available.");

            var targetStart = sessionStartUtc ?? booking.SessionStartUtc;
            var targetDuration = durationMinutes ?? booking.DurationMinutes;
            if (targetDuration <= 0)
                throw ApiException.Validation("invalid_duration", "durationMinutes must be positive.");
            var config = await GetConfigAsync(ct);
            if (targetStart > now.AddDays(config.MaxBookingAdvanceDays))
                throw ApiException.Validation("advance_window_exceeded", "The booking is too far in advance.");
            if (!await IsRequestedSlotAvailableAsync(
                    booking.TutorProfile, targetStart, targetDuration, ct, booking.Id))
                throw ApiException.Conflict(
                    "tutor_slot_unavailable",
                    "Scheduling changes are allowed only on a currently available tutor-calendar slot.");
        }

        var changes = new List<string>();
        if (scheduleChanged)
        {
            booking.SessionStartUtc = sessionStartUtc.Value;
            changes.Add($"sessionStartUtc={sessionStartUtc.Value:O}");
        }
        if (durationChanged)
        {
            booking.DurationMinutes = durationMinutes.Value;
            changes.Add($"durationMinutes={durationMinutes.Value}");
        }
        if (professionTrack is not null)
        {
            booking.ProfessionTrack = NormalizeProfessionTrack(professionTrack);
            changes.Add($"professionTrack={booking.ProfessionTrack}");
        }
        if (tutorNotes is not null)
        {
            booking.TutorNotes = tutorNotes;
            changes.Add("tutorNotes=updated");
        }

        booking.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await AuditAsync(booking.Id, adminId, "admin", "admin_booking_edited",
            changes.Count == 0 ? "no_changes" : string.Join(", ", changes), ct);

        if (scheduleChanged || durationChanged)
        {
            booking.ZoomMeetingId = null;
            booking.ZoomJoinUrl = null;
            booking.ZoomStartUrl = null;
            booking.ZoomMeetingPassword = null;
            booking.ZoomStatus = PrivateSpeakingZoomStatus.Pending;
            booking.ZoomError = null;
            booking.Status = PrivateSpeakingBookingStatus.Confirmed;
            QueueBookingPostCommitJobs(booking.Id, includeCalendarSync: false);
            QueueCalendarSyncJob(booking.Id);
            await db.SaveChangesAsync(ct);
        }

        return booking;
    }

    /// <summary>
    /// Admin performs the same strict full-refund eligibility check as the learner.
    /// Restores an unconsumed-and-not-rescheduled entitlement,
    /// issues a Stripe refund for direct-paid bookings, and flips the booking to
    /// Refunded. Rejects bookings already in the Refunded state.
    /// </summary>
    public async Task<(bool Success, string? Error)> OverrideRefundAsync(
        string bookingId, string adminId,
        int? amountMinorUnits, string? reason, CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings.FindAsync([bookingId], ct);
        if (booking is null) return (false, "Booking not found.");
        if (booking.Status == PrivateSpeakingBookingStatus.Refunded)
            return (false, "Booking has already been refunded.");

        var now = timeProvider.GetUtcNow();
        if (!SpeakingBookingPolicy.FullRefundEligible(booking.SessionStartUtc, now))
            return (false, "A full refund is available only when cancellation is made more than 24 hours before the scheduled start time.");
        if (amountMinorUnits.HasValue && amountMinorUnits.Value != booking.PriceMinorUnits)
            return (false, "Only a full refund is permitted by the Speaking booking policy.");

        if (!string.IsNullOrWhiteSpace(booking.StripePaymentIntentId) && booking.PriceMinorUnits > 0)
        {
            try
            {
                var refundId = await stripeService.CreateRefundAsync(
                    booking.StripePaymentIntentId,
                    amountMinorUnits ?? booking.PriceMinorUnits,
                    reason ?? "admin_override",
                    ct);
                booking.StripeRefundId = refundId;
                booking.RefundAmountMinorUnits = amountMinorUnits ?? booking.PriceMinorUnits;
                booking.PaymentStatus = PrivateSpeakingPaymentStatus.Refunded;
            }
            catch (Exception ex)
            {
                // Do not fail the override — leave StripeRefundId null so an admin
                // can retry the money refund out of band; the entitlement and
                // status changes still apply.
                logger.LogWarning(ex,
                    "Stripe override refund failed for booking {BookingId}; refund remains incomplete",
                    booking.Id);
                return (false, "The full refund could not be completed, so the booking remains active. Please retry.");
            }
        }

        booking.RefundIssued = true;
        if (booking.EntitlementConsumed
            && booking.EntitlementRestoredAt is null
            && booking.RescheduledToBookingId is null)
        {
            await RestoreSpeakingEntitlementAsync(booking, "admin_override_refund", ct);
        }

        booking.Status = PrivateSpeakingBookingStatus.Refunded;
        booking.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await AuditAsync(booking.Id, adminId, "admin", "admin_override_refund",
            $"Amount: {amountMinorUnits?.ToString() ?? "full"}, Reason: {reason ?? "admin_override"}", ct);
        return (true, null);
    }

    /// <summary>
    /// Admin moves a booking to a new time IN-PLACE, subject to future,
    /// advance-window, and canonical tutor-calendar availability checks. The
    /// booking remains Confirmed and its LiveKit room is provisioned by the
    /// speaking-room lifecycle worker. Rejects terminal-state bookings.
    /// </summary>
    public async Task<(bool Success, string? Error)> AdminManualRescheduleAsync(
        string bookingId, string adminId,
        DateTimeOffset newSessionStartUtc, string? reason, CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings
            .Include(b => b.TutorProfile)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null) return (false, "Booking not found.");

        if (booking.Status is PrivateSpeakingBookingStatus.Cancelled
            or PrivateSpeakingBookingStatus.Refunded
            or PrivateSpeakingBookingStatus.Completed
            or PrivateSpeakingBookingStatus.NoShow)
            return (false, "Booking cannot be rescheduled in its current state.");

        var now = timeProvider.GetUtcNow();
        if (!SpeakingBookingPolicy.RescheduleAllowed(booking.SessionStartUtc, now))
            return (false, "This session has already started and can no longer be rescheduled.");
        var config = await GetConfigAsync(ct);
        if (newSessionStartUtc > now.AddDays(config.MaxBookingAdvanceDays))
            return (false, $"Sessions cannot be rescheduled more than {config.MaxBookingAdvanceDays} days in advance.");
        if (booking.TutorProfile is null || !booking.TutorProfile.IsActive)
            return (false, "The assigned tutor is no longer available.");
        if (!await IsRequestedSlotAvailableAsync(
                booking.TutorProfile, newSessionStartUtc, booking.DurationMinutes, ct, booking.Id))
            return (false, "Rescheduling is available only to a slot currently open in the tutor calendar.");
        booking.SessionStartUtc = newSessionStartUtc;
        booking.UpdatedAt = now;

        // Clear legacy provider fields. LiveKit rooms are linked through the
        // speaking exam session and are provisioned by the lifecycle worker.
        booking.ZoomMeetingId = null;
        booking.ZoomJoinUrl = null;
        booking.ZoomStartUrl = null;
        booking.ZoomMeetingPassword = null;
        booking.ZoomStatus = PrivateSpeakingZoomStatus.Pending;
        booking.ZoomError = null;
        booking.Status = PrivateSpeakingBookingStatus.Confirmed;

        await db.SaveChangesAsync(ct);
        await AuditAsync(booking.Id, adminId, "admin", "admin_manual_reschedule",
            $"New start: {newSessionStartUtc:O}, Reason: {reason ?? "admin_manual_reschedule"}", ct);

        // Re-queue booking notification and calendar sync at the new time.
        QueueBookingPostCommitJobs(booking.Id, includeCalendarSync: false);
        QueueCalendarSyncJob(booking.Id);
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// Mark a booking as a no-show (only valid from Confirmed/ZoomCreated/InProgress).
    /// No refund or entitlement restore — a no-show forfeits the session.
    /// </summary>
    public async Task<(bool Success, string? Error)> MarkNoShowAsync(
        string bookingId, string actorId, string actorRole, CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings
            .Include(b => b.TutorProfile)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null) return (false, "Booking not found.");

        if (booking.Status is not (PrivateSpeakingBookingStatus.Confirmed
            or PrivateSpeakingBookingStatus.ZoomCreated
            or PrivateSpeakingBookingStatus.InProgress))
            return (false, "Booking cannot be marked as a no-show in its current state.");

        var now = timeProvider.GetUtcNow();
        booking.Status = PrivateSpeakingBookingStatus.NoShow;
        booking.CompletedAt = null;
        booking.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await AuditAsync(booking.Id, actorId, actorRole, "marked_no_show", null, ct);

        // PDF §10 no-show notifications. The T5 auto-sweep calls this method too, so
        // it inherits these. No-show forfeits the session per policy (no refund).
        var sessionTime = booking.SessionStartUtc.ToString("yyyy-MM-dd HH:mm 'UTC'");

        await notificationService.CreateForLearnerAsync(
            NotificationEventKey.LearnerPrivateSpeakingNoShow,
            booking.LearnerUserId,
            "private_speaking_booking",
            booking.Id,
            $"noshow-{now:yyyyMMddHHmm}",
            new Dictionary<string, object?>
            {
                ["sessionTime"] = sessionTime,
                ["bookingId"] = booking.Id
            },
            ct);

        if (booking.TutorProfile?.ExpertUserId is not null)
        {
            await notificationService.CreateForExpertAsync(
                NotificationEventKey.ExpertPrivateSpeakingNoShow,
                booking.TutorProfile.ExpertUserId,
                "private_speaking_booking",
                booking.Id,
                $"noshow-{now:yyyyMMddHHmm}",
                new Dictionary<string, object?>
                {
                    ["sessionTime"] = sessionTime,
                    ["bookingId"] = booking.Id
                },
                ct);
        }

        return (true, null);
    }

    /// <summary>
    /// Export bookings matching the supplied filters as an RFC4180 CSV string
    /// (header + rows, capped at 5000 rows). Mirrors the
    /// <see cref="GetAllBookingsAsync"/> filter shape without paging.
    /// </summary>
    public async Task<string> ExportBookingsCsvAsync(
        string? tutorProfileId, string? status, string? learnerId,
        DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        const int MaxRows = 5000;

        var query = db.PrivateSpeakingBookings
            .Include(b => b.TutorProfile)
            .AsQueryable();

        if (!string.IsNullOrEmpty(tutorProfileId))
            query = query.Where(b => b.TutorProfileId == tutorProfileId);
        if (!string.IsNullOrEmpty(learnerId))
            query = query.Where(b => b.LearnerUserId == learnerId);
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<PrivateSpeakingBookingStatus>(status, true, out var parsedStatus))
            query = query.Where(b => b.Status == parsedStatus);

        var bookings = await query.ToListAsync(ct);
        if (from.HasValue)
        {
            var fromUtc = from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            bookings = bookings.Where(b => b.SessionStartUtc >= fromUtc).ToList();
        }
        if (to.HasValue)
        {
            var toUtc = to.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            bookings = bookings.Where(b => b.SessionStartUtc <= toUtc).ToList();
        }

        bookings = bookings
            .OrderByDescending(b => b.SessionStartUtc)
            .Take(MaxRows)
            .ToList();

        var sb = new StringBuilder();
        sb.Append("Id,LearnerUserId,TutorProfileId,TutorName,Status,SessionStartUtc,DurationMinutes,")
          .Append("ProfessionTrack,PriceMinorUnits,Currency,PaymentStatus,RefundIssued,")
          .Append("RefundAmountMinorUnits,PenaltyAmountMinorUnits,CreatedAt")
          .Append('\n');

        foreach (var b in bookings)
        {
            sb.Append(CsvEscape(b.Id)).Append(',')
              .Append(CsvEscape(b.LearnerUserId)).Append(',')
              .Append(CsvEscape(b.TutorProfileId)).Append(',')
              .Append(CsvEscape(b.TutorProfile?.DisplayName)).Append(',')
              .Append(CsvEscape(b.Status.ToString())).Append(',')
              .Append(CsvEscape(b.SessionStartUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',')
              .Append(CsvEscape(b.DurationMinutes.ToString(CultureInfo.InvariantCulture))).Append(',')
              .Append(CsvEscape(b.ProfessionTrack)).Append(',')
              .Append(CsvEscape(b.PriceMinorUnits.ToString(CultureInfo.InvariantCulture))).Append(',')
              .Append(CsvEscape(b.Currency)).Append(',')
              .Append(CsvEscape(b.PaymentStatus.ToString())).Append(',')
              .Append(CsvEscape(b.RefundIssued ? "true" : "false")).Append(',')
              .Append(CsvEscape(b.RefundAmountMinorUnits?.ToString(CultureInfo.InvariantCulture))).Append(',')
              .Append(CsvEscape(b.PenaltyAmountMinorUnits?.ToString(CultureInfo.InvariantCulture))).Append(',')
              .Append(CsvEscape(b.CreatedAt.ToString("O", CultureInfo.InvariantCulture)))
              .Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>RFC4180 CSV field escaping plus CSV-injection neutralization: a
    /// value beginning with a formula trigger (=, +, -, @, TAB or CR) is prefixed
    /// with a single quote so spreadsheet apps treat it as text — user-controlled
    /// columns (tutor display name, profession track) would otherwise execute when
    /// the export is opened in Excel/Sheets. Then wrap in quotes and double internal
    /// quotes when the value contains a comma, quote, CR or LF.</summary>
    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public async Task RateSessionAsync(
        string bookingId, string learnerUserId, int rating, string? feedback, CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings.FindAsync([bookingId], ct);
        if (booking is null || booking.LearnerUserId != learnerUserId)
            throw new InvalidOperationException("Booking not found or access denied.");

        booking.LearnerRating = rating;
        booking.LearnerFeedback = feedback;
        booking.UpdatedAt = timeProvider.GetUtcNow();

        // Update tutor average rating
        var profile = await db.PrivateSpeakingTutorProfiles.FindAsync([booking.TutorProfileId], ct);
        if (profile is not null)
        {
            var allRatings = await db.PrivateSpeakingBookings
                .Where(b => b.TutorProfileId == profile.Id && b.LearnerRating.HasValue)
                .Select(b => b.LearnerRating!.Value)
                .ToListAsync(ct);
            allRatings.Add(rating);
            profile.AverageRating = allRatings.Average();
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Admin Dashboard Stats ───────────────────────────────────────────

    public async Task<PrivateSpeakingDashboardStats> GetDashboardStatsAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var thirtyDaysAgo = now.AddDays(-30);

        return new PrivateSpeakingDashboardStats(
            TotalBookings: await db.PrivateSpeakingBookings.CountAsync(ct),
            ConfirmedBookings: await db.PrivateSpeakingBookings
                .CountAsync(b => b.Status == PrivateSpeakingBookingStatus.Confirmed
                    || b.Status == PrivateSpeakingBookingStatus.ZoomCreated, ct),
            CompletedBookings: await db.PrivateSpeakingBookings
                .CountAsync(b => b.Status == PrivateSpeakingBookingStatus.Completed, ct),
            CancelledBookings: await db.PrivateSpeakingBookings
                .CountAsync(b => b.Status == PrivateSpeakingBookingStatus.Cancelled, ct),
            FailedPayments: await db.PrivateSpeakingBookings
                .CountAsync(b => b.PaymentStatus == PrivateSpeakingPaymentStatus.Failed, ct),
            ZoomFailures: await db.PrivateSpeakingBookings
                .CountAsync(b => b.ZoomStatus == PrivateSpeakingZoomStatus.Failed, ct),
            ActiveTutors: await db.PrivateSpeakingTutorProfiles.CountAsync(p => p.IsActive, ct),
            UpcomingSessions: await db.PrivateSpeakingBookings
                .CountAsync(b => b.SessionStartUtc > now
                    && (b.Status == PrivateSpeakingBookingStatus.Confirmed
                        || b.Status == PrivateSpeakingBookingStatus.ZoomCreated), ct),
            RevenueMinorUnitsLast30Days: await db.PrivateSpeakingBookings
                .Where(b => b.PaymentConfirmedAt >= thirtyDaysAgo
                    && b.PaymentStatus == PrivateSpeakingPaymentStatus.Succeeded)
                .SumAsync(b => b.PriceMinorUnits, ct));
    }
}
