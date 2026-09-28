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

    public async Task<PrivateSpeakingAvailabilityRule> UpdateAvailabilityRuleAsync(
        string ruleId, int dayOfWeek, string startTime, string endTime,
        DateOnly? effectiveFrom, DateOnly? effectiveTo, bool isActive,
        string actorId, CancellationToken ct)
    {
        var rule = await db.PrivateSpeakingAvailabilityRules.FindAsync([ruleId], ct)
            ?? throw new InvalidOperationException("Availability rule not found.");

        rule.DayOfWeek = dayOfWeek;
        rule.StartTime = startTime;
        rule.EndTime = endTime;
        rule.EffectiveFrom = effectiveFrom;
        rule.EffectiveTo = effectiveTo;
        rule.IsActive = isActive;

        await db.SaveChangesAsync(ct);
        await AuditAsync(null, actorId, "admin", "availability_rule_updated",
            $"Rule: {ruleId}, Day: {dayOfWeek}, {startTime}-{endTime}, Active: {isActive}", ct);
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
        string tutorProfileId, DateOnly fromDate, DateOnly toDate, CancellationToken ct,
        string? excludeBookingId = null)
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
                && (excludeBookingId == null || b.Id != excludeBookingId)
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
}
