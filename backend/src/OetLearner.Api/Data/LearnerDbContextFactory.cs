using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OetLearner.Api.Data;

public sealed class LearnerDbContextFactory : IDesignTimeDbContextFactory<LearnerDbContext>
{
    // Design-time migrations (dotnet ef migrations add/remove/script) MUST be
    // scaffolded against PostgreSQL because that is the only production
    // database provider. If a dev machine has a SQLite fallback in its config,
    // letting the factory pick it produces migrations with SQLite column
    // types (TEXT/INTEGER) that are unusable against the real database.
    //
    // We therefore always bind Npgsql at design time. The connection string
    // does not need to point at a real server — EF only uses the provider to
    // pick column type mappings.
    private const string DesignTimePlaceholder =
        "Host=localhost;Port=5432;Database=oet_design_time;Username=postgres;Password=postgres";

    public LearnerDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var configuredConnection = configuration.GetConnectionString("DefaultConnection");
        var connectionString = !string.IsNullOrWhiteSpace(configuredConnection)
            && !IsSqliteOrInMemory(configuredConnection)
                ? configuredConnection
                : DesignTimePlaceholder;

        var optionsBuilder = new DbContextOptionsBuilder<LearnerDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new LearnerDbContext(optionsBuilder.Options);
    }

    private static bool IsSqliteOrInMemory(string connectionString) =>
        connectionString.StartsWith("InMemory:", StringComparison.OrdinalIgnoreCase)
        || connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase)
        || connectionString.StartsWith("Filename=", StringComparison.OrdinalIgnoreCase);
}
