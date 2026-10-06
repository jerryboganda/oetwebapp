using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services.Caching;

/// <summary>
/// Feeds the runtime kill switch of <see cref="UserStateCache"/> from the
/// <see cref="UserStateCache.FeatureFlagKey"/> feature flag (Admin &gt; Feature Flags), so an
/// operator can turn the cache off within about 30 seconds without a deploy. No flag row means
/// ON (the owner-approved default); a row with <c>Enabled = false</c> turns it off. A read
/// failure keeps the last known value.
///
/// <para>Deliberately a background poll and not a read on the request path: the JWT check runs on
/// every authenticated request and must stay one database command.</para>
/// </summary>
public sealed class UserStateCacheSwitchWorker(
    IServiceScopeFactory scopeFactory,
    UserStateCache cache,
    ILogger<UserStateCacheSwitchWorker> logger) : BackgroundService
{
    internal static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await RefreshOnceAsync(stoppingToken);
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    /// <summary>One flag read. Never throws (other than cancellation of the host).</summary>
    internal async Task RefreshOnceAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ReadTimeout);
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            // Few rows per key; ordered in memory because SQLite cannot ORDER BY DateTimeOffset.
            var rows = await db.FeatureFlags.AsNoTracking()
                .Where(flag => flag.Key == UserStateCache.FeatureFlagKey)
                .ToListAsync(timeout.Token);
            var enabled = rows.OrderByDescending(flag => flag.UpdatedAt).FirstOrDefault()?.Enabled ?? true;
            cache.ApplyRuntimeSwitch(enabled);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Keep the last known value; the next attempt is one interval away.
            logger.LogWarning(ex, "User-state cache kill-switch flag '{Flag}' could not be read; keeping the last value.", UserStateCache.FeatureFlagKey);
        }
    }
}
