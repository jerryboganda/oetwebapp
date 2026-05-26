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

public sealed class DunningCampaignService : IDunningCampaignService, IDunningService
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

    /// <summary>
    /// Wave A5 smart-retry cadence: attempt 1 at T+24h, attempt 2 at T+72h,
    /// attempt 3 at T+168h (7 days). After attempt 3 the dunning ladder
    /// gives up and cancels the subscription with
    /// <c>reason="dunning_exhausted"</c>.
    /// </summary>
    public static readonly TimeSpan[] SmartRetryDelays =
    {
        TimeSpan.FromHours(24),
        TimeSpan.FromHours(72),
        TimeSpan.FromHours(168),
    };

    public const int SmartRetryMaxAttempts = 3;

    private readonly LearnerDbContext _db;
    private readonly IStripeService? _stripe;
    private readonly ISubscriptionService? _subscriptions;
    private readonly IBillingNotificationDispatcher? _dispatcher;
    private readonly TimeProvider _clock;
    private readonly ILogger<DunningCampaignService>? _logger;

    public DunningCampaignService(LearnerDbContext db)
        : this(db, stripe: null, subscriptions: null, dispatcher: null, clock: null, logger: null)
    {
    }

    public DunningCampaignService(
        LearnerDbContext db,
        IStripeService? stripe,
        ISubscriptionService? subscriptions,
        IBillingNotificationDispatcher? dispatcher,
        TimeProvider? clock,
        ILogger<DunningCampaignService>? logger)
    {
        _db = db;
        _stripe = stripe;
        _subscriptions = subscriptions;
        _dispatcher = dispatcher;
        _clock = clock ?? TimeProvider.System;
        _logger = logger;
    }

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

    // ── IDunningService ──────────────────────────────────────────────────────────────

    public async Task OnInvoicePaymentFailedAsync(
        string stripeSubscriptionId, string userId, CancellationToken ct = default)
    {
        var sub = await _db.CustomerSubscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscriptionId, ct);

        if (sub is not null)
        {
            sub.Status = "past_due";
            sub.UpdatedAt = DateTimeOffset.UtcNow;
        }

        _db.BillingEvents.Add(new OetLearner.Api.Domain.BillingEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            SubscriptionId = stripeSubscriptionId,
            EventType = "invoice.payment_failed",
            EntityType = "subscription",
            EntityId = stripeSubscriptionId,
            OccurredAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task OnInvoicePaymentSucceededAsync(
        string stripeSubscriptionId, string userId, CancellationToken ct = default)
    {
        var sub = await _db.CustomerSubscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscriptionId, ct);

        if (sub is not null)
        {
            sub.Status = "active";
            sub.UpdatedAt = DateTimeOffset.UtcNow;
        }

        _db.BillingEvents.Add(new OetLearner.Api.Domain.BillingEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            SubscriptionId = stripeSubscriptionId,
            EventType = "invoice.payment_succeeded",
            EntityType = "subscription",
            EntityId = stripeSubscriptionId,
            OccurredAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task OnSubscriptionCanceledAsync(
        string stripeSubscriptionId, string userId, CancellationToken ct = default)
    {
        var sub = await _db.CustomerSubscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscriptionId, ct);

        if (sub is not null)
        {
            sub.Status = "canceled";
            sub.CanceledAt = DateTimeOffset.UtcNow;
            sub.UpdatedAt = DateTimeOffset.UtcNow;
        }

        _db.BillingEvents.Add(new OetLearner.Api.Domain.BillingEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            SubscriptionId = stripeSubscriptionId,
            EventType = "customer.subscription.deleted",
            EntityType = "subscription",
            EntityId = stripeSubscriptionId,
            OccurredAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);
    }

    // ── Wave A5: smart-retry ladder (T+24h / T+72h / T+168h) ─────────────

    public async Task ScheduleInvoiceRetryAsync(
        string stripeSubscriptionId,
        string stripeInvoiceId,
        string userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stripeInvoiceId))
            throw new ArgumentException("invoice id required", nameof(stripeInvoiceId));

        // Count attempts already on the ladder for this invoice — idempotent
        // on (invoiceId, attemptNumber). A duplicate webhook from Stripe must
        // not duplicate rows.
        var nextAttempt = await _db.DunningAttempts
            .Where(a => a.InvoiceId == stripeInvoiceId)
            .OrderByDescending(a => a.AttemptNumber)
            .Select(a => (int?)a.AttemptNumber)
            .FirstOrDefaultAsync(ct) ?? 0;
        nextAttempt += 1;

        if (nextAttempt > SmartRetryMaxAttempts)
        {
            _logger?.LogInformation(
                "Dunning ladder for invoice {InvoiceId} already exhausted (next attempt={Next}); ignoring.",
                stripeInvoiceId, nextAttempt);
            return;
        }

        var now = _clock.GetUtcNow();
        var delay = SmartRetryDelays[nextAttempt - 1];
        var attempt = new DunningAttempt
        {
            Id = Guid.NewGuid().ToString("N"),
            SubscriptionId = stripeSubscriptionId,
            InvoiceId = stripeInvoiceId,
            UserId = userId,
            AttemptNumber = nextAttempt,
            ScheduledAt = now + delay,
            Outcome = DunningAttemptOutcome.Pending,
            CreatedAt = now,
        };
        _db.DunningAttempts.Add(attempt);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<DunningRetryExecutionResult> ExecutePendingRetryAsync(
        string attemptId, CancellationToken ct = default)
    {
        var attempt = await _db.DunningAttempts.FirstOrDefaultAsync(a => a.Id == attemptId, ct)
            ?? throw new InvalidOperationException($"DunningAttempt {attemptId} not found.");

        if (attempt.Outcome != DunningAttemptOutcome.Pending)
        {
            return new DunningRetryExecutionResult(
                attempt.Id,
                attempt.AttemptNumber,
                Succeeded: attempt.Outcome == DunningAttemptOutcome.Succeeded,
                FinalAttemptExhausted: false,
                attempt.StripeFailureCode,
                attempt.FailureReason);
        }

        var now = _clock.GetUtcNow();
        attempt.ExecutedAt = now;

        // Stripe is optional only to keep unit tests light; in production the
        // DI container always provides the concrete StripeService.
        PayInvoiceResult? payResult = null;
        if (_stripe is not null)
        {
            try
            {
                payResult = await _stripe.PayInvoiceAsync(attempt.InvoiceId, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Stripe PayInvoiceAsync threw for invoice {InvoiceId}", attempt.InvoiceId);
                payResult = new PayInvoiceResult(false, "stripe_exception", "exception", ex.Message);
            }
        }
        else
        {
            payResult = new PayInvoiceResult(true, "paid", null, null);
        }

        if (payResult.Succeeded)
        {
            attempt.Outcome = DunningAttemptOutcome.Succeeded;
            attempt.StripeFailureCode = null;
            attempt.FailureReason = null;
            await _db.SaveChangesAsync(ct);
            return new DunningRetryExecutionResult(
                attempt.Id,
                attempt.AttemptNumber,
                Succeeded: true,
                FinalAttemptExhausted: false,
                FailureCode: null,
                FailureReason: null);
        }

        // Card declined — mark this attempt failed.
        attempt.Outcome = DunningAttemptOutcome.Failed;
        attempt.StripeFailureCode = Truncate(payResult.FailureCode, 64);
        attempt.FailureReason = Truncate(payResult.FailureReason, 512);

        // Email the learner about this attempt outcome.
        if (_dispatcher is not null)
        {
            var updateCardUrl = "/settings/billing"; // portal/update-card link is built by frontend
            try
            {
                await _dispatcher.SendDunningAttemptAsync(
                    attemptNumber: attempt.AttemptNumber,
                    userId: attempt.UserId,
                    invoiceId: attempt.InvoiceId,
                    failureReason: attempt.FailureReason ?? "card_declined",
                    updateCardUrl: updateCardUrl,
                    ct);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Dunning attempt {AttemptNumber} notification failed for invoice {InvoiceId}",
                    attempt.AttemptNumber, attempt.InvoiceId);
            }
        }

        var isFinal = attempt.AttemptNumber >= SmartRetryMaxAttempts;
        if (!isFinal)
        {
            // Schedule the next attempt automatically — the scheduler picks
            // it up when ScheduledAt passes.
            var nextNumber = attempt.AttemptNumber + 1;
            var nextDelay = SmartRetryDelays[nextNumber - 1] - SmartRetryDelays[attempt.AttemptNumber - 1];
            _db.DunningAttempts.Add(new DunningAttempt
            {
                Id = Guid.NewGuid().ToString("N"),
                SubscriptionId = attempt.SubscriptionId,
                InvoiceId = attempt.InvoiceId,
                UserId = attempt.UserId,
                AttemptNumber = nextNumber,
                ScheduledAt = now + nextDelay,
                Outcome = DunningAttemptOutcome.Pending,
                CreatedAt = now,
            });
        }
        else if (_subscriptions is not null)
        {
            // Final exhaustion — cancel the subscription immediately (not at
            // period end) and notify the learner that their access is lost.
            try
            {
                await _subscriptions.CancelAsync(
                    userId: attempt.UserId,
                    cancelAtPeriodEnd: false,
                    reason: "dunning_exhausted",
                    ct);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Subscription cancel after dunning exhaustion failed (user={UserId})", attempt.UserId);
            }

            if (_dispatcher is not null)
            {
                try
                {
                    await _dispatcher.SendSubscriptionLostAsync(attempt.UserId, attempt.SubscriptionId, ct);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Subscription lost notification failed (user={UserId})", attempt.UserId);
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        return new DunningRetryExecutionResult(
            attempt.Id,
            attempt.AttemptNumber,
            Succeeded: false,
            FinalAttemptExhausted: isFinal,
            FailureCode: attempt.StripeFailureCode,
            FailureReason: attempt.FailureReason);
    }

    private static string? Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
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
