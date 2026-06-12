using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

public sealed class SubscriptionExpiryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SubscriptionExpiryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SweepAsync(stoppingToken);

        using var timer = new PeriodicTimer(SweepInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SweepAsync(stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var now = DateTimeOffset.UtcNow;
            List<Subscription> expired;
            try
            {
                expired = await db.Subscriptions
                    .Where(subscription =>
                        (subscription.Status == SubscriptionStatus.Active
                            || subscription.Status == SubscriptionStatus.Trial
                            || subscription.Status == SubscriptionStatus.FreezeRequested)
                        && subscription.ExpiresAt != null
                        && subscription.ExpiresAt < now)
                    .ToListAsync(ct);
            }
            catch (InvalidOperationException) when (db.Database.IsSqlite())
            {
                // The SQLite provider (bundled desktop backend) cannot translate
                // DateTimeOffset comparisons — same limitation handled by the
                // /health/ready stuck-jobs probe. Narrow server-side on the
                // translatable predicates, compare the timestamp in memory.
                var candidates = await db.Subscriptions
                    .Where(subscription =>
                        (subscription.Status == SubscriptionStatus.Active
                            || subscription.Status == SubscriptionStatus.Trial
                            || subscription.Status == SubscriptionStatus.FreezeRequested)
                        && subscription.ExpiresAt != null)
                    .ToListAsync(ct);
                expired = candidates.Where(subscription => subscription.ExpiresAt < now).ToList();
            }

            foreach (var subscription in expired)
            {
                SubscriptionStateMachine.Transition(subscription, SubscriptionStatus.Expired, "subscription_expiry_sweep");
                subscription.ChangedAt = now;
            }

            if (expired.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Expired {Count} subscriptions in expiry sweep.", expired.Count);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Subscription expiry sweep failed.");
        }
    }
}
