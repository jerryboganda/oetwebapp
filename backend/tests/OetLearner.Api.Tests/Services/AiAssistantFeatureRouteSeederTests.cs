using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant;
using OetLearner.Api.Services.Seeding;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// First-boot seeding of the AI Assistant feature routes: the route default is "the best credentialed
/// text-chat row", and that pick must never land on a keyless subscription sidecar or on an
/// explicit-only provider (OpenCode). SQLite, so the real EF query is translated and executed.
/// </summary>
public sealed class AiAssistantFeatureRouteSeederTests : IAsyncDisposable
{
    private static readonly string[] AssistantFeatures =
    [
        AiFeatureCodes.AiAssistantAdmin,
        AiFeatureCodes.AiAssistantExpert,
        AiFeatureCodes.AiAssistantLearner,
    ];

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public AiAssistantFeatureRouteSeederTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private static AiProvider Row(string code, int priority, string encryptedKey, string defaultModel, bool active = true) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Code = code,
        Name = code,
        Dialect = AiProviderDialect.OpenAiCompatible,
        Category = AiProviderCategory.TextChat,
        BaseUrl = "https://provider.example.test",
        EncryptedApiKey = encryptedKey,
        ApiKeyHint = string.Empty,
        DefaultModel = defaultModel,
        IsActive = active,
        FailoverPriority = priority,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private async Task AddRowsAsync(params AiProvider[] rows)
    {
        await using var db = new LearnerDbContext(_options);
        db.AiProviders.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private AiAssistantFeatureRouteSeeder NewSeeder()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_options);
        services.AddScoped(sp => new LearnerDbContext(sp.GetRequiredService<DbContextOptions<LearnerDbContext>>()));
        var provider = services.BuildServiceProvider();
        return new AiAssistantFeatureRouteSeeder(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiAssistantFeatureRouteSeeder>.Instance);
    }

    private async Task<List<AiFeatureRoute>> RoutesAsync()
    {
        await using var db = new LearnerDbContext(_options);
        return await db.AiFeatureRoutes.AsNoTracking()
            .Where(route => AssistantFeatures.Contains(route.FeatureCode))
            .OrderBy(route => route.FeatureCode)
            .ToListAsync();
    }

    [Fact]
    public async Task Seed_UsesTheBestCredentialedRow_NotAKeyedExplicitOnlyOpenCodeRow()
    {
        await AddRowsAsync(
            // The best priority number, active and keyed: without the guard it would win the pick.
            Row(OpenCodeProviderDefaults.ProviderCode, 1, "ciphertext-of-a-real-key", OpenCodeProviderDefaults.DefaultModel),
            Row("anthropic", 20, "ciphertext-of-a-real-key", "claude-sonnet-5"));

        await NewSeeder().SeedAsync(default);

        var routes = await RoutesAsync();
        Assert.Equal(AssistantFeatures.OrderBy(code => code, StringComparer.Ordinal), routes.Select(r => r.FeatureCode));
        Assert.All(routes, route =>
        {
            Assert.Equal("anthropic", route.ProviderCode);
            Assert.Equal("claude-sonnet-5", route.Model);
            Assert.True(route.IsActive);
        });
    }

    [Fact]
    public async Task Seed_SeedsNothing_WhenTheOnlyCredentialedRowIsExplicitOnly()
    {
        await AddRowsAsync(
            Row(OpenCodeProviderDefaults.ProviderCode, 1, "ciphertext-of-a-real-key", OpenCodeProviderDefaults.DefaultModel));

        await NewSeeder().SeedAsync(default);

        Assert.Empty(await RoutesAsync());
    }

    [Fact]
    public async Task Seed_StillSkipsKeylessSidecarRowsAndUncredentialedOrInactiveRows()
    {
        await AddRowsAsync(
            Row(WritingSubscriptionProviderDefaults.ClaudeCode, 1, WritingSubscriptionProviderDefaults.MarkerKey, "claude-opus-5-5"),
            Row("keyless", 5, string.Empty, "keyless-model"),
            Row("inactive", 6, "ciphertext-of-a-real-key", "inactive-model", active: false),
            Row("openai-platform", 10, "ciphertext-of-a-real-key", "text-default-model"));

        await NewSeeder().SeedAsync(default);

        var routes = await RoutesAsync();
        Assert.Equal(3, routes.Count);
        Assert.All(routes, route => Assert.Equal("openai-platform", route.ProviderCode));
    }

    [Fact]
    public async Task Seed_LeavesAnExistingRouteTheAdminPointedAtOpenCodeUntouched()
    {
        await AddRowsAsync(
            Row(OpenCodeProviderDefaults.ProviderCode, 900, "ciphertext-of-a-real-key", OpenCodeProviderDefaults.DefaultModel),
            Row("anthropic", 20, "ciphertext-of-a-real-key", "claude-sonnet-5"));
        await using (var db = new LearnerDbContext(_options))
        {
            db.AiFeatureRoutes.Add(new AiFeatureRoute
            {
                Id = Guid.NewGuid().ToString("N"),
                FeatureCode = AiFeatureCodes.AiAssistantLearner,
                ProviderCode = OpenCodeProviderDefaults.ProviderCode,
                Model = OpenCodeProviderDefaults.DefaultModel,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await NewSeeder().SeedAsync(default);

        var routes = await RoutesAsync();
        Assert.Equal(3, routes.Count);
        var learner = routes.Single(r => r.FeatureCode == AiFeatureCodes.AiAssistantLearner);
        Assert.Equal(OpenCodeProviderDefaults.ProviderCode, learner.ProviderCode);
        Assert.Equal(OpenCodeProviderDefaults.DefaultModel, learner.Model);
        Assert.All(routes.Where(r => r.FeatureCode != AiFeatureCodes.AiAssistantLearner),
            route => Assert.Equal("anthropic", route.ProviderCode));
    }
}
