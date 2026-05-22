using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Reads <see cref="ChurnRiskSnapshot"/> rows with a non-null
/// <c>RecommendedAction</c> and <c>ActionDispatched=false</c>, then fires
/// the matching billing notification via <see cref="IBillingNotificationDispatcher"/>.
/// Idempotent — already-dispatched rows are skipped.
/// </summary>
public interface IRetentionActionDispatcher
{
    Task<int> DispatchPendingAsync(CancellationToken ct);
}

public sealed class RetentionActionDispatcher : IRetentionActionDispatcher
{
    private readonly LearnerDbContext _db;
    private readonly IBillingNotificationDispatcher _notifier;
    private readonly ILogger<RetentionActionDispatcher> _logger;

    public RetentionActionDispatcher(LearnerDbContext db, IBillingNotificationDispatcher notifier, ILogger<RetentionActionDispatcher> logger)
    {
        _db = db;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<int> DispatchPendingAsync(CancellationToken ct)
    {
        var latestDate = await _db.ChurnRiskSnapshots.MaxAsync(s => (DateOnly?)s.SnapshotDate, ct);
        if (latestDate is null) return 0;

        var pending = await _db.ChurnRiskSnapshots
            .Where(s => s.SnapshotDate == latestDate
                && s.RecommendedAction != null
                && !s.ActionDispatched
                && (s.RiskBand == "medium" || s.RiskBand == "high"))
            .Take(500)
            .ToListAsync(ct);

        int dispatched = 0;
        foreach (var snapshot in pending)
        {
            try
            {
                var (eventCode, couponCode) = MapAction(snapshot.RecommendedAction!);
                var vars = new Dictionary<string, string>
                {
                    ["riskBand"] = snapshot.RiskBand,
                    ["riskScore"] = (snapshot.RiskScore * 100m).ToString("F0"),
                };
                if (couponCode is not null) vars["couponCode"] = couponCode;

                await _notifier.DispatchAsync(new BillingNotificationEvent(
                    EventCode: eventCode,
                    EventId: $"churn_{snapshot.Id}",
                    UserId: snapshot.UserId,
                    Variables: vars), ct);

                snapshot.ActionDispatched = true;
                dispatched++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Retention dispatch failed for snapshot {Id}", snapshot.Id);
            }
        }
        await _db.SaveChangesAsync(ct);
        return dispatched;
    }

    private static (string EventCode, string? CouponCode) MapAction(string action) => action switch
    {
        "send_winback_coupon" => ("retention_winback_coupon", "WINBACK25"),
        "trial_extension_offer" => ("retention_trial_extension", "TRIALPLUS7"),
        "expedite_dunning_winback" => ("retention_dunning_winback", "COMEBACK25"),
        "schedule_check_in_email" => ("retention_check_in", null),
        _ => ("retention_generic", null),
    };
}

public sealed class RetentionDispatchWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<RetentionDispatchWorker> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(6);

    public RetentionDispatchWorker(IServiceProvider services, ILogger<RetentionDispatchWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch { }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IRetentionActionDispatcher>();
                var n = await dispatcher.DispatchPendingAsync(stoppingToken);
                _logger.LogInformation("Retention dispatcher fired {N} actions.", n);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RetentionDispatchWorker iteration failed.");
            }

            try { await Task.Delay(_interval, stoppingToken); } catch { }
        }
    }
}
