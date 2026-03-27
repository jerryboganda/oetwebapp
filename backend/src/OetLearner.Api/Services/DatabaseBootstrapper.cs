using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services;

public static class DatabaseBootstrapper
{
    public static async Task InitializeAsync(
        LearnerDbContext db,
        IWebHostEnvironment environment,
        BootstrapOptions options,
        CancellationToken cancellationToken = default)
    {
        var autoMigrate = options.AutoMigrate ?? environment.IsDevelopment();
        var seedDemoData = options.SeedDemoData ?? environment.IsDevelopment();

        if (db.Database.IsInMemory())
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }
        else if (autoMigrate)
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        // Reference data (professions, subtests, criteria, content) is always seeded
        await SeedData.EnsureReferenceDataAsync(db, cancellationToken);

        // Demo/test data (mock user, goals, settings) only in development or when explicitly enabled
        if (seedDemoData)
        {
            await SeedData.EnsureDemoDataAsync(db, cancellationToken);
        }

        await SeedData.EnsureBootstrapAuthAsync(db, options, cancellationToken);
    }
}
