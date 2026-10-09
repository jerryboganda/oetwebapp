using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Seeding;

namespace OetLearner.Api.Services;

public static class DatabaseBootstrapper
{
    public static async Task InitializeAsync(
        LearnerDbContext db,
        IWebHostEnvironment environment,
        BootstrapOptions options,
        StorageOptions storageOptions,
        OetLearner.Api.Services.Content.IFileStorage storage,
        CancellationToken cancellationToken = default)
    {
        var autoMigrate = options.AutoMigrate ?? environment.IsDevelopment();
        var seedDemoData = options.SeedDemoData ?? environment.IsDevelopment();

        if (options.SkipSchemaChanges)
        {
            return;
        }

        if (db.Database.IsInMemory() || db.Database.IsSqlite())
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }
        else if (environment.IsDevelopment() && !autoMigrate)
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }
        else if (autoMigrate && !environment.IsProduction())
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
        await EnsureAttemptSchemaCompatibilityAsync(db, cancellationToken);
        await EnsureVocabularySchemaCompatibilityAsync(db, cancellationToken);
        await EnsureVoiceDesignSchemaCompatibilityAsync(db, cancellationToken);
        await EnsurePronunciationSchemaCompatibilityAsync(db, cancellationToken);
        await EnsureFreezePolicyAsync(db, cancellationToken);
        await EnsureLiveClassTutorOwnerBackfillAsync(db, cancellationToken);
        await EnsurePrivateSpeakingConfigDefaultsBackfillAsync(db, cancellationToken);
        await OetLearner.Api.Services.Billing.InvoiceEvidenceReconciliationService.ReconcileAsync(db, cancellationToken);

        // Reference data (professions, subtests, criteria, content) is always seeded
        await SeedData.EnsureReferenceDataAsync(db, cancellationToken);

        // Bootstrap rulebook tables from canonical JSON on first run.
        // Idempotent: skipped if RulebookVersions already has rows.
        await OetLearner.Api.Services.Rulebooks.RulebookSeeder.EnsureAsync(db, environment, cancellationToken);

        // Recalls Content Pack v1 — DISABLED: admin manages recalls catalog manually.
        // await OetLearner.Api.Services.Recalls.RecallsContentSeeder.EnsureAsync(
        //     db, environment, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, cancellationToken);

        // Recalls year/source dimension — DISABLED: admin manages recalls catalog manually.
        // await OetLearner.Api.Services.Recalls.RecallSetTagSeeder.EnsureAsync(
        //     db, environment, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, cancellationToken);

        // Demo/test data (mock user, goals, settings) only in development or when explicitly enabled
        if (seedDemoData)
        {
            await SeedData.EnsureDemoDataAsync(db, cancellationToken);
            if (!db.Database.IsInMemory())
            {
                await SeedData.EnsureDemoOperationalStateAsync(db, cancellationToken);
            }
            await SeedData.EnsureDemoMediaAsync(db, storage, cancellationToken);
            // Wave 3 of docs/SPEAKING-MODULE-PLAN.md - seed canonical
            // speaking mock set when both st-001 and st-002 exist.
            await SeedData.EnsureSpeakingMockSetsAsync(db, cancellationToken);
        }
    }

    /// <summary>
    /// Create provider rows from the environment at first boot, so a new deployment has a working
    /// provider without anyone pasting a key by hand.
    ///
    /// <para>
    /// Two independent channels:
    /// <list type="bullet">
    /// <item><c>AI__ApiKey</c> / <c>AI__BaseUrl</c> / <c>AI__DefaultModel</c> / <c>AI__ProviderId</c>
    /// — the original platform-key channel, keyed by <c>AI__ProviderId</c> (default
    /// <c>digitalocean-serverless</c>).</item>
    /// <item><c>ZAI__ApiKey</c> / <c>ZAI__BaseUrl</c> / <c>ZAI__DefaultModel</c> — a dedicated
    /// channel for the <c>z-ai</c> row.</item>
    /// </list>
    ///
    /// <para>
    /// The dedicated channel is not redundancy. <c>AI__ProviderId</c> does double duty: it is both
    /// the row code here AND the DI name of the legacy env-only provider
    /// (<c>OpenAiCompatibleProvider.Name</c>). Because <c>AiGatewayService</c> matches
    /// <c>p.Name == request.Provider</c> BEFORE consulting the registry, setting
    /// <c>AI__ProviderId=z-ai</c> would create a shadow provider that reads the env key and the
    /// runtime-settings base URL — never the registry's encrypted key. Separate variables keep the
    /// two roles from colliding.
    /// </para>
    ///
    /// <para>
    /// <b>Create-only.</b> An existing row is NEVER rewritten from the environment (owner directive
    /// 2026-10-09): once the row exists, the dashboard owns the key, the models and the on/off
    /// switch. When env is present but the row already exists this now LOGS that env is being
    /// ignored — previously it returned in silence, which is how an env edit becomes a mystery.
    /// </para>
    /// </summary>
    public static async Task SynchroniseAiProviderFromEnvAsync(
        LearnerDbContext db,
        IDataProtectionProvider dpProvider,
        AiProviderOptions options,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await SynchroniseOneAsync(
            db, dpProvider, logger,
            providerCode: string.IsNullOrWhiteSpace(options.ProviderId)
                ? AiProviderEnvSeedDefaults.DigitalOceanCode
                : options.ProviderId.Trim(),
            apiKey: options.ApiKey,
            baseUrl: options.BaseUrl,
            defaultModel: options.DefaultModel,
            reasoningEffort: options.ReasoningEffort,
            envPrefix: "AI__",
            cancellationToken);

        // The Z.AI channel. Read directly rather than through options because it is a distinct
        // vendor with its own balance, not a second name for the platform key.
        await SynchroniseOneAsync(
            db, dpProvider, logger,
            providerCode: ZaiProviderDefaults.ProviderCode,
            apiKey: Environment.GetEnvironmentVariable("ZAI__ApiKey"),
            baseUrl: Environment.GetEnvironmentVariable("ZAI__BaseUrl"),
            defaultModel: Environment.GetEnvironmentVariable("ZAI__DefaultModel"),
            reasoningEffort: ZaiProviderDefaults.DefaultReasoningEffort,
            envPrefix: "ZAI__",
            cancellationToken);

        await ReconcileSeedRowAsync(db, logger, cancellationToken);
    }

    /// <summary>
    /// Align the NON-SECRET identity fields of a known seeded row with the code that defines them.
    ///
    /// <para>
    /// The seeder is insert-only, which is right for everything that is an owner decision: the
    /// <b>key</b>, <b>IsActive</b> and <b>ParticipatesInAutoSelection</b> are never touched. But three
    /// fields are code-owned by construction and drift silently when they change:
    /// </para>
    /// <list type="bullet">
    /// <item><b>BaseUrl</b> — an endpoint change is a code change. This is not hypothetical: Z.AI moved
    /// from the pay-as-you-go endpoint to the Coding Plan endpoint, and a row left on the old URL
    /// fails with <c>429 / 1113 "Insufficient balance"</c> on every call, which reads exactly like an
    /// empty wallet rather than a stale URL.</item>
    /// <item><b>AllowedModelsCsv</b> — must match what the plan can actually call.</item>
    /// <item><b>Price columns</b> — must match the billing model (zero for a subscription row).</item>
    /// </list>
    ///
    /// <para>
    /// Reconciling these means the next deploy fixes a stale row with no manual database edit and no
    /// hand-written SQL, and the change is logged rather than silent. Anything the owner owns is left
    /// exactly as they set it.
    /// </para>
    /// </summary>
    private static async Task ReconcileSeedRowAsync(
        LearnerDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        foreach (var seed in AiProviderEnvSeedDefaults.KnownCodes.Select(AiProviderEnvSeedDefaults.For).OfType<ProviderEnvSeed>())
        {
            var row = await db.AiProviders.FirstOrDefaultAsync(p => p.Code == seed.Code, cancellationToken);
            if (row is null) continue;

            // Z.AI owns its own base URL, model list and billing model, because those encode the
            // Coding Plan's endpoint contract. Any other seeded row uses its seed values verbatim.
            var isZai = string.Equals(seed.Code, ZaiProviderDefaults.ProviderCode, StringComparison.OrdinalIgnoreCase);

            var changes = new List<string>();
            var wantBaseUrl = isZai ? ZaiProviderDefaults.BaseUrl : seed.DefaultBaseUrl;
            var wantModels = isZai ? ZaiProviderDefaults.AllowedModelsCsv : null;
            var wantModel = isZai ? ZaiProviderDefaults.DefaultModel : seed.DefaultModel;
            var rates = isZai ? ZaiProviderDefaults.RatesFor(wantModel) : seed.RateResolver(wantModel);

            if (!string.Equals(row.BaseUrl, wantBaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add($"BaseUrl {row.BaseUrl} -> {wantBaseUrl}");
                row.BaseUrl = wantBaseUrl;
            }
            if (wantModels is not null
                && !string.Equals(row.AllowedModelsCsv, wantModels, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add($"AllowedModelsCsv '{row.AllowedModelsCsv}' -> '{wantModels}'");
                row.AllowedModelsCsv = wantModels;
            }
            if (!string.Equals(row.DefaultModel, wantModel, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add($"DefaultModel {row.DefaultModel} -> {wantModel}");
                row.DefaultModel = wantModel;
            }
            if (row.PricePer1kPromptTokens != rates.PromptPer1k
                || row.PricePer1kCompletionTokens != rates.CompletionPer1k)
            {
                changes.Add($"prices {row.PricePer1kPromptTokens}/{row.PricePer1kCompletionTokens} -> {rates.PromptPer1k}/{rates.CompletionPer1k} per 1k");
                row.PricePer1kPromptTokens = rates.PromptPer1k;
                row.PricePer1kCompletionTokens = rates.CompletionPer1k;
            }

            if (changes.Count == 0) continue;

            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning(
                "Reconciled code-owned fields on provider '{Code}': {Changes}. The API key, active flag and "
                + "auto-selection flag were NOT touched — those stay owner-owned.",
                seed.Code, string.Join("; ", changes));
        }
    }

    private static async Task SynchroniseOneAsync(
        LearnerDbContext db,
        IDataProtectionProvider dpProvider,
        ILogger logger,
        string providerCode,
        string? apiKey,
        string? baseUrl,
        string? defaultModel,
        string? reasoningEffort,
        string envPrefix,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // Previously a silent return: a blank key created nothing and logged nothing, so
            // "why is there no provider row" had no answer anywhere in the logs.
            if (AiProviderEnvSeedDefaults.IsKnown(providerCode))
            {
                logger.LogWarning(
                    "{Env}ApiKey is not set; no '{Code}' provider row will be created from the environment. "
                    + "The provider must be added from /admin/ai-providers instead.",
                    envPrefix, providerCode);
            }
            return;
        }

        var code = providerCode.Trim().ToLowerInvariant();
        var row = await db.AiProviders.FirstOrDefaultAsync(p => p.Code == code, cancellationToken);

        var protector = dpProvider.CreateProtector("AiProvider.PlatformKey.v1");
        var encrypted = protector.Protect(apiKey);

        if (row is not null)
        {
            // Owner directive 2026-10-09: an existing row is NEVER rewritten from the environment.
            // Say so, or the next person to edit the env file concludes it took effect.
            logger.LogInformation(
                "'{Code}' already exists; {Env}ApiKey is being IGNORED. The stored (encrypted) key, "
                + "model and active flag are managed from the admin AI Providers screen.",
                code, envPrefix);
            return;
        }

        // Identity and pricing come from the per-code seed table so a Z.AI row is never labelled
        // DigitalOcean and is never costed at the old 100x-wrong rates.
        var seed = AiProviderEnvSeedDefaults.For(code);
        var name = seed?.DisplayName ?? code;
        var resolvedBaseUrl = !string.IsNullOrWhiteSpace(baseUrl) ? baseUrl.Trim() : seed?.DefaultBaseUrl ?? "";
        var model = !string.IsNullOrWhiteSpace(defaultModel) ? defaultModel.Trim() : seed?.DefaultModel ?? "";
        var rates = seed?.RateResolver(model) ?? (0m, 0m);

        var hint = apiKey.Length > 10
            ? apiKey[..4] + "..." + apiKey[^4..]
            : "(short)";

        var now = DateTimeOffset.UtcNow;
        var created = new AiProvider
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = code,
            Name = name,
            Dialect = AiProviderDialect.OpenAiCompatible,
            BaseUrl = resolvedBaseUrl,
            EncryptedApiKey = encrypted,
            ApiKeyHint = hint,
            DefaultModel = model,
            ReasoningEffort = string.IsNullOrWhiteSpace(reasoningEffort) ? null : reasoningEffort.Trim().ToLowerInvariant(),
            PricePer1kPromptTokens = rates.PromptPer1k,
            PricePer1kCompletionTokens = rates.CompletionPer1k,
            RetryCount = 2,
            CircuitBreakerThreshold = 5,
            CircuitBreakerWindowSeconds = 30,
            FailoverPriority = seed?.FailoverPriority ?? 100,
            IsActive = true,
            // Registered but NOT auto-selectable. A row created by the environment must never
            // become the implicit "first active credentialed row" for every feature nobody routed;
            // the owner opts it in from /admin/ai-providers.
            ParticipatesInAutoSelection = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (seed is null)
        {
            // An unknown code means we would be inventing a display name and pricing. Say so rather
            // than writing a row that looks official and costs nothing.
            logger.LogWarning(
                "Provider code '{Code}' is not a known env seed; the row was created with the code as its "
                + "name and ZERO prices. Set its name, base URL and rates from /admin/ai-providers, or add it "
                + "to AiProviderEnvSeedDefaults.",
                code);
        }

        db.AiProviders.Add(created);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Created provider row '{Code}' ({Name}) from the environment: base {BaseUrl}, model {Model}, "
            + "auto-selection OFF. Verify it from /admin/ai-providers, then opt it in if you want it considered "
            + "for unrouted features.",
            code, name, resolvedBaseUrl, model);
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

    private static async Task EnsureAttemptSchemaCompatibilityAsync(LearnerDbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            return;
        }

        var attemptTable = GetQualifiedTableName(db.Model.FindEntityType(typeof(Attempt)));
        if (string.IsNullOrWhiteSpace(attemptTable))
        {
            return;
        }

        var providerName = db.Database.ProviderName ?? string.Empty;
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlRawAsync(
                $"""
                ALTER TABLE IF EXISTS {attemptTable}
                    ADD COLUMN IF NOT EXISTS "CreatedAt" timestamp with time zone NOT NULL DEFAULT now();

                ALTER TABLE IF EXISTS {attemptTable}
                    ADD COLUMN IF NOT EXISTS "ModelVersionId" character varying(64);

                UPDATE {attemptTable}
                SET "CreatedAt" = COALESCE("CreatedAt", "StartedAt", now());
                """,
                cancellationToken);

            return;
        }

        if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await AddSqliteColumnIfMissingAsync(
                db,
                "Attempts",
                @"""CreatedAt"" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000+00:00'",
                cancellationToken);

            await AddSqliteColumnIfMissingAsync(
                db,
                "Attempts",
                @"""ModelVersionId"" TEXT NULL",
                cancellationToken);

            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE "Attempts"
                SET "CreatedAt" = CASE
                    WHEN "CreatedAt" IS NULL OR "CreatedAt" = '' OR "CreatedAt" = '0001-01-01T00:00:00.0000000+00:00'
                    THEN COALESCE("StartedAt", '0001-01-01T00:00:00.0000000+00:00')
                    ELSE "CreatedAt"
                END;
                """,
                cancellationToken);
        }
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

    private static async Task EnsureVoiceDesignSchemaCompatibilityAsync(LearnerDbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational()) return;

        const string DefaultElevenLabsVoiceId = "auq43ws1oslv0tO4BDa7";
        const string LegacyElevenLabsVoiceId = "21m00Tcm4TlvDq8ikWAM";

        var providerName = db.Database.ProviderName ?? string.Empty;
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE IF EXISTS "AudioRegenerationBatches" ADD COLUMN IF NOT EXISTS "ProviderName" character varying(64) NOT NULL DEFAULT 'elevenlabs';
                ALTER TABLE IF EXISTS "ConversationSettings" ADD COLUMN IF NOT EXISTS "ElevenLabsOutputFormat" character varying(64);
                ALTER TABLE IF EXISTS "ConversationSettings" ADD COLUMN IF NOT EXISTS "ElevenLabsPronunciationDictionaryId" character varying(128);
                ALTER TABLE IF EXISTS "ConversationSettings" ADD COLUMN IF NOT EXISTS "ElevenLabsPronunciationDictionaryVersionId" character varying(128);
                ALTER TABLE IF EXISTS "ConversationSettings" ADD COLUMN IF NOT EXISTS "ElevenLabsStability" double precision;
                ALTER TABLE IF EXISTS "ConversationSettings" ADD COLUMN IF NOT EXISTS "ElevenLabsSimilarityBoost" double precision;
                ALTER TABLE IF EXISTS "ConversationSettings" ADD COLUMN IF NOT EXISTS "ElevenLabsStyle" double precision;
                ALTER TABLE IF EXISTS "ConversationSettings" ADD COLUMN IF NOT EXISTS "ElevenLabsUseSpeakerBoost" boolean;
                """,
                cancellationToken);

                        await db.Database.ExecuteSqlInterpolatedAsync($"""
                                UPDATE "ConversationSettings"
                                SET "ElevenLabsDefaultVoiceId" = {DefaultElevenLabsVoiceId}
                                WHERE "Id" = 'default'
                                    AND (
                                        "ElevenLabsDefaultVoiceId" IS NULL
                                        OR btrim("ElevenLabsDefaultVoiceId") = ''
                                        OR "ElevenLabsDefaultVoiceId" = {LegacyElevenLabsVoiceId}
                                    );
                                """,
                                cancellationToken);
            return;
        }

        if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await AddSqliteColumnIfMissingAsync(db, "AudioRegenerationBatches", @"""ProviderName"" TEXT NOT NULL DEFAULT 'elevenlabs'", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "ConversationSettings", @"""ElevenLabsOutputFormat"" TEXT NULL", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "ConversationSettings", @"""ElevenLabsPronunciationDictionaryId"" TEXT NULL", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "ConversationSettings", @"""ElevenLabsPronunciationDictionaryVersionId"" TEXT NULL", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "ConversationSettings", @"""ElevenLabsStability"" REAL NULL", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "ConversationSettings", @"""ElevenLabsSimilarityBoost"" REAL NULL", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "ConversationSettings", @"""ElevenLabsStyle"" REAL NULL", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "ConversationSettings", @"""ElevenLabsUseSpeakerBoost"" INTEGER NULL", cancellationToken);

                        await db.Database.ExecuteSqlInterpolatedAsync($"""
                                UPDATE "ConversationSettings"
                                SET "ElevenLabsDefaultVoiceId" = {DefaultElevenLabsVoiceId}
                                WHERE "Id" = 'default'
                                    AND (
                                        "ElevenLabsDefaultVoiceId" IS NULL
                                        OR trim("ElevenLabsDefaultVoiceId") = ''
                                        OR "ElevenLabsDefaultVoiceId" = {LegacyElevenLabsVoiceId}
                                    );
                                """,
                                cancellationToken);
        }
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
                        ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                        ADD COLUMN IF NOT EXISTS "CommonMistakesJson" text NOT NULL DEFAULT '[]',
                        ADD COLUMN IF NOT EXISTS "SimilarSoundingJson" text NOT NULL DEFAULT '[]';

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
            await AddSqliteColumnIfMissingAsync(db, "VocabularyTerms", @"""CommonMistakesJson"" TEXT NOT NULL DEFAULT '[]'", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "VocabularyTerms", @"""SimilarSoundingJson"" TEXT NOT NULL DEFAULT '[]'", cancellationToken);
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

    /// <summary>
    /// Backfill <c>LiveClass.TutorProfileId</c> for tutor-created classes saved
    /// with a null owner link (the legacy tutor create passed
    /// <c>TutorProfileId: null</c>). The owner is recovered from the
    /// <c>LiveClassCreated</c> audit event's actor, mapped to their
    /// <c>PrivateSpeakingTutorProfile</c>. Without this, the tutor-portal
    /// ownership guards would 403 a tutor from their own pre-existing classes.
    /// Idempotent — only touches rows whose <c>TutorProfileId</c> is still null.
    /// Postgres-only; test databases create owned classes directly.
    /// </summary>
    private static async Task EnsureLiveClassTutorOwnerBackfillAsync(LearnerDbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsNpgsql())
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "LiveClasses" lc
            SET "TutorProfileId" = creator.profile_id,
                "TutorDisplayName" = COALESCE(lc."TutorDisplayName", creator.display_name)
            FROM (
                SELECT DISTINCT ON (ae."ResourceId")
                    ae."ResourceId" AS class_id,
                    tp."Id" AS profile_id,
                    tp."DisplayName" AS display_name
                FROM "AuditEvents" ae
                JOIN "PrivateSpeakingTutorProfiles" tp ON tp."ExpertUserId" = ae."ActorId"
                WHERE ae."Action" = 'LiveClassCreated' AND ae."ResourceType" = 'LiveClass'
                ORDER BY ae."ResourceId", ae."OccurredAt" ASC
            ) creator
            WHERE lc."Id" = creator.class_id
              AND lc."TutorProfileId" IS NULL;
            """,
            cancellationToken);
    }

    /// <summary>
    /// Backfill PDF-policy defaults onto the <c>PrivateSpeakingConfig</c> singleton
    /// for a row created before the reschedule-tier columns existed. Migration
    /// <c>20260606213420</c> adds <c>RescheduleFreeWindowHours</c> /
    /// <c>RescheduleSameDayPenaltyPercent</c> with a 0 default, so a pre-existing
    /// config row reads 0/0. Heal it to the
    /// PDF policy (strict 24h refund boundary and calendar-only rescheduling) and seed the
    /// candidate-facing policy texts where missing. Idempotent — only touches the
    /// 0/0 sentinel and empty policy texts. Postgres-only; fresh/test databases get
    /// the entity defaults from <c>PrivateSpeakingService.GetConfigAsync</c>.
    /// </summary>
    private static async Task EnsurePrivateSpeakingConfigDefaultsBackfillAsync(LearnerDbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsNpgsql())
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "PrivateSpeakingConfigs"
            SET "CancellationWindowHours" = 24,
                "RescheduleFreeWindowHours" = 24,
                "RescheduleSameDayPenaltyPercent" = 0,
                "CancellationPolicyText" = 'You may cancel your Speaking session with a full refund if the cancellation is made more than 24 hours before the scheduled start time. If you cancel 24 hours or less before the session, a full refund is not available.',
                "BookingPolicyText" = 'You may reschedule your Speaking session any time before it starts, subject to an alternative slot currently available in the tutor calendar.';
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "PrivateSpeakingConfigs"
            SET "CancellationWindowHours" = 24,
                "CancellationPolicyText" = 'You may cancel your Speaking session with a full refund if the cancellation is made more than 24 hours before the scheduled start time. If you cancel 24 hours or less before the session, a full refund is not available.';
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "PrivateSpeakingConfigs"
            SET "RescheduleSameDayPenaltyPercent" = 0,
                "BookingPolicyText" = 'You may reschedule your Speaking session any time before it starts, subject to an alternative slot currently available in the tutor calendar.';
            """,
            cancellationToken);
    }

    private static string QuoteIdentifier(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

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
