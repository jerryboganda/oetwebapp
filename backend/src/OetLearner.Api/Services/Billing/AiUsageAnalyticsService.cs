using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Read-only aggregation over <see cref="AiUsageRecord"/> for the learner-
/// facing and admin-facing AI usage analytics surfaces.
/// </summary>
public interface IAiUsageAnalyticsService
{
    Task<LearnerUsageSummary> GetLearnerSummaryAsync(string userId, DateOnly from, DateOnly to, CancellationToken ct);
    Task<AdminUsageSummary> GetAdminSummaryAsync(DateOnly from, DateOnly to, string? feature, string? provider, CancellationToken ct);
}

public sealed record FeatureBreakdown(string FeatureCode, int Calls, long TotalTokens, decimal CostUsd);
public sealed record ProviderBreakdown(string Provider, int Calls, long TotalTokens, decimal CostUsd, decimal SuccessRate, int AvgLatencyMs);
public sealed record DailyBucket(string Day, int Calls, long TotalTokens, decimal CostUsd);

public sealed record LearnerUsageSummary(
    DateOnly From,
    DateOnly To,
    int TotalCalls,
    long TotalTokens,
    decimal TotalCostUsd,
    int FailedCalls,
    int CreditsUsed,
    int WalletBalance,
    IReadOnlyList<FeatureBreakdown> ByFeature,
    IReadOnlyList<DailyBucket> Daily,
    int ForecastCalls30d,
    int ForecastCredits30d,
    decimal ForecastCostUsd30d,
    int SuggestedTopUpCredits);

public sealed record AdminUsageSummary(
    DateOnly From,
    DateOnly To,
    int TotalCalls,
    long TotalTokens,
    decimal TotalCostUsd,
    int UniqueUsers,
    decimal SuccessRate,
    int AvgLatencyMs,
    IReadOnlyList<FeatureBreakdown> ByFeature,
    IReadOnlyList<ProviderBreakdown> ByProvider,
    IReadOnlyList<DailyBucket> Daily,
    IReadOnlyList<TopUserUsage> TopUsers);

public sealed record TopUserUsage(string UserId, int Calls, long TotalTokens, decimal CostUsd);

public sealed class AiUsageAnalyticsService : IAiUsageAnalyticsService
{
    private readonly LearnerDbContext _db;

    public AiUsageAnalyticsService(LearnerDbContext db) => _db = db;

    public async Task<LearnerUsageSummary> GetLearnerSummaryAsync(string userId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var fromTs = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toTs = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var q = _db.AiUsageRecords.Where(r => r.UserId == userId && r.CreatedAt >= fromTs && r.CreatedAt <= toTs);

        var totalCalls = await q.CountAsync(ct);
        var totalTokens = await q.SumAsync(r => (long?)(r.PromptTokens + r.CompletionTokens), ct) ?? 0L;
        var totalCost = await q.SumAsync(r => (decimal?)r.CostEstimateUsd, ct) ?? 0m;
        var failedCalls = await q.CountAsync(r => r.Outcome != AiCallOutcome.Success, ct);

        var byFeatureRaw = await q
            .GroupBy(r => r.FeatureCode)
            .Select(g => new
            {
                Feature = g.Key,
                Calls = g.Count(),
                Tokens = g.Sum(x => (long)(x.PromptTokens + x.CompletionTokens)),
                Cost = g.Sum(x => x.CostEstimateUsd),
            })
            .ToListAsync(ct);
        var byFeature = byFeatureRaw
            .Select(f => new FeatureBreakdown(f.Feature, f.Calls, f.Tokens, f.Cost))
            .OrderByDescending(f => f.Calls)
            .ToList();

        var dailyRaw = await q
            .GroupBy(r => r.PeriodDayKey)
            .Select(g => new
            {
                Day = g.Key,
                Calls = g.Count(),
                Tokens = g.Sum(x => (long)(x.PromptTokens + x.CompletionTokens)),
                Cost = g.Sum(x => x.CostEstimateUsd),
            })
            .ToListAsync(ct);
        var daily = dailyRaw
            .Select(d => new DailyBucket(d.Day, d.Calls, d.Tokens, d.Cost))
            .OrderBy(b => b.Day)
            .ToList();

        var creditsUsed = await _db.AiCreditLedger
            .Where(e => e.UserId == userId && e.CreatedAt >= fromTs && e.CreatedAt <= toTs && e.TokensDelta < 0)
            .SumAsync(e => (int?)e.TokensDelta, ct) ?? 0;
        creditsUsed = Math.Abs(creditsUsed);

        var walletBalance = await _db.Wallets
            .Where(w => w.UserId == userId)
            .Select(w => (int?)w.CreditBalance)
            .FirstOrDefaultAsync(ct) ?? 0;

        var forecast = await _db.UsageForecastSnapshots
            .Where(f => f.UserId == userId && f.FeatureCode == "*")
            .OrderByDescending(f => f.SnapshotDate)
            .Select(f => new { f.ForecastCalls, f.ForecastCredits, f.ForecastCostUsd, f.SuggestedTopUpCredits })
            .FirstOrDefaultAsync(ct);

        return new LearnerUsageSummary(
            From: from,
            To: to,
            TotalCalls: totalCalls,
            TotalTokens: totalTokens,
            TotalCostUsd: decimal.Round(totalCost, 4),
            FailedCalls: failedCalls,
            CreditsUsed: creditsUsed,
            WalletBalance: walletBalance,
            ByFeature: byFeature,
            Daily: daily,
            ForecastCalls30d: forecast?.ForecastCalls ?? 0,
            ForecastCredits30d: forecast?.ForecastCredits ?? 0,
            ForecastCostUsd30d: forecast?.ForecastCostUsd ?? 0m,
            SuggestedTopUpCredits: forecast?.SuggestedTopUpCredits ?? 0);
    }

    public async Task<AdminUsageSummary> GetAdminSummaryAsync(DateOnly from, DateOnly to, string? feature, string? provider, CancellationToken ct)
    {
        var fromTs = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toTs = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var q = _db.AiUsageRecords.Where(r => r.CreatedAt >= fromTs && r.CreatedAt <= toTs);
        if (!string.IsNullOrEmpty(feature)) q = q.Where(r => r.FeatureCode == feature);
        if (!string.IsNullOrEmpty(provider)) q = q.Where(r => r.ProviderId == provider);

        var totalCalls = await q.CountAsync(ct);
        var totalTokens = await q.SumAsync(r => (long?)(r.PromptTokens + r.CompletionTokens), ct) ?? 0L;
        var totalCost = await q.SumAsync(r => (decimal?)r.CostEstimateUsd, ct) ?? 0m;
        var uniqueUsers = await q.Where(r => r.UserId != null).Select(r => r.UserId).Distinct().CountAsync(ct);
        var successes = await q.CountAsync(r => r.Outcome == AiCallOutcome.Success, ct);
        var avgLatency = totalCalls > 0 ? (int)Math.Round(await q.AverageAsync(r => (double)r.LatencyMs, ct)) : 0;
        var successRate = totalCalls > 0 ? decimal.Round((decimal)successes / totalCalls * 100m, 2) : 0m;

        var byFeatureRaw = await q
            .GroupBy(r => r.FeatureCode)
            .Select(g => new
            {
                Feature = g.Key,
                Calls = g.Count(),
                Tokens = g.Sum(x => (long)(x.PromptTokens + x.CompletionTokens)),
                Cost = g.Sum(x => x.CostEstimateUsd),
            })
            .ToListAsync(ct);
        var byFeature = byFeatureRaw
            .Select(f => new FeatureBreakdown(f.Feature, f.Calls, f.Tokens, f.Cost))
            .OrderByDescending(f => f.Calls)
            .Take(20)
            .ToList();

        var byProviderRaw = await q
            .Where(r => r.ProviderId != null)
            .GroupBy(r => r.ProviderId!)
            .Select(g => new
            {
                Provider = g.Key,
                Calls = g.Count(),
                Tokens = g.Sum(x => (long)(x.PromptTokens + x.CompletionTokens)),
                Cost = g.Sum(x => x.CostEstimateUsd),
                Succ = g.Count(x => x.Outcome == AiCallOutcome.Success),
                Lat = g.Count() == 0 ? 0d : g.Average(x => (double)x.LatencyMs),
            })
            .OrderByDescending(p => p.Calls)
            .Take(20)
            .ToListAsync(ct);

        var byProvider = byProviderRaw.Select(p => new ProviderBreakdown(
            p.Provider, p.Calls, p.Tokens, p.Cost,
            p.Calls > 0 ? decimal.Round((decimal)p.Succ / p.Calls * 100m, 2) : 0m,
            (int)Math.Round(p.Lat))).ToList();

        var dailyRaw = await q
            .GroupBy(r => r.PeriodDayKey)
            .Select(g => new
            {
                Day = g.Key,
                Calls = g.Count(),
                Tokens = g.Sum(x => (long)(x.PromptTokens + x.CompletionTokens)),
                Cost = g.Sum(x => x.CostEstimateUsd),
            })
            .ToListAsync(ct);
        var daily = dailyRaw
            .Select(d => new DailyBucket(d.Day, d.Calls, d.Tokens, d.Cost))
            .OrderBy(b => b.Day)
            .ToList();

        var topUsersRaw = await q
            .Where(r => r.UserId != null)
            .GroupBy(r => r.UserId!)
            .Select(g => new
            {
                UserId = g.Key,
                Calls = g.Count(),
                Tokens = g.Sum(x => (long)(x.PromptTokens + x.CompletionTokens)),
                Cost = g.Sum(x => x.CostEstimateUsd),
            })
            .ToListAsync(ct);
        var topUsers = topUsersRaw
            .Select(u => new TopUserUsage(u.UserId, u.Calls, u.Tokens, u.Cost))
            .OrderByDescending(u => u.Calls)
            .Take(25)
            .ToList();

        return new AdminUsageSummary(
            From: from,
            To: to,
            TotalCalls: totalCalls,
            TotalTokens: totalTokens,
            TotalCostUsd: decimal.Round(totalCost, 4),
            UniqueUsers: uniqueUsers,
            SuccessRate: successRate,
            AvgLatencyMs: avgLatency,
            ByFeature: byFeature,
            ByProvider: byProvider,
            Daily: daily,
            TopUsers: topUsers);
    }
}
