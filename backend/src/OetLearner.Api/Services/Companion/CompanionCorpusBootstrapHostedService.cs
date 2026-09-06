using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services.Companion;

/// <summary>
/// Builds the companion knowledge corpus on startup when it is missing.
///
/// <para>
/// Without this, the companion shipped with a manual step nobody would remember:
/// the retriever works, the indexer works, and the answer to every question is
/// still "I don't have verified information on that" until an operator calls the
/// reindex endpoint. A grounding system whose grounding has to be switched on by
/// hand is a grounding system that will be off in production.
/// </para>
///
/// <para><b>Three conditions, all required.</b> It runs only when the companion
/// flag is on (so a disabled surface never pays for embeddings), only when the
/// corpus is actually empty (so it is a bootstrap, not a re-index — content
/// changes go through the admin endpoint), and only on Postgres (SQLite test
/// databases have no vector column and no rulebook rows worth indexing).</para>
///
/// <para><b>It never blocks boot.</b> Indexing runs detached after startup and
/// every failure is caught: a database that is still migrating, an embedding
/// provider that is down, or a missing rulebook must degrade the companion, not
/// take the API offline.</para>
/// </summary>
public sealed class CompanionCorpusBootstrapHostedService(
    IServiceProvider services,
    ILogger<CompanionCorpusBootstrapHostedService> logger) : BackgroundService
{
    /// <summary>
    /// Let migrations and the tool-catalog seeder finish first. This is a
    /// convenience bootstrap, not a race to be first.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

            if (!db.Database.IsNpgsql())
            {
                logger.LogDebug("Companion corpus bootstrap skipped: not running on Postgres.");
                return;
            }

            var flags = scope.ServiceProvider.GetRequiredService<ICompanionFeatureFlags>();
            if (!await flags.IsEnabledAsync(stoppingToken))
            {
                logger.LogInformation(
                    "Companion corpus bootstrap skipped: ai_learning_companion is off. "
                    + "Enable the flag and restart, or POST /v1/admin/companion/knowledge/reindex.");
                return;
            }

            var existingChunks = await db.CompanionChunks.CountAsync(stoppingToken);
            if (existingChunks > 0)
            {
                logger.LogInformation(
                    "Companion corpus bootstrap skipped: {Chunks} chunks already indexed. "
                    + "Use the admin reindex endpoint to refresh after a rulebook change.",
                    existingChunks);
                return;
            }

            logger.LogInformation("Companion corpus is empty; indexing the rulebooks now.");

            var indexer = scope.ServiceProvider.GetRequiredService<ICompanionRulebookIndexer>();
            var result = await indexer.IndexAsync(professions: null, embed: true, stoppingToken);

            logger.LogInformation(
                "Companion corpus bootstrap complete: {Sources} sources, {Chunks} chunks, "
                + "{Embedded} embedded, {Warnings} warning(s).",
                result.SourcesWritten, result.ChunksWritten, result.ChunksEmbedded, result.Warnings.Count);

            foreach (var warning in result.Warnings)
            {
                logger.LogWarning("Companion corpus bootstrap: {Warning}", warning);
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutting down — nothing to report.
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Companion corpus bootstrap failed. The companion will answer without grounded "
                + "evidence until POST /v1/admin/companion/knowledge/reindex succeeds.");
        }
    }
}
