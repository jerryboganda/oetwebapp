using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Seeding;

namespace OetLearner.Api.Tests;

public sealed class AntigravityGatewaySeederTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;
    private readonly EphemeralDataProtectionProvider _dp = new();

    public AntigravityGatewaySeederTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task SeedAsync_WhenRoutesDisabled_SeedsInactiveProviderAndNoRoutes()
    {
        var prevToken = Environment.GetEnvironmentVariable("AGENTGATEWAY_INTERNAL_SERVICE_TOKEN");
        var prevRoutes = Environment.GetEnvironmentVariable("ANTIGRAVITY_GATEWAY_ROUTES_ENABLED");
        try
        {
            Environment.SetEnvironmentVariable("AGENTGATEWAY_INTERNAL_SERVICE_TOKEN", "test-secret-token");
            Environment.SetEnvironmentVariable("ANTIGRAVITY_GATEWAY_ROUTES_ENABLED", "false");

            await using (var db = new LearnerDbContext(_options))
            {
                var count = await AntigravityGatewaySeeder.SeedAsync(db, _dp);
                Assert.Equal(1, count); // 1 provider row inserted
            }

            await using (var db = new LearnerDbContext(_options))
            {
                var provider = await db.AiProviders.FirstOrDefaultAsync(p => p.Code == AntigravityGatewayRouteDefaults.ProviderCode);
                Assert.NotNull(provider);
                Assert.False(provider.IsActive);
                Assert.Equal("gateway-token", provider.ApiKeyHint);
                Assert.Equal(AiProviderDialect.OpenAiCompatible, provider.Dialect);

                var routes = await db.AiFeatureRoutes.Where(r => r.ProviderCode == AntigravityGatewayRouteDefaults.ProviderCode).ToListAsync();
                Assert.Empty(routes);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGENTGATEWAY_INTERNAL_SERVICE_TOKEN", prevToken);
            Environment.SetEnvironmentVariable("ANTIGRAVITY_GATEWAY_ROUTES_ENABLED", prevRoutes);
        }
    }

    [Fact]
    public async Task SeedAsync_WhenRoutesEnabled_SeedsActiveProviderAndAllRoutes()
    {
        var prevToken = Environment.GetEnvironmentVariable("AGENTGATEWAY_INTERNAL_SERVICE_TOKEN");
        var prevRoutes = Environment.GetEnvironmentVariable("ANTIGRAVITY_GATEWAY_ROUTES_ENABLED");
        try
        {
            Environment.SetEnvironmentVariable("AGENTGATEWAY_INTERNAL_SERVICE_TOKEN", "test-secret-token");
            Environment.SetEnvironmentVariable("ANTIGRAVITY_GATEWAY_ROUTES_ENABLED", "true");

            await using (var db = new LearnerDbContext(_options))
            {
                var count = await AntigravityGatewaySeeder.SeedAsync(db, _dp);
                Assert.True(count > 1); // provider + routes
            }

            await using (var db = new LearnerDbContext(_options))
            {
                var provider = await db.AiProviders.FirstOrDefaultAsync(p => p.Code == AntigravityGatewayRouteDefaults.ProviderCode);
                Assert.NotNull(provider);
                Assert.True(provider.IsActive);

                var routes = await db.AiFeatureRoutes.Where(r => r.ProviderCode == AntigravityGatewayRouteDefaults.ProviderCode).ToListAsync();
                Assert.Equal(AntigravityGatewayRouteDefaults.Routes.Count, routes.Count);
                foreach (var route in routes)
                {
                    Assert.True(route.IsActive);
                    Assert.StartsWith("agent:", route.Model);
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGENTGATEWAY_INTERNAL_SERVICE_TOKEN", prevToken);
            Environment.SetEnvironmentVariable("ANTIGRAVITY_GATEWAY_ROUTES_ENABLED", prevRoutes);
        }
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        var prevToken = Environment.GetEnvironmentVariable("AGENTGATEWAY_INTERNAL_SERVICE_TOKEN");
        var prevRoutes = Environment.GetEnvironmentVariable("ANTIGRAVITY_GATEWAY_ROUTES_ENABLED");
        try
        {
            Environment.SetEnvironmentVariable("AGENTGATEWAY_INTERNAL_SERVICE_TOKEN", "test-secret-token");
            Environment.SetEnvironmentVariable("ANTIGRAVITY_GATEWAY_ROUTES_ENABLED", "true");

            await using (var db = new LearnerDbContext(_options))
            {
                await AntigravityGatewaySeeder.SeedAsync(db, _dp);
            }

            await using (var db = new LearnerDbContext(_options))
            {
                var secondRunCount = await AntigravityGatewaySeeder.SeedAsync(db, _dp);
                Assert.Equal(0, secondRunCount); // No duplicates inserted
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGENTGATEWAY_INTERNAL_SERVICE_TOKEN", prevToken);
            Environment.SetEnvironmentVariable("ANTIGRAVITY_GATEWAY_ROUTES_ENABLED", prevRoutes);
        }
    }
}
