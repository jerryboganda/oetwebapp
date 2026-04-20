using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Pronunciation;

/// <summary>
/// Background worker that deletes pronunciation audio blobs whose
/// <see cref="Domain.PronunciationAttempt.AudioReapAt"/> has passed.
/// Scoring rows (assessments / progress) are retained forever; the raw
/// recording is only needed for the initial scoring pass + a retention
/// window for complaint-investigation.
/// </summary>
public sealed class PronunciationAudioRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PronunciationAudioRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run immediately at startup then every 6 hours. Cheap delete, safe to run often.
        var interval = TimeSpan.FromHours(6);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Pronunciation audio retention sweep failed");
            }
            try { await Task.Delay(interval, stoppingToken); } catch (TaskCanceledException) { /* shutdown */ }
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        var now = DateTimeOffset.UtcNow;
        var candidates = await db.PronunciationAttempts
            .Where(a => a.AudioStorageKey != null && a.AudioReapAt != null)
            .ToListAsync(ct);
        var dueAttempts = candidates
            .Where(a => a.AudioReapAt <= now)
            .Take(200)
            .ToList();

        int deleted = 0;
        foreach (var attempt in dueAttempts)
        {
            var key = attempt.AudioStorageKey!;
            try
            {
                if (storage.Exists(key))
                {
                    storage.Delete(key);
                    deleted++;
                }
                attempt.AudioStorageKey = null;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete pronunciation audio {Key}", key);
            }
        }
        if (dueAttempts.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Pronunciation audio retention swept {Count} attempts, deleted {Deleted} blobs.", dueAttempts.Count, deleted);
        }
    }
}
