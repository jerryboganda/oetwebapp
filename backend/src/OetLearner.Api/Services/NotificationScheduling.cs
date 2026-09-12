using System.Security.Cryptography;
using System.Text;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public static class NotificationScheduling
{
    public static string BuildDedupeKey(NotificationEventKey eventKey, string authAccountId, string entityType, string entityId, string versionOrDateBucket)
    {
        var raw = $"{eventKey}:{authAccountId}:{entityType}:{entityId}:{versionOrDateBucket}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Coarse hourly bucket used to aggregate repeat operational alerts.
    /// <para>
    /// A single stuck-job incident must not produce dozens of identical emails.
    /// Because the notification dedupe key includes the entity id, keying a
    /// stuck-job alert on the individual <c>BackgroundJobs.Id</c> emits one alert
    /// per affected job. Aggregating on (event, job type, hour) collapses a batch
    /// of jobs wedged by the same cause into one admin alert per type per hour,
    /// while a genuinely new incident in a later hour still raises its own alert —
    /// so real warnings are preserved, only duplicates are suppressed
    /// (12 Sep 2026 brief: "one incident should not generate dozens of identical
    /// alerts. Use sensible deduplication/cooldown/escalation").
    /// </para>
    /// </summary>
    public static string BuildIncidentBucket(DateTimeOffset utcTimestamp)
        => utcTimestamp.UtcDateTime.ToString("yyyy-MM-ddTHH");

    public static string GetLocalDateBucket(DateTimeOffset utcTimestamp, string timezone)
    {
        var localTime = ConvertToLocalTime(utcTimestamp, timezone);
        return DateOnly.FromDateTime(localTime.DateTime).ToString("yyyy-MM-dd");
    }

    public static bool IsWithinQuietHours(DateTimeOffset utcTimestamp, string timezone, int? startMinutes, int? endMinutes)
    {
        if (!startMinutes.HasValue || !endMinutes.HasValue)
        {
            return false;
        }

        var localTime = ConvertToLocalTime(utcTimestamp, timezone);
        var minuteOfDay = (localTime.Hour * 60) + localTime.Minute;
        return IsWithinQuietHoursWindow(minuteOfDay, startMinutes.Value, endMinutes.Value);
    }

    public static DateTimeOffset GetNextQuietHoursEndUtc(DateTimeOffset utcTimestamp, string timezone, int? startMinutes, int? endMinutes)
    {
        if (!startMinutes.HasValue || !endMinutes.HasValue)
        {
            return utcTimestamp;
        }

        var timeZoneInfo = ResolveTimeZone(timezone);
        var localTime = TimeZoneInfo.ConvertTime(utcTimestamp, timeZoneInfo);
        var minuteOfDay = (localTime.Hour * 60) + localTime.Minute;

        if (!IsWithinQuietHoursWindow(minuteOfDay, startMinutes.Value, endMinutes.Value))
        {
            return utcTimestamp;
        }

        var localDate = DateOnly.FromDateTime(localTime.DateTime);
        var endDate = startMinutes.Value <= endMinutes.Value || minuteOfDay < endMinutes.Value
            ? localDate
            : localDate.AddDays(1);
        var endLocal = endDate.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(endMinutes.Value)), DateTimeKind.Unspecified);
        var endOffset = new DateTimeOffset(endLocal, timeZoneInfo.GetUtcOffset(endLocal));
        return endOffset.ToUniversalTime();
    }

    public static int? ParseLocalTimeToMinutes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!TimeOnly.TryParse(value, out var time))
        {
            return null;
        }

        return (time.Hour * 60) + time.Minute;
    }

    public static string? FormatMinutesAsLocalTime(int? minutes)
    {
        if (!minutes.HasValue)
        {
            return null;
        }

        var time = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes.Value));
        return time.ToString("HH:mm");
    }

    public static DateTimeOffset ConvertToLocalTime(DateTimeOffset utcTimestamp, string timezone)
        => TimeZoneInfo.ConvertTime(utcTimestamp, ResolveTimeZone(timezone));

    public static TimeZoneInfo ResolveTimeZone(string? timezone)
    {
        if (!string.IsNullOrWhiteSpace(timezone))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timezone);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }

    private static bool IsWithinQuietHoursWindow(int minuteOfDay, int startMinutes, int endMinutes)
    {
        if (startMinutes == endMinutes)
        {
            return false;
        }

        if (startMinutes < endMinutes)
        {
            return minuteOfDay >= startMinutes && minuteOfDay < endMinutes;
        }

        return minuteOfDay >= startMinutes || minuteOfDay < endMinutes;
    }
}
