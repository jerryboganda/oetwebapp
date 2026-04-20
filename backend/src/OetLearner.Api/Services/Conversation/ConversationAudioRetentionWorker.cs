using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Conversation;

/// <summary>
/// Background worker that deletes conversation audio blobs older than
/// <see cref="ConversationOptions.AudioRetentionDays"/>. Transcript text
/// persists forever; raw audio is reaped on schedule.
/// </summary>
public sealed class ConversationAudioRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ConversationOptions> options,
    ILogger<ConversationAudioRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(6);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Conversation audio retention sweep failed");
            }
            try { await Task.Delay(interval, stoppingToken); } catch (TaskCanceledException) { /* shutdown */ }
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        var retentionDays = Math.Max(1, options.Value.AudioRetentionDays);
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(retentionDays);

        var dueTurns = await db.ConversationTurns
            .Where(t => t.AudioUrl != null && t.CreatedAt < cutoff)
            .OrderBy(t => t.CreatedAt)
            .Take(500)
            .ToListAsync(ct);

        int deleted = 0;
        foreach (var turn in dueTurns)
        {
            var url = turn.AudioUrl!;
            var key = UrlToKey(url);
            if (key is null)
            {
                turn.AudioUrl = null;
                continue;
            }
            try
            {
                if (storage.Exists(key))
                {
                    storage.Delete(key);
                    deleted++;
                }
                turn.AudioUrl = null;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete conversation audio {Key}", key);
            }
        }
        if (dueTurns.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Conversation audio retention swept {Count} turns, deleted {Deleted} blobs.", dueTurns.Count, deleted);
        }
    }

    /// <summary>Derive the storage key from the public playback URL.</summary>
    private static string? UrlToKey(string url)
    {
        const string prefix = "/v1/conversations/media/";
        var idx = url.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var fileName = url[(idx + prefix.Length)..];
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var dot = fileName.LastIndexOf('.');
        if (dot < 0) return null;
        var sha = fileName[..dot];
        var ext = fileName[(dot + 1)..];
        if (sha.Length < 4) return null;
        return $"conversation/audio/{sha[..2]}/{sha.Substring(2, 2)}/{sha}.{ext}";
    }
}
