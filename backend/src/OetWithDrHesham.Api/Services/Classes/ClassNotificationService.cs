using System.Globalization;
using System.Text;
using Ical.Net;
using IcalCalendar = Ical.Net.Calendar;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Classes;

/// <summary>
/// Class-specific wrapper over <see cref="NotificationService"/>. Centralises the
/// payload shape for live-class notifications and produces an iCalendar (.ics)
/// attachment for enrollment confirmations and the T-24h reminder so learners
/// can drop the class straight onto their calendar.
/// </summary>
public interface IClassNotificationService
{
    Task SendEnrollmentConfirmedAsync(LiveClassEnrollment enrollment, LiveClassSession session, CancellationToken ct);

    Task SendReminderAsync(LiveClassEnrollment enrollment, LiveClassSession session, int leadMinutes, CancellationToken ct);

    Task SendWaitlistOpeningAsync(LiveClassWaitlistEntry entry, LiveClassSession session, TimeSpan claimWindow, CancellationToken ct);

    Task SendCancellationAsync(LiveClassEnrollment enrollment, LiveClassSession session, bool cancelledByTutor, int refundCredits, CancellationToken ct);

    Task SendFeedbackRequestAsync(LiveClassEnrollment enrollment, LiveClassSession session, CancellationToken ct);

    Task SendRecordingReadyAsync(LiveClassRecording recording, LiveClassSession session, CancellationToken ct);

    Task SendTutorClassStartingSoonAsync(LiveClassSession session, CancellationToken ct);

    Task SendTutorRecordingReadyAsync(LiveClassRecording recording, LiveClassSession session, CancellationToken ct);

    EmailAttachment BuildIcsAttachment(LiveClassSession session, string? userTimezone, string? tutorEmail);
}

public sealed class ClassNotificationService(
    LearnerDbContext db,
    NotificationService notificationService,
    TimeProvider timeProvider,
    ILogger<ClassNotificationService> logger) : IClassNotificationService
{
    private const string IcsContentType = "text/calendar; method=REQUEST";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public async Task SendEnrollmentConfirmedAsync(LiveClassEnrollment enrollment, LiveClassSession session, CancellationToken ct)
    {
        var payload = BuildLearnerPayload(session);
        payload["icsFileName"] = BuildIcsFileName(session);
        await notificationService.CreateForLearnerAsync(
            NotificationEventKey.LearnerClassEnrollmentConfirmed,
            enrollment.UserId,
            "live_class_enrollment",
            enrollment.Id,
            BuildBucket(enrollment.EnrolledAt),
            payload,
            ct);
    }

    public async Task SendReminderAsync(LiveClassEnrollment enrollment, LiveClassSession session, int leadMinutes, CancellationToken ct)
    {
        var payload = BuildLearnerPayload(session);
        payload["leadMinutes"] = leadMinutes;
        if (leadMinutes >= 1440)
        {
            // T-24h reminder doubles as the calendar invite re-send.
            payload["icsFileName"] = BuildIcsFileName(session);
        }

        await notificationService.CreateForLearnerAsync(
            NotificationEventKey.LearnerLiveClassReminder,
            enrollment.UserId,
            "live_class_session",
            session.Id,
            BuildReminderBucket(session.ScheduledStartAt, leadMinutes),
            payload,
            ct);
    }

    public async Task SendWaitlistOpeningAsync(LiveClassWaitlistEntry entry, LiveClassSession session, TimeSpan claimWindow, CancellationToken ct)
    {
        var payload = BuildLearnerPayload(session);
        payload["claimWindow"] = FormatClaimWindow(claimWindow);
        await notificationService.CreateForLearnerAsync(
            NotificationEventKey.LearnerClassWaitlistOpening,
            entry.UserId,
            "live_class_waitlist",
            entry.Id,
            BuildBucket(timeProvider.GetUtcNow()),
            payload,
            ct);
    }

    public async Task SendCancellationAsync(LiveClassEnrollment enrollment, LiveClassSession session, bool cancelledByTutor, int refundCredits, CancellationToken ct)
    {
        var payload = BuildLearnerPayload(session);
        payload["refundCredits"] = refundCredits;
        payload["refundMessage"] = refundCredits > 0
            ? $"Refund of {refundCredits} credit(s) is reflected on your wallet."
            : "No refund was issued for this cancellation under the policy.";
        payload["reason"] = enrollment.CancellationReason;

        var key = cancelledByTutor
            ? NotificationEventKey.LearnerClassCancelledByTutor
            : NotificationEventKey.LearnerClassCancelledByUser;
        await notificationService.CreateForLearnerAsync(
            key,
            enrollment.UserId,
            "live_class_enrollment",
            enrollment.Id,
            BuildBucket(enrollment.CancelledAt ?? timeProvider.GetUtcNow()),
            payload,
            ct);
    }

    public async Task SendFeedbackRequestAsync(LiveClassEnrollment enrollment, LiveClassSession session, CancellationToken ct)
    {
        var payload = BuildLearnerPayload(session);
        await notificationService.CreateForLearnerAsync(
            NotificationEventKey.LearnerClassFeedbackRequest,
            enrollment.UserId,
            "live_class_feedback",
            enrollment.Id,
            BuildBucket(session.ScheduledEndAt),
            payload,
            ct);
    }

    public async Task SendRecordingReadyAsync(LiveClassRecording recording, LiveClassSession session, CancellationToken ct)
    {
        var payload = BuildLearnerPayload(session);
        payload["recordingId"] = recording.Id;
        // Active and Attended enrollments only — refunded/no-show learners do not get the recording link.
        var recipients = await db.LiveClassEnrollments
            .AsNoTracking()
            .Where(e => e.ClassSessionId == session.Id
                && (e.Status == LiveClassEnrollmentStatus.Active || e.Status == LiveClassEnrollmentStatus.Attended))
            .Select(e => e.UserId)
            .ToListAsync(ct);

        foreach (var learnerId in recipients)
        {
            await notificationService.CreateForLearnerAsync(
                NotificationEventKey.LearnerLiveClassRecordingReady,
                learnerId,
                "live_class_recording",
                recording.Id,
                BuildBucket(recording.ProcessedAt ?? timeProvider.GetUtcNow()),
                payload,
                ct);
        }

        await SendTutorRecordingReadyAsync(recording, session, ct);
    }

    public async Task SendTutorClassStartingSoonAsync(LiveClassSession session, CancellationToken ct)
    {
        var expertUserId = await ResolveTutorExpertUserIdAsync(session, ct);
        if (string.IsNullOrWhiteSpace(expertUserId))
        {
            return;
        }

        var payload = BuildLearnerPayload(session);
        await notificationService.CreateForExpertAsync(
            NotificationEventKey.TutorClassStarting15Min,
            expertUserId,
            "live_class_session",
            session.Id,
            BuildReminderBucket(session.ScheduledStartAt, 15),
            payload,
            ct);
    }

    public async Task SendTutorRecordingReadyAsync(LiveClassRecording recording, LiveClassSession session, CancellationToken ct)
    {
        var expertUserId = await ResolveTutorExpertUserIdAsync(session, ct);
        if (string.IsNullOrWhiteSpace(expertUserId))
        {
            return;
        }

        var payload = BuildLearnerPayload(session);
        payload["recordingId"] = recording.Id;
        await notificationService.CreateForExpertAsync(
            NotificationEventKey.TutorRecordingReady,
            expertUserId,
            "live_class_recording",
            recording.Id,
            BuildBucket(recording.ProcessedAt ?? timeProvider.GetUtcNow()),
            payload,
            ct);
    }

    public EmailAttachment BuildIcsAttachment(LiveClassSession session, string? userTimezone, string? tutorEmail)
    {
        var calendar = new IcalCalendar();
        var ev = new CalendarEvent
        {
            Uid = $"oet-live-class-{session.Id}@oetwithdrhesham",
            Summary = session.LiveClass?.Title ?? "OET Live Class",
            Description = BuildIcsDescription(session),
            Start = new CalDateTime(session.ScheduledStartAt.UtcDateTime, "UTC"),
            End = new CalDateTime(session.ScheduledEndAt.UtcDateTime, "UTC"),
            Location = session.ZoomJoinUrl ?? string.Empty,
            DtStamp = new CalDateTime(timeProvider.GetUtcNow().UtcDateTime, "UTC"),
        };

        if (!string.IsNullOrWhiteSpace(userTimezone) && !string.Equals(userTimezone, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                ev.Start = new CalDateTime(session.ScheduledStartAt.UtcDateTime, userTimezone);
                ev.End = new CalDateTime(session.ScheduledEndAt.UtcDateTime, userTimezone);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Falling back to UTC for ics TZID {Timezone}", userTimezone);
                ev.Start = new CalDateTime(session.ScheduledStartAt.UtcDateTime, "UTC");
                ev.End = new CalDateTime(session.ScheduledEndAt.UtcDateTime, "UTC");
            }
        }

        if (!string.IsNullOrWhiteSpace(tutorEmail))
        {
            ev.Organizer = new Organizer($"mailto:{tutorEmail}")
            {
                CommonName = session.LiveClass?.TutorDisplayName ?? "OET Tutor",
            };
        }

        calendar.Events.Add(ev);
        calendar.Method = "REQUEST";
        var serializer = new CalendarSerializer();
        var ics = serializer.SerializeToString(calendar) ?? string.Empty;
        var bytes = Encoding.UTF8.GetBytes(ics);
        return new EmailAttachment(BuildIcsFileName(session), IcsContentType, bytes);
    }

    private async Task<string?> ResolveTutorExpertUserIdAsync(LiveClassSession session, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.LiveClass?.TutorProfileId))
        {
            return null;
        }

        return await db.PrivateSpeakingTutorProfiles
            .AsNoTracking()
            .Where(profile => profile.Id == session.LiveClass.TutorProfileId)
            .Select(profile => profile.ExpertUserId)
            .FirstOrDefaultAsync(ct);
    }

    private static Dictionary<string, object?> BuildLearnerPayload(LiveClassSession session)
        => new()
        {
            ["classTitle"] = session.LiveClass?.Title ?? "OET Live Class",
            ["classId"] = session.LiveClassId,
            ["sessionId"] = session.Id,
            ["sessionTime"] = session.ScheduledStartAt.ToString("yyyy-MM-dd HH:mm 'UTC'", Invariant),
            ["scheduledStartAt"] = session.ScheduledStartAt.ToString("O", Invariant),
            ["scheduledEndAt"] = session.ScheduledEndAt.ToString("O", Invariant),
            ["joinUrl"] = session.ZoomJoinUrl,
        };

    private static string BuildBucket(DateTimeOffset moment)
        => moment.ToString("yyyyMMddHHmm", Invariant);

    private static string BuildReminderBucket(DateTimeOffset scheduledStartAt, int leadMinutes)
        => $"{scheduledStartAt.ToString("yyyyMMddHHmm", Invariant)}-T{leadMinutes}";

    private static string BuildIcsFileName(LiveClassSession session)
        => $"oet-class-{session.Id}.ics";

    private static string BuildIcsDescription(LiveClassSession session)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(session.LiveClass?.Description))
        {
            lines.Add(session.LiveClass.Description);
        }

        if (!string.IsNullOrWhiteSpace(session.ZoomJoinUrl))
        {
            lines.Add($"Join URL: {session.ZoomJoinUrl}");
        }

        if (!string.IsNullOrWhiteSpace(session.ZoomPasscode))
        {
            lines.Add($"Passcode: {session.ZoomPasscode}");
        }

        return string.Join("\n", lines);
    }

    private static string FormatClaimWindow(TimeSpan claimWindow)
    {
        if (claimWindow.TotalMinutes < 60)
        {
            return $"{(int)Math.Max(1, Math.Round(claimWindow.TotalMinutes))} minutes";
        }

        var hours = claimWindow.TotalHours;
        return hours <= 1.0 ? "60 minutes" : $"{(int)Math.Round(hours)} hours";
    }
}
