using System.Globalization;
using System.Data;
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
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services;

public sealed class PrivateSpeakingService(
    LearnerDbContext db,
    NotificationService notificationService,
    ZoomMeetingService zoomService,
    PrivateSpeakingCalendarService calendarService,
    IEffectiveEntitlementResolver entitlementResolver,
    TimeProvider timeProvider,
    ILogger<PrivateSpeakingService> logger)
{
    private const double CalibrationRedDriftThreshold100 = 40.0;
    private const string CalibrationOverrideAction = "tutor_calibration_override";

    // ── Config ──────────────────────────────────────────────────────────

    public async Task<PrivateSpeakingConfig> GetConfigAsync(CancellationToken ct)
    {
        var config = await db.PrivateSpeakingConfigs.FirstOrDefaultAsync(ct);
        if (config is not null) return config;

        config = new PrivateSpeakingConfig { UpdatedAt = timeProvider.GetUtcNow() };
        db.PrivateSpeakingConfigs.Add(config);
        await db.SaveChangesAsync(ct);
        return config;
    }

    public async Task<PrivateSpeakingConfig> UpdateConfigAsync(
        Action<PrivateSpeakingConfig> mutate, string adminId, CancellationToken ct)
    {
        var config = await GetConfigAsync(ct);
        mutate(config);
        config.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await AuditAsync(null, adminId, "admin", "config_updated", null, ct);
        return config;
    }

    // ── Tutor Profile Management ────────────────────────────────────────

    public async Task<List<PrivateSpeakingTutorProfile>> ListTutorProfilesAsync(
        bool? activeOnly, CancellationToken ct)
    {
        var query = db.PrivateSpeakingTutorProfiles.AsNoTracking();
        if (activeOnly == true) query = query.Where(p => p.IsActive);
        return await query.OrderBy(p => p.DisplayName).ToListAsync(ct);
    }

    public async Task<PrivateSpeakingTutorProfile?> GetTutorProfileAsync(
        string profileId, CancellationToken ct)
        => await db.PrivateSpeakingTutorProfiles.FindAsync([profileId], ct);

    public async Task<PrivateSpeakingTutorProfile?> GetTutorProfileByExpertIdAsync(
        string expertUserId, CancellationToken ct)
        => await db.PrivateSpeakingTutorProfiles
            .FirstOrDefaultAsync(p => p.ExpertUserId == expertUserId, ct);

    public async Task<PrivateSpeakingTutorProfile> CreateTutorProfileAsync(
        string expertUserId, string displayName, string timezone, string? bio,
        int? priceOverride, int? durationOverride, string specialtiesJson,
        string adminId, CancellationToken ct)
    {
        var existing = await db.PrivateSpeakingTutorProfiles
            .AnyAsync(p => p.ExpertUserId == expertUserId, ct);
        if (existing)
            throw new InvalidOperationException("Tutor profile already exists for this expert.");

        var expert = await db.ExpertUsers.FindAsync([expertUserId], ct)
            ?? throw new InvalidOperationException("Expert user not found.");

        var profile = new PrivateSpeakingTutorProfile
        {
            Id = $"pstp-{Guid.NewGuid():N}",
            ExpertUserId = expertUserId,
            DisplayName = displayName,
            Bio = bio,
            Timezone = timezone,
            PriceOverrideMinorUnits = priceOverride,
            SlotDurationOverrideMinutes = durationOverride,
            SpecialtiesJson = specialtiesJson,
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow(),
            UpdatedAt = timeProvider.GetUtcNow()
        };

        db.PrivateSpeakingTutorProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        await AuditAsync(null, adminId, "admin", "tutor_profile_created",
            $"Expert: {expertUserId}, Profile: {profile.Id}", ct);
        return profile;
    }

    public async Task<PrivateSpeakingTutorProfile> UpdateTutorProfileAsync(
        string profileId, Action<PrivateSpeakingTutorProfile> mutate,
        string adminId, CancellationToken ct)
    {
        var profile = await db.PrivateSpeakingTutorProfiles.FindAsync([profileId], ct)
            ?? throw new InvalidOperationException("Tutor profile not found.");

        mutate(profile);
        profile.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await AuditAsync(null, adminId, "admin", "tutor_profile_updated",
            $"Profile: {profileId}", ct);
        return profile;
    }

    public async Task<object> CreateTutorCalibrationOverrideAsync(
        string profileId,
        string adminId,
        string? reason,
        DateTimeOffset? expiresAt,
        CancellationToken ct)
    {
        var profile = await db.PrivateSpeakingTutorProfiles.FindAsync([profileId], ct)
            ?? throw ApiException.NotFound("private_speaking_tutor_not_found", "Tutor profile not found.");
        var now = timeProvider.GetUtcNow();
        var expiry = expiresAt.HasValue && expiresAt.Value > now
            ? expiresAt.Value
            : now.AddDays(7);
        var details = JsonSupport.Serialize(new
        {
            profileId = profile.Id,
            expertUserId = profile.ExpertUserId,
            reason = string.IsNullOrWhiteSpace(reason) ? "Admin calibration override" : reason.Trim(),
            expiresAt = expiry,
        });

        await AuditAsync(profile.Id, adminId, "admin", CalibrationOverrideAction, details, ct);

        return new
        {
            profileId = profile.Id,
            expertUserId = profile.ExpertUserId,
            overrideActive = true,
            expiresAt = expiry,
        };
    }

    // ── Availability Rules ──────────────────────────────────────────────

    public async Task<List<PrivateSpeakingAvailabilityRule>> GetAvailabilityRulesAsync(
        string tutorProfileId, CancellationToken ct)
        => await db.PrivateSpeakingAvailabilityRules
            .Where(r => r.TutorProfileId == tutorProfileId)
            .OrderBy(r => r.DayOfWeek).ThenBy(r => r.StartTime)
            .ToListAsync(ct);

    public async Task<PrivateSpeakingAvailabilityRule> CreateAvailabilityRuleAsync(
        string tutorProfileId, int dayOfWeek, string startTime, string endTime,
        DateOnly? effectiveFrom, DateOnly? effectiveTo,
        string adminId, CancellationToken ct)
    {
        var rule = new PrivateSpeakingAvailabilityRule
        {
            Id = $"psar-{Guid.NewGuid():N}",
            TutorProfileId = tutorProfileId,
            DayOfWeek = dayOfWeek,
            StartTime = startTime,
            EndTime = endTime,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            IsActive = true
        };

        db.PrivateSpeakingAvailabilityRules.Add(rule);
        await db.SaveChangesAsync(ct);
        await AuditAsync(null, adminId, "admin", "availability_rule_created",
            $"Tutor: {tutorProfileId}, Day: {dayOfWeek}, {startTime}-{endTime}", ct);
        return rule;
    }

    public async Task DeleteAvailabilityRuleAsync(
        string ruleId, string adminId, CancellationToken ct)
    {
        var rule = await db.PrivateSpeakingAvailabilityRules.FindAsync([ruleId], ct)
            ?? throw new InvalidOperationException("Availability rule not found.");
        db.PrivateSpeakingAvailabilityRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        await AuditAsync(null, adminId, "admin", "availability_rule_deleted",
            $"Rule: {ruleId}", ct);
    }

    // ── Availability Overrides ──────────────────────────────────────────

    public async Task<List<PrivateSpeakingAvailabilityOverride>> GetOverridesAsync(
        string tutorProfileId, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct)
    {
        var query = db.PrivateSpeakingAvailabilityOverrides
            .Where(o => o.TutorProfileId == tutorProfileId);
        if (fromDate.HasValue) query = query.Where(o => o.Date >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(o => o.Date <= toDate.Value);
        return await query.OrderBy(o => o.Date).ToListAsync(ct);
    }

    public async Task<PrivateSpeakingAvailabilityOverride> CreateOverrideAsync(
        string tutorProfileId, DateOnly date, PrivateSpeakingOverrideType type,
        string? startTime, string? endTime, string? reason,
        string adminId, CancellationToken ct)
    {
        var over = new PrivateSpeakingAvailabilityOverride
        {
            Id = $"psao-{Guid.NewGuid():N}",
            TutorProfileId = tutorProfileId,
            Date = date,
            OverrideType = type,
            StartTime = startTime,
            EndTime = endTime,
            Reason = reason
        };

        db.PrivateSpeakingAvailabilityOverrides.Add(over);
        await db.SaveChangesAsync(ct);
        await AuditAsync(null, adminId, "admin", "override_created",
            $"Tutor: {tutorProfileId}, Date: {date}, Type: {type}", ct);
        return over;
    }

    public async Task DeleteOverrideAsync(string overrideId, string adminId, CancellationToken ct)
    {
        var over = await db.PrivateSpeakingAvailabilityOverrides.FindAsync([overrideId], ct)
            ?? throw new InvalidOperationException("Override not found.");
        db.PrivateSpeakingAvailabilityOverrides.Remove(over);
        await db.SaveChangesAsync(ct);
        await AuditAsync(null, adminId, "admin", "override_deleted", $"Override: {overrideId}", ct);
    }

    // ── Slot Generation (Dynamic) ───────────────────────────────────────

    /// <summary>
    /// Dynamically generates available slots for a tutor within a date range.
    /// Combines weekly rules, overrides, and existing bookings to produce real-time availability.
    /// </summary>
    public async Task<List<AvailableSlot>> GetAvailableSlotsAsync(
        string tutorProfileId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var config = await GetConfigAsync(ct);
        var profile = await db.PrivateSpeakingTutorProfiles.FindAsync([tutorProfileId], ct);
        if (profile is null || !profile.IsActive || !config.IsEnabled)
            return [];
        var now = timeProvider.GetUtcNow();
        var calibration = await CheckTutorCalibrationBookingGuardAsync(profile, now, ct);
        if (!calibration.Allowed)
        {
            return [];
        }

        var rules = await db.PrivateSpeakingAvailabilityRules
            .Where(r => r.TutorProfileId == tutorProfileId && r.IsActive)
            .ToListAsync(ct);

        var overrides = await db.PrivateSpeakingAvailabilityOverrides
            .Where(o => o.TutorProfileId == tutorProfileId && o.Date >= fromDate && o.Date <= toDate)
            .ToListAsync(ct);

        var slotDuration = profile.SlotDurationOverrideMinutes ?? config.DefaultSlotDurationMinutes;
        var bufferMinutes = config.BufferMinutesBetweenSlots;
        var minBookingTime = now.AddHours(config.MinBookingLeadTimeHours);
        var tutorTz = TimeZoneInfo.FindSystemTimeZoneById(profile.Timezone);
        var priceMinorUnits = profile.PriceOverrideMinorUnits ?? config.DefaultPriceMinorUnits;
        var queryStartUtc = new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(fromDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), tutorTz),
            TimeSpan.Zero).AddMinutes(-bufferMinutes);
        var queryEndUtc = new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(toDate.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Unspecified), tutorTz),
            TimeSpan.Zero).AddMinutes(bufferMinutes);

        var existingBookings = await db.PrivateSpeakingBookings
            .Where(b => b.TutorProfileId == tutorProfileId
                && b.SessionStartUtc < queryEndUtc
                && b.SessionStartUtc.AddMinutes(b.DurationMinutes) > queryStartUtc
                && b.Status != PrivateSpeakingBookingStatus.Cancelled
                && b.Status != PrivateSpeakingBookingStatus.Expired
                && b.Status != PrivateSpeakingBookingStatus.Failed
                && b.Status != PrivateSpeakingBookingStatus.Refunded)
            .Select(b => new { b.SessionStartUtc, b.DurationMinutes })
            .ToListAsync(ct);

        var slots = new List<AvailableSlot>();

        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            var blockedOverrides = overrides
                .Where(o => o.Date == date && o.OverrideType == PrivateSpeakingOverrideType.Blocked)
                .ToList();

            // If the entire day is blocked
            if (blockedOverrides.Any(o => o.StartTime is null))
                continue;

            var dow = (int)date.DayOfWeek;

            // Get time windows from rules + extra availability overrides
            var windows = new List<(TimeOnly Start, TimeOnly End)>();

            // Add windows from weekly rules
            foreach (var rule in rules.Where(r => r.DayOfWeek == dow))
            {
                if (rule.EffectiveFrom.HasValue && date < rule.EffectiveFrom.Value) continue;
                if (rule.EffectiveTo.HasValue && date > rule.EffectiveTo.Value) continue;
                windows.Add((TimeOnly.Parse(rule.StartTime), TimeOnly.Parse(rule.EndTime)));
            }

            // Add windows from extra availability overrides
            foreach (var extra in overrides
                .Where(o => o.Date == date
                    && o.OverrideType == PrivateSpeakingOverrideType.ExtraAvailability
                    && o.StartTime is not null && o.EndTime is not null))
            {
                windows.Add((TimeOnly.Parse(extra.StartTime!), TimeOnly.Parse(extra.EndTime!)));
            }

            // Generate slots within each window
            foreach (var (windowStart, windowEnd) in windows)
            {
                var current = windowStart;
                while (current.AddMinutes(slotDuration) <= windowEnd)
                {
                    // Convert slot time from tutor timezone to UTC
                    var localDateTime = date.ToDateTime(current);
                    var utcStart = TimeZoneInfo.ConvertTimeToUtc(
                        DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified), tutorTz);
                    var utcStartOffset = new DateTimeOffset(utcStart, TimeSpan.Zero);
                    var utcEnd = utcStartOffset.AddMinutes(slotDuration);

                    // Check if slot is in the future with sufficient lead time
                    if (utcStartOffset <= minBookingTime)
                    {
                        current = current.AddMinutes(slotDuration + bufferMinutes);
                        continue;
                    }

                    // Check for partial-day blocks
                    var slotEndLocal = current.AddMinutes(slotDuration);
                    var isBlockedByOverride = blockedOverrides.Any(o =>
                    {
                        if (o.StartTime is null || o.EndTime is null)
                        {
                            return false;
                        }

                        var blockStart = TimeOnly.Parse(o.StartTime);
                        var blockEnd = TimeOnly.Parse(o.EndTime);
                        return blockStart < slotEndLocal && blockEnd > current;
                    });

                    if (isBlockedByOverride)
                    {
                        current = current.AddMinutes(slotDuration + bufferMinutes);
                        continue;
                    }

                    // Check for conflicts with existing bookings
                    var hasConflict = existingBookings.Any(b =>
                    {
                        var bEnd = b.SessionStartUtc.AddMinutes(b.DurationMinutes);
                        var slotEndWithBuffer = utcEnd.AddMinutes(bufferMinutes);
                        var slotStartWithBuffer = utcStartOffset.AddMinutes(-bufferMinutes);
                        return b.SessionStartUtc < slotEndWithBuffer && bEnd > slotStartWithBuffer;
                    });

                    if (!hasConflict)
                    {
                        var calendarBusy = await calendarService.CheckBusyAsync(tutorProfileId, utcStartOffset, utcEnd, ct);
                        if (calendarBusy.Connected && (calendarBusy.IsBusy || calendarBusy.Error is not null))
                        {
                            current = current.AddMinutes(slotDuration + bufferMinutes);
                            continue;
                        }

                        slots.Add(new AvailableSlot(
                            TutorProfileId: tutorProfileId,
                            TutorDisplayName: profile.DisplayName,
                            TutorTimezone: profile.Timezone,
                            Date: date,
                            StartTimeLocal: current.ToString("HH:mm"),
                            StartTimeUtc: utcStartOffset,
                            EndTimeUtc: utcEnd,
                            DurationMinutes: slotDuration,
                            PriceMinorUnits: priceMinorUnits,
                            Currency: config.Currency));
                    }

                    current = current.AddMinutes(slotDuration + bufferMinutes);
                }
            }
        }

        return slots;
    }

    /// <summary>Get available slots across all active tutors for a date range.</summary>
    public async Task<List<AvailableSlot>> GetAllAvailableSlotsAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var config = await GetConfigAsync(ct);
        if (!config.IsEnabled) return [];

        var tutorIds = await db.PrivateSpeakingTutorProfiles
            .Where(p => p.IsActive)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var allSlots = new List<AvailableSlot>();
        foreach (var tutorId in tutorIds)
        {
            var slots = await GetAvailableSlotsAsync(tutorId, fromDate, toDate, ct);
            allSlots.AddRange(slots);
        }

        return allSlots.OrderBy(s => s.StartTimeUtc).ThenBy(s => s.TutorDisplayName).ToList();
    }

    // ── Booking Flow ────────────────────────────────────────────────────

    /// <summary>
    /// Reserve a slot by consuming one bundled/private-speaking entitlement.
    /// Uses a serializable transaction to prevent double-booking and double-debiting.
    /// </summary>
    public async Task<BookingCheckoutResult> CreateBookingAndCheckoutAsync(
        string learnerUserId, string tutorProfileId,
        DateTimeOffset sessionStartUtc, int durationMinutes,
        string learnerTimezone, string? learnerNotes,
        string idempotencyKey, CancellationToken ct)
    {
        var config = await GetConfigAsync(ct);
        if (!config.IsEnabled)
            return BookingCheckoutResult.Fail("Private Speaking Sessions are currently disabled.");
        if (!IsUsableIdempotencyKey(idempotencyKey))
            return BookingCheckoutResult.Fail("A valid booking idempotency key is required.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var idempotencyScope = BuildIdempotencyScopeKey("book", learnerUserId, idempotencyKey);
        var idempotencyPrefix = BuildScopedIdempotencyPrefix(idempotencyScope);
        var scopedIdempotencyKey = BuildScopedIdempotencyKey(
            idempotencyScope,
            tutorProfileId,
            sessionStartUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            durationMinutes.ToString(CultureInfo.InvariantCulture),
            learnerTimezone,
            learnerNotes ?? string.Empty);

        // Idempotency check
        var existingBooking = await db.PrivateSpeakingBookings
            .FirstOrDefaultAsync(b => b.LearnerUserId == learnerUserId
                && b.IdempotencyKey != null
                && b.IdempotencyKey.StartsWith(idempotencyPrefix), ct);
        if (existingBooking is not null)
        {
            if (!string.Equals(existingBooking.IdempotencyKey, scopedIdempotencyKey, StringComparison.Ordinal))
            {
                return BookingCheckoutResult.Fail("This idempotency key was already used with different booking details. Please retry with a new key.");
            }

            await transaction.CommitAsync(ct);
            return existingBooking.Status == PrivateSpeakingBookingStatus.Expired
                ? BookingCheckoutResult.Fail("Previous booking attempt expired. Please try again with a new request.")
                : new BookingCheckoutResult(true, null, existingBooking.Id,
                    existingBooking.StripeCheckoutSessionId, null, existingBooking.EntitlementConsumed);
        }

        var profile = await db.PrivateSpeakingTutorProfiles.FindAsync([tutorProfileId], ct);
        if (profile is null || !profile.IsActive)
            return BookingCheckoutResult.Fail("Tutor is not available.");

        var now = timeProvider.GetUtcNow();
        var calibration = await CheckTutorCalibrationBookingGuardAsync(profile, now, ct);
        if (!calibration.Allowed)
        {
            return BookingCheckoutResult.Fail("Tutor is temporarily unavailable while calibration quality is reviewed.");
        }

        var minBookingTime = now.AddHours(config.MinBookingLeadTimeHours);
        if (sessionStartUtc <= minBookingTime)
            return BookingCheckoutResult.Fail($"Sessions must be booked at least {config.MinBookingLeadTimeHours} hours in advance.");

        var maxBookingTime = now.AddDays(config.MaxBookingAdvanceDays);
        if (sessionStartUtc > maxBookingTime)
            return BookingCheckoutResult.Fail($"Sessions cannot be booked more than {config.MaxBookingAdvanceDays} days in advance.");

        if (!await IsRequestedSlotAvailableAsync(profile, sessionStartUtc, durationMinutes, ct))
            return BookingCheckoutResult.Fail("This time slot is no longer available. Please select another slot.");

        var sessionEnd = sessionStartUtc.AddMinutes(durationMinutes);
        var sessionStartWithBuffer = sessionStartUtc.AddMinutes(-config.BufferMinutesBetweenSlots);
        var sessionEndWithBuffer = sessionEnd.AddMinutes(config.BufferMinutesBetweenSlots);

        // Check for conflicting tutor bookings (race protection via DB unique constraint)
        var hasConflict = await db.PrivateSpeakingBookings.AnyAsync(b =>
            b.TutorProfileId == tutorProfileId
            && b.SessionStartUtc < sessionEndWithBuffer
            && b.SessionStartUtc.AddMinutes(b.DurationMinutes) > sessionStartWithBuffer
            && b.Status != PrivateSpeakingBookingStatus.Cancelled
            && b.Status != PrivateSpeakingBookingStatus.Expired
            && b.Status != PrivateSpeakingBookingStatus.Failed
            && b.Status != PrivateSpeakingBookingStatus.Refunded, ct);

        if (hasConflict)
            return BookingCheckoutResult.Fail("This time slot is no longer available. Please select another slot.");

        // Check for overlapping learner bookings
        var learnerConflict = await db.PrivateSpeakingBookings.AnyAsync(b =>
            b.LearnerUserId == learnerUserId
            && b.SessionStartUtc < sessionEnd
            && b.SessionStartUtc.AddMinutes(b.DurationMinutes) > sessionStartUtc
            && b.Status != PrivateSpeakingBookingStatus.Cancelled
            && b.Status != PrivateSpeakingBookingStatus.Expired
            && b.Status != PrivateSpeakingBookingStatus.Failed
            && b.Status != PrivateSpeakingBookingStatus.Refunded, ct);

        if (learnerConflict)
            return BookingCheckoutResult.Fail("You already have a booking at this time. Please select a different slot.");

        var priceMinorUnits = profile.PriceOverrideMinorUnits ?? config.DefaultPriceMinorUnits;
        var calendarBusy = await calendarService.CheckBusyAsync(tutorProfileId, sessionStartUtc, sessionEnd, ct);
        if (calendarBusy.Connected)
        {
            if (calendarBusy.Error is not null)
                return BookingCheckoutResult.Fail("Tutor calendar availability could not be verified. Please try another slot shortly.");
            if (calendarBusy.IsBusy)
                return BookingCheckoutResult.Fail("Tutor calendar shows this slot is no longer available. Please select another slot.");
        }

        var subscription = await ResolveEligibleSpeakingSubscriptionAsync(learnerUserId, now, ct);
        if (subscription is null)
            return BookingCheckoutResult.Fail("You need an available private speaking session credit to book this session.");

        subscription.SpeakingSessionsRemaining -= 1;

        var booking = new PrivateSpeakingBooking
        {
            Id = $"psb-{Guid.NewGuid():N}",
            LearnerUserId = learnerUserId,
            TutorProfileId = tutorProfileId,
            Status = PrivateSpeakingBookingStatus.Confirmed,
            SessionStartUtc = sessionStartUtc,
            DurationMinutes = durationMinutes,
            TutorTimezone = profile.Timezone,
            LearnerTimezone = learnerTimezone,
            PriceMinorUnits = 0,
            Currency = config.Currency,
            PaymentStatus = PrivateSpeakingPaymentStatus.Succeeded,
            PaymentConfirmedAt = now,
            EntitlementSubscriptionId = subscription.Id,
            EntitlementConsumed = true,
            EntitlementConsumedAt = now,
            ReservationExpiresAt = null,
            IdempotencyKey = scopedIdempotencyKey,
            LearnerNotes = learnerNotes,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.PrivateSpeakingBookings.Add(booking);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return BookingCheckoutResult.Fail("This time slot was just booked. Please select another slot.");
        }

        await AuditAsync(booking.Id, learnerUserId, "learner", "booking_reserved",
            $"Tutor: {tutorProfileId}, Time: {sessionStartUtc:O}, Entitlement subscription: {subscription.Id}, Catalog price minor units: {priceMinorUnits}", ct);

        QueueBookingPostCommitJobs(booking.Id, includeCalendarSync: false);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new BookingCheckoutResult(
            Success: true,
            Error: null,
            BookingId: booking.Id,
            CheckoutSessionId: null,
            CheckoutUrl: null,
            EntitlementUsed: true,
            SpeakingSessionsRemaining: subscription.SpeakingSessionsRemaining);
    }

    /// <summary>
    /// Handle successful payment webhook - confirm booking, create Zoom meeting, send notifications.
    /// Idempotent: safe to call multiple times for the same booking.
    /// </summary>
    public async Task<bool> ConfirmBookingPaymentAsync(
        string stripeCheckoutSessionId, string? paymentIntentId, CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings
            .FirstOrDefaultAsync(b => b.StripeCheckoutSessionId == stripeCheckoutSessionId, ct);

        if (booking is null)
        {
            logger.LogWarning("No booking found for Stripe session {SessionId}", stripeCheckoutSessionId);
            return false;
        }

        // Idempotent: already confirmed
        if (booking.PaymentStatus == PrivateSpeakingPaymentStatus.Succeeded)
            return true;

        booking.PaymentStatus = PrivateSpeakingPaymentStatus.Succeeded;
        booking.PaymentConfirmedAt = timeProvider.GetUtcNow();
        booking.StripePaymentIntentId = paymentIntentId;
        booking.Status = PrivateSpeakingBookingStatus.Confirmed;
        booking.ReservationExpiresAt = null; // Clear reservation timeout
        booking.UpdatedAt = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);

        await AuditAsync(booking.Id, "system", "system", "payment_confirmed",
            $"Stripe session: {stripeCheckoutSessionId}", ct);

        QueueBookingPostCommitJobs(booking.Id, includeCalendarSync: false);
        await db.SaveChangesAsync(ct);

        return true;
    }

    /// <summary>Handle payment failure for a booking.</summary>
    public async Task HandlePaymentFailureAsync(
        string stripeCheckoutSessionId, CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings
            .FirstOrDefaultAsync(b => b.StripeCheckoutSessionId == stripeCheckoutSessionId, ct);

        if (booking is null) return;
        if (booking.PaymentStatus == PrivateSpeakingPaymentStatus.Succeeded) return;

        booking.PaymentStatus = PrivateSpeakingPaymentStatus.Failed;
        booking.Status = PrivateSpeakingBookingStatus.Failed;
        booking.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);

        await AuditAsync(booking.Id, "system", "system", "payment_failed",
            $"Stripe session: {stripeCheckoutSessionId}", ct);
    }

    /// <summary>Handle expired checkout sessions - release the slot.</summary>
    public async Task HandleCheckoutExpiredAsync(
        string stripeCheckoutSessionId, CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings
            .FirstOrDefaultAsync(b => b.StripeCheckoutSessionId == stripeCheckoutSessionId, ct);

        if (booking is null) return;
        if (booking.PaymentStatus == PrivateSpeakingPaymentStatus.Succeeded) return;

        booking.Status = PrivateSpeakingBookingStatus.Expired;
        booking.PaymentStatus = PrivateSpeakingPaymentStatus.Failed;
        booking.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);

        await AuditAsync(booking.Id, "system", "system", "checkout_expired",
            $"Stripe session: {stripeCheckoutSessionId}", ct);
    }

    // ── Zoom Meeting Creation ───────────────────────────────────────────

    public async Task CreateZoomMeetingForBookingAsync(string bookingId, CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings
            .Include(b => b.TutorProfile)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null || booking.Status != PrivateSpeakingBookingStatus.Confirmed)
        {
            logger.LogWarning("Cannot create Zoom for booking {BookingId}: not found or wrong status", bookingId);
            return;
        }

        if (booking.ZoomStatus == PrivateSpeakingZoomStatus.Created)
            return; // Idempotent

        booking.ZoomStatus = PrivateSpeakingZoomStatus.Creating;
        booking.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);

        try
        {
            var tutorName = booking.TutorProfile?.DisplayName ?? "Tutor";
            var result = await zoomService.CreateMeetingAsync(
                topic: $"OET Private Speaking Session with {tutorName}",
                startTime: booking.SessionStartUtc,
                durationMinutes: booking.DurationMinutes,
                timezone: booking.TutorTimezone,
                ct);

            booking.ZoomMeetingId = result.MeetingId;
            booking.ZoomJoinUrl = result.JoinUrl;
            booking.ZoomStartUrl = result.StartUrl;
            booking.ZoomMeetingPassword = result.Password;
            booking.ZoomStatus = PrivateSpeakingZoomStatus.Created;
            booking.Status = PrivateSpeakingBookingStatus.ZoomCreated;
            booking.UpdatedAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(ct);

            await AuditAsync(booking.Id, "system", "system", "zoom_created",
                $"Meeting ID: {result.MeetingId}", ct);

            QueueCalendarSyncJob(booking.Id);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            booking.ZoomStatus = PrivateSpeakingZoomStatus.Failed;
            booking.ZoomError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            booking.ZoomRetryCount++;
            booking.UpdatedAt = timeProvider.GetUtcNow();
            if (booking.ZoomRetryCount >= 3)
            {
                booking.Status = PrivateSpeakingBookingStatus.Failed;
                await RestoreSpeakingEntitlementAsync(booking, "zoom_creation_failed", ct);
                QueueCalendarSyncJob(booking.Id);
            }
            await db.SaveChangesAsync(ct);

            logger.LogError(ex, "Failed to create Zoom meeting for booking {BookingId}", bookingId);
            await AuditAsync(booking.Id, "system", "system", "zoom_failed", ex.Message, ct);

            if (booking.ZoomRetryCount < 3)
                throw; // Let background job retry
        }
    }

    // ── Notifications ───────────────────────────────────────────────────

    public async Task SendBookingConfirmationNotificationsAsync(string bookingId, CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings
            .Include(b => b.TutorProfile)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null) return;

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
    }

    // ── Reminder Processing ─────────────────────────────────────────────

    /// <summary>Process scheduled reminders for upcoming sessions.</summary>
    public async Task ProcessRemindersAsync(CancellationToken ct)
    {
        var config = await GetConfigAsync(ct);
        var now = timeProvider.GetUtcNow();

        var reminderOffsets = JsonSerializer.Deserialize<int[]>(config.ReminderOffsetsHoursJson) ?? [24, 1];

        var upcomingBookings = await db.PrivateSpeakingBookings
            .Include(b => b.TutorProfile)
            .Where(b => (b.Status == PrivateSpeakingBookingStatus.Confirmed
                || b.Status == PrivateSpeakingBookingStatus.ZoomCreated)
                && b.SessionStartUtc > now
                && b.SessionStartUtc <= now.AddHours(reminderOffsets.Max() + 1))
            .ToListAsync(ct);

        foreach (var booking in upcomingBookings)
        {
            var sentReminders = JsonSerializer.Deserialize<List<int>>(booking.RemindersSentJson) ?? [];
            var hoursUntilSession = (booking.SessionStartUtc - now).TotalHours;

            foreach (var offsetHours in reminderOffsets)
            {
                if (sentReminders.Contains(offsetHours)) continue;
                if (hoursUntilSession > offsetHours) continue;

                // Send reminder
                var tutorName = booking.TutorProfile?.DisplayName ?? "Tutor";
                var sessionTime = booking.SessionStartUtc.ToString("yyyy-MM-dd HH:mm 'UTC'");

                await notificationService.CreateForLearnerAsync(
                    NotificationEventKey.LearnerPrivateSpeakingReminder,
                    booking.LearnerUserId,
                    "private_speaking_reminder",
                    booking.Id,
                    $"reminder-{offsetHours}h",
                    new Dictionary<string, object?>
                    {
                        ["tutorName"] = tutorName,
                        ["sessionTime"] = sessionTime,
                        ["hoursUntil"] = offsetHours.ToString(),
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
                        $"reminder-{offsetHours}h",
                        new Dictionary<string, object?>
                        {
                            ["sessionTime"] = sessionTime,
                            ["hoursUntil"] = offsetHours.ToString(),
                            ["bookingId"] = booking.Id
                        },
                        ct);
                }

                sentReminders.Add(offsetHours);
            }

            booking.RemindersSentJson = JsonSerializer.Serialize(sentReminders);
            booking.UpdatedAt = timeProvider.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);
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
    }

    // ── Cancellation ────────────────────────────────────────────────────

    public async Task<(bool Success, string? Error)> CancelBookingAsync(
        string bookingId, string actorId, string actorRole, string? reason, CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings
            .Include(b => b.TutorProfile)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null) return (false, "Booking not found.");

        if (booking.Status is PrivateSpeakingBookingStatus.Cancelled
            or PrivateSpeakingBookingStatus.Refunded
            or PrivateSpeakingBookingStatus.Completed)
            return (false, "Booking cannot be cancelled in its current state.");

        // Check cancellation window for learners
        if (actorRole == "learner")
        {
            var config = await GetConfigAsync(ct);
            var hoursUntil = (booking.SessionStartUtc - timeProvider.GetUtcNow()).TotalHours;
            if (hoursUntil < config.CancellationWindowHours)
                return (false, $"Cancellations must be made at least {config.CancellationWindowHours} hours before the session.");

            // Verify the actor is the learner who booked
            if (booking.LearnerUserId != actorId)
                return (false, "You can only cancel your own bookings.");
        }

        booking.Status = PrivateSpeakingBookingStatus.Cancelled;
        booking.CancelledBy = actorId;
        booking.CancellationReason = reason;
        booking.CancelledAt = timeProvider.GetUtcNow();
        booking.UpdatedAt = timeProvider.GetUtcNow();

        if (booking.EntitlementConsumed && booking.EntitlementRestoredAt is null && booking.RescheduledToBookingId is null)
        {
            await RestoreSpeakingEntitlementAsync(booking, $"cancelled_by_{actorRole}", ct);
        }

        await db.SaveChangesAsync(ct);
        await AuditAsync(booking.Id, actorId, actorRole, "booking_cancelled", reason, ct);

        // Delete Zoom meeting if it exists
        if (booking.ZoomMeetingId.HasValue)
        {
            try { await zoomService.DeleteMeetingAsync(booking.ZoomMeetingId.Value, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to delete Zoom meeting {MeetingId}", booking.ZoomMeetingId); }
        }

        QueueCalendarSyncJob(booking.Id);
        await db.SaveChangesAsync(ct);

        // Notify parties
        var tutorName = booking.TutorProfile?.DisplayName ?? "Tutor";
        var sessionTime = booking.SessionStartUtc.ToString("yyyy-MM-dd HH:mm 'UTC'");

        await notificationService.CreateForLearnerAsync(
            NotificationEventKey.LearnerPrivateSpeakingCancelled,
            booking.LearnerUserId,
            "private_speaking_booking", booking.Id,
            $"cancel-{booking.CancelledAt:yyyyMMddHHmm}",
            new Dictionary<string, object?>
            {
                ["tutorName"] = tutorName,
                ["sessionTime"] = sessionTime,
                ["cancelledBy"] = actorRole
            }, ct);

        if (booking.TutorProfile?.ExpertUserId is not null)
        {
            await notificationService.CreateForExpertAsync(
                NotificationEventKey.ExpertPrivateSpeakingCancelled,
                booking.TutorProfile.ExpertUserId,
                "private_speaking_booking", booking.Id,
                $"cancel-{booking.CancelledAt:yyyyMMddHHmm}",
                new Dictionary<string, object?>
                {
                    ["sessionTime"] = sessionTime,
                    ["cancelledBy"] = actorRole
                }, ct);
        }

        return (true, null);
    }

    public async Task<BookingCheckoutResult> RescheduleBookingAsync(
        string bookingId,
        string learnerUserId,
        DateTimeOffset newSessionStartUtc,
        string learnerTimezone,
        string? learnerNotes,
        string idempotencyKey,
        CancellationToken ct)
    {
        var config = await GetConfigAsync(ct);
        if (!config.IsEnabled)
            return BookingCheckoutResult.Fail("Private Speaking Sessions are currently disabled.");
        if (!config.AllowReschedule)
            return BookingCheckoutResult.Fail("Rescheduling is not currently enabled.");
        if (!IsUsableIdempotencyKey(idempotencyKey))
            return BookingCheckoutResult.Fail("A valid reschedule idempotency key is required.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var idempotencyScope = BuildIdempotencyScopeKey("reschedule", learnerUserId, bookingId, idempotencyKey);
        var idempotencyPrefix = BuildScopedIdempotencyPrefix(idempotencyScope);
        var scopedIdempotencyKey = BuildScopedIdempotencyKey(
            idempotencyScope,
            newSessionStartUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            learnerTimezone,
            learnerNotes ?? string.Empty);

        var existingByIdempotency = await db.PrivateSpeakingBookings
            .FirstOrDefaultAsync(item => item.LearnerUserId == learnerUserId
                && item.IdempotencyKey != null
                && item.IdempotencyKey.StartsWith(idempotencyPrefix), ct);
        if (existingByIdempotency is not null)
        {
            if (!string.Equals(existingByIdempotency.IdempotencyKey, scopedIdempotencyKey, StringComparison.Ordinal))
            {
                return BookingCheckoutResult.Fail("This idempotency key was already used with different reschedule details. Please retry with a new key.");
            }

            await transaction.CommitAsync(ct);
            return new BookingCheckoutResult(
                true,
                null,
                existingByIdempotency.Id,
                existingByIdempotency.StripeCheckoutSessionId,
                null,
                existingByIdempotency.EntitlementConsumed);
        }

        var original = await db.PrivateSpeakingBookings
            .Include(item => item.TutorProfile)
            .FirstOrDefaultAsync(item => item.Id == bookingId, ct);
        if (original is null || original.LearnerUserId != learnerUserId)
            return BookingCheckoutResult.Fail("Booking not found.");
        if (original.Status is not (PrivateSpeakingBookingStatus.Confirmed or PrivateSpeakingBookingStatus.ZoomCreated))
            return BookingCheckoutResult.Fail("Only confirmed upcoming sessions can be rescheduled.");
        if (original.RescheduledToBookingId is not null)
            return BookingCheckoutResult.Fail("This booking has already been rescheduled.");

        var now = timeProvider.GetUtcNow();
        var hoursUntilOriginal = (original.SessionStartUtc - now).TotalHours;
        if (hoursUntilOriginal < config.RescheduleWindowHours)
            return BookingCheckoutResult.Fail($"Reschedules must be made at least {config.RescheduleWindowHours} hours before the session.");

        var profile = original.TutorProfile
            ?? await db.PrivateSpeakingTutorProfiles.FindAsync([original.TutorProfileId], ct);
        if (profile is null || !profile.IsActive)
            return BookingCheckoutResult.Fail("Tutor is not available.");

        var minBookingTime = now.AddHours(config.MinBookingLeadTimeHours);
        if (newSessionStartUtc <= minBookingTime)
            return BookingCheckoutResult.Fail($"Sessions must be booked at least {config.MinBookingLeadTimeHours} hours in advance.");
        var maxBookingTime = now.AddDays(config.MaxBookingAdvanceDays);
        if (newSessionStartUtc > maxBookingTime)
            return BookingCheckoutResult.Fail($"Sessions cannot be booked more than {config.MaxBookingAdvanceDays} days in advance.");

        var durationMinutes = original.DurationMinutes;
        if (!await IsRequestedSlotAvailableAsync(profile, newSessionStartUtc, durationMinutes, ct))
            return BookingCheckoutResult.Fail("This time slot is no longer available. Please select another slot.");

        var newSessionEndUtc = newSessionStartUtc.AddMinutes(durationMinutes);
        var newSessionStartWithBuffer = newSessionStartUtc.AddMinutes(-config.BufferMinutesBetweenSlots);
        var newSessionEndWithBuffer = newSessionEndUtc.AddMinutes(config.BufferMinutesBetweenSlots);
        var hasTutorConflict = await db.PrivateSpeakingBookings.AnyAsync(b =>
            b.Id != original.Id
            && b.TutorProfileId == original.TutorProfileId
            && b.SessionStartUtc < newSessionEndWithBuffer
            && b.SessionStartUtc.AddMinutes(b.DurationMinutes) > newSessionStartWithBuffer
            && b.Status != PrivateSpeakingBookingStatus.Cancelled
            && b.Status != PrivateSpeakingBookingStatus.Expired
            && b.Status != PrivateSpeakingBookingStatus.Failed
            && b.Status != PrivateSpeakingBookingStatus.Refunded, ct);
        if (hasTutorConflict)
            return BookingCheckoutResult.Fail("This time slot is no longer available. Please select another slot.");

        var learnerConflict = await db.PrivateSpeakingBookings.AnyAsync(b =>
            b.Id != original.Id
            && b.LearnerUserId == learnerUserId
            && b.SessionStartUtc < newSessionEndUtc
            && b.SessionStartUtc.AddMinutes(b.DurationMinutes) > newSessionStartUtc
            && b.Status != PrivateSpeakingBookingStatus.Cancelled
            && b.Status != PrivateSpeakingBookingStatus.Expired
            && b.Status != PrivateSpeakingBookingStatus.Failed
            && b.Status != PrivateSpeakingBookingStatus.Refunded, ct);
        if (learnerConflict)
            return BookingCheckoutResult.Fail("You already have a booking at this time. Please select a different slot.");

        var calendarBusy = await calendarService.CheckBusyAsync(original.TutorProfileId, newSessionStartUtc, newSessionEndUtc, ct);
        if (calendarBusy.Connected)
        {
            if (calendarBusy.Error is not null)
                return BookingCheckoutResult.Fail("Tutor calendar availability could not be verified. Please try another slot shortly.");
            if (calendarBusy.IsBusy)
                return BookingCheckoutResult.Fail("Tutor calendar shows this slot is no longer available. Please select another slot.");
        }

        var replacement = new PrivateSpeakingBooking
        {
            Id = $"psb-{Guid.NewGuid():N}",
            LearnerUserId = learnerUserId,
            TutorProfileId = original.TutorProfileId,
            Status = PrivateSpeakingBookingStatus.Confirmed,
            SessionStartUtc = newSessionStartUtc,
            DurationMinutes = durationMinutes,
            TutorTimezone = profile.Timezone,
            LearnerTimezone = learnerTimezone,
            PriceMinorUnits = original.PriceMinorUnits,
            Currency = original.Currency,
            PaymentStatus = original.PaymentStatus,
            PaymentConfirmedAt = original.PaymentConfirmedAt,
            EntitlementSubscriptionId = original.EntitlementSubscriptionId,
            EntitlementConsumed = original.EntitlementConsumed,
            EntitlementConsumedAt = original.EntitlementConsumedAt,
            StripeCheckoutSessionId = null,
            LearnerNotes = learnerNotes ?? original.LearnerNotes,
            IdempotencyKey = scopedIdempotencyKey,
            RescheduledFromBookingId = original.Id,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.PrivateSpeakingBookings.Add(replacement);
        original.Status = PrivateSpeakingBookingStatus.Cancelled;
        original.CancelledBy = learnerUserId;
        original.CancellationReason = "rescheduled";
        original.CancelledAt = now;
        original.RescheduledToBookingId = replacement.Id;
        original.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        await AuditAsync(original.Id, learnerUserId, "learner", "booking_rescheduled_from", replacement.Id, ct);
        await AuditAsync(replacement.Id, learnerUserId, "learner", "booking_rescheduled_to", original.Id, ct);
        QueueCalendarSyncJob(original.Id);
        QueueBookingPostCommitJobs(replacement.Id, includeCalendarSync: false);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        if (original.ZoomMeetingId.HasValue)
        {
            try { await zoomService.DeleteMeetingAsync(original.ZoomMeetingId.Value, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to delete Zoom meeting {MeetingId} for rescheduled booking", original.ZoomMeetingId); }
        }

        return new BookingCheckoutResult(
            true,
            null,
            replacement.Id,
            null,
            null,
            replacement.EntitlementConsumed,
            null);
    }

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

    public async Task<LiveClassJoinTokenResponse> CreateLearnerJoinTokenAsync(
        string bookingId,
        string learnerUserId,
        CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings
            .AsNoTracking()
            .Include(item => item.TutorProfile)
            .FirstOrDefaultAsync(item => item.Id == bookingId && item.LearnerUserId == learnerUserId, ct)
            ?? throw ApiException.NotFound("private_speaking_booking_not_found", "Private speaking booking not found.");

        ValidateJoinWindow(booking, role: "learner");

        var learner = await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == learnerUserId, ct)
            ?? throw ApiException.NotFound("learner_not_found", "Learner profile not found.");

        return await CreateJoinTokenAsync(booking, learner.DisplayName, learner.Email, role: 0, ct);
    }

    public async Task<LiveClassJoinTokenResponse> CreateExpertJoinTokenAsync(
        string bookingId,
        string expertUserId,
        CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings
            .AsNoTracking()
            .Include(item => item.TutorProfile)
            .FirstOrDefaultAsync(item => item.Id == bookingId, ct)
            ?? throw ApiException.NotFound("private_speaking_booking_not_found", "Private speaking booking not found.");

        if (booking.TutorProfile?.ExpertUserId != expertUserId)
        {
            throw ApiException.Forbidden("private_speaking_not_assigned", "This private speaking session is assigned to another tutor.");
        }

        ValidateJoinWindow(booking, role: "expert");

        var expert = await db.ExpertUsers.AsNoTracking().FirstOrDefaultAsync(user => user.Id == expertUserId, ct)
            ?? throw ApiException.NotFound("expert_not_found", "Expert profile not found.");

        return await CreateJoinTokenAsync(booking, expert.DisplayName, expert.Email, role: 1, ct);
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
        CancellationToken ct)
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
        var slots = await GetAvailableSlotsAsync(profile.Id, tutorLocalDate, tutorLocalDate, ct);
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
        var now = timeProvider.GetUtcNow();
        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"bgj-{Guid.NewGuid():N}",
            Type = JobType.PrivateSpeakingZoomCreate,
            ResourceId = bookingId,
            State = AsyncState.Queued,
            AvailableAt = now,
            CreatedAt = now,
            LastTransitionAt = now
        });

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

        if (includeCalendarSync)
        {
            QueueCalendarSyncJob(bookingId);
        }
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

    private void ValidateJoinWindow(PrivateSpeakingBooking booking, string role)
    {
        if (booking.Status is not (PrivateSpeakingBookingStatus.ZoomCreated or PrivateSpeakingBookingStatus.InProgress))
        {
            throw ApiException.Conflict("private_speaking_zoom_not_ready", "The Zoom room is not ready yet.");
        }

        if (booking.ZoomMeetingId is null)
        {
            throw ApiException.ServiceUnavailable("private_speaking_zoom_not_ready", "The Zoom room is not ready yet.");
        }

        var now = timeProvider.GetUtcNow();
        var opensAt = booking.SessionStartUtc.AddMinutes(-30);
        var closesAt = booking.SessionStartUtc.AddMinutes(booking.DurationMinutes).AddMinutes(15);
        if (now < opensAt || now > closesAt)
        {
            var accessLabel = role == "expert" ? "host access" : "joins";
            throw ApiException.Conflict("private_speaking_join_window_closed", $"Private speaking {accessLabel} open 30 minutes before start and close 15 minutes after the scheduled end.");
        }
    }

    private async Task<LiveClassJoinTokenResponse> CreateJoinTokenAsync(
        PrivateSpeakingBooking booking,
        string displayName,
        string? email,
        int role,
        CancellationToken ct)
    {
        var meetingNumber = booking.ZoomMeetingId?.ToString(CultureInfo.InvariantCulture)
            ?? throw ApiException.ServiceUnavailable("private_speaking_zoom_not_ready", "The Zoom room is not ready yet.");
        var now = timeProvider.GetUtcNow();
        var expiresAt = Min(now.AddHours(2), booking.SessionStartUtc.AddMinutes(booking.DurationMinutes).AddMinutes(15));
        var signature = await zoomService.GenerateMeetingSdkSignatureAsync(meetingNumber, role, expiresAt, ct);
        var sdkKey = await zoomService.GetMeetingSdkKeyAsync(ct);

        string? zak = null;
        if (role == 1)
        {
            var hostZoomUserId = await ResolveHostZoomUserIdAsync(booking, ct);
            if (!string.IsNullOrWhiteSpace(hostZoomUserId))
            {
                try
                {
                    zak = await zoomService.GetZakTokenAsync(hostZoomUserId, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch private speaking ZAK token for booking {BookingId}", booking.Id);
                }
            }
        }

        return new LiveClassJoinTokenResponse(
            "zoom",
            sdkKey,
            signature,
            meetingNumber,
            displayName,
            email,
            role,
            booking.ZoomMeetingPassword,
            zak,
            role == 0 ? booking.ZoomJoinUrl : booking.ZoomStartUrl,
            expiresAt);
    }

    private async Task<string?> ResolveHostZoomUserIdAsync(PrivateSpeakingBooking booking, CancellationToken ct)
    {
        var expertUserId = booking.TutorProfile?.ExpertUserId;
        if (string.IsNullOrWhiteSpace(expertUserId))
        {
            expertUserId = await db.PrivateSpeakingTutorProfiles
                .AsNoTracking()
                .Where(item => item.Id == booking.TutorProfileId)
                .Select(item => item.ExpertUserId)
                .FirstOrDefaultAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(expertUserId))
        {
            return null;
        }

        var zoomUserId = await db.Tutors
            .AsNoTracking()
            .Where(tutor => tutor.UserId == expertUserId && tutor.IsActive)
            .Select(tutor => tutor.ZoomUserId)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(zoomUserId) ? null : zoomUserId;
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
        => left <= right ? left : right;

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
