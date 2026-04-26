using System;
using System.Security.Cryptography;
using System.Text;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using Xunit;

namespace OetLearner.Api.Tests;

public class NotificationSchedulingTests
{
    // ── BuildDedupeKey ──────────────────────────────────────────────────

    [Fact]
    public void BuildDedupeKey_ProducesStableSha256Hex()
    {
        var key = NotificationScheduling.BuildDedupeKey(
            NotificationEventKey.LearnerEvaluationCompleted, "user-1", "Evaluation", "evt-99", "v1");

        // 64 hex chars, uppercase (Convert.ToHexString default).
        Assert.Equal(64, key.Length);
        Assert.Matches("^[0-9A-F]{64}$", key);

        var raw = $"{NotificationEventKey.LearnerEvaluationCompleted}:user-1:Evaluation:evt-99:v1";
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        Assert.Equal(expected, key);
    }

    [Fact]
    public void BuildDedupeKey_DiffersWhenAnyComponentChanges()
    {
        var baseKey = NotificationScheduling.BuildDedupeKey(
            NotificationEventKey.LearnerReviewCompleted, "u", "Review", "id", "2026-04-26");

        Assert.NotEqual(baseKey, NotificationScheduling.BuildDedupeKey(
            NotificationEventKey.LearnerReviewRequested, "u", "Review", "id", "2026-04-26"));
        Assert.NotEqual(baseKey, NotificationScheduling.BuildDedupeKey(
            NotificationEventKey.LearnerReviewCompleted, "u2", "Review", "id", "2026-04-26"));
        Assert.NotEqual(baseKey, NotificationScheduling.BuildDedupeKey(
            NotificationEventKey.LearnerReviewCompleted, "u", "Mock", "id", "2026-04-26"));
        Assert.NotEqual(baseKey, NotificationScheduling.BuildDedupeKey(
            NotificationEventKey.LearnerReviewCompleted, "u", "Review", "id2", "2026-04-26"));
        Assert.NotEqual(baseKey, NotificationScheduling.BuildDedupeKey(
            NotificationEventKey.LearnerReviewCompleted, "u", "Review", "id", "2026-04-27"));
    }

    // ── ResolveTimeZone ─────────────────────────────────────────────────

    [Fact]
    public void ResolveTimeZone_FallsBackToUtcForUnknownOrEmpty()
    {
        Assert.Equal(TimeZoneInfo.Utc, NotificationScheduling.ResolveTimeZone(null));
        Assert.Equal(TimeZoneInfo.Utc, NotificationScheduling.ResolveTimeZone(""));
        Assert.Equal(TimeZoneInfo.Utc, NotificationScheduling.ResolveTimeZone("   "));
        Assert.Equal(TimeZoneInfo.Utc, NotificationScheduling.ResolveTimeZone("Mars/Olympus"));
    }

    [Fact]
    public void ResolveTimeZone_ReturnsKnownZone()
    {
        // Both Windows and IANA IDs are tried; pick one likely available on the host.
        var tz = NotificationScheduling.ResolveTimeZone("UTC");
        Assert.Equal(TimeZoneInfo.Utc.Id, tz.Id);
    }

    // ── GetLocalDateBucket ──────────────────────────────────────────────

    [Fact]
    public void GetLocalDateBucket_FormatsAsYearMonthDay()
    {
        var bucket = NotificationScheduling.GetLocalDateBucket(
            new DateTimeOffset(2026, 4, 26, 23, 30, 0, TimeSpan.Zero), "UTC");
        Assert.Equal("2026-04-26", bucket);
    }

    // ── ParseLocalTimeToMinutes / FormatMinutesAsLocalTime ──────────────

    [Theory]
    [InlineData("00:00", 0)]
    [InlineData("09:30", 570)]
    [InlineData("23:59", 1439)]
    public void ParseLocalTimeToMinutes_ParsesValidValues(string input, int expected)
    {
        Assert.Equal(expected, NotificationScheduling.ParseLocalTimeToMinutes(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a time")]
    [InlineData("25:99")]
    public void ParseLocalTimeToMinutes_ReturnsNullForInvalid(string? input)
    {
        Assert.Null(NotificationScheduling.ParseLocalTimeToMinutes(input));
    }

    [Fact]
    public void FormatMinutesAsLocalTime_RoundTripsWithParse()
    {
        var formatted = NotificationScheduling.FormatMinutesAsLocalTime(570);
        Assert.Equal("09:30", formatted);
        Assert.Equal(570, NotificationScheduling.ParseLocalTimeToMinutes(formatted));
    }

    [Fact]
    public void FormatMinutesAsLocalTime_ReturnsNullWhenNull()
    {
        Assert.Null(NotificationScheduling.FormatMinutesAsLocalTime(null));
    }

    // ── IsWithinQuietHours ──────────────────────────────────────────────

    [Fact]
    public void IsWithinQuietHours_ReturnsFalseWhenUnconfigured()
    {
        var ts = new DateTimeOffset(2026, 4, 26, 3, 0, 0, TimeSpan.Zero);
        Assert.False(NotificationScheduling.IsWithinQuietHours(ts, "UTC", null, null));
        Assert.False(NotificationScheduling.IsWithinQuietHours(ts, "UTC", 60, null));
        Assert.False(NotificationScheduling.IsWithinQuietHours(ts, "UTC", null, 60));
    }

    [Fact]
    public void IsWithinQuietHours_ReturnsFalseWhenStartEqualsEnd()
    {
        var ts = new DateTimeOffset(2026, 4, 26, 3, 0, 0, TimeSpan.Zero);
        Assert.False(NotificationScheduling.IsWithinQuietHours(ts, "UTC", 60, 60));
    }

    [Theory]
    // Same-day window 22:00 → 23:00 (UTC)
    [InlineData(22, 0, 22 * 60, 23 * 60, true)]
    [InlineData(22, 30, 22 * 60, 23 * 60, true)]
    [InlineData(23, 0, 22 * 60, 23 * 60, false)] // end is exclusive
    [InlineData(21, 59, 22 * 60, 23 * 60, false)]
    // Overnight window 22:00 → 07:00
    [InlineData(23, 0, 22 * 60, 7 * 60, true)]
    [InlineData(2, 0, 22 * 60, 7 * 60, true)]
    [InlineData(6, 59, 22 * 60, 7 * 60, true)]
    [InlineData(7, 0, 22 * 60, 7 * 60, false)]
    [InlineData(12, 0, 22 * 60, 7 * 60, false)]
    public void IsWithinQuietHours_HandlesSameDayAndOvernightWindows(
        int hour, int minute, int startMinutes, int endMinutes, bool expected)
    {
        var ts = new DateTimeOffset(2026, 4, 26, hour, minute, 0, TimeSpan.Zero);
        Assert.Equal(expected, NotificationScheduling.IsWithinQuietHours(ts, "UTC", startMinutes, endMinutes));
    }

    // ── GetNextQuietHoursEndUtc ─────────────────────────────────────────

    [Fact]
    public void GetNextQuietHoursEndUtc_ReturnsInputWhenNotInQuietHours()
    {
        var ts = new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);
        var result = NotificationScheduling.GetNextQuietHoursEndUtc(ts, "UTC", 22 * 60, 7 * 60);
        Assert.Equal(ts, result);
    }

    [Fact]
    public void GetNextQuietHoursEndUtc_ReturnsInputWhenWindowUnconfigured()
    {
        var ts = new DateTimeOffset(2026, 4, 26, 2, 0, 0, TimeSpan.Zero);
        Assert.Equal(ts, NotificationScheduling.GetNextQuietHoursEndUtc(ts, "UTC", null, null));
    }

    [Fact]
    public void GetNextQuietHoursEndUtc_AdvancesToEndForSameDayWindow()
    {
        // 22:30 UTC, window 22:00–23:00 UTC → next end is 23:00 same day.
        var ts = new DateTimeOffset(2026, 4, 26, 22, 30, 0, TimeSpan.Zero);
        var result = NotificationScheduling.GetNextQuietHoursEndUtc(ts, "UTC", 22 * 60, 23 * 60);
        Assert.Equal(new DateTimeOffset(2026, 4, 26, 23, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void GetNextQuietHoursEndUtc_AdvancesToNextDayWhenAfterMidnightInOvernightWindow()
    {
        // 02:00 UTC, window 22:00→07:00 → next end is 07:00 SAME local day (still AM).
        var ts = new DateTimeOffset(2026, 4, 26, 2, 0, 0, TimeSpan.Zero);
        var result = NotificationScheduling.GetNextQuietHoursEndUtc(ts, "UTC", 22 * 60, 7 * 60);
        Assert.Equal(new DateTimeOffset(2026, 4, 26, 7, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void GetNextQuietHoursEndUtc_AdvancesToNextDayWhenBeforeMidnightInOvernightWindow()
    {
        // 23:30 UTC, window 22:00→07:00 → next end is 07:00 NEXT day.
        var ts = new DateTimeOffset(2026, 4, 26, 23, 30, 0, TimeSpan.Zero);
        var result = NotificationScheduling.GetNextQuietHoursEndUtc(ts, "UTC", 22 * 60, 7 * 60);
        Assert.Equal(new DateTimeOffset(2026, 4, 27, 7, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void GetNextQuietHoursEndUtc_ResultIsAlwaysUtc()
    {
        var ts = new DateTimeOffset(2026, 4, 26, 22, 30, 0, TimeSpan.Zero);
        var result = NotificationScheduling.GetNextQuietHoursEndUtc(ts, "UTC", 22 * 60, 23 * 60);
        Assert.Equal(TimeSpan.Zero, result.Offset);
    }
}
