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
            await RunOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _log.LogError(ex, "Listening V2 backfill threw at top level — skipping.");
        }
    }

    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        // Let the database perform the anti-join so an already-current startup
        // returns no rows instead of materializing both complete user sets.
        var pending = await db.ListeningAttempts
            .Where(attempt => !db.ListeningPathwayProgress
                .Any(progress => progress.UserId == attempt.UserId))
            .Select(attempt => attempt.UserId)
            .Distinct()
            .ToListAsync(ct);
        if (pending.Count == 0) return 0;

        _log.LogInformation(
            "Listening V2 backfill: seeding pathway for {Count} users.",
            pending.Count);

        var pathway = scope.ServiceProvider.GetRequiredService<ListeningPathwayProgressService>();
        foreach (var userId in pending)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await pathway.RecomputeAsync(userId, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "Listening V2 backfill failed for user {UserId}.", userId);
            }
        }

        _log.LogInformation("Listening V2 backfill complete.");
        return pending.Count;
    }
}
