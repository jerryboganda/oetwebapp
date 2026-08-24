namespace OetLearner.Api.Services;

/// <summary>
/// Controls whether the API applies EF Core migrations during startup.
///
/// Normal production flow remains the GitHub Actions deployment gate
/// (idempotent SQL applied before the new image starts). As a permanent
/// safety net, an explicit <c>Bootstrap:AutoMigrate=true</c> opt-in is now
/// honored in EVERY environment, including Production: on container start
/// the API applies any pending migrations before serving traffic, so no
/// deploy path (CI gate, manual rollout script, blue/green cutover) can
/// leave schema drift behind again. EF Core 9+ serializes concurrent
/// migrators through the <c>__EFMigrationsLock</c> table, so simultaneous
/// blue/green startups are safe.
/// </summary>
public static class DatabaseMigrationExecutionPolicy
{
    public static bool ShouldApplyAtStartup(
        string? environmentName,
        bool isPostgreSql,
        bool autoMigrate)
    {
        return isPostgreSql && autoMigrate;
    }
}
