using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using OetLearner.Api.Services;

namespace OetLearner.Api.Data;

public static class DatabaseConfiguration
{
    private const string DefaultDevelopmentConnectionString = "Host=localhost;Port=5432;Database=oet_learner_dev;Username=postgres;Password=postgres";

    // Singleton interceptor — stateless, safe to share across DbContext
    // instances. Slice C billing-hardening: physically blocks any UPDATE or
    // DELETE on BillingPlanVersion / BillingAddOnVersion / BillingCouponVersion
    // snapshot rows, regardless of provider (Postgres, Sqlite, InMemory).
    private static readonly BillingCatalogVersionImmutabilityInterceptor BillingCatalogVersionImmutability = new();

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
        optionsBuilder.AddInterceptors(BillingCatalogVersionImmutability);

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

        // UseVector() enables Pgvector.EntityFrameworkCore so EF can map the
        // `vector(n)` column type to Pgvector.Vector and translate
        // L2/cosine/inner-product distance operators into Postgres
        // `<->` / `<=>` / `<#>` operator expressions. Harmless when the DB
        // does not yet have the `vector` extension installed — the connection
        // still works, only queries that touch a Vector column will fail.
        optionsBuilder.UseNpgsql(WithPoolCap(connectionString), npgsql => npgsql.UseVector());
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    // Npgsql's default Maximum Pool Size is 100 PER PROCESS and Postgres' default
    // max_connections is 100 for ALL of them: both API slots (blue/green) and the
    // ai-worker share one database. A single request burst in one process could
    // therefore take every slot and turn every endpoint in every process into
    // `53300: sorry, too many clients already` (seen in production on 30 Sep 2026).
    // Three processes x 25 leaves the database ~20 slots of headroom, and a burst
    // now queues inside its own pool (Npgsql's 15 s acquire timeout) instead.
    // An explicit `Maximum Pool Size` (or alias) in the connection string wins.
    private const int DefaultMaxPoolSize = 25;

    private static string WithPoolCap(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        // True only when the string itself sets the keyword (or its MaxPoolSize alias).
        if (!builder.ShouldSerialize("Maximum Pool Size"))
        {
            builder.MaxPoolSize = DefaultMaxPoolSize;
        }
        return builder.ConnectionString;
    }

    private static bool IsSqliteConnectionString(string connectionString)
    {
        return connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase)
               || connectionString.StartsWith("Filename=", StringComparison.OrdinalIgnoreCase)
               || connectionString.StartsWith("Mode=", StringComparison.OrdinalIgnoreCase)
               || connectionString.StartsWith("Cache=", StringComparison.OrdinalIgnoreCase);
    }
}