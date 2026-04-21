using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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
        await EnsureVocabularySchemaCompatibilityAsync(db, cancellationToken);
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

    /// <summary>
    /// Copy the AI provider configuration from environment variables
    /// (<c>AI__ApiKey</c>, <c>AI__BaseUrl</c>, <c>AI__DefaultModel</c>,
    /// <c>AI__ProviderId</c>) into the <c>AiProviders</c> row so the
    /// registry-backed provider resolves the key at runtime. Encrypts the
    /// key with the Data Protection key ring before persistence.
    ///
    /// Idempotent: runs on every boot, updates only when the plain-text
    /// env value actually differs from what the stored ciphertext decrypts
    /// to. Call sites outside bootstrap should never touch these rows.
    /// </summary>
    public static async Task SynchroniseAiProviderFromEnvAsync(
        LearnerDbContext db,
        IDataProtectionProvider dpProvider,
        AiProviderOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey)) return;
        var code = string.IsNullOrWhiteSpace(options.ProviderId)
            ? "digitalocean-serverless" : options.ProviderId.Trim().ToLowerInvariant();

        var row = await db.AiProviders.FirstOrDefaultAsync(p => p.Code == code, cancellationToken);
        var protector = dpProvider.CreateProtector("AiProvider.PlatformKey.v1");
        var encrypted = protector.Protect(options.ApiKey);
        var model = string.IsNullOrWhiteSpace(options.DefaultModel)
            ? "anthropic-claude-opus-4.7" : options.DefaultModel;
        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? "https://inference.do-ai.run/v1" : options.BaseUrl;

        var hint = options.ApiKey.Length > 10
            ? options.ApiKey[..4] + "..." + options.ApiKey[^4..]
            : "(short)";

        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            db.AiProviders.Add(new AiProvider
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = code,
                Name = "DigitalOcean Serverless Inference (Claude Opus 4.7)",
                Dialect = AiProviderDialect.OpenAiCompatible,
                BaseUrl = baseUrl,
                EncryptedApiKey = encrypted,
                ApiKeyHint = hint,
                DefaultModel = model,
                ReasoningEffort = string.IsNullOrWhiteSpace(options.ReasoningEffort) ? null : options.ReasoningEffort.Trim().ToLowerInvariant(),
                PricePer1kPromptTokens = 0.015m,
                PricePer1kCompletionTokens = 0.075m,
                RetryCount = 2,
                CircuitBreakerThreshold = 5,
                CircuitBreakerWindowSeconds = 30,
                FailoverPriority = 100,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Existing row: update the volatile fields if they drift.
        var changed = false;
        string? decrypted = null;
        if (!string.IsNullOrEmpty(row.EncryptedApiKey))
        {
            try { decrypted = protector.Unprotect(row.EncryptedApiKey); }
            catch { decrypted = null; }
        }
        if (!string.Equals(decrypted, options.ApiKey, StringComparison.Ordinal))
        {
            row.EncryptedApiKey = encrypted;
            row.ApiKeyHint = hint;
            changed = true;
        }
        if (!string.Equals(row.BaseUrl, baseUrl, StringComparison.OrdinalIgnoreCase))
        {
            row.BaseUrl = baseUrl;
            changed = true;
        }
        if (!string.Equals(row.DefaultModel, model, StringComparison.OrdinalIgnoreCase))
        {
            row.DefaultModel = model;
            changed = true;
        }
        var desiredEffort = string.IsNullOrWhiteSpace(options.ReasoningEffort)
            ? null : options.ReasoningEffort.Trim().ToLowerInvariant();
        if (row.ReasoningEffort is null && desiredEffort is not null)
        {
            // Seed the per-provider column from env on first encounter so
            // admins inherit the configured default. Explicit admin edits
            // (including explicitly empty = "inherit") take priority afterwards.
            row.ReasoningEffort = desiredEffort;
            changed = true;
        }
        if (!row.IsActive) { row.IsActive = true; changed = true; }
        if (changed)
        {
            row.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
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

    private static async Task EnsureVocabularySchemaCompatibilityAsync(LearnerDbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            return;
        }

        var vocabularyTable = GetQualifiedTableName(db.Model.FindEntityType(typeof(VocabularyTerm)));
        var learnerVocabularyTable = GetQualifiedTableName(db.Model.FindEntityType(typeof(LearnerVocabulary)));
        var quizResultTable = GetQualifiedTableName(db.Model.FindEntityType(typeof(VocabularyQuizResult)));
        var providerName = db.Database.ProviderName ?? string.Empty;

        if (db.Database.IsNpgsql())
        {
            if (!string.IsNullOrWhiteSpace(vocabularyTable))
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"""
                    ALTER TABLE IF EXISTS {vocabularyTable}
                        ADD COLUMN IF NOT EXISTS "IpaPronunciation" character varying(64),
                        ADD COLUMN IF NOT EXISTS "AudioMediaAssetId" character varying(64),
                        ADD COLUMN IF NOT EXISTS "SourceProvenance" character varying(512),
                        ADD COLUMN IF NOT EXISTS "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                        ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now();

                    ALTER TABLE IF EXISTS {vocabularyTable}
                        ALTER COLUMN "Category" TYPE character varying(64);

                    UPDATE {vocabularyTable}
                    SET
                        "CreatedAt" = COALESCE("CreatedAt", now()),
                        "UpdatedAt" = COALESCE("UpdatedAt", now());

                    CREATE INDEX IF NOT EXISTS "IX_VocabularyTerms_ProfessionId_Category_Status"
                        ON {vocabularyTable} ("ProfessionId", "Category", "Status");

                    CREATE INDEX IF NOT EXISTS "IX_VocabularyTerms_Term_ExamTypeCode_ProfessionId"
                        ON {vocabularyTable} ("Term", "ExamTypeCode", "ProfessionId");
                    """,
                    cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(learnerVocabularyTable))
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"""ALTER TABLE IF EXISTS {learnerVocabularyTable} ADD COLUMN IF NOT EXISTS "SourceRef" character varying(128);""",
                    cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(quizResultTable))
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"""
                    ALTER TABLE IF EXISTS {quizResultTable}
                        ADD COLUMN IF NOT EXISTS "Format" character varying(32) NOT NULL DEFAULT 'definition_match';

                    UPDATE {quizResultTable}
                    SET "Format" = 'definition_match'
                    WHERE "Format" IS NULL OR "Format" = '';

                    CREATE INDEX IF NOT EXISTS "IX_VocabularyQuizResults_UserId_CompletedAt"
                        ON {quizResultTable} ("UserId", "CompletedAt");
                    """,
                    cancellationToken);
            }

            return;
        }

        if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await AddSqliteColumnIfMissingAsync(db, "VocabularyTerms", @"""IpaPronunciation"" TEXT NULL", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "VocabularyTerms", @"""AudioMediaAssetId"" TEXT NULL", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "VocabularyTerms", @"""SourceProvenance"" TEXT NULL", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "VocabularyTerms", @"""CreatedAt"" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000+00:00'", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "VocabularyTerms", @"""UpdatedAt"" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000+00:00'", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "LearnerVocabularies", @"""SourceRef"" TEXT NULL", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "VocabularyQuizResults", @"""Format"" TEXT NOT NULL DEFAULT 'definition_match'", cancellationToken);

            await db.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_VocabularyTerms_ProfessionId_Category_Status" ON "VocabularyTerms" ("ProfessionId", "Category", "Status");""",
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_VocabularyTerms_Term_ExamTypeCode_ProfessionId" ON "VocabularyTerms" ("Term", "ExamTypeCode", "ProfessionId");""",
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_VocabularyQuizResults_UserId_CompletedAt" ON "VocabularyQuizResults" ("UserId", "CompletedAt");""",
                cancellationToken);
        }
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

    private static string? GetQualifiedTableName(IEntityType? entityType)
    {
        var tableName = entityType?.GetTableName();
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return null;
        }

        return !string.IsNullOrWhiteSpace(entityType?.GetSchema())
            ? $"{QuoteIdentifier(entityType!.GetSchema()!)}.{QuoteIdentifier(tableName)}"
            : QuoteIdentifier(tableName);
    }
}
