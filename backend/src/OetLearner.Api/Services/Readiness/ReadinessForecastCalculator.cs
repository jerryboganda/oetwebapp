using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Readiness;

/// <summary>
/// Target-date probability forecast. Runs a linear regression over the
/// learner's weekly <see cref="ReadinessHistory"/> series to estimate
/// improvement rate, then applies a logistic function on
/// <c>weeksAvailable - weeksNeeded</c> to produce a probability of hitting
/// the target by exam day.
/// </summary>
public sealed class ReadinessForecastCalculator
{
    public const int MinHistoryPointsForForecast = 3;
    public const decimal MinAssumedSlope = 0.1m;
    public const decimal DefaultPointsPerHourCoefficient = 0.4m;

    public ForecastResult? Compute(
        decimal currentReadiness,
        decimal target,
        int weeksRemaining,
        IReadOnlyList<ReadinessHistory> history)
    {
        if (history.Count < MinHistoryPointsForForecast)
        {
            return null;
        }

        var window = history.TakeLast(8).ToList();
        var slope = (decimal)ComputeSlope(window);
        var requiredImprovement = Math.Max(0m, target - currentReadiness);

        decimal probability;
        decimal weeksNeeded;

        if (requiredImprovement == 0m)
        {
            probability = 95m;
            weeksNeeded = 0m;
        }
        else if (slope <= 0m)
        {
            var penalty = (decimal)Math.Max(5d, 30d - (double)requiredImprovement);
            probability = penalty;
            weeksNeeded = 999m;
        }
        else
        {
            weeksNeeded = requiredImprovement / Math.Max(slope, MinAssumedSlope);
            var diff = (double)(weeksRemaining - weeksNeeded);
            var logistic = 100d / (1d + Math.Exp(-diff / 1.5d));
            probability = (decimal)Math.Clamp(logistic, 5d, 95d);
        }

        var scenarios = BuildScenarios(currentReadiness, target, weeksRemaining, slope);

        return new ForecastResult(
            Probability: Math.Round(probability, 2),
            WeeksNeeded: Math.Round(weeksNeeded, 2),
            WeeksAvailable: weeksRemaining,
            RequiredImprovement: Math.Round(requiredImprovement, 2),
            SlopePerWeek: Math.Round(slope, 4),
            Scenarios: scenarios);
    }

    public ForecastResult ComputeScenarioOverride(
        decimal currentReadiness,
        decimal target,
        int weeksRemaining,
        int hoursPerWeek)
    {
        var projectedSlope = DefaultPointsPerHourCoefficient * hoursPerWeek;
        var requiredImprovement = Math.Max(0m, target - currentReadiness);
        var weeksNeeded = requiredImprovement / Math.Max(projectedSlope, MinAssumedSlope);
        var diff = (double)(weeksRemaining - weeksNeeded);
        var logistic = 100d / (1d + Math.Exp(-diff / 1.5d));
        return new ForecastResult(
            Probability: (decimal)Math.Clamp(logistic, 5d, 95d),
            WeeksNeeded: Math.Round(weeksNeeded, 2),
            WeeksAvailable: weeksRemaining,
            RequiredImprovement: Math.Round(requiredImprovement, 2),
            SlopePerWeek: Math.Round(projectedSlope, 4),
            Scenarios: Array.Empty<ForecastScenarioDto>());
    }

    private static IReadOnlyList<ForecastScenarioDto> BuildScenarios(
        decimal currentReadiness,
        decimal target,
        int weeksRemaining,
        decimal currentSlope)
    {
        var hoursFromSlope = currentSlope <= 0 ? 0 : currentSlope / DefaultPointsPerHourCoefficient;
        var current = new ForecastScenarioDto(
            Label: "Current pace",
            HoursPerWeek: (int)Math.Round(hoursFromSlope, 0),
            ProjectedReadinessAtTarget: Math.Round(Math.Min(100m, currentReadiness + currentSlope * weeksRemaining), 2),
            Probability: 0m);

        var requiredImprovement = Math.Max(0m, target - currentReadiness);
        var recommendedHours = weeksRemaining == 0 ? 0 : (int)Math.Ceiling(requiredImprovement / DefaultPointsPerHourCoefficient / Math.Max(weeksRemaining, 1) * 1.2m);
        var recommended = ScenarioForHours("Recommended", recommendedHours, currentReadiness, target, weeksRemaining);
        var boost = ScenarioForHours("+50% effort", (int)Math.Ceiling(recommendedHours * 1.5m), currentReadiness, target, weeksRemaining);

        // Fill in probability for current pace using same logistic
        var currentProbability = Probability(currentReadiness, target, weeksRemaining, currentSlope);
        return new[] { current with { Probability = currentProbability }, recommended, boost };
    }

    private static ForecastScenarioDto ScenarioForHours(string label, int hoursPerWeek, decimal currentReadiness, decimal target, int weeksRemaining)
    {
        var slope = DefaultPointsPerHourCoefficient * hoursPerWeek;
        var projected = Math.Round(Math.Min(100m, currentReadiness + slope * weeksRemaining), 2);
        var probability = Probability(currentReadiness, target, weeksRemaining, slope);
        return new ForecastScenarioDto(label, hoursPerWeek, projected, probability);
    }

    private static decimal Probability(decimal currentReadiness, decimal target, int weeksRemaining, decimal slope)
    {
        var requiredImprovement = Math.Max(0m, target - currentReadiness);
        if (requiredImprovement == 0m) return 95m;
        if (slope <= 0m) return Math.Max(5m, 30m - requiredImprovement);
        var weeksNeeded = requiredImprovement / Math.Max(slope, MinAssumedSlope);
        var diff = (double)(weeksRemaining - weeksNeeded);
        var logistic = 100d / (1d + Math.Exp(-diff / 1.5d));
        return (decimal)Math.Clamp(logistic, 5d, 95d);
    }

    private static double ComputeSlope(IReadOnlyList<ReadinessHistory> series)
    {
        if (series.Count < 2) return 0d;
        var n = series.Count;
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        for (int i = 0; i < n; i++)
        {
            double x = i;
            double y = (double)series[i].Overall;
            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumX2 += x * x;
        }
        double denom = n * sumX2 - sumX * sumX;
        if (denom == 0) return 0d;
        return (n * sumXY - sumX * sumY) / denom;
    }
}

public sealed record ForecastResult(
    decimal Probability,
    decimal WeeksNeeded,
    int WeeksAvailable,
    decimal RequiredImprovement,
    decimal SlopePerWeek,
    IReadOnlyList<ForecastScenarioDto> Scenarios);

public sealed record ForecastScenarioDto(
    string Label,
    int HoursPerWeek,
    decimal ProjectedReadinessAtTarget,
    decimal Probability);
