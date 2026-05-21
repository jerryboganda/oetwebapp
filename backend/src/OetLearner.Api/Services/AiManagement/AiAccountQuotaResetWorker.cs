using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services.AiManagement;

/// <summary>
/// Resets <see cref="OetLearner.Api.Domain.AiProviderAccount.RequestsUsedThisMonth"/>
/// for any account whose <c>PeriodMonthKey</c> is older than the current
/// UTC month. Runs hourly; idempotent — once an account's PeriodMonthKey
/// matches the current month, the worker leaves it alone.
///
/// Implementation uses a single set-based <see cref="EntityFrameworkQueryableExtensions.ExecuteUpdateAsync"/>
/// on relational providers, with a tracked fallback for EF's in-memory test provider.
/// Concurrent reservations during the reset are safe: any reservation
/// that lands between this UPDATE and a competing pick will simply
/// increment the freshly-zeroed counter to 1, which is correct.
/// </summary>
public sealed class AiAccountQuotaResetWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<AiAccountQuotaResetWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Light random jitter so multi-instance deployments don't all race.
        await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(5, 30)), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var resetCount = await RunOnceAsync(stoppingToken);
                if (resetCount > 0)
                {
                    logger.LogInformation(
                        "AiAccountQuotaResetWorker reset {Count} account counters for new month.",
                        resetCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AiAccountQuotaResetWorker tick failed.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var now = clock.GetUtcNow();
        var currentKey = now.ToString("yyyy-MM");

        if (string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.InMemory", StringComparison.Ordinal))
        {
            var staleAccounts = await db.AiProviderAccounts
                .Where(a => a.PeriodMonthKey != currentKey)
                .ToListAsync(ct);

            foreach (var account in staleAccounts)
            {
                account.RequestsUsedThisMonth = 0;
                account.PeriodMonthKey = currentKey;
                account.UpdatedAt = now;
            }

            if (staleAccounts.Count > 0)
            {
                await db.SaveChangesAsync(ct);
            }

            return staleAccounts.Count;
        }

        // Set-based update: any row whose PeriodMonthKey is not the current
        // month gets RequestsUsedThisMonth=0 and PeriodMonthKey=currentKey.
        // This is the only safe way to reset without race vs. PickAndReserve.
        var rowsAffected = await db.AiProviderAccounts
            .Where(a => a.PeriodMonthKey != currentKey)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.RequestsUsedThisMonth, _ => 0)
                .SetProperty(a => a.PeriodMonthKey, _ => currentKey)
                .SetProperty(a => a.UpdatedAt, _ => now), ct);

        return rowsAffected;
    }
}
