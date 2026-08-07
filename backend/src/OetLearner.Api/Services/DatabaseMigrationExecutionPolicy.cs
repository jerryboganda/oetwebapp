using Microsoft.Extensions.Hosting;

namespace OetLearner.Api.Services;

/// <summary>
/// Controls the only environment in which the API is allowed to apply EF
/// migrations during startup. Production migrations are generated and applied
/// by the GitHub Actions deployment gate instead.
/// </summary>
public static class DatabaseMigrationExecutionPolicy
{
    public static bool ShouldApplyAtStartup(
        string? environmentName,
        bool isPostgreSql,
        bool autoMigrate)
    {
        return isPostgreSql
            && autoMigrate
            && string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase);
    }
}
