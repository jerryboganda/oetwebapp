using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

/// <summary>
/// Phase 2 worker. Hourly sweep for in-flight Listening attempts that are
/// past their <see cref="ListeningAttempt.DeadlineAt"/> or have been idle
/// past the <see cref="ListeningPolicy.AutoExpireAfterMinutes"/> window.
///
/// <para>
/// Mirrors <see cref="OetLearner.Api.Services.Reading.ReadingAttemptExpireWorker"/>
/// but talks to <see cref="LearnerDbContext"/> directly because the next-gen
/// Listening attempt service has not landed yet — Phase 2 is the entity +
/// migration slice. Once a `IListeningAttemptService.SweepExpiredAsync` ships
/// in a follow-up, this worker delegates to it for symmetry with Reading.
/// </para>
///
/// <para>
/// Respects the <see cref="ListeningPolicy.AutoExpireWorkerEnabled"/> flag so
/// admins can disable the sweep without restarting the API. Always safe to
/// run before any rows exist (no-op on an empty table).
/// </para>
/// </summary>
public sealed class ListeningAttemptExpireWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ListeningAttemptExpireWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stagger initial run so multiple replicas don't all sweep at once.
        try { await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(15, 45)), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var count = await SweepOnceAsync(stoppingToken);
                if (count > 0)
                    logger.LogInformation("ListeningAttemptExpireWorker expired {Count} attempts.", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "ListeningAttemptExpireWorker tick failed.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<int> SweepOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var policy = await db.ListeningPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == "global", ct);

        // Default to "enabled" when the singleton row hasn't been seeded yet —
        // a fresh deployment shouldn't accumulate stuck attempts forever.
        if (policy is not null && !policy.AutoExpireWorkerEnabled) return 0;

        var idleMinutes = policy?.AutoExpireAfterMinutes ?? 180;
        var now = DateTimeOffset.UtcNow;
        var idleCutoff = now.AddMinutes(-idleMinutes);

        var stale = await db.ListeningAttempts
            .Where(a => a.Status == ListeningAttemptStatus.InProgress
                && ((a.DeadlineAt != null && a.DeadlineAt < now)
                    || a.LastActivityAt < idleCutoff))
            .Take(500)
            .ToListAsync(ct);

        if (stale.Count == 0) return 0;

        foreach (var a in stale)
        {
            a.Status = ListeningAttemptStatus.Expired;
            a.SubmittedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return stale.Count;
    }
}
