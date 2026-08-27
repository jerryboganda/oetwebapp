using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using OetLearner.Api.Data.Migrations;

namespace OetLearner.Api.Tests;

public class MigrationIntegrityTests
{
    [Fact]
    public void WritingOptionsSettings_DoesNotRecreateSchemaOwnedByNeighborMigrations()
    {
        var migrationBuilder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var upMethod = typeof(WritingOptionsSettings).GetMethod(
            "Up",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find WritingOptionsSettings.Up.");

        upMethod.Invoke(new WritingOptionsSettings(), [migrationBuilder]);

        var createdTables = migrationBuilder.Operations
            .OfType<CreateTableOperation>()
            .Select(operation => operation.Name)
            .ToHashSet(StringComparer.Ordinal);
        var addedColumns = migrationBuilder.Operations
            .OfType<AddColumnOperation>()
            .Select(operation => $"{operation.Table}.{operation.Name}")
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("WritingOptions", createdTables);
        Assert.Contains("ExpertCompensationRates", createdTables);

        foreach (var duplicateTable in new[]
        {
            "AiFeatureRoutes",
            "AiFeatureToolGrants",
            "AiProviderAccounts",
            "AiToolInvocations",
            "AiTools",
            "RecallBookmarks",
            "UserNotes",
        })
        {
            Assert.DoesNotContain(duplicateTable, createdTables);
        }

        foreach (var duplicateColumn in new[]
        {
            "AiUsageRecords.AccountId",
            "AiUsageRecords.FailoverTrace",
            "AiProviders.LastTestError",
            "AiProviders.LastTestStatus",
            "AiProviders.LastTestedAt",
        })
        {
            Assert.DoesNotContain(duplicateColumn, addedColumns);
        }
    }

    /// <summary>
    /// W0 (incident INC-2026-CLAUDE-01). The Listening skip/retry migration must
    /// stay strictly additive: four new nullable-or-defaulted columns on
    /// <c>ListeningAnswers</c>, no table creation, no drops, and — critically —
    /// no touch of the deterministic mark (<c>IsCorrect</c> / <c>PointsEarned</c>).
    /// </summary>
    [Fact]
    public void AddListeningAnswerAiSkipAndRetry_IsAdditiveAndLeavesDeterministicColumnsAlone()
    {
        var migrationBuilder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var upMethod = typeof(AddListeningAnswerAiSkipAndRetry).GetMethod(
            "Up",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find AddListeningAnswerAiSkipAndRetry.Up.");

        upMethod.Invoke(new AddListeningAnswerAiSkipAndRetry(), [migrationBuilder]);

        var addedColumns = migrationBuilder.Operations
            .OfType<AddColumnOperation>()
            .Select(operation => $"{operation.Table}.{operation.Name}")
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "ListeningAnswers.AiSkipReason",
                "ListeningAnswers.AiAttemptCount",
                "ListeningAnswers.AiNextAttemptAt",
                "ListeningAnswers.AiIncidentId",
            },
            addedColumns);

        var attemptCount = migrationBuilder.Operations
            .OfType<AddColumnOperation>()
            .Single(operation => operation.Name == "AiAttemptCount");
        Assert.False(attemptCount.IsNullable);
        Assert.Equal(0, attemptCount.DefaultValue);

        Assert.Empty(migrationBuilder.Operations.OfType<CreateTableOperation>());
        Assert.Empty(migrationBuilder.Operations.OfType<DropTableOperation>());
        Assert.Empty(migrationBuilder.Operations.OfType<DropColumnOperation>());
        Assert.Empty(migrationBuilder.Operations.OfType<AlterColumnOperation>());
        Assert.Empty(migrationBuilder.Operations.OfType<SqlOperation>());
    }
}
