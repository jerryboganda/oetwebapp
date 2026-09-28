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
    // ── Retired provider job compatibility ──────────────────────────────

    public async Task CreateZoomMeetingForBookingAsync(string bookingId, CancellationToken ct)
    {
        logger.LogWarning(
            "Ignoring legacy Zoom provisioning request for private Speaking booking {BookingId}; LiveKit is authoritative",
            bookingId);
    }

    // ── Notifications ───────────────────────────────────────────────────

    public async Task SendBookingConfirmationNotificationsAsync(string bookingId, CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings
            .Include(b => b.TutorProfile)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null
            || booking.Status is not (PrivateSpeakingBookingStatus.Confirmed
                or PrivateSpeakingBookingStatus.ZoomCreated
                or PrivateSpeakingBookingStatus.InProgress))
        {
            return;
        }

        var tutorName = booking.TutorProfile?.DisplayName ?? "Tutor";
        var sessionTime = booking.SessionStartUtc.ToString("yyyy-MM-dd HH:mm 'UTC'");

        // Notify learner
        await notificationService.CreateForLearnerAsync(
            NotificationEventKey.LearnerPrivateSpeakingBooked,
            booking.LearnerUserId,
            "private_speaking_booking",
            booking.Id,
            booking.CreatedAt.ToString("yyyyMMdd"),
            new Dictionary<string, object?>
            {
                ["tutorName"] = tutorName,
                ["sessionTime"] = sessionTime,
                ["duration"] = booking.DurationMinutes.ToString(),
                ["bookingId"] = booking.Id
            },
            ct);

        // Notify tutor
        if (booking.TutorProfile?.ExpertUserId is not null)
        {
            await notificationService.CreateForExpertAsync(
                NotificationEventKey.ExpertPrivateSpeakingAssigned,
                booking.TutorProfile.ExpertUserId,
                "private_speaking_booking",
                booking.Id,
                booking.CreatedAt.ToString("yyyyMMdd"),
                new Dictionary<string, object?>
                {
                    ["sessionTime"] = sessionTime,
                    ["duration"] = booking.DurationMinutes.ToString(),
                    ["bookingId"] = booking.Id
                },
                ct);
        }

        // Notify admins
        await notificationService.CreateForAdminsAsync(
            NotificationEventKey.AdminPrivateSpeakingBooked,
            "private_speaking_booking",
            booking.Id,
            booking.CreatedAt.ToString("yyyyMMdd"),
            new Dictionary<string, object?>
            {
                ["tutorName"] = tutorName,
                ["sessionTime"] = sessionTime,
                ["bookingId"] = booking.Id
            },
            ct);

        if (booking.RescheduledFromBookingId is not null)
        {
            await SendRescheduleConfirmationNotificationsAsync(booking.Id, ct);
        }
    }

    /// <summary>
    /// PDF §10 reschedule confirmation: notify the learner and the tutor that a
    /// private speaking session has been moved to a new start time. For a same-day
    /// penalty reschedule the learner message also surfaces the penalty amount.
    /// </summary>
    public async Task SendRescheduleConfirmationNotificationsAsync(string bookingId, CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings
            .Include(b => b.TutorProfile)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null
            || booking.Status is not (PrivateSpeakingBookingStatus.Confirmed
                or PrivateSpeakingBookingStatus.ZoomCreated
                or PrivateSpeakingBookingStatus.InProgress))
        {
            return;
        }

        var sessionTime = booking.SessionStartUtc.ToString("yyyy-MM-dd HH:mm 'UTC'");
        var dedupeBucket = booking.UpdatedAt.ToString("yyyyMMddHHmm");

        // Notify learner
        await notificationService.CreateForLearnerAsync(
            NotificationEventKey.LearnerPrivateSpeakingRescheduled,
            booking.LearnerUserId,
            "private_speaking_booking",
            booking.Id,
            dedupeBucket,
            new Dictionary<string, object?>
            {
                ["sessionTime"] = sessionTime,
                ["bookingId"] = booking.Id,
                // The current workflow has no reschedule penalty. Keep the
                // contract stable for older notification templates without
                // exposing a legacy penalty amount.
                ["penalty"] = string.Empty
            },
            ct);

        // Notify tutor
        if (booking.TutorProfile?.ExpertUserId is not null)
        {
            await notificationService.CreateForExpertAsync(
                NotificationEventKey.ExpertPrivateSpeakingRescheduled,
                booking.TutorProfile.ExpertUserId,
                "private_speaking_booking",
                booking.Id,
                dedupeBucket,
                new Dictionary<string, object?>
                {
                    ["sessionTime"] = sessionTime,
                    ["bookingId"] = booking.Id
                },
                ct);
        }
    }

    // ── Reminder Processing ─────────────────────────────────────────────

    /// <summary>
    /// Process scheduled reminders for upcoming sessions. PDF §10 mandates reminders
    /// at 24h, 1h, and 15 minutes before the session start, so offsets are expressed
    /// in MINUTES (default [1440, 60, 15]) to support sub-hour granularity.
    /// </summary>
    public async Task ProcessRemindersAsync(CancellationToken ct)
    {
        var config = await GetConfigAsync(ct);
        var now = timeProvider.GetUtcNow();

        int[] reminderOffsets;
        try
        {
            reminderOffsets = JsonSerializer.Deserialize<int[]>(config.ReminderOffsetsMinutesJson) ?? [1440, 60, 15];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Malformed ReminderOffsetsMinutesJson '{Json}'; falling back to default offsets [1440, 60, 15].", config.ReminderOffsetsMinutesJson);
            reminderOffsets = [1440, 60, 15];
        }
        if (reminderOffsets.Length == 0) return;
        var maxOffset = reminderOffsets.Max();

        var upcomingBookings = await db.PrivateSpeakingBookings
            .Include(b => b.TutorProfile)
            .Where(b => (b.Status == PrivateSpeakingBookingStatus.Confirmed
                || b.Status == PrivateSpeakingBookingStatus.ZoomCreated)
                && b.SessionStartUtc > now
                && b.SessionStartUtc <= now.AddMinutes(maxOffset + 5))
            .ToListAsync(ct);

        foreach (var booking in upcomingBookings)
        {
            List<int> sentReminders;
            try
            {
                sentReminders = JsonSerializer.Deserialize<List<int>>(booking.RemindersSentJson) ?? [];
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Malformed RemindersSentJson '{Json}' on booking {BookingId}; treating as no reminders sent.", booking.RemindersSentJson, booking.Id);
                sentReminders = [];
            }
            var minutesUntilSession = (booking.SessionStartUtc - now).TotalMinutes;
            var changed = false;

            // Process descending so the nearest-due offset fires last and any
            // collapsed multi-offset window (e.g. a booking already 15 min out)
            // marks every elapsed offset.
            foreach (var offsetMinutes in reminderOffsets.OrderByDescending(o => o))
            {
                if (sentReminders.Contains(offsetMinutes)) continue;
                if (minutesUntilSession > offsetMinutes) continue;

                var tutorName = booking.TutorProfile?.DisplayName ?? "Tutor";
                var sessionTime = booking.SessionStartUtc.ToString("yyyy-MM-dd HH:mm 'UTC'");
                var timeUntil = FormatReminderTimeUntil(offsetMinutes);

                await notificationService.CreateForLearnerAsync(
                    NotificationEventKey.LearnerPrivateSpeakingReminder,
                    booking.LearnerUserId,
                    "private_speaking_reminder",
                    booking.Id,
                    $"reminder-{offsetMinutes}m",
                    new Dictionary<string, object?>
                    {
                        ["tutorName"] = tutorName,
                        ["sessionTime"] = sessionTime,
                        ["timeUntil"] = timeUntil,
                        ["bookingId"] = booking.Id
                    },
                    ct);

                if (booking.TutorProfile?.ExpertUserId is not null)
                {
                    await notificationService.CreateForExpertAsync(
                        NotificationEventKey.ExpertPrivateSpeakingReminder,
                        booking.TutorProfile.ExpertUserId,
                        "private_speaking_reminder",
                        booking.Id,
                        $"reminder-{offsetMinutes}m",
                        new Dictionary<string, object?>
                        {
                            ["sessionTime"] = sessionTime,
                            ["timeUntil"] = timeUntil,
                            ["bookingId"] = booking.Id
                        },
                        ct);
                }

                sentReminders.Add(offsetMinutes);
                changed = true;
            }

            if (changed)
            {
                booking.RemindersSentJson = JsonSerializer.Serialize(sentReminders);
                booking.UpdatedAt = timeProvider.GetUtcNow();
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Render a friendly "time until session" phrase from a minute offset, matching
    /// the PDF §10 wording: 1440 → "24 hours", 60 → "1 hour", 15 → "15 minutes".
    /// General rule for other values: whole days → "{n} day(s)", whole hours →
    /// "{n} hour(s)", else "{n} minute(s)".
    /// </summary>
    private static string FormatReminderTimeUntil(int offsetMinutes)
    {
        // PDF mandates the day-before reminder be phrased as "24 hours", not "1 day".
        if (offsetMinutes == 1440) return "24 hours";

        if (offsetMinutes >= 1440 && offsetMinutes % 1440 == 0)
        {
            var days = offsetMinutes / 1440;
            return days == 1 ? "1 day" : $"{days} days";
        }

        if (offsetMinutes >= 60 && offsetMinutes % 60 == 0)
        {
            var hours = offsetMinutes / 60;
            return hours == 1 ? "1 hour" : $"{hours} hours";
        }

        return offsetMinutes == 1 ? "1 minute" : $"{offsetMinutes} minutes";
    }

    /// <summary>Expire reservation-only bookings that timed out without payment.</summary>
    public async Task ExpireStaleReservationsAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var staleBookings = await db.PrivateSpeakingBookings
            .Where(b => (b.Status == PrivateSpeakingBookingStatus.Reserved
                || b.Status == PrivateSpeakingBookingStatus.PendingPayment)
                && b.ReservationExpiresAt.HasValue
                && b.ReservationExpiresAt < now)
            .ToListAsync(ct);

        foreach (var booking in staleBookings)
        {
            booking.Status = PrivateSpeakingBookingStatus.Expired;
            booking.PaymentStatus = PrivateSpeakingPaymentStatus.Failed;
            booking.UpdatedAt = now;
            logger.LogInformation("Expired stale reservation {BookingId}", booking.Id);
        }

        if (staleBookings.Count > 0)
            await db.SaveChangesAsync(ct);

        // A legacy reschedule replacement is a PendingPayment row with a reservation timeout.
        // If its Stripe checkout never resolves and this sweep expires it, mirror the webhook
        // revert so the original booking is freed and no credit is stranded.
        // RevertRescheduleIfPendingAsync is a no-op for normal (non-reschedule) reservations.
        foreach (var booking in staleBookings)
        {
            await RevertRescheduleIfPendingAsync(booking, ct);
        }
    }

    /// <summary>
    /// T5 (PDF §3.3.6/§13) — automatic no-show sweep. Finds bookings whose session
    /// has ended (start + duration + <see cref="NoShowGraceMinutes"/> grace is in the
    /// past), are still in an "expected to run" state (Confirmed/ZoomCreated/InProgress),
    /// and where the learner's attendance was never verified by the LiveKit presence
    /// webhook (<see cref="PrivateSpeakingBooking.AttendanceVerified"/> == false), and
    /// marks each as a no-show via <see cref="MarkNoShowAsync"/> (which also emits the
    /// learner + tutor no-show notifications). Each booking is processed in its own
    /// try/catch so a single failure does not abort the batch.
    /// </summary>
    public async Task ProcessNoShowSweepAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        // The end-of-session predicate (SessionStartUtc + DurationMinutes + grace < now)
        // cannot be translated to SQL with AddMinutes on an entity column, so filter on
        // status/attendance in the DB and apply the time cutoff in memory. The candidate
        // set is small (only sessions still in a running state), so this is cheap.
        var candidates = await db.PrivateSpeakingBookings
            .Where(b => (b.Status == PrivateSpeakingBookingStatus.Confirmed
                    || b.Status == PrivateSpeakingBookingStatus.ZoomCreated
                    || b.Status == PrivateSpeakingBookingStatus.InProgress)
                && !b.AttendanceVerified)
            .OrderBy(b => b.SessionStartUtc)
            .Take(NoShowSweepBatchCap)
            .ToListAsync(ct);

        var due = candidates
            .Where(b => b.SessionStartUtc.AddMinutes(b.DurationMinutes + NoShowGraceMinutes) < now)
            .ToList();

        foreach (var booking in due)
        {
            try
            {
                var (success, error) = await MarkNoShowAsync(booking.Id, "system", "system", ct);
                if (!success)
                {
                    logger.LogWarning(
                        "No-show sweep skipped booking {BookingId}: {Error}", booking.Id, error);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "No-show sweep failed to mark booking {BookingId}", booking.Id);
            }
        }
    }

    // Compatibility reader for pre-LiveKit webhook retries. No production
    // endpoint dispatches private-speaking attendance through this method.
    public async Task ApplyZoomAttendanceWebhookAsync(string eventType, JsonElement root, CancellationToken ct)
    {
        if (eventType is not ("meeting.participant_joined" or "meeting.participant_left")) return;
        if (!root.TryGetProperty("payload", out var payload)
            || !payload.TryGetProperty("object", out var meetingObject)) return;

        var meetingId = TryReadMeetingId(meetingObject);
        if (meetingId is null) return;

        var booking = await db.PrivateSpeakingBookings
            .FirstOrDefaultAsync(item => item.ZoomMeetingId == meetingId.Value, ct);
        if (booking is null) return;

        var participant = meetingObject.TryGetProperty("participant", out var participantValue)
            ? participantValue
            : default;
        var participantEmail = ReadJsonString(participant, "user_email")
            ?? ReadJsonString(participant, "email");
        var learnerEmail = await db.Users.AsNoTracking()
            .Where(user => user.Id == booking.LearnerUserId)
            .Select(user => user.Email)
            .FirstOrDefaultAsync(ct);
        var isLearner = !string.IsNullOrWhiteSpace(participantEmail)
            && string.Equals(participantEmail, learnerEmail, StringComparison.OrdinalIgnoreCase);

        var now = timeProvider.GetUtcNow();
        if (eventType == "meeting.participant_joined")
        {
            booking.AttendanceJoinedAt ??= now;
            if (isLearner) booking.AttendanceVerified = true;
        }
        else if (isLearner)
        {
            booking.AttendanceLeftAt = now;
        }

        booking.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    private static long? TryReadMeetingId(JsonElement meetingObject)
    {
        if (!meetingObject.TryGetProperty("id", out var id)) return null;
        return id.ValueKind switch
        {
            JsonValueKind.String => long.TryParse(id.GetString(), out var value) ? value : null,
            JsonValueKind.Number => id.TryGetInt64(out var value) ? value : null,
            _ => null,
        };
    }

    private static string? ReadJsonString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
