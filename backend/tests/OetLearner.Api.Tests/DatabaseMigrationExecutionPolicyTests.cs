using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public sealed class DatabaseMigrationExecutionPolicyTests
{
    [Theory]
    [InlineData("Production", true, false, false)]
    [InlineData("Production", true, true, true)]
    [InlineData("Development", true, true, true)]
    [InlineData("Development", true, false, false)]
    [InlineData("Staging", true, true, true)]
    [InlineData("Development", false, true, false)]
    [InlineData("Production", false, true, false)]
    [InlineData("", true, false, false)]
    [InlineData(null, true, false, false)]
    public void StartupMigrationRequiresPostgreSqlAndExplicitOptIn(
        string? environmentName,
        bool isPostgreSql,
        bool autoMigrate,
        bool expected)
    {
        Assert.Equal(expected, DatabaseMigrationExecutionPolicy.ShouldApplyAtStartup(
            environmentName,
            isPostgreSql,
            autoMigrate));
    }
}
