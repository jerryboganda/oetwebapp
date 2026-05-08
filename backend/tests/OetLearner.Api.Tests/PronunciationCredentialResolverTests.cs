using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Pronunciation;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

/// <summary>
/// Phase 6c — covers <see cref="PronunciationCredentialResolver"/>:
/// registry-first resolution, options-fallback, sync IsRegistryConfigured
/// hot-path semantics, Azure region extraction, and cache invalidation.
/// </summary>
public sealed class PronunciationCredentialResolverTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;
    private readonly EphemeralDataProtectionProvider _dp = new();

    public PronunciationCredentialResolverTests()
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
        var resolver = BuildResolver();
        Assert.Null(await resolver.ResolveAsync("azure-phoneme", default));
        Assert.Null(await resolver.ResolveAsync("whisper-asr", default));
    }

    [Fact]
    public async Task ResolveAsync_ActiveAzureRow_ReturnsKeyAndExtractedRegion()
    {
        await SeedRowAsync("azure-phoneme", AiProviderCategory.Phoneme, AiProviderDialect.AzurePhoneme,
            baseUrl: "https://westeurope.stt.speech.microsoft.com",
            plaintextKey: "azure-key-12345");

        var resolver = BuildResolver();
        var creds = await resolver.ResolveAsync("azure-phoneme", default);

        Assert.NotNull(creds);
        Assert.Equal("azure-key-12345", creds!.ApiKey);
        Assert.Equal("westeurope", creds.AzureRegion);
    }

    [Fact]
    public async Task ResolveAsync_ActiveWhisperRow_ReturnsKeyBaseUrlAndModel()
    {
        await SeedRowAsync("whisper-asr", AiProviderCategory.Asr, AiProviderDialect.OpenAiCompatible,
            baseUrl: "https://api.openai.com/v1",
            plaintextKey: "sk-whisper-key-9999",
            defaultModel: "whisper-1");

        var resolver = BuildResolver();
        var creds = await resolver.ResolveAsync("whisper-asr", default);

        Assert.NotNull(creds);
        Assert.Equal("sk-whisper-key-9999", creds!.ApiKey);
        Assert.Equal("https://api.openai.com/v1", creds.BaseUrl);
        Assert.Equal("whisper-1", creds.DefaultModel);
        Assert.Null(creds.AzureRegion); // whisper has no Azure region
    }

    [Fact]
    public async Task ResolveAsync_InactiveRow_ReturnsNull()
    {
        await SeedRowAsync("azure-phoneme", AiProviderCategory.Phoneme, AiProviderDialect.AzurePhoneme,
            baseUrl: "https://uksouth.stt.speech.microsoft.com",
            plaintextKey: "azure-key", isActive: false);

        var resolver = BuildResolver();
        Assert.Null(await resolver.ResolveAsync("azure-phoneme", default));
    }

    [Fact]
    public async Task ResolveAsync_EmptyEncryptedKey_ReturnsNull()
    {
        // Seeder-style row: active but with no key yet.
        await using (var db = new LearnerDbContext(_options))
        {
            db.AiProviders.Add(new AiProvider
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = "azure-phoneme",
                Name = "Azure Phoneme",
                Category = AiProviderCategory.Phoneme,
                Dialect = AiProviderDialect.AzurePhoneme,
                BaseUrl = "https://uksouth.stt.speech.microsoft.com",
                EncryptedApiKey = string.Empty,
                ApiKeyHint = string.Empty,
                DefaultModel = string.Empty,
                AllowedModelsCsv = string.Empty,
                IsActive = true,
                FailoverPriority = 0,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var resolver = BuildResolver();
        Assert.Null(await resolver.ResolveAsync("azure-phoneme", default));
    }

    [Fact]
    public void IsRegistryConfigured_ColdCache_ReturnsFalse()
    {
        // Sync hot-path contract: cold cache returns false even when the
        // DB has a row, by design — first request after restart falls
        // through to options. ResolveAsync warms the cache.
        var resolver = BuildResolver();
        Assert.False(resolver.IsRegistryConfigured("azure-phoneme"));
    }

    [Fact]
    public async Task IsRegistryConfigured_WarmCache_ReturnsTrueForKnownCode()
    {
        await SeedRowAsync("whisper-asr", AiProviderCategory.Asr, AiProviderDialect.OpenAiCompatible,
            baseUrl: "https://api.openai.com/v1", plaintextKey: "sk-warm-cache-9999");

        var resolver = BuildResolver();
        await resolver.ResolveAsync("whisper-asr", default); // warms cache

        Assert.True(resolver.IsRegistryConfigured("whisper-asr"));
        Assert.False(resolver.IsRegistryConfigured("azure-phoneme")); // not seeded
    }

    [Fact]
    public async Task Invalidate_ForcesRefreshOnNextResolve()
    {
        await SeedRowAsync("whisper-asr", AiProviderCategory.Asr, AiProviderDialect.OpenAiCompatible,
            baseUrl: "https://api.openai.com/v1", plaintextKey: "first-key-aaaaaaaa");

        var resolver = BuildResolver();
        var first = await resolver.ResolveAsync("whisper-asr", default);
        Assert.Equal("first-key-aaaaaaaa", first!.ApiKey);

        // Rotate the key.
        await using (var db = new LearnerDbContext(_options))
        {
            var row = await db.AiProviders.FirstAsync(r => r.Code == "whisper-asr");
            var protector = _dp.CreateProtector("AiProvider.PlatformKey.v1");
            row.EncryptedApiKey = protector.Protect("rotated-key-bbbbbbbb");
            row.ApiKeyHint = "bbbb";
            await db.SaveChangesAsync();
        }

        // Without invalidation we'd still see the cached key.
        resolver.Invalidate();
        var second = await resolver.ResolveAsync("whisper-asr", default);
        Assert.Equal("rotated-key-bbbbbbbb", second!.ApiKey);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private IPronunciationCredentialResolver BuildResolver()
    {
        // Build a real DI scope so the singleton resolver can request a
        // scoped IAiProviderRegistry, exactly like production does.
        var services = new ServiceCollection();
        services.AddSingleton(_options);
        services.AddSingleton<IDataProtectionProvider>(_dp);
        services.AddScoped(sp => new LearnerDbContext(sp.GetRequiredService<DbContextOptions<LearnerDbContext>>()));
        services.AddScoped<IAiProviderRegistry, AiProviderRegistry>();
        services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
        services.AddSingleton(Options.Create(new PronunciationOptions()));
        services.AddSingleton<IPronunciationCredentialResolver, PronunciationCredentialResolver>();
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IPronunciationCredentialResolver>();
    }

    private async Task SeedRowAsync(
        string code, AiProviderCategory category, AiProviderDialect dialect,
        string baseUrl, string plaintextKey, string? defaultModel = null, bool isActive = true)
    {
        await using var db = new LearnerDbContext(_options);
        var protector = _dp.CreateProtector("AiProvider.PlatformKey.v1");
        db.AiProviders.Add(new AiProvider
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = code,
            Name = code,
            Category = category,
            Dialect = dialect,
            BaseUrl = baseUrl,
            EncryptedApiKey = protector.Protect(plaintextKey),
            ApiKeyHint = plaintextKey[^4..],
            DefaultModel = defaultModel ?? string.Empty,
            AllowedModelsCsv = string.Empty,
            IsActive = isActive,
            FailoverPriority = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
