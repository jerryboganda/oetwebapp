using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public class Sm2SchedulerTests
{
    private readonly ISpacedRepetitionScheduler _scheduler = new Sm2Scheduler();

    [Fact]
    public void First_review_passed_uses_interval_1()
    {
        var state = new Sm2State(EaseFactor: 2.5, IntervalDays: 1, ReviewCount: 0, CorrectCount: 0);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var u = _scheduler.Schedule(state, quality: 4, today);

        Assert.Equal(1, u.ReviewCount);
        Assert.Equal(1, u.CorrectCount);
        Assert.Equal(1, u.IntervalDays);
        Assert.Equal(today.AddDays(1), u.NextReviewDate);
        Assert.True(u.CorrectAnswer);
    }

    [Fact]
    public void Second_review_passed_uses_interval_6()
    {
        var state = new Sm2State(EaseFactor: 2.5, IntervalDays: 1, ReviewCount: 1, CorrectCount: 1);
        var u = _scheduler.Schedule(state, quality: 4);

        Assert.Equal(2, u.ReviewCount);
        Assert.Equal(6, u.IntervalDays);
    }

    [Fact]
    public void Third_review_multiplies_previous_interval_by_ease_factor()
    {
        var state = new Sm2State(EaseFactor: 2.5, IntervalDays: 6, ReviewCount: 2, CorrectCount: 2);
        var u = _scheduler.Schedule(state, quality: 4);

        // 6 * ~2.5 = 15 (EF unchanged at quality=4 since 0.1 - 0.08 - 0.02 = 0)
        Assert.Equal(3, u.ReviewCount);
        Assert.InRange(u.IntervalDays, 14, 16);
    }

    [Fact]
    public void Failed_review_resets_interval_to_one_and_increments_review_count()
    {
        var state = new Sm2State(EaseFactor: 2.5, IntervalDays: 15, ReviewCount: 3, CorrectCount: 3);
        var u = _scheduler.Schedule(state, quality: 1);

        Assert.Equal(4, u.ReviewCount);
        Assert.Equal(3, u.CorrectCount);   // correct count not incremented
        Assert.Equal(1, u.IntervalDays);   // interval reset
        Assert.False(u.CorrectAnswer);
    }

    [Fact]
    public void Ease_factor_is_clamped_to_minimum_1_3()
    {
        // Start near the floor.
        var state = new Sm2State(EaseFactor: 1.3, IntervalDays: 1, ReviewCount: 1, CorrectCount: 1);
        var u = _scheduler.Schedule(state, quality: 3);   // barely-pass decrements EF further

        Assert.True(u.EaseFactor >= 1.3);
    }

    [Fact]
    public void Quality_out_of_range_throws()
    {
        var state = new Sm2State(2.5, 1, 0, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => _scheduler.Schedule(state, quality: 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => _scheduler.Schedule(state, quality: -1));
    }

    [Theory]
    [InlineData(0, "new", 0)]             // never reviewed
    [InlineData(1, "learning", 1)]
    [InlineData(3, "learning", 3)]
    [InlineData(4, "reviewing", 4)]
    [InlineData(10, "mastered", 10)]
    public void DeriveMastery_enforces_tier_thresholds(int reviewCount, string expected, int correct)
    {
        var update = new Sm2Update(
            EaseFactor: 2.5,
            IntervalDays: 1,
            ReviewCount: reviewCount,
            CorrectCount: correct,
            NextReviewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            CorrectAnswer: true);

        Assert.Equal(expected, update.DeriveMastery());
    }

    [Fact]
    public void DeriveMastery_requires_both_review_and_correct_thresholds_for_mastered()
    {
        // 10 reviews but only 7 correct → NOT mastered.
        var u = new Sm2Update(2.5, 1, 10, 7, DateOnly.FromDateTime(DateTime.UtcNow), true);
        Assert.Equal("reviewing", u.DeriveMastery());
    }
}
