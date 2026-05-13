using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services.Listening;

/// <summary>
/// Listening V2 — one-shot startup backfill. Idempotent: skips users that
/// already have any <c>ListeningPathwayProgress</c> row. Walks
/// <see cref="ListeningPathwayProgressService.PathwayStages"/> for each
/// remaining user with at least one Listening attempt and seeds the rows.
///
/// Schema-level backfills (version pinning) are baked into the
/// <c>20260511110000_Listening_V2_Schema</c> migration SQL itself, so this
/// service only handles the pathway view that requires service logic.
/// </summary>
public sealed class ListeningV2BackfillService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ListeningV2BackfillService> _log;

    public ListeningV2BackfillService(
        IServiceScopeFactory scopes,
        ILogger<ListeningV2BackfillService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Defer slightly so the rest of startup completes before we touch DB.
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var pathway = scope.ServiceProvider.GetRequiredService<ListeningPathwayProgressService>();

            // Find candidate users: have at least one Listening attempt and
            // no pathway rows yet.
            var withAttempts = await db.ListeningAttempts
                .Select(a => a.UserId)
                .Distinct()
                .ToListAsync(stoppingToken);
            if (withAttempts.Count == 0) return;

            var alreadySeeded = await db.ListeningPathwayProgress
                .Select(p => p.UserId)
                .Distinct()
                .ToListAsync(stoppingToken);
            var pending = withAttempts.Except(alreadySeeded).ToList();
            if (pending.Count == 0) return;

            _log.LogInformation(
                "Listening V2 backfill: seeding pathway for {Count} users.",
                pending.Count);

            foreach (var userId in pending)
            {
                if (stoppingToken.IsCancellationRequested) break;
                try
                {
                    await pathway.RecomputeAsync(userId, stoppingToken);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex,
                        "Listening V2 backfill failed for user {UserId}.", userId);
                }
            }

            _log.LogInformation("Listening V2 backfill complete.");
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _log.LogError(ex, "Listening V2 backfill threw at top level — skipping.");
        }
    }
}
