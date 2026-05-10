using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Aggregates AI trust escalation statistics for the admin AI config dashboard.
/// Per docs/product-strategy/03_current_project_audit.md:
/// "AI confidence/provenance/escalation visibility" is a Partial capability that
/// needs backend aggregation from ReviewEscalations to surface in admin UI.
/// </summary>
public interface IAIEscalationStatsService
{
    Task<AIEscalationStatsResponse> GetStatsAsync(string? configId, CancellationToken ct);
    Task<AIEscalationStatsResponse> GetStatsByTaskTypeAsync(string taskType, CancellationToken ct);
    Task<List<AIEscalationConfigStats>> GetConfigStatsAsync(CancellationToken ct);
}

public sealed record AIEscalationStatsResponse(
    int TotalEvaluations,
    int TotalEscalations,
    double OverallEscalationRate,
    double MeanDivergence,
    List<AIEscalationSubtestBreakdown> SubtestBreakdown,
    List<AIEscalationDailyTrend> TrendLast30Days);

public sealed record AIEscalationConfigStats(
    string ConfigId,
    string Model,
    string TaskType,
    int TotalEvaluations,
    int TotalEscalations,
    double EscalationRate,
    double MeanDivergence,
    DateTimeOffset LastEscalationAt);

public sealed record AIEscalationSubtestBreakdown(
    string SubtestCode,
    int Evaluations,
    int Escalations,
    double EscalationRate);

public sealed record AIEscalationDailyTrend(
    DateTime Date,
    int Evaluations,
    int Escalations);

public sealed class AIEscalationStatsService(LearnerDbContext db, ILogger<AIEscalationStatsService> logger) : IAIEscalationStatsService
{
    private const int DivergenceThreshold = 40;

    public async Task<AIEscalationStatsResponse> GetStatsAsync(string? configId, CancellationToken ct)
    {
        logger.LogDebug("Loading AI escalation stats for config {ConfigId}", configId ?? "all");

        var thirtyDaysAgo = DateTimeOffset.UtcNow.AddDays(-30);

        // Count evaluations in last 30 days
        var evaluationsQuery = db.Evaluations
            .AsNoTracking()
            .Where(e => e.CreatedAt >= thirtyDaysAgo);

        if (!string.IsNullOrWhiteSpace(configId))
            evaluationsQuery = evaluationsQuery.Where(e => e.ModelVersionId == configId);

        var totalEvaluations = await evaluationsQuery.CountAsync(ct);

        // Count escalations in last 30 days
        var escalationsQuery = db.ReviewEscalations
            .AsNoTracking()
            .Where(re => re.CreatedAt >= thirtyDaysAgo);

        if (!string.IsNullOrWhiteSpace(configId))
            escalationsQuery = escalationsQuery.Where(re => re.ConfigId == configId);

        var totalEscalations = await escalationsQuery.CountAsync(ct);
        var overallRate = totalEvaluations > 0 ? (totalEscalations * 100.0 / totalEvaluations) : 0.0;

        // Mean divergence
        var meanDivergence = await escalationsQuery
            .Select(re => (double?)re.Divergence)
            .AverageAsync(ct) ?? 0.0;

        // Subtest breakdown
        var subtestBreakdown = await GetSubtestBreakdownAsync(evaluationsQuery, escalationsQuery, ct);

        // Daily trend
        var trend = await GetDailyTrendAsync(evaluationsQuery, escalationsQuery, thirtyDaysAgo, ct);

        return new AIEscalationStatsResponse(
            totalEvaluations,
            totalEscalations,
            overallRate,
            meanDivergence,
            subtestBreakdown,
            trend);
    }

    public async Task<AIEscalationStatsResponse> GetStatsByTaskTypeAsync(string taskType, CancellationToken ct)
    {
        var thirtyDaysAgo = DateTimeOffset.UtcNow.AddDays(-30);

        // Get config IDs for this task type
        var configIds = await db.AIConfigVersions
            .AsNoTracking()
            .Where(c => c.TaskType == taskType)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (configIds.Count == 0)
        {
            return new AIEscalationStatsResponse(0, 0, 0, 0, [], []);
        }

        var totalEvaluations = await db.Evaluations
            .AsNoTracking()
            .Where(e => e.CreatedAt >= thirtyDaysAgo && configIds.Contains(e.ModelVersionId!))
            .CountAsync(ct);

        var totalEscalations = await db.ReviewEscalations
            .AsNoTracking()
            .Where(re => re.CreatedAt >= thirtyDaysAgo && configIds.Contains(re.ConfigId!))
            .CountAsync(ct);

        var overallRate = totalEvaluations > 0 ? (totalEscalations * 100.0 / totalEvaluations) : 0.0;

        var meanDivergence = await db.ReviewEscalations
            .AsNoTracking()
            .Where(re => re.CreatedAt >= thirtyDaysAgo && configIds.Contains(re.ConfigId!))
            .Select(re => (double?)re.Divergence)
            .AverageAsync(ct) ?? 0.0;

        var subtestBreakdown = new List<AIEscalationSubtestBreakdown>();
        var trend = new List<AIEscalationDailyTrend>();

        return new AIEscalationStatsResponse(
            totalEvaluations,
            totalEscalations,
            overallRate,
            meanDivergence,
            subtestBreakdown,
            trend);
    }

    public async Task<List<AIEscalationConfigStats>> GetConfigStatsAsync(CancellationToken ct)
    {
        var thirtyDaysAgo = DateTimeOffset.UtcNow.AddDays(-30);
        var configs = await db.AIConfigVersions.AsNoTracking().ToListAsync(ct);

        var results = new List<AIEscalationConfigStats>();
        foreach (var config in configs)
        {
            var evalCount = await db.Evaluations
                .AsNoTracking()
                .CountAsync(e => e.CreatedAt >= thirtyDaysAgo && e.ModelVersionId == config.Id, ct);

            var escalationCount = await db.ReviewEscalations
                .AsNoTracking()
                .CountAsync(re => re.CreatedAt >= thirtyDaysAgo && re.ConfigId == config.Id, ct);

            var meanDiv = await db.ReviewEscalations
                .AsNoTracking()
                .Where(re => re.CreatedAt >= thirtyDaysAgo && re.ConfigId == config.Id)
                .Select(re => (double?)re.Divergence)
                .AverageAsync(ct) ?? 0.0;

            var lastEscalation = await db.ReviewEscalations
                .AsNoTracking()
                .Where(re => re.ConfigId == config.Id)
                .OrderByDescending(re => re.CreatedAt)
                .Select(re => (DateTimeOffset?)re.CreatedAt)
                .FirstOrDefaultAsync(ct);

            results.Add(new AIEscalationConfigStats(
                config.Id,
                config.Model,
                config.TaskType,
                evalCount,
                escalationCount,
                evalCount > 0 ? (escalationCount * 100.0 / evalCount) : 0.0,
                meanDiv,
                lastEscalation ?? DateTimeOffset.MinValue));
        }

        return results;
    }

    private async Task<List<AIEscalationSubtestBreakdown>> GetSubtestBreakdownAsync(
        IQueryable<Evaluation> evaluationsQuery,
        IQueryable<ReviewEscalation> escalationsQuery,
        CancellationToken ct)
    {
        // Group by subtest. Evaluations don't have subtest directly, so we join with Attempts.
        var evalBySubtest = await evaluationsQuery
            .Join(
                db.Attempts.AsNoTracking(),
                e => e.AttemptId,
                a => a.Id,
                (e, a) => a.SubtestCode)
            .GroupBy(subtest => subtest)
            .Select(g => new { Subtest = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var escBySubtest = await escalationsQuery
            .Join(
                db.Attempts.AsNoTracking(),
                re => re.AttemptId,
                a => a.Id,
                (re, a) => a.SubtestCode)
            .GroupBy(subtest => subtest)
            .Select(g => new { Subtest = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var allSubtests = evalBySubtest.Select(x => x.Subtest)
            .Union(escBySubtest.Select(x => x.Subtest))
            .Distinct()
            .ToList();

        return allSubtests.Select(subtest =>
        {
            var evals = evalBySubtest.FirstOrDefault(x => x.Subtest == subtest)?.Count ?? 0;
            var escs = escBySubtest.FirstOrDefault(x => x.Subtest == subtest)?.Count ?? 0;
            return new AIEscalationSubtestBreakdown(subtest, evals, escs, evals > 0 ? (escs * 100.0 / evals) : 0.0);
        }).ToList();
    }

    private async Task<List<AIEscalationDailyTrend>> GetDailyTrendAsync(
        IQueryable<Evaluation> evaluationsQuery,
        IQueryable<ReviewEscalation> escalationsQuery,
        DateTimeOffset fromDate,
        CancellationToken ct)
    {
        // Daily aggregation for last 30 days
        var evalDaily = await evaluationsQuery
            .GroupBy(e => new { e.CreatedAt.Year, e.CreatedAt.Month, e.CreatedAt.Day })
            .Select(g => new { Date = new DateTime(g.Key.Year, g.Key.Month, g.Key.Day), Count = g.Count() })
            .ToListAsync(ct);

        var escDaily = await escalationsQuery
            .GroupBy(re => new { re.CreatedAt.Year, re.CreatedAt.Month, re.CreatedAt.Day })
            .Select(g => new { Date = new DateTime(g.Key.Year, g.Key.Month, g.Key.Day), Count = g.Count() })
            .ToListAsync(ct);

        var dates = Enumerable.Range(0, 30)
            .Select(i => fromDate.AddDays(i).Date)
            .ToList();

        return dates.Select(d => new AIEscalationDailyTrend(
            d,
            evalDaily.FirstOrDefault(x => x.Date == d)?.Count ?? 0,
            escDaily.FirstOrDefault(x => x.Date == d)?.Count ?? 0
        )).ToList();
    }
}
