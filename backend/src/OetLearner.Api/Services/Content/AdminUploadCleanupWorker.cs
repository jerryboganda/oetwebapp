using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Content;

/// <summary>
/// Hourly sweep that expires incomplete admin uploads past their TTL and
/// removes any orphaned staging files from disk. Idempotent; safe to run
/// on multiple instances (uses state transitions, not timestamps, to guard).
/// </summary>
public sealed class AdminUploadCleanupWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AdminUploadCleanupWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(10, 45)), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "AdminUploadCleanupWorker tick failed."); }
            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var opts = scope.ServiceProvider.GetRequiredService<IOptions<StorageOptions>>().Value.ContentUpload;

        var now = DateTimeOffset.UtcNow;
        var candidates = await db.AdminUploadSessions
            .Where(x => x.State != AdminUploadState.Completed
                && x.State != AdminUploadState.Aborted
                && x.State != AdminUploadState.Expired)
            .ToListAsync(ct);
        var expired = candidates
            .Where(x => x.ExpiresAt <= now)
            .ToList();

        foreach (var s in expired)
        {
            try
            {
                storage.DeletePrefix(ContentAddressed.StagingSessionPrefix(
                    opts.StagingSubpath, s.AdminUserId, s.Id));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete staging for expired session {Id}", s.Id);
            }
            s.State = AdminUploadState.Expired;
        }

        if (expired.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Expired {Count} admin upload sessions.", expired.Count);
        }
        return expired.Count;
    }
}
