using OetLearner.Api.Domain;
using OetLearner.Api.Services.Readiness;

namespace OetLearner.Api.Tests.Readiness;

public sealed class ReadinessForecastCalculatorTests
{
    private static ReadinessHistory MakeHistory(int weeksAgo, decimal overall)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        return new ReadinessHistory
        {
            Id = $"rh-{Guid.NewGuid():N}",
            UserId = "u-test",
            WeekStartDate = today.AddDays(-7 * weeksAgo),
            RecordedAt = DateTimeOffset.UtcNow.AddDays(-7 * weeksAgo),
            Overall = overall,
            Writing = overall,
            Speaking = overall,
            Reading = overall,
            Listening = overall,
            Vocabulary = overall,
            Risk = "Moderate"
        };
    }

    [Fact]
    public void Compute_ReturnsNull_WhenFewerThanThreeHistoryPoints()
    {
        var calc = new ReadinessForecastCalculator();
        var result = calc.Compute(60m, 75m, 6, new List<ReadinessHistory> { MakeHistory(2, 55m), MakeHistory(1, 60m) });
        Assert.Null(result);
    }

    [Fact]
    public void Compute_HighProbability_WhenAlreadyAtTarget()
    {
        var calc = new ReadinessForecastCalculator();
        var history = new List<ReadinessHistory>
        {
            MakeHistory(3, 70m),
            MakeHistory(2, 72m),
            MakeHistory(1, 75m),
        };
        var result = calc.Compute(75m, 70m, 6, history);
        Assert.NotNull(result);
        Assert.True(result!.Probability >= 90m, $"Expected probability >= 90, got {result.Probability}");
        Assert.Equal(0m, result.RequiredImprovement);
    }

    [Fact]
    public void Compute_LowProbability_WhenSlopeNegative()
    {
        var calc = new ReadinessForecastCalculator();
        var history = new List<ReadinessHistory>
        {
            MakeHistory(3, 70m),
            MakeHistory(2, 65m),
            MakeHistory(1, 60m),
        };
        var result = calc.Compute(60m, 80m, 4, history);
        Assert.NotNull(result);
        Assert.True(result!.Probability <= 30m, $"Expected probability <= 30, got {result.Probability}");
    }

    [Fact]
    public void Compute_ProbabilityClampedToBounds()
    {
        var calc = new ReadinessForecastCalculator();
        var history = new List<ReadinessHistory>
        {
            MakeHistory(3, 10m),
            MakeHistory(2, 10m),
            MakeHistory(1, 10m),
        };
        var result = calc.Compute(10m, 95m, 1, history);
        Assert.NotNull(result);
        Assert.InRange(result!.Probability, 5m, 95m);
    }

    [Fact]
    public void ComputeScenarioOverride_HoursIncreaseImproveProbability()
    {
        var calc = new ReadinessForecastCalculator();
        var low = calc.ComputeScenarioOverride(60m, 80m, 8, 5);
        var high = calc.ComputeScenarioOverride(60m, 80m, 8, 20);
        Assert.True(high.Probability >= low.Probability, $"Expected higher hours to give higher probability. low={low.Probability} high={high.Probability}");
    }

    [Fact]
    public void Compute_HappyPath_ReturnsThreeScenarios()
    {
        var calc = new ReadinessForecastCalculator();
        var history = new List<ReadinessHistory>
        {
            MakeHistory(4, 55m),
            MakeHistory(3, 58m),
            MakeHistory(2, 62m),
            MakeHistory(1, 65m),
        };
        var result = calc.Compute(65m, 80m, 8, history);
        Assert.NotNull(result);
        Assert.Equal(3, result!.Scenarios.Count);
        Assert.Contains(result.Scenarios, s => s.Label == "Current pace");
        Assert.Contains(result.Scenarios, s => s.Label == "Recommended");
        Assert.Contains(result.Scenarios, s => s.Label == "+50% effort");
    }
}
