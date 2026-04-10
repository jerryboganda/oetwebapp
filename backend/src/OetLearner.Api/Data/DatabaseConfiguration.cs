using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OetLearner.Api.Data;

public static class DatabaseConfiguration
{
    private const string DefaultDevelopmentConnectionString = "Host=localhost;Port=5432;Database=oet_learner_dev;Username=postgres;Password=postgres";

    public static string ResolveConnectionString(IConfiguration configuration, bool isDevelopment)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString.Trim();
        }

        if (isDevelopment)
        {
            return DefaultDevelopmentConnectionString;
        }

        throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured outside the Development environment.");
    }

    public static void ConfigureDbContext(DbContextOptionsBuilder optionsBuilder, string connectionString)
    {
        if (connectionString.StartsWith("InMemory:", StringComparison.OrdinalIgnoreCase))
        {
            optionsBuilder.UseInMemoryDatabase(connectionString["InMemory:".Length..]);
            return;
        }

        if (IsSqliteConnectionString(connectionString))
        {
            optionsBuilder.UseSqlite(connectionString);
            return;
        }

        optionsBuilder.UseNpgsql(connectionString);
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    private static bool IsSqliteConnectionString(string connectionString)
    {
        return connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase)
               || connectionString.StartsWith("Filename=", StringComparison.OrdinalIgnoreCase)
               || connectionString.StartsWith("Mode=", StringComparison.OrdinalIgnoreCase)
               || connectionString.StartsWith("Cache=", StringComparison.OrdinalIgnoreCase);
    }
}