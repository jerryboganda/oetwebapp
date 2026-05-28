using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Heuristic-AI churn-risk scorer. Combines billing + engagement + AI-usage
/// signals into a 0.0–1.0 risk score per user. Re-runs daily via
/// <see cref="ChurnPredictionWorker"/>.
///
/// Feature weights are configured at the top of the class and tuned offline
/// using historical cancellations. The function is intentionally interpretable
/// (no opaque model artifact) so admins can answer "why is this user flagged?"
/// from the FactorsJson breakdown.
/// </summary>
public interface IChurnPredictionService
{
    Task<ChurnRiskSnapshot> ScoreUserAsync(string userId, CancellationToken ct);
    Task<int> RollupAllUsersAsync(CancellationToken ct);
}

public sealed class ChurnPredictionService : IChurnPredictionService
{
    private readonly LearnerDbContext _db;
    private readonly ILogger<ChurnPredictionService> _logger;

    // ── Feature weights (sum need not = 1; final score is sigmoid-normalised) ──
    private const decimal WeightInactivityDays = 0.020m;       // per day since last login
    private const decimal WeightFailedPayments = 0.150m;       // per failed payment in last 30d
    private const decimal WeightCreditUnused = 0.0001m;        // per credit unused vs included
    private const decimal WeightDunningActive = 0.350m;        // active dunning campaign
    private const decimal WeightCancelIntent = 0.300m;         // any prior cancellation intent
    private const decimal WeightTrialEndingSoon = 0.250m;      // trial ends ≤ 3 days
    private const decimal WeightShortTenure = 0.080m;          // tenure < 14d
    private const decimal WeightAiUsageDeclining = 0.180m;     // AI calls last week / prior week < 0.5
    private const decimal WeightSupportTickets = 0.040m;       // per open ticket
    private const decimal WeightPlanDowngraded = 0.120m;       // any downgrade in last 30d
    private const decimal WeightRefundRequested = 0.220m;      // any refund in last 60d

    public ChurnPredictionService(LearnerDbContext db, ILogger<ChurnPredictionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ChurnRiskSnapshot> ScoreUserAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var snapshotDate = DateOnly.FromDateTime(now.UtcDateTime);

        var account = await _db.ApplicationUserAccounts.FirstOrDefaultAsync(a => a.Id == userId, ct)
            ?? throw new InvalidOperationException($"User not found: {userId}");

        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        var dunning = await _db.DunningCampaigns
            .Where(d => d.UserId == userId && d.Status == "active")
            .CountAsync(ct);
        var intents = await _db.CancellationIntents
            .Where(i => i.UserId == userId)
            .CountAsync(ct);

        var thirtyDaysAgo = now.AddDays(-30);
        var sixtyDaysAgo = now.AddDays(-60);
        var sevenDaysAgo = now.AddDays(-7);
        var fourteenDaysAgo = now.AddDays(-14);

        var failedPayments = await _db.PaymentTransactions
            .Where(p => p.LearnerUserId == userId && p.Status == "failed" && p.CreatedAt >= thirtyDaysAgo)
            .CountAsync(ct);

        var refundCount = await _db.PaymentTransactions
            .Where(p => p.LearnerUserId == userId && p.Status == "refunded" && p.CreatedAt >= sixtyDaysAgo)
            .CountAsync(ct);

        // AI usage trend: last 7d calls vs prior 7d.
        var aiCallsRecent = await _db.AiUsageRecords
            .Where(r => r.UserId == userId && r.CreatedAt >= sevenDaysAgo)
            .CountAsync(ct);
        var aiCallsPrior = await _db.AiUsageRecords
            .Where(r => r.UserId == userId && r.CreatedAt >= fourteenDaysAgo && r.CreatedAt < sevenDaysAgo)
            .CountAsync(ct);

        var lastLogin = account.LastLoginAt ?? account.CreatedAt;
        var inactivityDays = Math.Max(0, (decimal)(now - lastLogin).TotalDays);
        var tenureDays = Math.Max(0, (decimal)(now - account.CreatedAt).TotalDays);

        var factors = new Dictionary<string, decimal>();
        decimal score = 0m;

        if (inactivityDays > 7)
        {
            var contribution = Math.Min(0.30m, WeightInactivityDays * (inactivityDays - 7));
            score += contribution;
            factors["inactivity_days"] = contribution;
        }
        if (failedPayments > 0)
        {
            var c = Math.Min(0.45m, WeightFailedPayments * failedPayments);
            score += c;
            factors["failed_payments_30d"] = c;
        }
        if (dunning > 0)
        {
            score += WeightDunningActive;
            factors["dunning_active"] = WeightDunningActive;
        }
        if (intents > 0)
        {
            var c = Math.Min(0.45m, WeightCancelIntent * intents);
            score += c;
            factors["cancel_intents"] = c;
        }
        if (subscription is { Status: SubscriptionStatus.Trial })
        {
            var daysToTrialEnd = (decimal)(subscription.NextRenewalAt - now).TotalDays;
            if (daysToTrialEnd <= 3)
            {
                score += WeightTrialEndingSoon;
                factors["trial_ending_soon"] = WeightTrialEndingSoon;
            }
        }
        if (tenureDays < 14)
        {
            score += WeightShortTenure;
            factors["short_tenure"] = WeightShortTenure;
        }
        if (aiCallsPrior >= 5 && aiCallsRecent < aiCallsPrior * 0.5m)
        {
            score += WeightAiUsageDeclining;
            factors["ai_usage_declining"] = WeightAiUsageDeclining;
        }
        if (refundCount > 0)
        {
            score += WeightRefundRequested;
            factors["refund_60d"] = WeightRefundRequested;
        }

        // Sigmoid-style cap to [0,1].
        var normalized = score / (1m + score);

        var band = normalized switch
        {
            < 0.25m => "low",
            < 0.60m => "medium",
            _ => "high",
        };

        var recommendedAction = band switch
        {
            "high" when dunning > 0 => "expedite_dunning_winback",
            "high" when subscription?.Status == SubscriptionStatus.Trial => "trial_extension_offer",
            "high" => "send_winback_coupon",
            "medium" => "schedule_check_in_email",
            _ => null,
        };

        // Upsert snapshot for today.
        var existing = await _db.ChurnRiskSnapshots
            .FirstOrDefaultAsync(s => s.UserId == userId && s.SnapshotDate == snapshotDate, ct);

        if (existing is null)
        {
            existing = new ChurnRiskSnapshot
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                SnapshotDate = snapshotDate,
                RiskScore = decimal.Round(normalized, 4),
                RiskBand = band,
                FactorsJson = JsonSerializer.Serialize(factors),
                RecommendedAction = recommendedAction,
                ActionDispatched = false,
                ComputedAt = now,
            };
            _db.ChurnRiskSnapshots.Add(existing);
        }
        else
        {
            existing.RiskScore = decimal.Round(normalized, 4);
            existing.RiskBand = band;
            existing.FactorsJson = JsonSerializer.Serialize(factors);
            existing.RecommendedAction = recommendedAction;
            existing.ComputedAt = now;
        }

        await _db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<int> RollupAllUsersAsync(CancellationToken ct)
    {
        // Score active learners only (excludes cancelled / expired older than 90d).
        var cutoff = DateTimeOffset.UtcNow.AddDays(-90);
        var activeUserIds = await _db.Subscriptions
            .Where(s => s.Status != SubscriptionStatus.Expired || s.ChangedAt > cutoff)
            .Select(s => s.UserId)
            .Distinct()
            .ToListAsync(ct);

        // Subscriptions can outlive their user account (hard-deleted learners,
        // mock/seed ids). Drop ids with no live account so a stale row cannot
        // abort its own scoring with "User not found".
        var existingUserIds = await _db.ApplicationUserAccounts
            .Where(a => activeUserIds.Contains(a.Id))
            .Select(a => a.Id)
            .ToHashSetAsync(ct);

        int scored = 0;
        int skipped = 0;
        foreach (var userId in activeUserIds)
        {
            if (!existingUserIds.Contains(userId))
            {
                skipped++;
                continue;
            }

            try
            {
                await ScoreUserAsync(userId, ct);
                scored++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ChurnPredictionService failed for user {UserId}", userId);
            }
        }

        if (skipped > 0)
        {
            _logger.LogDebug(
                "Churn rollup skipped {Skipped} subscription(s) with no matching user account.", skipped);
        }

        return scored;
    }
}

/// <summary>Background worker — daily churn rollup.</summary>
public sealed class ChurnPredictionWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ChurnPredictionWorker> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(12);

    public ChurnPredictionWorker(IServiceProvider services, ILogger<ChurnPredictionWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stagger start by 30s so it doesn't compete with metrics rollup.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch { }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IChurnPredictionService>();
                var n = await svc.RollupAllUsersAsync(stoppingToken);
                _logger.LogInformation("Churn rollup scored {N} users.", n);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChurnPredictionWorker iteration failed.");
            }

            try { await Task.Delay(_interval, stoppingToken); } catch { }
        }
    }
}
