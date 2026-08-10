using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests;

public sealed class SpeakingBookingPolicyTests
{
    [Fact]
    public void TutorWindow_FailsClosedWithoutDate_AndAllowsExactlySevenDays()
    {
        var today = new DateOnly(2026, 8, 10);

        Assert.True(SpeakingBookingPolicy.TutorWindowClosed(null, today));
        Assert.True(SpeakingBookingPolicy.TutorWindowClosed(today.AddDays(6), today));
        Assert.False(SpeakingBookingPolicy.TutorWindowClosed(today.AddDays(7), today));
    }

    [Fact]
    public void FullRefund_RequiresStrictlyMoreThan24Hours()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

        Assert.False(SpeakingBookingPolicy.FullRefundEligible(now.AddHours(24), now));
        Assert.True(SpeakingBookingPolicy.FullRefundEligible(now.AddHours(24).AddSeconds(1), now));
        Assert.True(SpeakingBookingPolicy.RescheduleAllowed(now.AddSeconds(1), now));
        Assert.False(SpeakingBookingPolicy.RescheduleAllowed(now, now));
    }
}
