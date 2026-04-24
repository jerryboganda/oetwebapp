using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services;

/// <summary>
/// Background sweeper for high-volume, append-only event tables
/// (<c>AnalyticsEvents</c>, <c>AuditEvents</c>, <c>PaymentWebhookEvents</c>,
/// <c>NotificationDeliveryAttempts</c>). Each table has an independent
/// retention window configured via <see cref="DataRetentionOptions"/>.
///
/// Deletion is batched (default: 5 000 rows per table per sweep) so a large
/// backlog drains gradually without holding long locks. A retention value of
/// <see cref="TimeSpan.Zero"/> or negative disables the sweep for that table
/// — useful for environments that externalise audit storage.
/// </summary>
public sealed class DataRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DataRetentionOptions> options,
    ILogger<DataRetentionWorker> logger) : BackgroundService
{
    private readonly DataRetentionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay at startup so migrations finish before we hit tables.
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (TaskCanceledException) { return; }

        var interval = _options.SweepInterval > TimeSpan.Zero
            ? _options.SweepInterval
            : TimeSpan.FromHours(24);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Data-retention sweep failed");
            }
            try { await Task.Delay(interval, stoppingToken); }
            catch (TaskCanceledException) { /* shutdown */ }
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var now = DateTimeOffset.UtcNow;
        var batch = Math.Max(1, _options.BatchSize);

        var analytics = 0;
        if (_options.AnalyticsEvents > TimeSpan.Zero)
        {
            var cutoff = now - _options.AnalyticsEvents;
            analytics = await db.AnalyticsEvents
                .Where(e => e.OccurredAt < cutoff)
                .OrderBy(e => e.OccurredAt)
                .Take(batch)
                .ExecuteDeleteAsync(ct);
        }

        var audit = 0;
        if (_options.AuditEvents > TimeSpan.Zero)
        {
            var cutoff = now - _options.AuditEvents;
            audit = await db.AuditEvents
                .Where(e => e.OccurredAt < cutoff)
                .OrderBy(e => e.OccurredAt)
                .Take(batch)
                .ExecuteDeleteAsync(ct);
        }

        var webhooks = 0;
        if (_options.PaymentWebhookEvents > TimeSpan.Zero)
        {
            var cutoff = now - _options.PaymentWebhookEvents;
            webhooks = await db.PaymentWebhookEvents
                .Where(e => e.ReceivedAt < cutoff)
                .OrderBy(e => e.ReceivedAt)
                .Take(batch)
                .ExecuteDeleteAsync(ct);
        }

        var deliveries = 0;
        if (_options.NotificationDeliveryAttempts > TimeSpan.Zero)
        {
            var cutoff = now - _options.NotificationDeliveryAttempts;
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
