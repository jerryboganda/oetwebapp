using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// W2 of the AI cost/reliability remediation — proves
/// <see cref="AiPricingResolver"/> resolves exactly one effective-dated rate
/// per (provider, model, instant), and that
/// <see cref="AiPricingResolution.ComputeCostUsd"/> prices normal and
/// cache-write/cache-read tokens as disjoint buckets without double-counting.
/// </summary>
public sealed class AiPricingResolverCacheClassTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public AiPricingResolverCacheClassTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task ResolveAsync_NoRow_ReturnsNull()
    {
        await using var db = new LearnerDbContext(_options);
        var resolver = new AiPricingResolver(db);

        var result = await resolver.ResolveAsync("anthropic", "claude-sonnet-5", DateTimeOffset.UtcNow, default);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ResolveAsync_BlankProviderOrModel_ReturnsNull(string blank)
    {
        await using var db = new LearnerDbContext(_options);
        var resolver = new AiPricingResolver(db);

        Assert.Null(await resolver.ResolveAsync(blank, "claude-sonnet-5", DateTimeOffset.UtcNow, default));
        Assert.Null(await resolver.ResolveAsync("anthropic", blank, DateTimeOffset.UtcNow, default));
    }

    [Fact]
    public async Task ResolveAsync_PicksRowEffectiveAtInstant_NotLatestOverall()
    {
        await using var db = new LearnerDbContext(_options);
        var now = DateTimeOffset.UtcNow;
        await SeedRateAsync(db, "anthropic", "claude-sonnet-5", "2020-legacy", now.AddDays(-100), now.AddDays(-10), inputPer1k: 0.001m, outputPer1k: 0.002m);
        await SeedRateAsync(db, "anthropic", "claude-sonnet-5", "2026-11-01", now.AddDays(-10), null, inputPer1k: 0.003m, outputPer1k: 0.015m);
        var resolver = new AiPricingResolver(db);

        var historical = await resolver.ResolveAsync("anthropic", "claude-sonnet-5", now.AddDays(-50), default);
        var current = await resolver.ResolveAsync("anthropic", "claude-sonnet-5", now, default);

        Assert.NotNull(historical);
        Assert.Equal("2020-legacy", historical!.PricingVersion);
        Assert.NotNull(current);
        Assert.Equal("2026-11-01", current!.PricingVersion);
    }

    [Fact]
    public async Task ResolveAsync_InstantBeforeAnyEffectiveFrom_ReturnsNull()
    {
        await using var db = new LearnerDbContext(_options);
        var now = DateTimeOffset.UtcNow;
        await SeedRateAsync(db, "anthropic", "claude-sonnet-5", "2026-11-01", now.AddDays(-1), null, inputPer1k: 0.003m, outputPer1k: 0.015m);
        var resolver = new AiPricingResolver(db);

        var result = await resolver.ResolveAsync("anthropic", "claude-sonnet-5", now.AddDays(-30), default);

        Assert.Null(result);
    }

    [Fact]
    public void ComputeCostUsd_NormalTokensOnly_NoCacheRates()
    {
        var resolution = new AiPricingResolution("anthropic", "claude-sonnet-5", "2026-11-01", 0.003m, 0.015m, null, null);

        var cost = resolution.ComputeCostUsd(normalInputTokens: 1000, normalOutputTokens: 1000, cacheWriteTokens: 0, cacheReadTokens: 0);

        Assert.Equal(0.003m + 0.015m, cost);
    }

    [Fact]
    public void ComputeCostUsd_CacheWriteAndRead_AreAddedAsDisjointBuckets_NotDoubleCounted()
    {
        var resolution = new AiPricingResolution("anthropic", "claude-sonnet-5", "2026-11-01", 0.003m, 0.015m, 0.00375m, 0.0003m);

        var cost = resolution.ComputeCostUsd(
            normalInputTokens: 1000,
            normalOutputTokens: 500,
            cacheWriteTokens: 2000,
            cacheReadTokens: 4000);

        var expected =
            1000 / 1000m * 0.003m +
            500 / 1000m * 0.015m +
            2000 / 1000m * 0.00375m +
            4000 / 1000m * 0.0003m;

        Assert.Equal(expected, cost);
    }

    [Fact]
    public void ComputeCostUsd_CacheTokensPresent_ButProviderHasNoCacheRate_CostsNothingExtra()
    {
        // OpenAI-shaped row: no separate cache-write billing tier.
        var resolution = new AiPricingResolution("openai", "gpt-4o", "2026-11-01", 0.0025m, 0.01m, null, 0.00125m);

        var cost = resolution.ComputeCostUsd(
            normalInputTokens: 1000,
            normalOutputTokens: 0,
            cacheWriteTokens: 5000, // reported by provider but not billed
            cacheReadTokens: 0);

        Assert.Equal(1000 / 1000m * 0.0025m, cost);
    }

    private static async Task SeedRateAsync(
        LearnerDbContext db,
        string providerId,
        string model,
        string pricingVersion,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        decimal inputPer1k,
        decimal outputPer1k)
    {
        db.AiModelPrices.Add(new AiModelPrice
        {
            Id = Guid.NewGuid().ToString("N"),
            ProviderId = providerId,
            Model = model,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            PricingVersion = pricingVersion,
            InputPer1k = inputPer1k,
            OutputPer1k = outputPer1k,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
