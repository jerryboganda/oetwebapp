using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Phase 5 dunning state machine. Campaigns advance through a fixed Day 0→21
/// schedule of retry attempts + notifications + access changes.
/// </summary>
public interface IDunningCampaignService
{
    Task<DunningCampaign> StartAsync(string subscriptionId, string userId, string? failureCode, string? failureReason, CancellationToken ct);
    Task AdvanceAsync(DunningCampaign campaign, CancellationToken ct);
    Task RecoverAsync(string campaignId, CancellationToken ct);
}

public sealed class DunningCampaignService : IDunningCampaignService
{
    /// <summary>Days from campaign start when each step fires.</summary>
    public static readonly (int Day, string Code)[] Schedule = new[]
    {
        (0, "day0_email"),
        (1, "day1_retry"),
        (3, "day3_retry_email"),
        (5, "day5_sms_banner"),
        (7, "day7_retry_whatsapp"),
        (10, "day10_pause"),
        (14, "day14_winback_coupon"),
        (21, "day21_cancel"),
    };

    private readonly LearnerDbContext _db;

    public DunningCampaignService(LearnerDbContext db) => _db = db;

    public async Task<DunningCampaign> StartAsync(string subscriptionId, string userId, string? failureCode, string? failureReason, CancellationToken ct)
    {
        var existing = await _db.DunningCampaigns
            .FirstOrDefaultAsync(c => c.SubscriptionId == subscriptionId && c.Status == "active", ct);

        if (existing is not null)
        {
            existing.LastFailureCode = failureCode;
            existing.LastFailureReason = failureReason;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var campaign = new DunningCampaign
        {
            Id = Guid.NewGuid().ToString("N"),
            SubscriptionId = subscriptionId,
            UserId = userId,
            Status = "active",
            StartedAt = now,
            NextAttemptAt = now,
            AttemptCount = 0,
            LastFailureCode = failureCode,
            LastFailureReason = failureReason,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.DunningCampaigns.Add(campaign);
        await _db.SaveChangesAsync(ct);
        return campaign;
    }

    public async Task AdvanceAsync(DunningCampaign campaign, CancellationToken ct)
    {
        var elapsedDays = (int)Math.Floor((DateTimeOffset.UtcNow - campaign.StartedAt).TotalDays);
        var completed = new HashSet<string>(campaign.StepsCompletedCsv.Split(',', StringSplitOptions.RemoveEmptyEntries));

        foreach (var (day, code) in Schedule)
        {
            if (elapsedDays >= day && completed.Add(code))
            {
                campaign.AttemptCount += code.Contains("retry") ? 1 : 0;

                // Day 5 starts grace banner — extend GracePeriodUntil by 7 days on the subscription.
                if (code == "day5_sms_banner")
                {
                    var sub = await _db.Subscriptions.FirstOrDefaultAsync(s => s.Id == campaign.SubscriptionId, ct);
                    if (sub is not null)
                    {
                        sub.GracePeriodUntil = DateTimeOffset.UtcNow.AddDays(7);
                    }
                }
            }
        }

        campaign.StepsCompletedCsv = string.Join(',', completed);
        campaign.UpdatedAt = DateTimeOffset.UtcNow;

        // Determine next attempt timing — the soonest pending step in the schedule.
        var nextStep = Schedule.FirstOrDefault(s => !completed.Contains(s.Code));
        campaign.NextAttemptAt = campaign.StartedAt.AddDays(nextStep == default ? 21 : nextStep.Day);

        // Terminal transitions.
        if (completed.Contains("day21_cancel"))
        {
            campaign.Status = "cancelled";
            campaign.CancelledAt = DateTimeOffset.UtcNow;
        }
        else if (completed.Contains("day10_pause"))
        {
            campaign.Status = "paused";
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task RecoverAsync(string campaignId, CancellationToken ct)
    {
        var campaign = await _db.DunningCampaigns.FirstOrDefaultAsync(c => c.Id == campaignId, ct)
            ?? throw new InvalidOperationException("Dunning campaign not found.");
        campaign.Status = "recovered";
        campaign.RecoveredAt = DateTimeOffset.UtcNow;
        campaign.UpdatedAt = campaign.RecoveredAt.Value;
        await _db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Background worker that polls due dunning campaigns and advances them.
/// </summary>
public sealed class DunningWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DunningWorker> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromMinutes(15);

    public DunningWorker(IServiceProvider services, ILogger<DunningWorker> logger)
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
                var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
                var service = scope.ServiceProvider.GetRequiredService<IDunningCampaignService>();
                var gatewayProvider = scope.ServiceProvider.GetRequiredService<IPaymentGatewayProvider>();
                var notifier = scope.ServiceProvider.GetService<IBillingNotificationDispatcher>();
                var now = DateTimeOffset.UtcNow;
                var due = await db.DunningCampaigns
                    .Where(c => c.Status == "active" && c.NextAttemptAt <= now)
                    .ToListAsync(stoppingToken);
                foreach (var c in due)
                {
                    await service.AdvanceAsync(c, stoppingToken);

                    // Day-N retry steps trigger an actual gateway charge attempt.
                    var lastCompleted = c.StepsCompletedCsv
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .LastOrDefault() ?? string.Empty;
                    if (lastCompleted.Contains("retry"))
                    {
                        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == c.SubscriptionId, stoppingToken);
                        var lastTxn = await db.PaymentTransactions
                            .Where(t => t.LearnerUserId == c.UserId && t.Status == "completed")
                            .OrderByDescending(t => t.CreatedAt)
                            .FirstOrDefaultAsync(stoppingToken);
                        if (sub is not null && lastTxn is not null)
                        {
                            try
                            {
                                var gateway = gatewayProvider.GetGateway(lastTxn.Gateway);
                                var result = await gateway.CreatePaymentIntentAsync(new CreatePaymentIntentRequest(
                                    UserId: c.UserId,
                                    Amount: sub.PriceAmount,
                                    Currency: sub.Currency,
                                    ProductType: "subscription_payment",
                                    ProductId: sub.Id,
                                    Description: $"Dunning retry #{c.AttemptCount}",
                                    Metadata: new Dictionary<string, string> { ["dunning_campaign_id"] = c.Id },
                                    IdempotencyKey: $"dunning_{c.Id}_{lastCompleted}"), stoppingToken);

                                if (string.Equals(result.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
                                {
                                    await service.RecoverAsync(c.Id, stoppingToken);
                                    SubscriptionStateMachine.Transition(sub, SubscriptionStatus.Active, "dunning_recovered");
                                    await db.SaveChangesAsync(stoppingToken);
                                }
                            }
                            catch (Exception retryEx)
                            {
                                _logger.LogWarning(retryEx, "Dunning retry failed for subscription {SubId} step {Step}.", c.SubscriptionId, lastCompleted);
                            }
                        }
                    }

                    // Fire notification for every newly-completed step.
                    if (notifier is not null)
                    {
                        var lastStep = c.StepsCompletedCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                        if (!string.IsNullOrEmpty(lastStep))
                        {
                            try
                            {
                                await notifier.DispatchAsync(new BillingNotificationEvent(
                                    EventCode: $"dunning_{lastStep}",
                                    EventId: $"{c.Id}_{lastStep}",
                                    UserId: c.UserId,
                                    Variables: new Dictionary<string, string>
                                    {
                                        ["attemptCount"] = c.AttemptCount.ToString(),
                                        ["failureReason"] = c.LastFailureReason ?? string.Empty,
                                    }), stoppingToken);
                            }
                            catch (Exception notifyEx)
                            {
                                _logger.LogWarning(notifyEx, "Dunning notification dispatch failed.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DunningWorker iteration failed.");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }
    }
}
