using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using OetLearner.Api.Data;
using OetLearner.Api.Data.Migrations;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// W1 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01).
///
/// Covers the new control-plane schema from two independent angles that need
/// no live database, so a regression is caught regardless of which layer
/// drifts:
/// <list type="bullet">
///   <item>enum-exactness — the state machine's members are a public
///   contract for a later wave, checked without any database;</item>
///   <item>EF model configuration — indexes, keys, delete behaviour, and the
///   Npgsql-only xmin token, checked via an in-memory/translation-only
///   context (no live connection);</item>
///   <item>migration operation shape — what
///   <see cref="AddAiControlPlaneCore"/> / <see cref="ExtendAiUsageRecordProvenance"/>
///   actually emit, checked via reflection the same way
///   <c>MigrationIntegrityTests</c> does.</item>
/// </list>
///
/// <para>
/// The real-PostgreSQL-gated coverage (partition-safe index creation,
/// idempotent backfill, uniqueness enforcement) lives in the sibling
/// <c>AiControlPlaneSchemaPostgreSqlTests</c> — split out purely to keep both
/// files under the repo's per-file line cap; there is no behavioural reason
/// for the split.
/// </para>
/// </summary>
public sealed class AiControlPlaneSchemaTests
{
    private const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    // ---------------------------------------------------------------
    // Enum exactness — no database required.
    // ---------------------------------------------------------------

    [Fact]
    public void AiOperationState_HasExactlyTheTenRequiredMembersInOrder()
    {
        Assert.Equal(
            new[]
            {
                "Queued",
                "Leased",
                "ProviderSucceeded",
                "Completed",
                "RetryScheduled",
                "BlockedBudget",
                "Indeterminate",
                "FailedTerminal",
                "SkippedNoEvidence",
                "Cancelled",
            },
            Enum.GetNames<AiOperationState>());
        Assert.Equal(Enumerable.Range(0, 10), Enum.GetValues<AiOperationState>().Select(value => (int)value));
    }

    [Fact]
    public void AiOperationClass_HasExactlyTheThreeRequiredMembers()
    {
        Assert.Equal(
            new[] { "ScoringCritical", "InteractiveLearning", "AdminBatch" },
            Enum.GetNames<AiOperationClass>());
    }

    [Fact]
    public void AiCreditReservationState_HasExactlyTheThreeRequiredMembers()
    {
        Assert.Equal(
            new[] { "Reserved", "Committed", "Released" },
            Enum.GetNames<AiCreditReservationState>());
    }

    // ---------------------------------------------------------------
    // EF model configuration — provider-agnostic parts via InMemory,
    // Npgsql-only parts via a translation-only (never-opened) connection,
    // matching the precedent in
    // EffectiveEntitlementResolverPerformanceTests.LegacyPlanFallback_PostgresTranslationUsesILikeWithoutLower.
    // ---------------------------------------------------------------

    [Fact]
    public void Model_ConfiguresRequiredIndexesKeysAndDeleteBehavior()
    {
        using var db = CreateInMemoryContext();
        var model = db.Model;

        var operation = model.FindEntityType(typeof(AiOperation))!;
        AssertUniqueIndex(operation, "UX_AiOperations_IdempotencyKey", nameof(AiOperation.IdempotencyKey));
        AssertIndex(
            operation,
            "IX_AiOperations_State_NextAttemptAt",
            nameof(AiOperation.State),
            nameof(AiOperation.NextAttemptAt));
        AssertIndex(operation, "IX_AiOperations_LeaseExpiresAt", nameof(AiOperation.LeaseExpiresAt));

        var attempt = model.FindEntityType(typeof(AiOperationAttempt))!;
        var primaryKey = attempt.FindPrimaryKey();
        Assert.NotNull(primaryKey);
        Assert.Equal(
            new[] { nameof(AiOperationAttempt.OperationId), nameof(AiOperationAttempt.AttemptNumber) },
            primaryKey!.Properties.Select(p => p.Name));
        var attemptForeignKey = Assert.Single(attempt.GetForeignKeys());
        Assert.Equal(typeof(AiOperation), attemptForeignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, attemptForeignKey.DeleteBehavior);

        var budget = model.FindEntityType(typeof(AiBudgetPeriod))!;
        AssertUniqueIndex(
            budget,
            "UX_AiBudgetPeriods_Scope_PeriodKey",
            nameof(AiBudgetPeriod.Scope),
            nameof(AiBudgetPeriod.PeriodKey));

        var reservation = model.FindEntityType(typeof(AiCreditReservation))!;
        AssertUniqueIndex(
            reservation,
            "UX_AiCreditReservations_BusinessReference",
            nameof(AiCreditReservation.BusinessReference));
        var reservationForeignKey = Assert.Single(reservation.GetForeignKeys());
        Assert.Equal(typeof(AiOperation), reservationForeignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, reservationForeignKey.DeleteBehavior);

        // No EF-model index on AiUsageRecord(OperationId, AttemptNumber):
        // the only index over those columns
        // (UX_AiUsageRecords_Operation_Attempt) is created conditionally by
        // the ExtendAiUsageRecordProvenance migration's raw SQL — see the
        // migration-shape tests below — and is deliberately kept out of the
        // model to avoid a permanent snapshot/table mismatch. AiUsageRecord
        // still has its other pre-existing indexes ([Index] attributes,
        // AuthAccountId FK) configured elsewhere; this only asserts that
        // OperationId/AttemptNumber specifically are not modelled together.
        var usage = model.FindEntityType(typeof(AiUsageRecord))!;
        Assert.DoesNotContain(
            usage.GetIndexes(),
            index => index.Properties.Select(p => p.Name).SequenceEqual(
                new[] { nameof(AiUsageRecord.OperationId), nameof(AiUsageRecord.AttemptNumber) }));
    }

    [Fact]
    public void Model_DoesNotConfigureXminTokenForNonNpgsqlProviders()
    {
        using var db = CreateInMemoryContext();
        var budget = db.Model.FindEntityType(typeof(AiBudgetPeriod))!;

        Assert.Null(budget.FindProperty("xmin"));
    }

    [Fact]
    public void Model_ConfiguresXminConcurrencyTokenForAiBudgetPeriodUnderNpgsql()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=translation_only;Username=none;Password=none",
                npgsql => npgsql.UseVector())
            .Options;
        using var db = new LearnerDbContext(options);

        var budget = db.Model.FindEntityType(typeof(AiBudgetPeriod))!;
        var xmin = budget.FindProperty("xmin");

        Assert.NotNull(xmin);
        Assert.True(xmin!.IsConcurrencyToken);
        Assert.Equal("xid", xmin.GetColumnType());
    }

    // ---------------------------------------------------------------
    // Migration operation shape — reflection-invoked Up(), same technique as
    // MigrationIntegrityTests. No database required.
    // ---------------------------------------------------------------

    [Fact]
    public void AddAiControlPlaneCore_CreatesExactlyTheFourNewTablesWithRequiredIndexes()
    {
        var operations = ApplyMigrationUp<AddAiControlPlaneCore>();

        Assert.Equal(
            new[] { "AiOperations", "AiOperationAttempts", "AiBudgetPeriods", "AiCreditReservations" },
            operations.OfType<CreateTableOperation>().Select(o => o.Name));

        var indexes = operations.OfType<CreateIndexOperation>().ToList();
        var uniqueIndexNames = indexes.Where(i => i.IsUnique).Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
        var allIndexNames = indexes.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "UX_AiOperations_IdempotencyKey",
                "UX_AiBudgetPeriods_Scope_PeriodKey",
                "UX_AiCreditReservations_BusinessReference",
            },
            uniqueIndexNames);
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "UX_AiOperations_IdempotencyKey",
                "IX_AiOperations_State_NextAttemptAt",
                "IX_AiOperations_LeaseExpiresAt",
                "UX_AiBudgetPeriods_Scope_PeriodKey",
                "UX_AiCreditReservations_BusinessReference",
                "IX_AiCreditReservations_OperationId",
            },
            allIndexNames);

        var attemptsTable = operations.OfType<CreateTableOperation>().Single(o => o.Name == "AiOperationAttempts");
        Assert.Equal(
            new[] { "OperationId", "AttemptNumber" },
            attemptsTable.PrimaryKey!.Columns);
        var attemptsForeignKey = Assert.Single(attemptsTable.ForeignKeys);
        Assert.Equal("AiOperations", attemptsForeignKey.PrincipalTable);
        Assert.Equal(ReferentialAction.Cascade, attemptsForeignKey.OnDelete);

        var reservationsTable = operations.OfType<CreateTableOperation>().Single(o => o.Name == "AiCreditReservations");
        var reservationsForeignKey = Assert.Single(reservationsTable.ForeignKeys);
        Assert.Equal("AiOperations", reservationsForeignKey.PrincipalTable);
        Assert.Equal(ReferentialAction.Restrict, reservationsForeignKey.OnDelete);

        // Strictly additive: no existing table/column touched by this migration.
        Assert.Empty(operations.OfType<DropTableOperation>());
        Assert.Empty(operations.OfType<AddColumnOperation>());
        Assert.Empty(operations.OfType<SqlOperation>());
    }

    [Fact]
    public void AddAiControlPlaneCore_DownDropsAllFourTablesInDependencyOrder()
    {
        var migrationBuilder = new MigrationBuilder(NpgsqlProvider);
        var downMethod = typeof(AddAiControlPlaneCore).GetMethod("Down", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find AddAiControlPlaneCore.Down.");
        downMethod.Invoke(new AddAiControlPlaneCore(), [migrationBuilder]);

        var droppedTables = migrationBuilder.Operations.OfType<DropTableOperation>().Select(o => o.Name).ToList();

        Assert.Equal(4, droppedTables.Count);
        // Child tables (AiOperationAttempts, AiCreditReservations both FK to
        // AiOperations) must drop before their FK target.
        Assert.True(droppedTables.IndexOf("AiOperationAttempts") < droppedTables.IndexOf("AiOperations"));
        Assert.True(droppedTables.IndexOf("AiCreditReservations") < droppedTables.IndexOf("AiOperations"));
    }

    [Fact]
    public void ExtendAiUsageRecordProvenance_AddsExactlyTheFourteenNullableColumnsToAiUsageRecords()
    {
        var operations = ApplyMigrationUp<ExtendAiUsageRecordProvenance>();
        var addedColumns = operations.OfType<AddColumnOperation>().ToList();

        Assert.Equal(
            new[]
            {
                "AiUsageRecords.OperationId",
                "AiUsageRecords.AttemptNumber",
                "AiUsageRecords.ProviderInvoked",
                "AiUsageRecords.ProviderRequestId",
                "AiUsageRecords.ProviderHttpStatus",
                "AiUsageRecords.NormalInputTokens",
                "AiUsageRecords.NormalOutputTokens",
                "AiUsageRecords.CacheWriteTokens",
                "AiUsageRecords.CacheReadTokens",
                "AiUsageRecords.BilledTokenClass",
                "AiUsageRecords.PricingVersion",
                "AiUsageRecords.CalculatedCostUsd",
                "AiUsageRecords.RetryReason",
                "AiUsageRecords.ErrorClass",
            },
            addedColumns.Select(o => $"{o.Table}.{o.Name}"));
        Assert.All(addedColumns, column => Assert.True(column.IsNullable));

        // Strictly additive: no existing column altered/dropped, no table
        // created/dropped.
        Assert.Empty(operations.OfType<AlterColumnOperation>());
        Assert.Empty(operations.OfType<DropColumnOperation>());
        Assert.Empty(operations.OfType<CreateTableOperation>());
        Assert.Empty(operations.OfType<DropTableOperation>());
    }

    [Fact]
    public void ExtendAiUsageRecordProvenance_BackfillIsGatedOnIsNullAndIndexIsGuardedOnRelkind()
    {
        var sqlStatements = ApplyMigrationUp<ExtendAiUsageRecordProvenance>()
            .OfType<SqlOperation>()
            .Select(o => o.Sql)
            .ToList();

        Assert.Equal(3, sqlStatements.Count);

        Assert.Contains("PricingVersion", sqlStatements[0], StringComparison.Ordinal);
        Assert.Contains("'legacy-pre-remediation'", sqlStatements[0], StringComparison.Ordinal);
        Assert.Contains("WHERE \"PricingVersion\" IS NULL", sqlStatements[0], StringComparison.Ordinal);

        Assert.Contains("ProviderInvoked", sqlStatements[1], StringComparison.Ordinal);
        Assert.Contains("\"ProviderId\" IS NOT NULL", sqlStatements[1], StringComparison.Ordinal);
        Assert.Contains("WHERE \"ProviderInvoked\" IS NULL", sqlStatements[1], StringComparison.Ordinal);

        var indexSql = sqlStatements[2];
        Assert.Contains("DO $$", indexSql, StringComparison.Ordinal);
        Assert.Contains("to_regclass(", indexSql, StringComparison.Ordinal);
        Assert.Contains("relkind = 'p'", indexSql, StringComparison.Ordinal);
        Assert.Contains("UX_AiUsageRecords_Operation_Attempt", indexSql, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS", indexSql, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtendAiUsageRecordProvenance_DownRemovesIndexThenColumnsInReverseOrder()
    {
        var migrationBuilder = new MigrationBuilder(NpgsqlProvider);
        var downMethod = typeof(ExtendAiUsageRecordProvenance).GetMethod("Down", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find ExtendAiUsageRecordProvenance.Down.");
        downMethod.Invoke(new ExtendAiUsageRecordProvenance(), [migrationBuilder]);

        var sqlOperations = migrationBuilder.Operations.OfType<SqlOperation>().ToList();
        var droppedColumns = migrationBuilder.Operations.OfType<DropColumnOperation>().Select(o => o.Name).ToList();

        var dropIndexSql = Assert.Single(sqlOperations);
        Assert.Contains("DROP INDEX IF EXISTS", dropIndexSql.Sql, StringComparison.Ordinal);
        Assert.Contains("UX_AiUsageRecords_Operation_Attempt", dropIndexSql.Sql, StringComparison.Ordinal);

        Assert.Equal(14, droppedColumns.Count);
        // The DROP INDEX must run before any column it depends on is dropped.
        Assert.Equal(0, migrationBuilder.Operations.IndexOf(dropIndexSql));
    }

    private static LearnerDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static void AssertIndex(IEntityType entityType, string name, params string[] propertyNames)
    {
        var index = entityType.GetIndexes().SingleOrDefault(i => i.GetDatabaseName() == name);
        Assert.NotNull(index);
        Assert.Equal(propertyNames, index!.Properties.Select(p => p.Name));
    }

    private static void AssertUniqueIndex(IEntityType entityType, string name, params string[] propertyNames)
    {
        var index = entityType.GetIndexes().SingleOrDefault(i => i.GetDatabaseName() == name);
        Assert.NotNull(index);
        Assert.True(index!.IsUnique);
        Assert.Equal(propertyNames, index.Properties.Select(p => p.Name));
    }

    private static IReadOnlyList<MigrationOperation> ApplyMigrationUp<TMigration>()
        where TMigration : Migration, new()
    {
        var builder = new MigrationBuilder(NpgsqlProvider);
        var method = typeof(TMigration).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{typeof(TMigration).Name}.Up was not found.");
        method.Invoke(new TMigration(), [builder]);
        return builder.Operations;
    }
}
