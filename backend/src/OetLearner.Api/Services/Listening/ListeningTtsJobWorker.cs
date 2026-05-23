using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// ListeningTtsJobWorker.
//
// Polls `ListeningTtsJobs` for Pending items and calls ListeningTtsService
// to synthesise audio. Follows the same pattern as other hosted workers in
// this codebase: 50-batch poll, 3-retry cap with exponential back-off.
//
// Back-off schedule: retry 1 → +30 s, retry 2 → +2 min, retry 3 → +10 min.
// After 3 failures the job is permanently Failed (admin must re-enqueue).
// Provider used for synthesis is selected in Program.cs via the
// `Listening:TtsProvider` configuration switch (stub for dev/CI, real provider
// for production).
// ═════════════════════════════════════════════════════════════════════════════

public sealed class ListeningTtsJobWorker(
    IServiceProvider services,
    ILogger<ListeningTtsJobWorker> logger) : BackgroundService
{
    private const int BatchSize = 50;
    private const int MaxRetries = 3;

    private static readonly TimeSpan[] BackoffDelays =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ListeningTtsJobWorker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in ListeningTtsJobWorker loop.");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        logger.LogInformation("ListeningTtsJobWorker stopping.");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db     = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var tts    = scope.ServiceProvider.GetRequiredService<IListeningTtsService>();

        var now = DateTimeOffset.UtcNow;
        var jobs = await db.ListeningTtsJobs
            .Where(j => j.Status == ListeningTtsJobStatus.Pending
                     && (j.RetryAfter == null || j.RetryAfter <= now))
            .OrderBy(j => j.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (jobs.Count == 0) return;

        logger.LogInformation("Processing {Count} TTS job(s).", jobs.Count);

        foreach (var job in jobs)
        {
            if (ct.IsCancellationRequested) break;
            await ProcessOneAsync(db, tts, job, ct);
        }
    }

    private async Task ProcessOneAsync(
        LearnerDbContext db,
        IListeningTtsService tts,
        ListeningTtsJob job,
        CancellationToken ct)
    {
        // Mark running (visible to admin UI).
        job.Status    = ListeningTtsJobStatus.Running;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        try
        {
            var result = await tts.SynthesizeAsync(job.ExtractId, job.RequestedBy, ct);
            job.Status       = ListeningTtsJobStatus.Completed;
            job.ErrorMessage = null;
            job.UpdatedAt    = DateTimeOffset.UtcNow;
            logger.LogInformation(
                "TTS job {JobId} completed — extract {ExtractId}, {Bytes} bytes, {Segments} segments.",
                job.Id, job.ExtractId, result.ByteLength, result.SegmentCount);
        }
        catch (Exception ex)
        {
            job.RetryCount++;
            job.ErrorMessage = ex.Message.Length > 2000
                ? ex.Message[..2000]
                : ex.Message;

            if (job.RetryCount >= MaxRetries)
            {
                job.Status    = ListeningTtsJobStatus.Failed;
                job.UpdatedAt = DateTimeOffset.UtcNow;
                logger.LogError(ex,
                    "TTS job {JobId} permanently failed after {Retries} retries.",
                    job.Id, job.RetryCount);
            }
            else
            {
                job.Status     = ListeningTtsJobStatus.Pending;
                job.RetryAfter = DateTimeOffset.UtcNow + BackoffDelays[job.RetryCount - 1];
                job.UpdatedAt  = DateTimeOffset.UtcNow;
                logger.LogWarning(ex,
                    "TTS job {JobId} failed (attempt {Attempt}/{Max}). Retrying after {Delay}.",
                    job.Id, job.RetryCount, MaxRetries, job.RetryAfter);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
