using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Billing;

/// <summary>
/// EMA-based AI usage forecaster. Projects per-user call/credit/cost
/// consumption over the next N days using a 30-day exponentially-weighted
/// moving average of daily calls.
/// </summary>
public interface IUsageForecastService
{
    Task<UsageForecastSnapshot> ForecastUserAsync(string userId, int windowDays, CancellationToken ct);
    Task<int> RollupAllUsersAsync(CancellationToken ct);
}

public sealed class UsageForecastService : IUsageForecastService
{
    private readonly LearnerDbContext _db;
    private readonly ILogger<UsageForecastService> _logger;

    // EMA smoothing factor (higher = more weight to recent days).
    private const decimal EmaAlpha = 0.30m;

    public UsageForecastService(LearnerDbContext db, ILogger<UsageForecastService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<UsageForecastSnapshot> ForecastUserAsync(string userId, int windowDays, CancellationToken ct)
    {
        if (windowDays <= 0) windowDays = 30;

        var now = DateTimeOffset.UtcNow;
        var snapshotDate = DateOnly.FromDateTime(now.UtcDateTime);
        var thirtyDaysAgo = now.AddDays(-30);

        // Per-day call counts for the last 30 days. Materialize first; the
        // EF Core InMemory provider does not support GroupBy → typed-record
        // projection, and Postgres handles the same shape fine.
        var rawRecords = await _db.AiUsageRecords
            .Where(r => r.UserId == userId && r.CreatedAt >= thirtyDaysAgo)
            .Select(r => new
            {
                r.PeriodDayKey,
                r.FeatureCode,
                r.PromptTokens,
                r.CompletionTokens,
                r.CostEstimateUsd,
            })
            .ToListAsync(ct);

        var dailyBuckets = rawRecords
            .GroupBy(r => r.PeriodDayKey)
            .Select(g => new
            {
                Day = g.Key,
                Calls = g.Count(),
                CostUsd = g.Sum(x => x.CostEstimateUsd),
                Tokens = g.Sum(x => (long)(x.PromptTokens + x.CompletionTokens)),
            })
            .ToList();

        var perFeature = rawRecords
            .GroupBy(r => r.FeatureCode)
            .Select(g => new
            {
                Feature = g.Key,
                Calls = g.Count(),
                CostUsd = g.Sum(x => x.CostEstimateUsd),
            })
            .ToList();

        // Compute EMA of daily calls. If no usage, EMA = 0.
        decimal ema = 0m;
        if (dailyBuckets.Count > 0)
        {
            var sorted = dailyBuckets.OrderBy(b => b.Day).ToList();
            ema = sorted[0].Calls;
            foreach (var bucket in sorted.Skip(1))
            {
                ema = EmaAlpha * bucket.Calls + (1m - EmaAlpha) * ema;
            }
        }

        var forecastCalls = (int)Math.Round(ema * windowDays);

        // Cost per call averaged across window.
        decimal avgCostPerCall = 0m;
        var totalCallsWindow = dailyBuckets.Sum(b => b.Calls);
        var totalCostWindow = dailyBuckets.Sum(b => b.CostUsd);
        if (totalCallsWindow > 0)
        {
            avgCostPerCall = totalCostWindow / totalCallsWindow;
        }
        var forecastCost = decimal.Round(avgCostPerCall * forecastCalls, 4);

        // Forecast credit consumption: 1 call ≈ 1 credit by default unless the
        // FeatureCode is in the high-cost set (writing.grade / mock.full_grade
        // typically cost more). Heuristic uses the existing credit ledger if
        // present, else 1:1.
        var creditsConsumed30d = await _db.AiCreditLedger
            .Where(e => e.UserId == userId && e.CreatedAt >= thirtyDaysAgo && e.TokensDelta < 0)
            .SumAsync(e => (int?)e.TokensDelta, ct) ?? 0;
        var ratio = totalCallsWindow > 0 ? Math.Abs((decimal)creditsConsumed30d) / totalCallsWindow : 1m;
        var forecastCredits = (int)Math.Round(forecastCalls * Math.Max(0.1m, ratio));

        // Top-up suggestion: if predicted credits exceed current wallet balance
        // by ≥ 20%, recommend buying the gap (rounded up to nearest 5).
        var walletBalance = await _db.Wallets
            .Where(w => w.UserId == userId)
            .Select(w => (int?)w.CreditBalance)
            .FirstOrDefaultAsync(ct) ?? 0;
        int suggested = 0;
        if (forecastCredits > walletBalance * 1.2m)
        {
            suggested = (int)Math.Ceiling((forecastCredits - walletBalance) / 5.0) * 5;
        }

        var perFeatureDict = perFeature.ToDictionary(
            f => f.Feature,
            f => new { calls = f.Calls, costUsd = f.CostUsd });

        var existing = await _db.UsageForecastSnapshots
            .FirstOrDefaultAsync(s => s.UserId == userId && s.SnapshotDate == snapshotDate && s.FeatureCode == "*" && s.WindowDays == windowDays, ct);

        if (existing is null)
        {
            existing = new UsageForecastSnapshot
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                SnapshotDate = snapshotDate,
                FeatureCode = "*",
                WindowDays = windowDays,
                ForecastCalls = forecastCalls,
                ForecastCredits = forecastCredits,
                ForecastCostUsd = forecastCost,
                Ema30DailyCalls = decimal.Round(ema, 3),
                PerFeatureJson = JsonSerializer.Serialize(perFeatureDict),
                SuggestedTopUpCredits = suggested,
                ComputedAt = now,
            };
            _db.UsageForecastSnapshots.Add(existing);
        }
        else
        {
            existing.ForecastCalls = forecastCalls;
            existing.ForecastCredits = forecastCredits;
            existing.ForecastCostUsd = forecastCost;
            existing.Ema30DailyCalls = decimal.Round(ema, 3);
            existing.PerFeatureJson = JsonSerializer.Serialize(perFeatureDict);
            existing.SuggestedTopUpCredits = suggested;
            existing.ComputedAt = now;
        }

        await _db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<int> RollupAllUsersAsync(CancellationToken ct)
    {
        var thirtyDaysAgo = DateTimeOffset.UtcNow.AddDays(-30);
        var activeUserIds = await _db.AiUsageRecords
            .Where(r => r.CreatedAt >= thirtyDaysAgo && r.UserId != null)
            .Select(r => r.UserId!)
            .Distinct()
            .ToListAsync(ct);

        int scored = 0;
        foreach (var userId in activeUserIds)
        {
            try
            {
                await ForecastUserAsync(userId, 30, ct);
                scored++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UsageForecastService failed for user {UserId}", userId);
            }
        }
        return scored;
    }
}

/// <summary>Background worker — daily usage-forecast rollup.</summary>
public sealed class UsageForecastWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<UsageForecastWorker> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(12);

    public UsageForecastWorker(IServiceProvider services, ILogger<UsageForecastWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); } catch { }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IUsageForecastService>();
                var n = await svc.RollupAllUsersAsync(stoppingToken);
                _logger.LogInformation("Usage forecast rollup scored {N} users.", n);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UsageForecastWorker iteration failed.");
            }

            try { await Task.Delay(_interval, stoppingToken); } catch { }
        }
    }
}
