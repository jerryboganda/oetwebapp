using OetLearner.Api.Data;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Deploy restarts (WAI-03): when this API slot or the ai-worker stops, its own in-flight Writing
/// grades go back to <c>queued</c> (due now) so the surviving slot grades them, instead of reading
/// "grading" until the 25-minute stale reclaim. A hard kill skips this; the reclaim or a manual
/// Retry covers that.
/// </summary>
public sealed class WritingGradeShutdownRequeue(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<WritingGradeShutdownRequeue> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var requeued = await WritingGradeRecovery.RequeueOwnClaimsAsync(
                db, WritingGradeRecovery.ProcessOwnerPrefix, clock.GetUtcNow(), cancellationToken);
            if (requeued > 0)
            {
                logger.LogInformation("Shutdown requeued {Count} in-flight Writing grade(s).", requeued);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Shutdown requeue of in-flight Writing grades failed; the stale reclaim will recover them.");
        }
    }
}
