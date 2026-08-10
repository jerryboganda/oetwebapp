namespace OetLearner.Api.Services.Speaking;

/// <summary>Single source of truth for the enforced Speaking tutor booking rules.</summary>
public static class SpeakingBookingPolicy
{
    public static bool TutorWindowClosed(DateOnly? targetExamDate, DateOnly today)
        => targetExamDate is null || targetExamDate.Value.DayNumber - today.DayNumber < 7;

    /// <summary>Exactly 24 hours is not eligible; only strictly more than 24 hours is.</summary>
    public static bool FullRefundEligible(DateTimeOffset scheduledStartAt, DateTimeOffset now)
        => scheduledStartAt - now > TimeSpan.FromHours(24);

    public static bool RescheduleAllowed(DateTimeOffset scheduledStartAt, DateTimeOffset now)
        => scheduledStartAt > now;
}
