using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public sealed class DatabaseMigrationExecutionPolicyTests
{
    [Fact]
    public void ProductionNeverAppliesMigrationsAtApiStartup()
    {
        Assert.False(DatabaseMigrationExecutionPolicy.ShouldApplyAtStartup(
            environmentName: "Production",
            isPostgreSql: true,
            autoMigrate: true));
    }

    [Theory]
    [InlineData("Production", false, true)]
    [InlineData("Development", true, false)]
    [InlineData("Development", false, true)]
    [InlineData("", true, true)]
    [InlineData(null, true, true)]
    public void StartupMigrationRequiresDevelopmentPostgreSqlAndExplicitOptIn(
        string? environmentName,
        bool isPostgreSql,
        bool autoMigrate)
    {
        Assert.False(DatabaseMigrationExecutionPolicy.ShouldApplyAtStartup(
            environmentName,
            isPostgreSql,
            autoMigrate));
    }

    [Fact]
    public void DevelopmentExplicitOptInMayApplyMigrationsAtStartup()
    {
        Assert.True(DatabaseMigrationExecutionPolicy.ShouldApplyAtStartup(
            environmentName: "Development",
            isPostgreSql: true,
            autoMigrate: true));
    }
}
