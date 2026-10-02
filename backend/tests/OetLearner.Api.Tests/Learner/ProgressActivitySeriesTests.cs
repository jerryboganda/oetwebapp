using OetLearner.Api.Services;

namespace OetLearner.Api.Tests.Learner;

/// <summary>
/// The /v1/progress completion and volume charts come from the learner's own
/// attempts (they were fixed demo arrays). The clock is pinned to Thursday
/// 1 Oct 2026, 10:00 UTC.
/// </summary>
public class ProgressActivitySeriesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Completion_CountsCompletedAttemptsPerDayOverTheLastSevenDays()
    {
        DateTimeOffset?[] completedAt =
        [
            Now.AddHours(-1),
            Now.AddHours(-2),
            Now.AddDays(-1),
            Now.AddDays(-6), // Friday 25 Sep: the oldest day in the window
            Now.AddDays(-7), // Thursday 24 Sep: outside it
            null,            // not completed
        ];

        var (completion, _) = LearnerService.BuildProgressActivitySeries(Now, completedAt, []);

        Assert.Equal(["Fri", "Sat", "Sun", "Mon", "Tue", "Wed", "Thu"], completion.Select(point => point.Day));
        Assert.Equal([1, 0, 0, 0, 0, 1, 2], completion.Select(point => point.Completed));
    }

    [Fact]
    public void SubmissionVolume_CountsSubmissionsPerMondayWeekOverTheLastFiveWeeks()
    {
        DateTimeOffset?[] submittedAt =
        [
            Now,
            new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero),   // Monday: this week
            new DateTimeOffset(2026, 9, 27, 23, 59, 0, TimeSpan.Zero), // Sunday before: last week
            new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero),  // oldest week in the window
            new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero),  // outside it
            null,
        ];

        var (_, volume) = LearnerService.BuildProgressActivitySeries(Now, [], submittedAt);

        Assert.Equal(["31 Aug", "7 Sep", "14 Sep", "21 Sep", "28 Sep"], volume.Select(point => point.Week));
        Assert.Equal([1, 0, 0, 1, 2], volume.Select(point => point.Submissions));
    }

    [Fact]
    public void NoActivity_YieldsZeroesNotInventedNumbers()
    {
        var (completion, volume) = LearnerService.BuildProgressActivitySeries(Now, [], []);

        Assert.Equal(7, completion.Count);
        Assert.All(completion, point => Assert.Equal(0, point.Completed));
        Assert.Equal(5, volume.Count);
        Assert.All(volume, point => Assert.Equal(0, point.Submissions));
    }
}
