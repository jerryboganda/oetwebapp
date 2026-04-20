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

        if (db.Database.IsInMemory() || db.Database.IsSqlite())
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
        await EnsurePronunciationSchemaCompatibilityAsync(db, cancellationToken);
        await EnsureFreezePolicyAsync(db, cancellationToken);

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

    private static async Task EnsurePronunciationSchemaCompatibilityAsync(LearnerDbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            return;
        }

        var providerName = db.Database.ProviderName ?? string.Empty;
        if (!providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await AddSqliteColumnIfMissingAsync(db, "PronunciationDrills", @"""Profession"" TEXT NOT NULL DEFAULT 'all'", cancellationToken);
        await AddSqliteColumnIfMissingAsync(db, "PronunciationDrills", @"""Focus"" TEXT NOT NULL DEFAULT 'phoneme'", cancellationToken);
        await AddSqliteColumnIfMissingAsync(db, "PronunciationDrills", @"""PrimaryRuleId"" TEXT NULL", cancellationToken);
        await AddSqliteColumnIfMissingAsync(db, "PronunciationDrills", @"""AudioModelAssetId"" TEXT NULL", cancellationToken);
        await AddSqliteColumnIfMissingAsync(db, "PronunciationDrills", @"""OrderIndex"" INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddSqliteColumnIfMissingAsync(db, "PronunciationDrills", @"""CreatedAt"" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000+00:00'", cancellationToken);
        await AddSqliteColumnIfMissingAsync(db, "PronunciationDrills", @"""UpdatedAt"" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000+00:00'", cancellationToken);

        await AddSqliteColumnIfMissingAsync(db, "PronunciationAssessments", @"""DrillId"" TEXT NULL", cancellationToken);
        await AddSqliteColumnIfMissingAsync(db, "PronunciationAssessments", @"""ProjectedSpeakingScaled"" INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddSqliteColumnIfMissingAsync(db, "PronunciationAssessments", @"""ProjectedSpeakingGrade"" TEXT NOT NULL DEFAULT 'B'", cancellationToken);
        await AddSqliteColumnIfMissingAsync(db, "PronunciationAssessments", @"""Provider"" TEXT NOT NULL DEFAULT 'mock'", cancellationToken);
        await AddSqliteColumnIfMissingAsync(db, "PronunciationAssessments", @"""RulebookVersion"" TEXT NOT NULL DEFAULT '1.0.0'", cancellationToken);
        await AddSqliteColumnIfMissingAsync(db, "PronunciationAssessments", @"""FindingsJson"" TEXT NOT NULL DEFAULT '[]'", cancellationToken);
        await AddSqliteColumnIfMissingAsync(db, "PronunciationAssessments", @"""FeedbackJson"" TEXT NOT NULL DEFAULT '{}'", cancellationToken);

        await AddSqliteColumnIfMissingAsync(db, "LearnerPronunciationProgress", @"""NextDueAt"" TEXT NULL", cancellationToken);
        await AddSqliteColumnIfMissingAsync(db, "LearnerPronunciationProgress", @"""IntervalDays"" INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddSqliteColumnIfMissingAsync(db, "LearnerPronunciationProgress", @"""Ease"" REAL NOT NULL DEFAULT 2.5", cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "PronunciationAttempts" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_PronunciationAttempts" PRIMARY KEY,
                "UserId" TEXT NOT NULL,
                "DrillId" TEXT NOT NULL,
                "AudioStorageKey" TEXT NULL,
                "AudioSha256" TEXT NULL,
                "AudioBytes" INTEGER NULL,
                "AudioMimeType" TEXT NULL,
                "AudioDurationMs" INTEGER NULL,
                "Status" TEXT NOT NULL,
                "AssessmentId" TEXT NULL,
                "ErrorCode" TEXT NULL,
                "ErrorMessage" TEXT NULL,
                "Provider" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                "CompletedAt" TEXT NULL,
                "AudioReapAt" TEXT NULL
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "LearnerPronunciationDiscriminationAttempts" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_LearnerPronunciationDiscriminationAttempts" PRIMARY KEY,
                "UserId" TEXT NOT NULL,
                "DrillId" TEXT NOT NULL,
                "TargetPhoneme" TEXT NOT NULL,
                "RoundsTotal" INTEGER NOT NULL,
                "RoundsCorrect" INTEGER NOT NULL,
                "CreatedAt" TEXT NOT NULL
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_PronunciationAssessments_DrillId" ON "PronunciationAssessments" ("DrillId");""",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_PronunciationAssessments_UserId_CreatedAt" ON "PronunciationAssessments" ("UserId", "CreatedAt");""",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_LearnerPronunciationProgress_UserId_AverageScore" ON "LearnerPronunciationProgress" ("UserId", "AverageScore");""",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """DROP INDEX IF EXISTS "IX_LearnerPronunciationProgress_UserId_PhonemeCode";""",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_LearnerPronunciationProgress_UserId_PhonemeCode" ON "LearnerPronunciationProgress" ("UserId", "PhonemeCode");""",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_PronunciationAttempts_UserId_CreatedAt" ON "PronunciationAttempts" ("UserId", "CreatedAt");""",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_PronunciationAttempts_DrillId_CreatedAt" ON "PronunciationAttempts" ("DrillId", "CreatedAt");""",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_PronunciationAttempts_UserId_DrillId_CreatedAt" ON "PronunciationAttempts" ("UserId", "DrillId", "CreatedAt");""",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_PronunciationAttempts_Status" ON "PronunciationAttempts" ("Status");""",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_LearnerPronunciationDiscriminationAttempts_UserId_CreatedAt" ON "LearnerPronunciationDiscriminationAttempts" ("UserId", "CreatedAt");""",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "PronunciationDrills"
            SET
                "Profession" = COALESCE(NULLIF("Profession", ''), 'all'),
                "Focus" = COALESCE(NULLIF("Focus", ''), 'phoneme'),
                "CreatedAt" = CASE WHEN "CreatedAt" IS NULL OR "CreatedAt" = '' THEN '0001-01-01T00:00:00.0000000+00:00' ELSE "CreatedAt" END,
                "UpdatedAt" = CASE WHEN "UpdatedAt" IS NULL OR "UpdatedAt" = '' THEN '0001-01-01T00:00:00.0000000+00:00' ELSE "UpdatedAt" END;
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "PronunciationAssessments"
            SET
                "ProjectedSpeakingGrade" = COALESCE(NULLIF("ProjectedSpeakingGrade", ''), 'B'),
                "Provider" = COALESCE(NULLIF("Provider", ''), 'mock'),
                "RulebookVersion" = COALESCE(NULLIF("RulebookVersion", ''), '1.0.0'),
                "FindingsJson" = COALESCE(NULLIF("FindingsJson", ''), '[]'),
                "FeedbackJson" = COALESCE(NULLIF("FeedbackJson", ''), '{{}}');
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """UPDATE "LearnerPronunciationProgress" SET "Ease" = 2.5 WHERE "Ease" IS NULL OR "Ease" <= 0;""",
            cancellationToken);
    }

    private static async Task AddSqliteColumnIfMissingAsync(
        LearnerDbContext db,
        string tableName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        try
        {
            var sql = $"ALTER TABLE {QuoteIdentifier(tableName)} ADD COLUMN {columnDefinition};";
            // ExecuteSqlRawAsync still runs string.Format-style placeholder parsing.
            // Escape braces in the FINAL SQL so JSON defaults like '{}' do not get
            // misread as composite-format placeholders.
            sql = sql
                .Replace("{", "{{", StringComparison.Ordinal)
                .Replace("}", "}}", StringComparison.Ordinal);
            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
        catch (Exception ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
            // The compatibility column has already been added.
        }
    }
#pragma warning restore EF1002

    private static async Task EnsureFreezePolicyAsync(LearnerDbContext db, CancellationToken cancellationToken)
    {
        if (await db.AccountFreezePolicies.AnyAsync(cancellationToken))
        {
            return;
        }

        db.AccountFreezePolicies.Add(new AccountFreezePolicy
        {
            Id = "freeze-policy-default",
            IsEnabled = true,
            SelfServiceEnabled = true,
            ApprovalMode = FreezeApprovalMode.AutoApprove,
            MinDurationDays = 1,
            MaxDurationDays = 365,
            AllowScheduling = true,
            AccessMode = FreezeAccessMode.ReadOnly,
            EntitlementPauseMode = FreezeEntitlementPauseMode.InternalClock,
            RequireReason = true,
            RequireInternalNotes = false,
            AllowActivePaid = true,
            AllowGracePeriod = true,
            AllowTrial = false,
            AllowComplimentary = false,
            AllowCancelled = false,
            AllowExpired = false,
            AllowReviewOnly = false,
            AllowPastDue = false,
            AllowSuspended = false,
            PolicyNotes = "Default internal freeze policy seeded at startup.",
            EligibilityReasonCodesJson = "[\"active_paid\",\"grace_period\"]",
            UpdatedByAdminId = null,
            UpdatedByAdminName = null,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string QuoteIdentifier(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
