using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Services.Settings;

namespace OetWithDrHesham.Api.Services;

/// <summary>
/// Background sweeper for high-volume, append-only event tables
/// (<c>AnalyticsEvents</c>, <c>AuditEvents</c>, <c>PaymentWebhookEvents</c>,
/// <c>NotificationDeliveryAttempts</c>). Each table has an independent
/// retention window resolved from <see cref="IRuntimeSettingsProvider"/>
/// (admin DB overrides merged over the <c>DataRetention</c> env/appsettings
/// defaults) so retention windows can be tuned from the admin panel without a
/// redeploy.
///
/// Deletion is batched (default: 5 000 rows per table per sweep) so a large
/// backlog drains gradually without holding long locks. A retention value of
/// <see cref="TimeSpan.Zero"/> or negative disables the sweep for that table
/// — useful for environments that externalise audit storage.
/// </summary>
public sealed class DataRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IRuntimeSettingsProvider runtimeSettings,
    ILogger<DataRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay at startup so migrations finish before we hit tables.
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            // Re-read the merged settings each loop so an admin change to the
            // retention windows or sweep cadence takes effect on the next tick.
            var interval = TimeSpan.FromHours(24);
            try
            {
                var settings = (await runtimeSettings.GetAsync(stoppingToken)).DataRetention;
                await SweepOnceAsync(settings, stoppingToken);
                interval = settings.SweepInterval > TimeSpan.Zero ? settings.SweepInterval : TimeSpan.FromHours(24);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Data-retention sweep failed");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (TaskCanceledException) { /* shutdown */ }
        }
    }

    private async Task SweepOnceAsync(DataRetentionSettings settings, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var now = DateTimeOffset.UtcNow;
        var batch = Math.Max(1, settings.BatchSize);

        var analytics = 0;
        if (settings.AnalyticsEvents > TimeSpan.Zero)
        {
            var cutoff = now - settings.AnalyticsEvents;
            analytics = await db.AnalyticsEvents
                .Where(e => e.OccurredAt < cutoff)
                .OrderBy(e => e.OccurredAt)
                .Take(batch)
                .ExecuteDeleteAsync(ct);
        }

        var audit = 0;
        if (settings.AuditEvents > TimeSpan.Zero)
        {
            var cutoff = now - settings.AuditEvents;
            audit = await db.AuditEvents
                .Where(e => e.OccurredAt < cutoff)
                .OrderBy(e => e.OccurredAt)
                .Take(batch)
                .ExecuteDeleteAsync(ct);
        }

        var webhooks = 0;
        if (settings.PaymentWebhookEvents > TimeSpan.Zero)
        {
            var cutoff = now - settings.PaymentWebhookEvents;
            webhooks = await db.PaymentWebhookEvents
                .Where(e => e.ReceivedAt < cutoff)
                .OrderBy(e => e.ReceivedAt)
                .Take(batch)
                .ExecuteDeleteAsync(ct);
        }

        var deliveries = 0;
        if (settings.NotificationDeliveryAttempts > TimeSpan.Zero)
        {
            var cutoff = now - settings.NotificationDeliveryAttempts;
            deliveries = await db.NotificationDeliveryAttempts
                .Where(a => a.AttemptedAt < cutoff)
                .OrderBy(a => a.AttemptedAt)
                .Take(batch)
                .ExecuteDeleteAsync(ct);
        }

        if (analytics + audit + webhooks + deliveries > 0)
        {
            logger.LogInformation(
                "Data-retention swept: analytics={A} audit={U} webhooks={W} deliveries={D}",
                analytics, audit, webhooks, deliveries);
        }
    }
}
