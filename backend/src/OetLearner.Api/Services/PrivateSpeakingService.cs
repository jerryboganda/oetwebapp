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

public sealed partial class PrivateSpeakingService(
    LearnerDbContext db,
    NotificationService notificationService,
    ZoomMeetingService zoomService,
    PrivateSpeakingCalendarService calendarService,
    IEffectiveEntitlementResolver entitlementResolver,
    IAddonEligibilityService addonEligibility,
    IStripeService stripeService,
    PaymentGatewayService paymentGateways,
    PlatformLinkService platformLinks,
    TimeProvider timeProvider,
    ILogger<PrivateSpeakingService> logger,
    // Optional so existing hand-built test instances keep compiling; DI always
    // supplies the registered gateway. null is treated as "available".
    ILiveKitGateway? liveKitGateway = null)
{
    private const double CalibrationRedDriftThreshold100 = 40.0;
    private const string CalibrationOverrideAction = "tutor_calibration_override";
    private const int PdfCancellationWindowHours = 24;
    private const string PdfReminderOffsetsHoursJson = "[24, 1, 0.25]";
    private const string PdfReminderOffsetsMinutesJson = "[1440, 60, 15]";
    private const string PdfCancellationPolicyText = "You may cancel your Speaking session with a full refund if the cancellation is made more than 24 hours before the scheduled start time. If you cancel 24 hours or less before the session, a full refund is not available.";
    private const string PdfBookingPolicyText = "You may reschedule your Speaking session any time before it starts, subject to an alternative slot currently available in the tutor calendar.";

    // ── Live tutor room availability (B9) ───────────────────────────────

    public const string AnyTutorId = "any";
    public const string TutorRoomsUnavailableCode = "tutor_rooms_unavailable";
    public const string TutorRoomsUnavailableMessage = "Live tutor sessions are temporarily unavailable.";

    /// <summary>False when the registered gateway is <see cref="LiveKitProviderUnavailable"/>
    /// (LiveKit not configured outside Development/Testing).</summary>
    public bool LiveRoomsAvailable => liveKitGateway is not LiveKitProviderUnavailable;

    /// <summary>Throws 503 <c>tutor_rooms_unavailable</c> so nobody pays for a room that cannot run.</summary>
    public void EnsureLiveRoomsAvailable()
    {
        if (!LiveRoomsAvailable)
            throw ApiException.ServiceUnavailable(TutorRoomsUnavailableCode, TutorRoomsUnavailableMessage);
    }

    /// <summary>null, blank or "any" means "Any available tutor".</summary>
    public static bool IsAnyTutor([NotNullWhen(false)] string? tutorProfileId)
        => string.IsNullOrWhiteSpace(tutorProfileId)
            || string.Equals(tutorProfileId.Trim(), AnyTutorId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Learner slot listing. Blank = every active tutor's slots (per tutor);
    /// "any" = one slot per start time across all active tutors, bookable with
    /// tutorProfileId "any"; otherwise the named tutor's slots.
    /// </summary>
    public async Task<List<AvailableSlot>> GetLearnerSlotsAsync(
        string? tutorProfileId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        EnsureLiveRoomsAvailable();
        if (string.IsNullOrWhiteSpace(tutorProfileId))
            return await GetAllAvailableSlotsAsync(fromDate, toDate, ct);
        if (!IsAnyTutor(tutorProfileId))
            return await GetAvailableSlotsAsync(tutorProfileId.Trim(), fromDate, toDate, ct);

        var all = await GetAllAvailableSlotsAsync(fromDate, toDate, ct);
        return all
            .GroupBy(slot => (slot.StartTimeUtc, slot.DurationMinutes))
            .Select(group => group.OrderBy(slot => slot.PriceMinorUnits).First() with
            {
                TutorProfileId = AnyTutorId,
                TutorDisplayName = "Any available tutor",
            })
            .OrderBy(slot => slot.StartTimeUtc)
            .ToList();
    }

    /// <summary>
    /// Picks the active tutor with the fewest upcoming bookings who has the
    /// slot open (availability rules, overrides, existing bookings, buffers,
    /// calibration guard and connected calendar all respected through
    /// <see cref="GetAvailableSlotsAsync"/>). Ties break on tutor id.
    /// <paramref name="exactDuration"/> true = the slot length must equal
    /// <paramref name="durationMinutes"/> (private booking); false = at least
    /// that long (mock bundle). Callers run this inside their booking transaction.
    /// </summary>
    public async Task<string?> FindLeastLoadedAvailableTutorAsync(
        DateTimeOffset sessionStartUtc, int durationMinutes, CancellationToken ct, bool exactDuration = true)
    {
        var now = timeProvider.GetUtcNow();
        var profiles = await db.PrivateSpeakingTutorProfiles
            .Where(p => p.IsActive)
            .ToListAsync(ct);
        var load = await db.PrivateSpeakingBookings
            .Where(b => b.SessionStartUtc >= now
                && b.Status != PrivateSpeakingBookingStatus.Cancelled
                && b.Status != PrivateSpeakingBookingStatus.Expired
                && b.Status != PrivateSpeakingBookingStatus.Failed
                && b.Status != PrivateSpeakingBookingStatus.Refunded)
            .GroupBy(b => b.TutorProfileId)
            .Select(g => new { TutorProfileId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TutorProfileId, x => x.Count, ct);

        // ponytail: sequential per-tutor slot check; fine for a small tutor pool.
        foreach (var profile in profiles
            .OrderBy(p => load.GetValueOrDefault(p.Id))
            .ThenBy(p => p.Id, StringComparer.Ordinal))
        {
            TimeZoneInfo tz;
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById(profile.Timezone);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                continue;
            }

            // GetAvailableSlotsAsync applies the calibration guard itself.
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(sessionStartUtc, tz).DateTime);
            var slots = await GetAvailableSlotsAsync(profile.Id, localDate, localDate, ct);
            if (slots.Any(slot => slot.StartTimeUtc == sessionStartUtc
                    && (exactDuration ? slot.DurationMinutes == durationMinutes : slot.DurationMinutes >= durationMinutes)))
                return profile.Id;
        }

        return null;
    }

    // ── Config ──────────────────────────────────────────────────────────

    public async Task<PrivateSpeakingConfig> GetConfigAsync(CancellationToken ct)
    {
        var config = await db.PrivateSpeakingConfigs.FirstOrDefaultAsync(ct);
        if (config is not null)
        {
            if (NormalizePdfWorkflowPolicy(config))
            {
                await db.SaveChangesAsync(ct);
            }

            return config;
        }

        config = new PrivateSpeakingConfig
        {
            UpdatedAt = timeProvider.GetUtcNow(),
        };
        NormalizePdfWorkflowPolicy(config);
        db.PrivateSpeakingConfigs.Add(config);
        await db.SaveChangesAsync(ct);
        return config;
    }

    public async Task<PrivateSpeakingConfig> UpdateConfigAsync(
        Action<PrivateSpeakingConfig> mutate, string adminId, CancellationToken ct)
    {
        var config = await GetConfigAsync(ct);
        mutate(config);
        // The PDF workflow is policy, not an admin-configurable variation. Keep
        // the mandated refund, reschedule, reminder, and copy invariants here
        // as well as in the endpoint so every caller is fail-closed.
        NormalizePdfWorkflowPolicy(config);
        config.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await AuditAsync(null, adminId, "admin", "config_updated", null, ct);
        return config;
    }

    private static bool NormalizePdfWorkflowPolicy(PrivateSpeakingConfig config)
    {
        var changed = false;

        if (config.CancellationWindowHours != PdfCancellationWindowHours)
        {
            config.CancellationWindowHours = PdfCancellationWindowHours;
            changed = true;
        }

        if (!config.AllowReschedule)
        {
            config.AllowReschedule = true;
            changed = true;
        }

        if (!string.Equals(config.ReminderOffsetsHoursJson, PdfReminderOffsetsHoursJson, StringComparison.Ordinal))
        {
            config.ReminderOffsetsHoursJson = PdfReminderOffsetsHoursJson;
            changed = true;
        }

        if (!string.Equals(config.ReminderOffsetsMinutesJson, PdfReminderOffsetsMinutesJson, StringComparison.Ordinal))
        {
            config.ReminderOffsetsMinutesJson = PdfReminderOffsetsMinutesJson;
            changed = true;
        }

        if (config.RescheduleSameDayPenaltyPercent != 0)
        {
            config.RescheduleSameDayPenaltyPercent = 0;
            changed = true;
        }

        if (!string.Equals(config.CancellationPolicyText, PdfCancellationPolicyText, StringComparison.Ordinal))
        {
            config.CancellationPolicyText = PdfCancellationPolicyText;
            changed = true;
        }

        if (!string.Equals(config.BookingPolicyText, PdfBookingPolicyText, StringComparison.Ordinal))
        {
            config.BookingPolicyText = PdfBookingPolicyText;
            changed = true;
        }

        return changed;
    }

    private static readonly string[] ValidProfessionTracks =
        ["Medicine", "Nursing", "Pharmacy", "Dentistry", "Other"];

    /// <summary>Clamp a candidate-supplied profession track to the five known
    /// values (PDF §3.2.4). Blank → null; any unknown non-blank value → "Other",
    /// so untrusted input is never persisted raw (the booking row feeds the admin
    /// CSV export and dashboards).</summary>
    private static string? NormalizeProfessionTrack(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        foreach (var track in ValidProfessionTracks)
        {
            if (string.Equals(track, trimmed, StringComparison.OrdinalIgnoreCase))
                return track;
        }
        return "Other";
    }

    // ── No-show Sweep ───────────────────────────────────────────────────

    /// <summary>Grace period (minutes) after a session's scheduled end before the
    /// sweep is allowed to flag a non-attending booking as a no-show. Gives late
    /// joiners and webhook-delivery lag room before the booking is forfeited.</summary>
    private const int NoShowGraceMinutes = 15;

    /// <summary>Maximum bookings processed per sweep pass, to bound DB/notification work.</summary>
    private const int NoShowSweepBatchCap = 500;

    // ── Private Helpers ─────────────────────────────────────────────────

    private async Task<Subscription?> ResolveEligibleSpeakingSubscriptionAsync(
        string learnerUserId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var snapshot = await entitlementResolver.ResolveAsync(learnerUserId, ct);
        if (!snapshot.HasEligibleSubscription
            || snapshot.IsFrozen
            || snapshot.SpeakingSessionsRemaining <= 0
            || string.IsNullOrWhiteSpace(snapshot.SubscriptionId))
        {
            return null;
        }

        return await db.Subscriptions
            .FirstOrDefaultAsync(subscription => subscription.Id == snapshot.SubscriptionId
                && subscription.UserId == learnerUserId
                && subscription.SpeakingSessionsRemaining > 0
                && (subscription.ExpiresAt == null || subscription.ExpiresAt > now), ct);
    }

    private async Task<bool> IsRequestedSlotAvailableAsync(
        PrivateSpeakingTutorProfile profile,
        DateTimeOffset sessionStartUtc,
        int durationMinutes,
        CancellationToken ct,
        string? excludeBookingId = null)
    {
        TimeZoneInfo tutorTimeZone;
        try
        {
            tutorTimeZone = TimeZoneInfo.FindSystemTimeZoneById(profile.Timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }

        var tutorLocalStart = TimeZoneInfo.ConvertTime(sessionStartUtc, tutorTimeZone);
        var tutorLocalDate = DateOnly.FromDateTime(tutorLocalStart.DateTime);
        var slots = await GetAvailableSlotsAsync(profile.Id, tutorLocalDate, tutorLocalDate, ct, excludeBookingId);
        return slots.Any(slot => slot.StartTimeUtc == sessionStartUtc && slot.DurationMinutes == durationMinutes);
    }

    private async Task RestoreSpeakingEntitlementAsync(
        PrivateSpeakingBooking booking,
        string reason,
        CancellationToken ct)
    {
        if (!booking.EntitlementConsumed || booking.EntitlementRestoredAt is not null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(booking.EntitlementSubscriptionId))
        {
            booking.EntitlementRestoredAt = timeProvider.GetUtcNow();
            booking.EntitlementRestorationReason = "subscription_missing";
            return;
        }

        var subscription = await db.Subscriptions
            .FirstOrDefaultAsync(item => item.Id == booking.EntitlementSubscriptionId, ct);
        if (subscription is null)
        {
            booking.EntitlementRestoredAt = timeProvider.GetUtcNow();
            booking.EntitlementRestorationReason = "subscription_missing";
            return;
        }

        subscription.SpeakingSessionsRemaining = checked(subscription.SpeakingSessionsRemaining + 1);
        booking.EntitlementRestoredAt = timeProvider.GetUtcNow();
        booking.EntitlementRestorationReason = reason.Length > 128 ? reason[..128] : reason;
    }

    private void QueueBookingPostCommitJobs(string bookingId, bool includeCalendarSync)
    {
        QueueBookingConfirmationJob(bookingId);
        if (includeCalendarSync)
        {
            QueueCalendarSyncJob(bookingId);
        }
    }

    private void QueueBookingConfirmationJob(string bookingId)
    {
        var now = timeProvider.GetUtcNow();
        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"bgj-{Guid.NewGuid():N}",
            Type = JobType.PrivateSpeakingBookingConfirmation,
            ResourceId = bookingId,
            State = AsyncState.Queued,
            AvailableAt = now,
            CreatedAt = now,
            LastTransitionAt = now
        });
    }

    private void QueueCalendarSyncJob(string bookingId)
    {
        var now = timeProvider.GetUtcNow();
        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"bgj-{Guid.NewGuid():N}",
            Type = JobType.PrivateSpeakingCalendarSync,
            ResourceId = bookingId,
            State = AsyncState.Queued,
            AvailableAt = now,
            CreatedAt = now,
            LastTransitionAt = now
        });
    }

    private static string BuildIdempotencyScopeKey(params string[] parts)
    {
        var normalized = string.Join('|', parts.Select(part => part.Trim()));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildScopedIdempotencyPrefix(string scopeHash)
        => $"psik-{scopeHash}-";

    private static string BuildScopedIdempotencyKey(string scopeHash, params string[] payloadParts)
    {
        var normalizedPayload = string.Join('|', payloadParts.Select(part => part.Trim()));
        var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPayload));
        return $"{BuildScopedIdempotencyPrefix(scopeHash)}{Convert.ToHexString(payloadHash)[..16].ToLowerInvariant()}";
    }

    private static bool IsUsableIdempotencyKey(string? idempotencyKey)
    {
        var trimmed = idempotencyKey?.Trim();
        if (trimmed is null || trimmed.Length is < 16 or > 256)
        {
            return false;
        }

        if (Guid.TryParse(trimmed, out var guid))
        {
            var formatted = guid.ToString("D");
            return guid != Guid.Empty
                && formatted[14] == '4'
                && formatted[19] is '8' or '9' or 'a' or 'b'
                && formatted.Where(char.IsAsciiHexDigit).Distinct().Take(8).Count() == 8;
        }

        return trimmed.Length >= 32
            && trimmed.All(IsIdempotencyTokenChar)
            && trimmed.Distinct().Take(8).Count() == 8;
    }

    private static bool IsIdempotencyTokenChar(char value)
    {
        return value is >= 'a' and <= 'z'
            || value is >= 'A' and <= 'Z'
            || value is >= '0' and <= '9'
            || value is '-' or '_' or '.' or '~';
    }

    private async Task<TutorCalibrationBookingGuard> CheckTutorCalibrationBookingGuardAsync(
        PrivateSpeakingTutorProfile profile,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var rows = await db.SpeakingCalibrationScores
            .AsNoTracking()
            .Join(db.SpeakingCalibrationSamples.AsNoTracking(),
                score => score.SampleId,
                sample => sample.Id,
                (score, sample) => new { score, sample })
            .Where(x => x.sample.Status == SpeakingCalibrationSampleStatus.Published
                        && x.score.TutorId == profile.ExpertUserId)
            .Select(x => new
            {
                x.score.ScoresJson,
                x.sample.GoldScoresJson,
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return new TutorCalibrationBookingGuard(true, null, false);
        }

        var errorSum = 0.0;
        var count = 0;
        foreach (var row in rows)
        {
            var scores = JsonSupport.Deserialize<Dictionary<string, double>>(row.ScoresJson, new Dictionary<string, double>());
            var gold = JsonSupport.Deserialize<Dictionary<string, double>>(row.GoldScoresJson, new Dictionary<string, double>());
            foreach (var (code, max) in SpeakingCriterionMaxima)
            {
                if (!scores.TryGetValue(code, out var score) || !gold.TryGetValue(code, out var expected))
                {
                    continue;
                }
                errorSum += Math.Abs(score - expected) / max * 100.0;
                count++;
            }
        }

        if (count == 0)
        {
            return new TutorCalibrationBookingGuard(true, null, false);
        }

        var meanDrift100 = errorSum / count;
        if (meanDrift100 <= CalibrationRedDriftThreshold100)
        {
            return new TutorCalibrationBookingGuard(true, meanDrift100, false);
        }

        var overrideActive = await HasActiveCalibrationOverrideAsync(profile.Id, now, ct);
        return new TutorCalibrationBookingGuard(overrideActive, meanDrift100, overrideActive);
    }

    private async Task<bool> HasActiveCalibrationOverrideAsync(
        string tutorProfileId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var auditRows = await db.PrivateSpeakingAuditLogs.AsNoTracking()
            .Where(x => x.BookingId == tutorProfileId && x.Action == CalibrationOverrideAction)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Details)
            .Take(5)
            .ToListAsync(ct);
        foreach (var details in auditRows)
        {
            var payload = JsonSupport.Deserialize<Dictionary<string, JsonElement>>(details, new Dictionary<string, JsonElement>());
            if (payload.TryGetValue("expiresAt", out var expiresElement)
                && expiresElement.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(expiresElement.GetString(), out var expiresAt)
                && expiresAt > now)
            {
                return true;
            }
        }

        return false;
    }

    private static readonly IReadOnlyDictionary<string, double> SpeakingCriterionMaxima =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["intelligibility"] = 6,
            ["fluency"] = 6,
            ["appropriateness"] = 6,
            ["grammarExpression"] = 6,
            ["relationshipBuilding"] = 3,
            ["patientPerspective"] = 3,
            ["structure"] = 3,
            ["informationGathering"] = 3,
            ["informationGiving"] = 3,
        };

    private sealed record TutorCalibrationBookingGuard(
        bool Allowed,
        double? MeanDrift100,
        bool OverrideActive);

    private async Task AuditAsync(
        string? bookingId, string actorId, string actorRole,
        string action, string? details, CancellationToken ct)
    {
        db.PrivateSpeakingAuditLogs.Add(new PrivateSpeakingAuditLog
        {
            Id = $"psal-{Guid.NewGuid():N}",
            BookingId = bookingId,
            ActorId = actorId,
            ActorRole = actorRole,
            Action = action,
            Details = details?.Length > 2000 ? details[..2000] : details,
            CreatedAt = timeProvider.GetUtcNow()
        });
        await db.SaveChangesAsync(ct);
    }
}

// ── DTOs ────────────────────────────────────────────────────────────────

public record AvailableSlot(
    string TutorProfileId,
    string TutorDisplayName,
    string TutorTimezone,
    DateOnly Date,
    string StartTimeLocal,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset EndTimeUtc,
    int DurationMinutes,
    int PriceMinorUnits,
    string Currency);

public record BookingCheckoutResult(
    bool Success,
    string? Error,
    string? BookingId = null,
    string? CheckoutSessionId = null,
    string? CheckoutUrl = null,
    bool EntitlementUsed = false,
    int? SpeakingSessionsRemaining = null)
{
    public static BookingCheckoutResult Fail(string error) => new(false, error);
}

public record PrivateSpeakingCalendarInvite(string FileName, string ContentType, string Content);

public record PrivateSpeakingDashboardStats(
    int TotalBookings,
    int ConfirmedBookings,
    int CompletedBookings,
    int CancelledBookings,
    int FailedPayments,
    int ZoomFailures,
    int ActiveTutors,
    int UpcomingSessions,
    int RevenueMinorUnitsLast30Days);
