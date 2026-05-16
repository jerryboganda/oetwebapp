using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Billing-hardening I-9 (May 2026 closure addendum).
///
/// Explicit named retention worker that nulls the <c>PayloadJson</c> column on
/// <c>PaymentWebhookEvents</c> once a row is older than
/// <see cref="DataRetentionOptions.PaymentWebhookPiiNullOutAge"/> (default
/// 90 days). The event metadata (id, status, gateway, gateway-event-id,
/// gateway-transaction-id, normalized status, timestamps) is **retained**
/// for forensic chain-of-custody (refund disputes, regulator requests,
/// "did we ever receive this event" lookups); only the payload body — even
/// though it is already PII-stripped at ingest by
/// <see cref="PaymentWebhookPiiRedactor"/> — is nulled to <c>"{}"</c>.
///
/// The companion <see cref="DataRetentionWorker"/> deletes the entire row
/// at a later cutoff (default 180 days). The two windows together implement
/// a tiered retention model:
///
///   • 0..90 days:    Full safe-payload kept for debugging.
///   • 90..180 days:  Payload nulled; metadata kept.
///   • 180+ days:     Entire row deleted.
///
/// Idempotent: once <c>PayloadJson == "{}"</c>, the worker leaves the row
/// alone. Sweeps in batches of 5 000 rows per tick (configurable via
/// <see cref="DataRetentionOptions.BatchSize"/>) to keep delete-batch
/// latency bounded.
/// </summary>
public sealed class WebhookPiiRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DataRetentionOptions> options,
    ILogger<WebhookPiiRetentionWorker> logger) : BackgroundService
{
    private readonly DataRetentionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); }
        catch (TaskCanceledException) { return; }

        var interval = _options.SweepInterval > TimeSpan.Zero
            ? _options.SweepInterval
            : TimeSpan.FromHours(24);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nulled = await RunOnceAsync(stoppingToken);
                if (nulled > 0)
                {
                    logger.LogInformation(
                        "WebhookPiiRetentionWorker nulled payloads on {Count} rows.",
                        nulled);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "WebhookPiiRetentionWorker tick failed.");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Internal entry point used by tests. Returns the number of rows
    /// whose payload was nulled in this sweep.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        if (_options.PaymentWebhookPiiNullOutAge <= TimeSpan.Zero)
        {
            return 0;
        }

        var cutoff = DateTimeOffset.UtcNow - _options.PaymentWebhookPiiNullOutAge;
        var batch = Math.Max(1, _options.BatchSize);

        // EF InMemory does not support ExecuteUpdateAsync; fall back to
        // tracked SaveChanges for the test path. Same set-membership
        // predicate either way.
        if (string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.InMemory", StringComparison.Ordinal))
        {
            var stale = await db.PaymentWebhookEvents
                .Where(e => e.ReceivedAt < cutoff && e.PayloadJson != "{}")
                .OrderBy(e => e.ReceivedAt)
                .Take(batch)
                .ToListAsync(ct);

            foreach (var row in stale)
            {
                row.PayloadJson = "{}";
                row.ErrorMessage = null;
            }

            if (stale.Count > 0)
            {
                await db.SaveChangesAsync(ct);
            }

            return stale.Count;
        }

        // Relational set-based update.
        return await db.PaymentWebhookEvents
            .Where(e => e.ReceivedAt < cutoff && e.PayloadJson != "{}")
            .OrderBy(e => e.ReceivedAt)
            .Take(batch)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.PayloadJson, _ => "{}")
                .SetProperty(e => e.ErrorMessage, _ => (string?)null), ct);
    }
}
