using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.VideoLibrary;

/// <summary>
/// Every 5 minutes (leader replica only):
///   1. Reconcile stale non-terminal encode states — videos stuck in
///      Uploading/Queued/Processing/Encoding for &gt;10 minutes are re-read
///      from Bunny (belt and suspenders for missed webhooks).
///   2. Sweep expired attestation challenges older than 1 hour.
/// Skips silently while Bunny is unconfigured (dormant feature).
/// </summary>
public sealed class BunnyEncodeStatusWorker(
    IServiceScopeFactory scopeFactory,
    IVideoWorkerLeaderLock leaderLock,
    ILogger<BunnyEncodeStatusWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ChallengeRetention = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await leaderLock.IsLeaderAsync(stoppingToken))
                {
                    await RunOnceAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "BunnyEncodeStatusWorker sweep failed; retrying next cycle.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Single sweep, exposed for deterministic tests.</summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var bunny = scope.ServiceProvider.GetRequiredService<IBunnyStreamClient>();
        var now = DateTimeOffset.UtcNow;

        // 1. Reconcile stale non-terminal encode states.
        var staleBefore = now - StaleAfter;
        var stale = await db.LibraryVideos
            .Where(v => v.BunnyVideoId != null
                && (v.EncodeStatus == VideoEncodeStatus.Uploading
                    || v.EncodeStatus == VideoEncodeStatus.Queued
                    || v.EncodeStatus == VideoEncodeStatus.Processing
                    || v.EncodeStatus == VideoEncodeStatus.Encoding)
                && v.UpdatedAt < staleBefore)
            .OrderBy(v => v.UpdatedAt)
            .Take(25)
            .ToListAsync(ct);

        foreach (var video in stale)
        {
            try
            {
                var info = await bunny.GetVideoAsync(video.BunnyVideoId!, ct);
                var before = video.EncodeStatus;
                VideoLibraryAdminService.ApplyBunnyInfo(video, info);
                video.UpdatedAt = now;
                if (video.EncodeStatus != before)
                {
                    logger.LogInformation(
                        "Reconciled encode status of video {VideoId}: {Before} → {After} (progress {Progress}%).",
                        video.Id, before, video.EncodeStatus, video.EncodeProgress);
                }
            }
            catch (BunnyNotConfiguredException)
            {
                // Dormant — nothing to reconcile until an admin configures Bunny.
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not reconcile Bunny status for video {VideoId}.", video.Id);
            }
        }
        if (stale.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        // 2. Sweep expired attestation challenges older than 1 hour.
        var challengeCutoff = now - ChallengeRetention;
        if (db.Database.IsRelational())
        {
            var removed = await db.VideoAttestationChallenges
                .Where(c => c.ExpiresAt < challengeCutoff)
                .ExecuteDeleteAsync(ct);
            if (removed > 0)
            {
                logger.LogDebug("Swept {Count} expired video attestation challenges.", removed);
            }
        }
        else
        {
            var expired = await db.VideoAttestationChallenges
                .Where(c => c.ExpiresAt < challengeCutoff)
                .ToListAsync(ct);
            if (expired.Count > 0)
            {
                db.VideoAttestationChallenges.RemoveRange(expired);
                await db.SaveChangesAsync(ct);
            }
        }
    }
}
