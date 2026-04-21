using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Conversation;

public sealed class ConversationAudioRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ConversationAudioRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(6);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepOnceAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Conversation audio retention sweep failed"); }
            try { await Task.Delay(interval, stoppingToken); } catch (TaskCanceledException) { }
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var optionsProvider = scope.ServiceProvider.GetRequiredService<IConversationOptionsProvider>();
        var options = await optionsProvider.GetAsync(ct);

        var retentionDays = Math.Max(1, options.AudioRetentionDays);
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(retentionDays);

        var dueTurns = await db.ConversationTurns
            .Where(t => t.AudioUrl != null && t.CreatedAt < cutoff)
            .OrderBy(t => t.CreatedAt).Take(500).ToListAsync(ct);

        int deleted = 0;
        foreach (var turn in dueTurns)
        {
            var url = turn.AudioUrl!;
            var key = UrlToKey(url);
            if (key is null) { turn.AudioUrl = null; continue; }
            try
            {
                if (storage.Exists(key)) { storage.Delete(key); deleted++; }
                turn.AudioUrl = null;
            }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to delete audio {Key}", key); }
        }
        if (dueTurns.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Swept {Count} turns, deleted {Deleted} blobs.", dueTurns.Count, deleted);
        }
    }

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
