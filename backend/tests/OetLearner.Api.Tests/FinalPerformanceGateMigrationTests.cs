using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using OetLearner.Api.Data;
using OetLearner.Api.Data.Migrations;
using OetLearner.Api.Domain;
using OetLearner.Api.Domain.Billing;

namespace OetLearner.Api.Tests;

public sealed class FinalPerformanceGateMigrationTests
{
    private const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";

    [Theory]
    [InlineData(NpgsqlProvider)]
    [InlineData(SqliteProvider)]
    public void HostedCheckoutUrl_IsNullableExpandOnlyColumn_ForEveryProvider(string provider)
    {
        var up = Apply<PersistHostedCheckoutUrl>(provider, "Up");
        var column = Assert.Single(up.Operations.OfType<AddColumnOperation>());

        Assert.Equal("CheckoutSessions", column.Table);
        Assert.Equal("HostedCheckoutUrl", column.Name);
        Assert.True(column.IsNullable);
        Assert.Equal(2048, column.MaxLength);
        Assert.Empty(up.Operations.Where(operation => operation is not AddColumnOperation));

        var down = Apply<PersistHostedCheckoutUrl>(provider, "Down");
        var dropped = Assert.Single(down.Operations.OfType<DropColumnOperation>());
        Assert.Equal("CheckoutSessions", dropped.Table);
        Assert.Equal("HostedCheckoutUrl", dropped.Name);
    }

    [Fact]
    public void RuntimeModel_MapsHostedUrlAndMeasuredContentOrderingIndexes()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"performance-gate-model-{Guid.NewGuid():N}")
            .Options;
        using var db = new LearnerDbContext(options);

        var checkout = db.Model.FindEntityType(typeof(CheckoutSession))
            ?? throw new InvalidOperationException("CheckoutSession model missing.");
        var hostedUrl = checkout.FindProperty(nameof(CheckoutSession.HostedCheckoutUrl))
            ?? throw new InvalidOperationException("HostedCheckoutUrl mapping missing.");
        Assert.True(hostedUrl.IsNullable);
        Assert.Equal(2048, hostedUrl.GetMaxLength());

        Assert.Equal(4, (int)ContentStatus.Published);
        var content = db.Model.FindEntityType(typeof(ContentItem))
            ?? throw new InvalidOperationException("ContentItem model missing.");

        var browse = Assert.Single(content.GetIndexes().Where(index =>
            index.GetDatabaseName() == "IX_ContentItems_Published_Subtest_Title"));
        Assert.Equal(
            [nameof(ContentItem.SubtestCode), nameof(ContentItem.Title)],
            browse.Properties.Select(property => property.Name));
        Assert.Equal("\"Status\" = 4 AND \"FreshnessConfidence\" <> 'superseded'", browse.GetFilter());

        var provenance = Assert.Single(content.GetIndexes().Where(index =>
            index.GetDatabaseName() == "IX_ContentItems_Published_Provenance_Created"));
        Assert.Equal(
            [nameof(ContentItem.SourceProvenance), nameof(ContentItem.CreatedAt)],
            provenance.Properties.Select(property => property.Name));
        Assert.Equal("\"Status\" = 4", provenance.GetFilter());
    }

    [Fact]
    public void ContentIndexes_PostgresUseConcurrentReentrantSuppressedSql()
    {
        var migration = Apply<AddContentItemBrowseIndexes>(NpgsqlProvider, "Up");
        var sqlOperations = migration.Operations.OfType<SqlOperation>().ToList();
        var sql = string.Join("\n", sqlOperations.Select(operation => operation.Sql));

        Assert.NotEmpty(sqlOperations);
        Assert.All(sqlOperations, operation => Assert.True(operation.SuppressTransaction));
        Assert.Equal(2, Count(sql, "CREATE INDEX CONCURRENTLY IF NOT EXISTS"));
        Assert.Contains(
            "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_ContentItems_Published_Subtest_Title\"",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ON \"ContentItems\" (\"SubtestCode\", \"Title\")",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"Status\" = 4 AND \"FreshnessConfidence\" <> 'superseded'",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_ContentItems_Published_Provenance_Created\"",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ON \"ContentItems\" (\"SourceProvenance\", \"CreatedAt\" DESC)",
            sql,
            StringComparison.Ordinal);

        Assert.Equal(2, Count(sql, "NOT index_state.indisvalid"));
        Assert.Equal(2, Count(sql, "ALTER INDEX %I.%I RENAME TO %I"));
        Assert.True(Count(sql, "DROP INDEX CONCURRENTLY IF EXISTS") >= 4);

        // lower(...) expression indexes cannot serve the unchanged raw ILIKE OR
        // query, so the optional trigram work remains measurement-gated.
        Assert.DoesNotContain("pg_trgm", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gin_trgm_ops", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContentIndexes_NonPostgresProviderHasNoRawOperations()
    {
        Assert.Empty(Apply<AddContentItemBrowseIndexes>(SqliteProvider, "Up").Operations);
        Assert.Empty(Apply<AddContentItemBrowseIndexes>(SqliteProvider, "Down").Operations);
    }

    [Fact]
    public void ContentIndexes_DownIsConcurrentIdempotentAndTransactionSuppressed()
    {
        var migration = Apply<AddContentItemBrowseIndexes>(NpgsqlProvider, "Down");
        var operations = migration.Operations.OfType<SqlOperation>().ToList();
        var sql = string.Join("\n", operations.Select(operation => operation.Sql));

        Assert.Equal(4, operations.Count);
        Assert.All(operations, operation => Assert.True(operation.SuppressTransaction));
        Assert.All(
            operations,
            operation => Assert.Contains(
                "DROP INDEX CONCURRENTLY IF EXISTS",
                operation.Sql,
                StringComparison.Ordinal));
        Assert.Contains("IX_ContentItems_Published_Subtest_Title", sql, StringComparison.Ordinal);
        Assert.Contains("IX_ContentItems_Published_Provenance_Created", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(PersistHostedCheckoutUrl), "20260728090000_PersistHostedCheckoutUrl")]
    [InlineData(typeof(AddContentItemBrowseIndexes), "20260728091000_AddContentItemBrowseIndexes")]
    public void HandwrittenMigrationsAreDiscoverable(Type migrationType, string expectedId)
    {
        Assert.Equal(expectedId, migrationType.GetCustomAttribute<MigrationAttribute>()?.Id);
        Assert.Equal(
            typeof(LearnerDbContext),
            migrationType.GetCustomAttribute<DbContextAttribute>()?.ContextType);
    }

    private static MigrationBuilder Apply<TMigration>(string provider, string methodName)
        where TMigration : Migration, new()
    {
        var builder = new MigrationBuilder(provider);
        var method = typeof(TMigration).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{typeof(TMigration).Name}.{methodName} was not found.");
        method.Invoke(new TMigration(), [builder]);
        return builder;
    }

    private static int Count(string value, string fragment)
        => (value.Length - value.Replace(fragment, string.Empty, StringComparison.Ordinal).Length)
           / fragment.Length;
}
