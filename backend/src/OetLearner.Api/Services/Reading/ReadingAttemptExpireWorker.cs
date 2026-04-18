namespace OetLearner.Api.Services.Reading;

/// <summary>
/// Hourly sweep for abandoned / timer-expired Reading attempts. Respects
/// the <c>AutoExpireWorkerEnabled</c> global policy flag (graceful disable).
/// See <c>docs/READING-AUTHORING-POLICY.md</c> §10.
/// </summary>
public sealed class ReadingAttemptExpireWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ReadingAttemptExpireWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(15, 45)), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IReadingAttemptService>();
                var count = await svc.SweepExpiredAsync(stoppingToken);
                if (count > 0)
                    logger.LogInformation("ReadingAttemptExpireWorker expired {Count} attempts.", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "ReadingAttemptExpireWorker tick failed.");
            }
            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
