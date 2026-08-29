using OetLearner.Api.Data;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// W4 — dedicated <c>ai-worker</c> hosted loop. Claims
/// <c>AiOperations</c> with <c>FOR UPDATE SKIP LOCKED</c>, resumes
/// <c>ProviderSucceeded</c> without a provider call, and stops claiming
/// on SIGTERM so deploy drain can reach zero leased rows for this owner.
/// </summary>
public sealed class AiOperationWorker(
    IServiceScopeFactory scopeFactory,
    IAiOperationLeaseClaimer claimer,
    IAiLeasedOperationHandler handler,
    ILogger<AiOperationWorker> logger) : BackgroundService
{
    public const int ClaimBatchSize = 10;
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    internal static string CreateLeaseOwner()
        => $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var leaseOwner = CreateLeaseOwner();
        logger.LogInformation("AiOperationWorker started as {LeaseOwner}.", leaseOwner);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(leaseOwner, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AiOperationWorker tick failed for {LeaseOwner}.", leaseOwner);
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("AiOperationWorker stopping {LeaseOwner}; no further claims.", leaseOwner);
    }

    internal async Task TickAsync(string leaseOwner, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var claimed = await claimer.ClaimAsync(
            db,
            leaseOwner,
            DateTimeOffset.UtcNow,
            LeaseDuration,
            ClaimBatchSize,
            ct);

        foreach (var op in claimed)
        {
            ct.ThrowIfCancellationRequested();
            await handler.HandleAsync(op, ct);
        }
    }
}
