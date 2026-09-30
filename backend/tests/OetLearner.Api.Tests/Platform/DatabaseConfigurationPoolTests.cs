using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using OetLearner.Api.Data;

namespace OetLearner.Api.Tests.Platform;

/// <summary>
/// Production incident 30 Sep 2026: Npgsql's default pool (100 per process) is as
/// large as Postgres' default max_connections (100) and three API-image processes
/// share that one database, so one request burst became `53300: sorry, too many
/// clients already` on every endpoint. The app must cap its own pool.
/// </summary>
public class DatabaseConfigurationPoolTests
{
    private static string ConfiguredConnectionString(string connectionString)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>();
        DatabaseConfiguration.ConfigureDbContext(options, connectionString);
        return options.Options.Extensions
            .OfType<RelationalOptionsExtension>()
            .Single()
            .ConnectionString!;
    }

    [Fact]
    public void PostgresConnectionString_GetsAPerProcessPoolCap()
    {
        var configured = new NpgsqlConnectionStringBuilder(ConfiguredConnectionString(
            "Host=postgres;Port=5432;Database=oet;Username=app;Password=secret"));

        Assert.Equal(25, configured.MaxPoolSize);
        Assert.Equal("postgres", configured.Host);
        Assert.Equal("oet", configured.Database);
        Assert.Equal("secret", configured.Password);
    }

    [Fact]
    public void PostgresConnectionString_KeepsAwkwardPasswordCharactersIntact()
    {
        var configured = new NpgsqlConnectionStringBuilder(ConfiguredConnectionString(
            "Host=postgres;Database=oet;Username=app;Password=\"p;a=ss'w\"\"rd\""));

        Assert.Equal("p;a=ss'w\"rd", configured.Password);
        Assert.Equal(25, configured.MaxPoolSize);
    }

    [Theory]
    [InlineData("Maximum Pool Size=40")]
    [InlineData("MaxPoolSize=40")]
    public void ExplicitPoolSizeInTheConnectionString_Wins(string poolSetting)
    {
        var configured = new NpgsqlConnectionStringBuilder(ConfiguredConnectionString(
            $"Host=postgres;Database=oet;Username=app;Password=secret;{poolSetting}"));

        Assert.Equal(40, configured.MaxPoolSize);
    }

    [Fact]
    public void SqliteConnectionString_IsLeftUntouched()
    {
        Assert.Equal("Data Source=:memory:", ConfiguredConnectionString("Data Source=:memory:"));
    }

    [Fact]
    public void InMemoryConnectionString_ConfiguresNoRelationalProvider()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>();
        DatabaseConfiguration.ConfigureDbContext(options, "InMemory:pool-cap-test");

        Assert.DoesNotContain(options.Options.Extensions, e => e is RelationalOptionsExtension);
    }
}
