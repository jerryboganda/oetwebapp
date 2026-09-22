using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Completes the storage handoff after LiveKit has finished writing an
/// egress object. The webhook is acknowledged before a remote object is
/// guaranteed to be visible, so this worker retries readiness and never
/// exposes a partially available recording as playable.
/// </summary>
public sealed class LiveKitRecordingReadinessWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<LiveKitRecordingReadinessWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                logger.LogError(ex, "LiveKit recording readiness sweep failed.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        var rows = await db.SpeakingRecordings
            .Include(recording => recording.MediaAsset)
            .Where(recording => recording.Source == SpeakingRecordingSource.LiveKitEgress
                && !recording.IsArchived
                && recording.MediaAsset != null
                && recording.MediaAsset.Status == MediaAssetStatus.Processing)
            .OrderBy(recording => recording.CreatedAt)
            .Take(10)
            .ToListAsync(ct);

        var ready = 0;
        foreach (var recording in rows)
        {
            var asset = recording.MediaAsset;
            if (asset is null || string.IsNullOrWhiteSpace(asset.StoragePath)) continue;

            bool exists;
            try
            {
                exists = await storage.ExistsAsync(asset.StoragePath, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(
                    ex,
                    "LiveKit recording storage check failed recordingId={RecordingId} path={StoragePath}",
                    recording.Id,
                    asset.StoragePath);
                continue;
            }

            if (!exists) continue;

            try
            {
                var length = await storage.LengthAsync(asset.StoragePath, ct);
                if (length > 0)
                {
                    asset.SizeBytes = length;
                    recording.SizeBytes = length;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(
                    ex,
                    "LiveKit recording metadata read failed recordingId={RecordingId} path={StoragePath}",
                    recording.Id,
                    asset.StoragePath);
                continue;
            }

            asset.Status = MediaAssetStatus.Ready;
            asset.ProcessedAt = DateTimeOffset.UtcNow;
            ready += 1;
        }

        if (ready > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "LiveKit recording readiness marked {Count} recording(s) playable.",
                ready);
        }
    }
}
