using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Ai;

namespace OetWithDrHesham.Api.Tests;

/// <summary>
/// Verifies <see cref="CoreAiProviderSeeder"/> seeds the canonical anthropic /
/// mistral-ocr / whisper-asr rows additively and never overwrites an existing
/// row (so admin keys + the env-derived whisper row survive).
/// </summary>
public sealed class CoreAiProviderSeederTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public CoreAiProviderSeederTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public void BuildSeeds_EmitsThreeCanonicalRows()
    {
        var seeds = CoreAiProviderSeeder.BuildSeeds();

        Assert.Equal(3, seeds.Count);
        Assert.Contains(seeds, s => s.Code == "anthropic"
            && s.Dialect == AiProviderDialect.Anthropic
            && s.Category == AiProviderCategory.TextChat
            && s.DefaultModel == "claude-sonnet-5");
        Assert.Contains(seeds, s => s.Code == "mistral-ocr"
            && s.Category == AiProviderCategory.Ocr
            && s.DefaultModel == "mistral-ocr-latest");
        Assert.Contains(seeds, s => s.Code == "whisper-asr"
            && s.Dialect == AiProviderDialect.WhisperAsr
            && s.Category == AiProviderCategory.Asr);
    }

    [Fact]
    public async Task StartAsync_InsertsAllMissingRows_Keyless()
    {
        var sp = BuildServiceProvider();
        var seeder = new CoreAiProviderSeeder(sp, NullLogger<CoreAiProviderSeeder>.Instance);

        await seeder.StartAsync(default);

        await using var db = new LearnerDbContext(_options);
        var rows = await db.AiProviders.AsNoTracking().ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(string.Empty, r.EncryptedApiKey));
        Assert.All(rows, r => Assert.True(r.IsActive));
    }

    [Fact]
    public async Task StartAsync_NeverOverwritesExistingKeyedRow()
    {
        await using (var seed = new LearnerDbContext(_options))
        {
            seed.AiProviders.Add(new AiProvider
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = "whisper-asr",
                Name = "Whisper (custom)",
                Dialect = AiProviderDialect.WhisperAsr,
                Category = AiProviderCategory.Asr,
                BaseUrl = "https://groq.example.com/openai/v1",
                EncryptedApiKey = "existing-cipher",
                ApiKeyHint = "…key",
                DefaultModel = "whisper-large-v3",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        var sp = BuildServiceProvider();
        var seeder = new CoreAiProviderSeeder(sp, NullLogger<CoreAiProviderSeeder>.Instance);
        await seeder.StartAsync(default);

        await using var db = new LearnerDbContext(_options);
        var whisper = await db.AiProviders.AsNoTracking().FirstAsync(p => p.Code == "whisper-asr");
        Assert.Equal("existing-cipher", whisper.EncryptedApiKey);        // untouched
        Assert.Equal("https://groq.example.com/openai/v1", whisper.BaseUrl);
        Assert.Equal("whisper-large-v3", whisper.DefaultModel);
        // The other two canonical rows are still backfilled.
        Assert.Equal(3, await db.AiProviders.CountAsync());
    }

    [Fact]
    public async Task StartAsync_IsIdempotent()
    {
        var sp = BuildServiceProvider();
        var seeder = new CoreAiProviderSeeder(sp, NullLogger<CoreAiProviderSeeder>.Instance);
        await seeder.StartAsync(default);
        await seeder.StartAsync(default);

        await using var db = new LearnerDbContext(_options);
        Assert.Equal(3, await db.AiProviders.CountAsync());
    }

    private IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_options);
        services.AddScoped(sp => new LearnerDbContext(sp.GetRequiredService<DbContextOptions<LearnerDbContext>>()));
        return services.BuildServiceProvider();
    }
}
