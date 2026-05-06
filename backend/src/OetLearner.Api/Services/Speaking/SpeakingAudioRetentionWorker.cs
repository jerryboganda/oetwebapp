using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Wave 7 of docs/SPEAKING-MODULE-PLAN.md - background sweeper that
/// physically deletes learner speaking audio once it is older than
/// <see cref="SpeakingComplianceOptions.AudioRetentionDays"/>. Mirrors
/// <see cref="OetLearner.Api.Services.Conversation.ConversationAudioRetentionWorker"/>.
///
/// Sweeps in batches of 500 to keep memory bounded. The blob is removed
/// via <see cref="MediaStorageService"/> (which is the same path used to
/// originally write speaking audio) and the <c>AudioObjectKey</c> column
/// is cleared. Other attempt fields (transcript, analysis, scores) are
/// retained — only the raw recording is reaped.
/// </summary>
public sealed class SpeakingAudioRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SpeakingAudioRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Speaking audio retention sweep failed");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Shutdown - exit loop quietly.
            }
        }
    }

    /// <summary>
    /// Internal entry point used by tests. Returns the number of
    /// attempts that had their audio cleared.
    /// </summary>
    public async Task<int> SweepOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var media = scope.ServiceProvider.GetRequiredService<MediaStorageService>();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptions<SpeakingComplianceOptions>>().Value;

        if (options.AudioRetentionDays <= 0)
        {
            return 0;
        }

        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(options.AudioRetentionDays);

        // We retain audio while the attempt is still active. Sweep on
        // SubmittedAt when present, otherwise StartedAt. Only speaking.
        var due = await db.Attempts
            .Where(a => a.SubtestCode == "speaking"
                && a.AudioObjectKey != null
                && (a.SubmittedAt != null
                    ? a.SubmittedAt < cutoff
                    : a.StartedAt < cutoff))
            .OrderBy(a => a.SubmittedAt ?? a.StartedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0)
        {
            return 0;
        }

        var clearedAttempts = 0;
        var deletedBlobs = 0;
        foreach (var attempt in due)
        {
            var key = attempt.AudioObjectKey!;
            var canClearPointer = true;
            try
            {
                if (media.Exists(key))
                {
                    media.DeleteFile(key);
                    deletedBlobs++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to delete speaking audio blob {Key} for attempt {AttemptId}",
                    key, attempt.Id);
                canClearPointer = false;
            }

            if (!canClearPointer)
            {
                continue;
            }

            attempt.AudioObjectKey = null;
            clearedAttempts++;
        }

        if (clearedAttempts == 0)
        {
            return 0;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Speaking audio retention sweep cleared {Count} attempts, deleted {Blobs} blobs.",
            clearedAttempts, deletedBlobs);

        return clearedAttempts;
    }
}
