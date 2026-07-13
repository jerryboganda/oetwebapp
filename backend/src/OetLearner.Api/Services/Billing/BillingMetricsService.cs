using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Phase 10 — computes the daily rollup of billing health metrics and writes
/// them to BillingMetricDaily. Idempotent: re-running the same date overwrites.
/// </summary>
public interface IBillingMetricsService
{
    Task RollupAsync(DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<BillingMetricDaily>> ReadAsync(DateOnly from, DateOnly to, string? metricCode, string? region, CancellationToken ct);
}

public sealed class BillingMetricsService : IBillingMetricsService
{
    private readonly LearnerDbContext _db;
    private readonly ILogger<BillingMetricsService> _logger;

    public BillingMetricsService(LearnerDbContext db, ILogger<BillingMetricsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RollupAsync(DateOnly date, CancellationToken ct)
    {
        var dayStart = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);

        // Compute headline metrics. Currency-converted aggregation deferred to Phase 11.
        var activeSubs = await _db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active && s.StartedAt < dayEnd)
            .CountAsync(ct);

        var newSubs = await _db.Subscriptions
            .Where(s => s.StartedAt >= dayStart && s.StartedAt < dayEnd)
            .CountAsync(ct);

        var cancelledSubs = await _db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Cancelled && s.ChangedAt >= dayStart && s.ChangedAt < dayEnd)
            .CountAsync(ct);

        var pausedSubs = await _db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Paused)
            .CountAsync(ct);

        var mrr = await _db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active && s.Interval == "monthly")
            .SumAsync(s => (decimal?)s.PriceAmount, ct) ?? 0m;

        var arr = mrr * 12m;

        var grossRevenue = await _db.PaymentTransactions
            .Where(p => p.CreatedAt >= dayStart && p.CreatedAt < dayEnd && p.Status == "completed")
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        var refundedAmount = await _db.PaymentTransactions
            .Where(p => p.CreatedAt >= dayStart && p.CreatedAt < dayEnd && p.Status == "refunded")
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        var netRevenue = grossRevenue - refundedAmount;

        var churnRate = activeSubs == 0 ? 0m : decimal.Round((decimal)cancelledSubs / activeSubs * 100m, 4);
        var refundRate = grossRevenue == 0m ? 0m : decimal.Round(refundedAmount / grossRevenue * 100m, 4);
        var arpu = activeSubs == 0 ? 0m : decimal.Round(mrr / activeSubs, 4);

        var creditsSold = await _db.WalletTransactions
            .Where(t => t.CreatedAt >= dayStart && t.CreatedAt < dayEnd && t.TransactionType == "credit_purchase")
            .SumAsync(t => (int?)t.Amount, ct) ?? 0;

        var rolled = new (string MetricCode, decimal Value)[]
        {
            ("mrr", mrr),
            ("arr", arr),
            ("active_subscriptions", activeSubs),
            ("new_subscriptions", newSubs),
            ("cancelled_subscriptions", cancelledSubs),
            ("paused_subscriptions", pausedSubs),
            ("churn_rate", churnRate),
            ("refund_rate", refundRate),
            ("arpu", arpu),
            ("gross_revenue", grossRevenue),
            ("net_revenue", netRevenue),
            ("credits_sold", creditsSold),
        };

        var metricCodes = rolled.Select(metric => metric.MetricCode).ToList();
        var existingMetrics = await _db.BillingMetricDailies
            .Where(m =>
                m.MetricDate == date &&
                m.Region == "GLOBAL" &&
                metricCodes.Contains(m.MetricCode))
            .ToListAsync(ct);
        var existingByCode = existingMetrics
            .GroupBy(m => m.MetricCode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var (code, value) in rolled)
        {
            if (!existingByCode.TryGetValue(code, out var existing))
            {
                _db.BillingMetricDailies.Add(new BillingMetricDaily
                {
                    Id = Guid.NewGuid().ToString("N"),
                    MetricDate = date,
                    MetricCode = code,
                    Region = "GLOBAL",
                    Currency = "USD",
                    Value = value,
                    ComputedAt = DateTimeOffset.UtcNow,
                });
            }
            else
            {
                existing.Value = value;
                existing.ComputedAt = DateTimeOffset.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Billing metrics rolled up for {Date}.", date);
    }

    public async Task<IReadOnlyList<BillingMetricDaily>> ReadAsync(
        DateOnly from,
        DateOnly to,
        string? metricCode,
        string? region,
        CancellationToken ct)
    {
        return await _db.BillingMetricDailies
            .AsNoTracking()
            .Where(m => m.MetricDate >= from && m.MetricDate <= to
                && (metricCode == null || m.MetricCode == metricCode)
                && (region == null || m.Region == region))
            .OrderBy(m => m.MetricDate)
            .ToListAsync(ct);
    }
}

/// <summary>Background worker — runs the rollup daily.</summary>
public sealed class BillingMetricsRollupWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<BillingMetricsRollupWorker> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(6);

    public BillingMetricsRollupWorker(IServiceProvider services, ILogger<BillingMetricsRollupWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IBillingMetricsService>();
                var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
                await service.RollupAsync(today.AddDays(-1), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BillingMetricsRollupWorker iteration failed.");
            }
            await Task.Delay(_interval, stoppingToken);
        }
    }
}
