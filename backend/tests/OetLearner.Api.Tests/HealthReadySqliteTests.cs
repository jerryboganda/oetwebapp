using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// Regression coverage for the bundled-desktop readiness deadlock: the desktop
/// shells (Electron's main.cjs and the Tauri orchestrator) spawn
/// OetLearner.Api with a SQLite connection string and poll /health/ready
/// before showing the app. SQLite schemas are created by
/// DatabaseBootstrapper via EnsureCreatedAsync, which never writes
/// __EFMigrationsHistory — so the readiness pending-migrations probe used to
/// report "migrations":"pending" and 503 forever on a fresh database, timing
/// out desktop startup. The endpoint now skips the pending-migrations check
/// for SQLite (mirroring the bootstrapper's provider branch).
/// </summary>
public sealed class HealthReadySqliteTests : IDisposable
{
    private sealed class SqliteTestWebApplicationFactory : TestWebApplicationFactory
    {
        public string DatabasePath { get; } = Path.Combine(
            Path.GetTempPath(),
            $"oet-health-ready-test-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            // Later configuration sources win: replace the base factory's
            // InMemory connection string with a fresh on-disk SQLite database,
            // matching how the desktop shells launch the bundled backend.
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = $"Data Source={DatabasePath}",
                });
            });
        }
    }

    private readonly SqliteTestWebApplicationFactory _factory = new();

    [Fact]
    public async Task HealthReady_FreshSqliteDatabase_ReportsReady()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<HealthReadyResponse>();
        Assert.NotNull(payload);
        Assert.Equal("ok", payload!.Status);
        Assert.NotNull(payload.Checks);
        Assert.Equal("ok", payload.Checks!["database"]);
        // The decisive assertion: EnsureCreated-managed SQLite must not be
        // held hostage by the migrations-history probe.
        Assert.Equal("ok", payload.Checks["migrations"]);
    }

    public void Dispose()
    {
        var databasePath = _factory.DatabasePath;
        _factory.Dispose();
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            try
            {
                File.Delete(databasePath + suffix);
            }
            catch
            {
                // Best-effort cleanup of test temp files.
            }
        }
    }

    private sealed record HealthReadyResponse(string Status, Dictionary<string, string>? Checks);
}
