using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public static class DatabaseBootstrapper
{
    public static async Task InitializeAsync(
        LearnerDbContext db,
        IWebHostEnvironment environment,
        BootstrapOptions options,
        StorageOptions storageOptions,
        CancellationToken cancellationToken = default)
    {
        var autoMigrate = options.AutoMigrate ?? environment.IsDevelopment();
        var seedDemoData = options.SeedDemoData ?? environment.IsDevelopment();

        if (db.Database.IsInMemory())
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }
        else if (environment.IsDevelopment() && !autoMigrate)
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }
        else if (autoMigrate)
        {
            await db.Database.MigrateAsync(cancellationToken);
        }
        else if (db.Database.IsRelational())
        {
            var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(cancellationToken))
                .ToArray();

            if (pendingMigrations.Length > 0)
            {
                var preview = string.Join(", ", pendingMigrations.Take(5));
                var suffix = pendingMigrations.Length > 5 ? ", ..." : string.Empty;
                throw new InvalidOperationException(
                    "Database has pending EF Core migrations. Apply them before starting the API " +
                    "or enable Bootstrap:AutoMigrate=true. Pending migrations: " +
                    $"{preview}{suffix}");
            }
        }

        await EnsureAdminSchemaCompatibilityAsync(db, cancellationToken);
        await EnsureExpertSchemaCompatibilityAsync(db, cancellationToken);

        // Reference data (professions, subtests, criteria, content) is always seeded
        await SeedData.EnsureReferenceDataAsync(db, cancellationToken);

        // Demo/test data (mock user, goals, settings) only in development or when explicitly enabled
        if (seedDemoData)
        {
            await SeedData.EnsureDemoDataAsync(db, cancellationToken);
            if (!db.Database.IsInMemory())
            {
                await SeedData.EnsureDemoOperationalStateAsync(db, cancellationToken);
            }
            await SeedData.EnsureDemoMediaAsync(db, environment, storageOptions, cancellationToken);
        }
    }

#pragma warning disable EF1002 // Identifiers come from EF model metadata and are sanitized with QuoteIdentifier.
    private static async Task EnsureAdminSchemaCompatibilityAsync(LearnerDbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            return;
        }

        var criterionEntity = db.Model.FindEntityType(typeof(CriterionReference));
        var tableName = criterionEntity?.GetTableName();
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return;
        }

        var qualifiedTableName = !string.IsNullOrWhiteSpace(criterionEntity?.GetSchema())
            ? $"{QuoteIdentifier(criterionEntity!.GetSchema()!)}.{QuoteIdentifier(tableName)}"
            : QuoteIdentifier(tableName);

        var providerName = db.Database.ProviderName ?? string.Empty;

        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlRawAsync(
                $"""ALTER TABLE IF EXISTS {qualifiedTableName} ADD COLUMN IF NOT EXISTS "Status" character varying(16) NOT NULL DEFAULT 'active';""",
                cancellationToken);
        }
        else if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"""ALTER TABLE {qualifiedTableName} ADD COLUMN "Status" TEXT NOT NULL DEFAULT 'active';""",
                    cancellationToken);
            }
            catch (Exception ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
                // The compatibility column has already been added.
            }
        }

        await db.Database.ExecuteSqlRawAsync(
            $"""UPDATE {qualifiedTableName} SET "Status" = 'active' WHERE "Status" IS NULL OR "Status" = '';""",
            cancellationToken);
    }

    private static async Task EnsureExpertSchemaCompatibilityAsync(LearnerDbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            return;
        }

        var draftEntity = db.Model.FindEntityType(typeof(ExpertReviewDraft));
        var tableName = draftEntity?.GetTableName();
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return;
        }

        var qualifiedTableName = !string.IsNullOrWhiteSpace(draftEntity?.GetSchema())
            ? $"{QuoteIdentifier(draftEntity!.GetSchema()!)}.{QuoteIdentifier(tableName)}"
            : QuoteIdentifier(tableName);

        var providerName = db.Database.ProviderName ?? string.Empty;

        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlRawAsync(
                $"""ALTER TABLE IF EXISTS {qualifiedTableName} ADD COLUMN IF NOT EXISTS "ScratchpadJson" text NOT NULL DEFAULT '""';""",
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                $"""ALTER TABLE IF EXISTS {qualifiedTableName} ADD COLUMN IF NOT EXISTS "ChecklistItemsJson" text NOT NULL DEFAULT '[]';""",
                cancellationToken);
        }
        else if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"""ALTER TABLE {qualifiedTableName} ADD COLUMN "ScratchpadJson" TEXT NOT NULL DEFAULT '""';""",
                    cancellationToken);
            }
            catch (Exception ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
                // The compatibility column has already been added.
            }

            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"""ALTER TABLE {qualifiedTableName} ADD COLUMN "ChecklistItemsJson" TEXT NOT NULL DEFAULT '[]';""",
                    cancellationToken);
            }
            catch (Exception ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
                // The compatibility column has already been added.
            }
        }

        await db.Database.ExecuteSqlRawAsync(
            $"""UPDATE {qualifiedTableName} SET "ScratchpadJson" = '""' WHERE "ScratchpadJson" IS NULL OR "ScratchpadJson" = '';""",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            $"""UPDATE {qualifiedTableName} SET "ChecklistItemsJson" = '[]' WHERE "ChecklistItemsJson" IS NULL OR "ChecklistItemsJson" = '';""",
            cancellationToken);
    }
#pragma warning restore EF1002

    private static string QuoteIdentifier(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
