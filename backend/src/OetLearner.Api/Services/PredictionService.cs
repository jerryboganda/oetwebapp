using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public class PredictionService(LearnerDbContext db)
{
    public async Task<object> GetPredictionsAsync(string userId, string? examTypeCode, CancellationToken ct)
    {
        var query = db.PredictionSnapshots.Where(p => p.UserId == userId);
        if (!string.IsNullOrEmpty(examTypeCode))
            query = query.Where(p => p.ExamTypeCode == examTypeCode);

        var snapshots = await query.OrderByDescending(p => p.ComputedAt).ToListAsync(ct);

        // Group by subtest to return latest per subtest
        var latest = snapshots
            .GroupBy(p => new { p.ExamTypeCode, p.SubtestCode })
            .Select(g => g.First())
            .OrderBy(p => p.SubtestCode);

        return latest.Select(MapSnapshot);
    }

    public async Task<object> GetPredictionAsync(string userId, string examTypeCode, string subtestCode, CancellationToken ct)
    {
        var snapshot = await db.PredictionSnapshots
            .Where(p => p.UserId == userId && p.ExamTypeCode == examTypeCode && p.SubtestCode == subtestCode)
            .OrderByDescending(p => p.ComputedAt)
            .FirstOrDefaultAsync(ct);

        if (snapshot == null)
            return new { available = false };

        return new { available = true, prediction = MapSnapshot(snapshot) };
    }

    public async Task<object> ComputePredictionAsync(string userId, string examTypeCode, string subtestCode, CancellationToken ct)
    {
        // Inline computation — gather evaluation history and compute prediction
        var evaluations = await db.Evaluations
            .Join(db.Attempts, e => e.AttemptId, a => a.Id, (e, a) => new { e, a })
            .Where(x => x.a.UserId == userId && x.e.SubtestCode == subtestCode && x.e.ExamTypeCode == examTypeCode && x.e.State == AsyncState.Completed)
            .OrderByDescending(x => x.e.GeneratedAt)
            .Take(30)
            .Select(x => x.e)
            .ToListAsync(ct);

        if (evaluations.Count < 2)
            return new { available = false, reason = "insufficient_data", minimumRequired = 2 };

        // Parse score ranges — format "300-350" or single number
        var scores = evaluations
            .Select(e => ParseMidScore(e.ScoreRange))
            .Where(s => s > 0)
            .ToList();

        if (scores.Count < 2)
            return new { available = false, reason = "unparseable_scores" };

        // Weighted moving average (recent scores weighted more)
        var weightedSum = 0.0;
        var weightTotal = 0.0;
        for (var i = 0; i < scores.Count; i++)
        {
            var weight = 1.0 / (i + 1); // most recent = 1.0, next = 0.5, etc
            weightedSum += scores[i] * weight;
            weightTotal += weight;
        }
        var predicted = weightedSum / weightTotal;

        // Compute variance for confidence interval
        var variance = scores.Count > 1
            ? scores.Select(s => Math.Pow(s - predicted, 2)).Average()
            : 400.0;
        var stdDev = Math.Sqrt(variance);

        // Trend analysis — are scores improving?
        var recentAvg = scores.Take(Math.Min(5, scores.Count)).Average(s => (double)s);
        var olderScores = scores.Skip(Math.Min(5, scores.Count)).Take(10).ToList();
        var olderAvg = olderScores.Count > 0 ? olderScores.Average(s => (double)s) : recentAvg;
        var trend = recentAvg - olderAvg;
        var trendAdjustment = Math.Clamp(trend * 0.3, -20, 20);

        var mid = Math.Clamp(predicted + trendAdjustment, 0, 500);
        var low = Math.Max(0, mid - stdDev * 1.2);
        var high = Math.Min(500, mid + stdDev * 1.2);

        // Build factors explanation
        var factors = new
        {
            evaluationCount = evaluations.Count,
            recentAverage = Math.Round(recentAvg, 1),
            trend = Math.Round(trend, 1),
            standardDeviation = Math.Round(stdDev, 1),
            trendDirection = trend > 5 ? "improving" : trend < -5 ? "declining" : "stable"
        };

        await StorePredictionAsync(userId, examTypeCode, subtestCode, low, high, JsonSupport.Serialize(factors), ct);

        // Return the latest prediction
        var latest = await db.PredictionSnapshots
            .Where(p => p.UserId == userId && p.ExamTypeCode == examTypeCode && p.SubtestCode == subtestCode)
            .OrderByDescending(p => p.ComputedAt)
            .FirstAsync(ct);

        return new { available = true, prediction = MapSnapshot(latest) };
    }

    private static int ParseMidScore(string scoreRange)
    {
        if (string.IsNullOrWhiteSpace(scoreRange)) return 0;
        var parts = scoreRange.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], out var lo) && int.TryParse(parts[1], out var hi))
            return (lo + hi) / 2;
        if (int.TryParse(scoreRange.Trim(), out var single))
            return single;
        return 0;
    }

    public async Task StorePredictionAsync(string userId, string examTypeCode, string subtestCode, double low, double high, string factorsJson, CancellationToken ct)
    {
        var mid = (low + high) / 2.0;
        var evalCount = await db.Evaluations
            .Join(db.Attempts, e => e.AttemptId, a => a.Id, (e, a) => new { e, a })
            .CountAsync(x => x.a.UserId == userId, ct);
        var confidence = evalCount >= 20 ? "good" : evalCount >= 10 ? "moderate" : evalCount >= 5 ? "low" : "insufficient";

        db.PredictionSnapshots.Add(new PredictionSnapshot
        {
            Id = $"pred-{Guid.NewGuid():N}",
            UserId = userId,
            ExamTypeCode = examTypeCode,
            SubtestCode = subtestCode,
            PredictedScoreLow = (int)low,
            PredictedScoreHigh = (int)high,
            PredictedScoreMid = (int)mid,
            ConfidenceLevel = confidence,
            FactorsJson = factorsJson,
            TrendJson = "{}",
            EvaluationCount = evalCount,
            ComputedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    private static object MapSnapshot(PredictionSnapshot p) => new
    {
        id = p.Id,
        examTypeCode = p.ExamTypeCode,
        subtestCode = p.SubtestCode,
        predictedScoreLow = p.PredictedScoreLow,
        predictedScoreHigh = p.PredictedScoreHigh,
        predictedScoreMid = p.PredictedScoreMid,
        confidenceLevel = p.ConfidenceLevel.ToString().ToLower(),
        factorsJson = p.FactorsJson,
        trendJson = p.TrendJson,
        evaluationCount = p.EvaluationCount,
        computedAt = p.ComputedAt
    };
}
