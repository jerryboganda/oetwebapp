using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;

namespace OetWithDrHesham.Api.Services.Billing;

/// <summary>
/// Scans recently-completed <c>PaymentTransaction</c> rows and records a
/// conversion against the matching <c>PricingExperimentAssignment</c>.
/// Decouples experiment instrumentation from the existing checkout pipeline
/// so adding experiments does not require touching the 2,600-line
/// <c>LearnerService.cs</c>.
/// </summary>
public sealed class ExperimentConversionWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ExperimentConversionWorker> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(10);

    public ExperimentConversionWorker(IServiceProvider services, ILogger<ExperimentConversionWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(90), stoppingToken); } catch { }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
                var exp = scope.ServiceProvider.GetRequiredService<IPricingExperimentService>();

                // Find completed payments in the last 24h that have a matching
                // un-converted experiment assignment for the same user.
                var since = DateTimeOffset.UtcNow.AddHours(-24);
                var pending = await (
                    from p in db.PaymentTransactions
                    join a in db.PricingExperimentAssignments on p.LearnerUserId equals a.UserId
                    where p.Status == "completed"
                        && p.CreatedAt >= since
                        && !a.Converted
                    select new { p.LearnerUserId, p.Amount, p.QuoteId, a.ExperimentId, p.Id }
                ).Take(500).ToListAsync(stoppingToken);

                foreach (var hit in pending)
                {
                    // Resolve the experiment's TargetType/TargetId to satisfy
                    // RecordConversionAsync's lookup. Lookup is per-experiment.
                    var experiment = await db.PricingExperiments
                        .Where(e => e.Id == hit.ExperimentId)
                        .Select(e => new { e.TargetType, e.TargetId })
                        .FirstOrDefaultAsync(stoppingToken);
                    if (experiment is null) continue;
                    try
                    {
                        await exp.RecordConversionAsync(hit.LearnerUserId, experiment.TargetType, experiment.TargetId, hit.Amount, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "RecordConversion failed for user {UserId} exp {ExperimentId}", hit.LearnerUserId, hit.ExperimentId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExperimentConversionWorker iteration failed.");
            }

            try { await Task.Delay(_interval, stoppingToken); } catch { }
        }
    }
}
