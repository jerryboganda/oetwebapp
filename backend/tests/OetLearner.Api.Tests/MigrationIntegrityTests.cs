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
}
