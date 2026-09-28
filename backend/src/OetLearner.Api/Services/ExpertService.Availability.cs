using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services;

public partial class ExpertService
{
    public async Task<object> GetAvailabilityAsync(string reviewerId, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var availability = await FirstOrDefaultOrderedDescendingAsync(
            db.ExpertAvailabilities
                .AsNoTracking()
                .Where(existingAvailability => existingAvailability.ReviewerId == reviewerId),
            existingAvailability => existingAvailability.EffectiveFrom,
            ct);

        if (availability is null)
        {
            return new
            {
                timezone = "UTC",
                days = DefaultScheduleDays(),
                lastUpdatedAt = (DateTimeOffset?)null
            };
        }

        return new
        {
            timezone = availability.Timezone,
            days = JsonSupport.Deserialize(availability.DaysJson, DefaultScheduleDays()),
            lastUpdatedAt = availability.EffectiveFrom
        };
    }

    public async Task<object> SaveAvailabilityAsync(string reviewerId, ExpertAvailabilityUpdateRequest request, CancellationToken ct)
    {
        ValidateAvailabilityRequest(request);

        var expert = await EnsureExpertAsync(reviewerId, ct);
        var availability = await db.ExpertAvailabilities
            .FirstOrDefaultAsync(existingAvailability => existingAvailability.ReviewerId == reviewerId, ct);

        if (availability is null)
        {
            availability = new ExpertAvailability
            {
                Id = $"ea-{Guid.NewGuid():N}",
                ReviewerId = reviewerId,
                EffectiveFrom = DateTimeOffset.UtcNow
            };
            db.ExpertAvailabilities.Add(availability);
        }

        availability.Timezone = request.Timezone.Trim();
        availability.DaysJson = JsonSupport.Serialize(request.Days);
        availability.EffectiveFrom = DateTimeOffset.UtcNow;
        expert.Timezone = availability.Timezone;

        await LogExpertAuditAsync(reviewerId, expert.DisplayName, "Updated Availability", reviewerId, $"Timezone set to {availability.Timezone}.", ct);
        await RecordExpertEventAsync(reviewerId, "expert_schedule_saved", new { timezone = availability.Timezone }, ct);
        await db.SaveChangesAsync(ct);

        return new
        {
            timezone = availability.Timezone,
            days = request.Days,
            lastUpdatedAt = availability.EffectiveFrom
        };
    }

    /// <summary>
    /// Returns static business rules for reviewer availability so the Schedule page can
    /// validate user input client-side without the server silently rejecting edits.
    /// Supplement: <c>GET /v1/expert/availability/constraints</c>.
    /// </summary>
    public async Task<ExpertAvailabilityConstraintsResponse> GetAvailabilityConstraintsAsync(string reviewerId, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        // Intentionally static; elevate to admin-configurable later if business rules change.
        return new ExpertAvailabilityConstraintsResponse(
            MinNoticeHours: 24,
            MaxHoursPerWeek: 60,
            MaxExceptionsPerMonth: 12,
            MinSlotDuration: "00:30",
            MaxSlotDuration: "12:00",
            SupportedTimezones: new[]
            {
                "UTC",
                "Europe/London",
                "Europe/Dublin",
                "Europe/Berlin",
                "America/New_York",
                "America/Chicago",
                "America/Denver",
                "America/Los_Angeles",
                "Asia/Dubai",
                "Asia/Karachi",
                "Asia/Kolkata",
                "Asia/Singapore",
                "Asia/Tokyo",
                "Australia/Sydney",
                "Pacific/Auckland"
            },
            DayKeys: new[] { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" });
    }

    private static void ValidateAvailabilityRequest(ExpertAvailabilityUpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Timezone) || request.Timezone.Length > 64)
        {
            throw ApiException.Validation(
                "timezone_invalid",
                "Provide a valid timezone identifier.",
                [new ApiFieldError("timezone", "invalid", "Timezone is required and must be shorter than 64 characters.")]);
        }

        var requiredDays = new[] { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" };
        foreach (var day in requiredDays)
        {
            if (!request.Days.TryGetValue(day, out var scheduleDay))
            {
                throw ApiException.Validation(
                    "schedule_day_missing",
                    "Every day of the week must be present in the schedule payload.",
                    [new ApiFieldError($"days.{day}", "required", $"Add schedule data for {day}.")]);
            }

            if (!TimeOnly.TryParseExact(scheduleDay.Start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                || !TimeOnly.TryParseExact(scheduleDay.End, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            {
                throw ApiException.Validation(
                    "schedule_time_invalid",
                    "Schedule times must use HH:mm format.",
                    [new ApiFieldError($"days.{day}", "invalid_time", $"Use HH:mm for the {day} schedule.")]);
            }

            if (scheduleDay.Active && end <= start)
            {
                throw ApiException.Validation(
                    "schedule_range_invalid",
                    "Availability end times must be later than start times.",
                    [new ApiFieldError($"days.{day}", "invalid_range", $"End time must be later than start time for {day}.")]);
            }
        }
    }

    private static Dictionary<string, ExpertScheduleDayDto> DefaultScheduleDays()
    {
        return new Dictionary<string, ExpertScheduleDayDto>(StringComparer.OrdinalIgnoreCase)
        {
            ["monday"] = new(true, "09:00", "17:00"),
            ["tuesday"] = new(true, "09:00", "17:00"),
            ["wednesday"] = new(true, "09:00", "17:00"),
            ["thursday"] = new(true, "09:00", "17:00"),
            ["friday"] = new(true, "09:00", "16:00"),
            ["saturday"] = new(false, "09:00", "12:00"),
            ["sunday"] = new(false, "09:00", "12:00")
        };
    }

    // ══════════════════════════════════════════════════════
    // P13 · Schedule Exceptions
    // ══════════════════════════════════════════════════════

    public async Task<object> CreateScheduleExceptionAsync(string reviewerId, CreateScheduleExceptionRequest request, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw ApiException.Validation("date_invalid", "Provide a valid date in yyyy-MM-dd format.",
                [new ApiFieldError("date", "invalid", "Date must be in yyyy-MM-dd format.")]);
        }

        if (!request.IsBlocked)
        {
            if (string.IsNullOrWhiteSpace(request.StartTime) || string.IsNullOrWhiteSpace(request.EndTime))
            {
                throw ApiException.Validation("custom_hours_required", "Custom-hours exceptions require start and end times.",
                    [new ApiFieldError("startTime", "required", "Provide start and end times for custom-hours exceptions.")]);
            }

            if (!TimeOnly.TryParseExact(request.StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                || !TimeOnly.TryParseExact(request.EndTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            {
                throw ApiException.Validation("time_invalid", "Times must use HH:mm format.",
                    [new ApiFieldError("startTime", "invalid_time", "Use HH:mm for schedule exception times.")]);
            }

            if (end <= start)
            {
                throw ApiException.Validation("time_range_invalid", "End time must be later than start time.",
                    [new ApiFieldError("endTime", "invalid_range", "End time must be later than start time.")]);
            }
        }

        var existing = await db.ScheduleExceptions
            .AnyAsync(e => e.ReviewerId == reviewerId && e.Date == date, ct);
        if (existing)
        {
            throw ApiException.Validation("duplicate_exception", "An exception already exists for this date.",
                [new ApiFieldError("date", "duplicate", "Remove the existing exception before creating a new one for this date.")]);
        }

        var entity = new ScheduleException
        {
            Id = $"se-{Guid.NewGuid():N}",
            ReviewerId = reviewerId,
            Date = date,
            IsBlocked = request.IsBlocked,
            StartTime = request.IsBlocked ? null : request.StartTime?.Trim(),
            EndTime = request.IsBlocked ? null : request.EndTime?.Trim(),
            Reason = request.Reason?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.ScheduleExceptions.Add(entity);
        await db.SaveChangesAsync(ct);

        return new
        {
            entity.Id,
            date = entity.Date.ToString("yyyy-MM-dd"),
            entity.IsBlocked,
            entity.StartTime,
            entity.EndTime,
            entity.Reason,
            entity.CreatedAt
        };
    }

    public async Task<object> GetScheduleExceptionsAsync(string reviewerId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var query = db.ScheduleExceptions
            .AsNoTracking()
            .Where(e => e.ReviewerId == reviewerId);

        if (from.HasValue)
            query = query.Where(e => e.Date >= from.Value);
        if (to.HasValue)
            query = query.Where(e => e.Date <= to.Value);

        var exceptions = await query
            .OrderBy(e => e.Date)
            .Select(e => new
            {
                e.Id,
                date = e.Date.ToString("yyyy-MM-dd"),
                e.IsBlocked,
                e.StartTime,
                e.EndTime,
                e.Reason,
                e.CreatedAt
            })
            .ToListAsync(ct);

        return new { exceptions };
    }

    public async Task<object> DeleteScheduleExceptionAsync(string reviewerId, string exceptionId, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var entity = await db.ScheduleExceptions.FirstOrDefaultAsync(e => e.Id == exceptionId, ct)
            ?? throw new KeyNotFoundException($"Schedule exception {exceptionId} not found.");

        if (entity.ReviewerId != reviewerId)
            throw new UnauthorizedAccessException("You can only delete your own schedule exceptions.");

        db.ScheduleExceptions.Remove(entity);
        await db.SaveChangesAsync(ct);

        return new { deleted = true };
    }
}
