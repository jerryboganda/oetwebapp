using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Billing;

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

        var q = _db.AiUsageRecords
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.CreatedAt >= fromTs && r.CreatedAt <= toTs);

        var aggregate = await q
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalCalls = g.Count(),
                TotalTokens = g.Sum(r => (long)(r.PromptTokens + r.CompletionTokens)),
                TotalCost = g.Sum(r => r.CostEstimateUsd),
                FailedCalls = g.Count(r => r.Outcome != AiCallOutcome.Success),
            })
            .FirstOrDefaultAsync(ct);

        var byFeatureRaw = await q
            .GroupBy(r => r.FeatureCode)
            .Select(g => new
            {
                Feature = g.Key,
                Calls = g.Count(),
                Tokens = g.Sum(x => (long)(x.PromptTokens + x.CompletionTokens)),
                Cost = g.Sum(x => x.CostEstimateUsd),
            })
            .OrderByDescending(f => f.Calls)
            .ToListAsync(ct);
        var byFeature = byFeatureRaw
            .Select(f => new FeatureBreakdown(f.Feature, f.Calls, f.Tokens, f.Cost))
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
            .OrderBy(d => d.Day)
            .ToListAsync(ct);
        var daily = dailyRaw
            .Select(d => new DailyBucket(d.Day, d.Calls, d.Tokens, d.Cost))
            .ToList();

        var creditsUsed = await _db.AiCreditLedger
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.CreatedAt >= fromTs && e.CreatedAt <= toTs && e.TokensDelta < 0)
            .SumAsync(e => (int?)e.TokensDelta, ct) ?? 0;
        creditsUsed = Math.Abs(creditsUsed);

        var walletBalance = await _db.Wallets
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .Select(w => (int?)w.CreditBalance)
            .FirstOrDefaultAsync(ct) ?? 0;

        var forecast = await _db.UsageForecastSnapshots
            .AsNoTracking()
            .Where(f => f.UserId == userId && f.FeatureCode == "*")
            .OrderByDescending(f => f.SnapshotDate)
            .Select(f => new { f.ForecastCalls, f.ForecastCredits, f.ForecastCostUsd, f.SuggestedTopUpCredits })
            .FirstOrDefaultAsync(ct);

        return new LearnerUsageSummary(
            From: from,
            To: to,
            TotalCalls: aggregate?.TotalCalls ?? 0,
            TotalTokens: aggregate?.TotalTokens ?? 0L,
            TotalCostUsd: decimal.Round(aggregate?.TotalCost ?? 0m, 4),
            FailedCalls: aggregate?.FailedCalls ?? 0,
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

        var q = _db.AiUsageRecords
            .AsNoTracking()
            .Where(r => r.CreatedAt >= fromTs && r.CreatedAt <= toTs);
        if (!string.IsNullOrEmpty(feature)) q = q.Where(r => r.FeatureCode == feature);
        if (!string.IsNullOrEmpty(provider)) q = q.Where(r => r.ProviderId == provider);

        var aggregate = await q
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalCalls = g.Count(),
                TotalTokens = g.Sum(r => (long)(r.PromptTokens + r.CompletionTokens)),
                TotalCost = g.Sum(r => r.CostEstimateUsd),
                UniqueUsers = g
                    .Where(r => r.UserId != null)
                    .Select(r => r.UserId)
                    .Distinct()
                    .Count(),
                Successes = g.Count(r => r.Outcome == AiCallOutcome.Success),
                AverageLatency = g.Average(r => (double)r.LatencyMs),
            })
            .FirstOrDefaultAsync(ct);

        var totalCalls = aggregate?.TotalCalls ?? 0;
        var successes = aggregate?.Successes ?? 0;
        var avgLatency = aggregate is not null ? (int)Math.Round(aggregate.AverageLatency) : 0;
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
            .OrderByDescending(f => f.Calls)
            .Take(20)
            .ToListAsync(ct);
        var byFeature = byFeatureRaw
            .Select(f => new FeatureBreakdown(f.Feature, f.Calls, f.Tokens, f.Cost))
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
            .OrderBy(d => d.Day)
            .ToListAsync(ct);
        var daily = dailyRaw
            .Select(d => new DailyBucket(d.Day, d.Calls, d.Tokens, d.Cost))
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
            .OrderByDescending(u => u.Calls)
            .Take(25)
            .ToListAsync(ct);
        var topUsers = topUsersRaw
            .Select(u => new TopUserUsage(u.UserId, u.Calls, u.Tokens, u.Cost))
            .ToList();

        return new AdminUsageSummary(
            From: from,
            To: to,
            TotalCalls: totalCalls,
            TotalTokens: aggregate?.TotalTokens ?? 0L,
            TotalCostUsd: decimal.Round(aggregate?.TotalCost ?? 0m, 4),
            UniqueUsers: aggregate?.UniqueUsers ?? 0,
            SuccessRate: successRate,
            AvgLatencyMs: avgLatency,
            ByFeature: byFeature,
            ByProvider: byProvider,
            Daily: daily,
            TopUsers: topUsers);
    }
}
