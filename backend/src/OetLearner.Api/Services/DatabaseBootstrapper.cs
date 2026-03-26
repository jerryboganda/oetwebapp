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
        var autoMigrate = options.AutoMigrate ?? !environment.IsDevelopment();
        var seedDemoData = options.SeedDemoData ?? environment.IsDevelopment();

        if (db.Database.IsInMemory())
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }
        else if (autoMigrate)
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        if (seedDemoData)
        {
            await SeedData.EnsureSeededAsync(db, cancellationToken);
        }
    }
}
