namespace OetLearner.Api.Services.AiTools;

/// <summary>
/// Phase 5 — Idempotent startup hook that ensures every registered
/// <see cref="IAiToolExecutor"/> has a matching active row in the
/// <c>AiTools</c> catalog. Runs once on host start; tolerates partial
/// failure (e.g. database not yet migrated) so the API still boots.
/// </summary>
public sealed class AiToolCatalogSeederHostedService(
    IServiceProvider services,
    ILogger<AiToolCatalogSeederHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = services.CreateScope();
            var registry = scope.ServiceProvider.GetRequiredService<IAiToolRegistry>();
            await registry.SeedCatalogAsync(cancellationToken);
            logger.LogInformation("AiToolCatalogSeeder: catalog reconciled.");
        }
        catch (Exception ex)
        {
            // Never block boot for this — tools simply won't resolve until
            // the catalog seeder succeeds on a later restart.
            logger.LogWarning(ex, "AiToolCatalogSeeder: skipping (DB unavailable or migration pending).");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
