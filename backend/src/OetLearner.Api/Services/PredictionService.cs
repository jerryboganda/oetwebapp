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
        // Queue background computation
        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"job-pred-{Guid.NewGuid():N}",
            Type = JobType.PredictionComputation,
            State = AsyncState.Queued,
            ResourceId = userId,
            PayloadJson = JsonSupport.Serialize(new { userId, examTypeCode, subtestCode }),
            CreatedAt = DateTimeOffset.UtcNow,
            AvailableAt = DateTimeOffset.UtcNow,
            LastTransitionAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return new { queued = true };
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
