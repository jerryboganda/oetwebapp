using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Voice;

namespace OetLearner.Api.Tests;

/// <summary>
/// Phase 6b — covers <see cref="AiProviderRegistry.ListByCategoryAsync"/>
/// and the <see cref="AiVoiceProviderSeeder"/> backfill rules.
/// </summary>
public sealed class AiVoiceProviderSeederTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;
    private readonly EphemeralDataProtectionProvider _dpProvider = new();

    public AiVoiceProviderSeederTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    // ─── BuildSeeds (pure) ────────────────────────────────────────────────

    [Fact]
    public void BuildSeeds_AllConfigured_EmitsFiveRows()
    {
        var conv = new ConversationOptions
        {
            AzureSpeechKey = "k", AzureSpeechRegion = "uksouth", AzureLocale = "en-GB",
            ElevenLabsApiKey = "ek",
            WhisperApiKey = "wk", WhisperBaseUrl = "https://api.openai.com/v1",
        };
        var pron = new PronunciationOptions
        {
            AzureSpeechKey = "pk", AzureSpeechRegion = "uksouth", AzureLocale = "en-GB",
        };

        var seeds = AiVoiceProviderSeeder.BuildSeeds(conv, pron);

        Assert.Equal(5, seeds.Count);
        Assert.Contains(seeds, s => s.Code == "azure-tts" && s.Category == AiProviderCategory.Tts);
        Assert.Contains(seeds, s => s.Code == "elevenlabs-tts" && s.Category == AiProviderCategory.Tts);
        Assert.Contains(seeds, s => s.Code == "azure-asr" && s.Category == AiProviderCategory.Asr);
        Assert.Contains(seeds, s => s.Code == "whisper-asr" && s.Category == AiProviderCategory.Asr);
        Assert.Contains(seeds, s => s.Code == "azure-phoneme" && s.Category == AiProviderCategory.Phoneme);
    }

    [Fact]
    public void BuildSeeds_NoneConfigured_EmitsEmptyList()
    {
        var seeds = AiVoiceProviderSeeder.BuildSeeds(new ConversationOptions(), new PronunciationOptions());
        Assert.Empty(seeds);
    }

    [Fact]
    public void BuildSeeds_OnlyAzure_EmitsTtsAsrAndPhoneme()
    {
        var conv = new ConversationOptions
        {
            AzureSpeechKey = "k", AzureSpeechRegion = "uksouth",
        };
        var pron = new PronunciationOptions
        {
            AzureSpeechKey = "pk", AzureSpeechRegion = "uksouth",
        };

        var seeds = AiVoiceProviderSeeder.BuildSeeds(conv, pron);

        Assert.Equal(3, seeds.Count);
        Assert.Contains(seeds, s => s.Code == "azure-tts");
        Assert.Contains(seeds, s => s.Code == "azure-asr");
        Assert.Contains(seeds, s => s.Code == "azure-phoneme");
    }

    // ─── Seeder hosted service ────────────────────────────────────────────

    [Fact]
    public async Task Seeder_InsertsRowsForConfiguredProviders()
    {
        var sp = BuildServiceProvider(
            new ConversationOptions
            {
                AzureSpeechKey = "k", AzureSpeechRegion = "uksouth",
                ElevenLabsApiKey = "ek",
            },
            new PronunciationOptions());

        var seeder = new AiVoiceProviderSeeder(sp, NullLogger<AiVoiceProviderSeeder>.Instance);
        await seeder.StartAsync(default);

        await using var db = new LearnerDbContext(_options);
        var rows = await db.AiProviders.AsNoTracking().ToListAsync();
        Assert.Equal(3, rows.Count);  // azure-tts, azure-asr, elevenlabs-tts
        Assert.All(rows, r => Assert.True(r.IsActive));
        Assert.All(rows, r => Assert.Equal(string.Empty, r.EncryptedApiKey));
    }

    [Fact]
    public async Task Seeder_IsIdempotent_DoesNotOverwriteExistingRows()
    {
        // Pre-seed an admin-edited row.
        await using (var db = new LearnerDbContext(_options))
        {
            db.AiProviders.Add(new AiProvider
            {
                Id = "fixed-id",
                Code = "azure-tts",
                Name = "Admin-edited Azure TTS",
                Category = AiProviderCategory.Tts,
                Dialect = AiProviderDialect.AzureTts,
                BaseUrl = "https://custom.example",
                EncryptedApiKey = "secret-blob",
                ApiKeyHint = "abcd",
                DefaultModel = "custom-voice",
                AllowedModelsCsv = string.Empty,
                IsActive = true,
                FailoverPriority = 99,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var sp = BuildServiceProvider(
            new ConversationOptions { AzureSpeechKey = "k", AzureSpeechRegion = "uksouth" },
            new PronunciationOptions());
        var seeder = new AiVoiceProviderSeeder(sp, NullLogger<AiVoiceProviderSeeder>.Instance);
        await seeder.StartAsync(default);

        await using var verify = new LearnerDbContext(_options);
        var azureTts = await verify.AiProviders.AsNoTracking().FirstAsync(r => r.Code == "azure-tts");
        Assert.Equal("Admin-edited Azure TTS", azureTts.Name);
        Assert.Equal("https://custom.example", azureTts.BaseUrl);
        Assert.Equal("secret-blob", azureTts.EncryptedApiKey);
        Assert.Equal(99, azureTts.FailoverPriority);

        // azure-asr was not pre-existing — should still get inserted.
        Assert.True(await verify.AiProviders.AnyAsync(r => r.Code == "azure-asr"));
    }

    [Fact]
    public async Task Seeder_NoConfig_InsertsNothing()
    {
        var sp = BuildServiceProvider(new ConversationOptions(), new PronunciationOptions());
        var seeder = new AiVoiceProviderSeeder(sp, NullLogger<AiVoiceProviderSeeder>.Instance);
        await seeder.StartAsync(default);

        await using var db = new LearnerDbContext(_options);
        Assert.Empty(await db.AiProviders.AsNoTracking().ToListAsync());
    }

    // ─── Registry.ListByCategoryAsync ─────────────────────────────────────

    [Fact]
    public async Task ListByCategoryAsync_FiltersByCategoryAndActive()
    {
        await using (var db = new LearnerDbContext(_options))
        {
            db.AiProviders.AddRange(
                Row("text-1", AiProviderCategory.TextChat, isActive: true, priority: 0),
                Row("text-2", AiProviderCategory.TextChat, isActive: true, priority: 1),
                Row("tts-1", AiProviderCategory.Tts, isActive: true, priority: 0),
                Row("tts-disabled", AiProviderCategory.Tts, isActive: false, priority: 1),
                Row("asr-1", AiProviderCategory.Asr, isActive: true, priority: 0));
            await db.SaveChangesAsync();
        }

        await using var ctx = new LearnerDbContext(_options);
        var registry = new AiProviderRegistry(ctx, _dpProvider);

        var ttsRows = await registry.ListByCategoryAsync(AiProviderCategory.Tts, default);
        var asrRows = await registry.ListByCategoryAsync(AiProviderCategory.Asr, default);
        var phonemeRows = await registry.ListByCategoryAsync(AiProviderCategory.Phoneme, default);

        Assert.Single(ttsRows);
        Assert.Equal("tts-1", ttsRows[0].Code);
        Assert.Single(asrRows);
        Assert.Equal("asr-1", asrRows[0].Code);
        Assert.Empty(phonemeRows);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private IServiceProvider BuildServiceProvider(ConversationOptions conv, PronunciationOptions pron)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_options);
        services.AddScoped(sp => new LearnerDbContext(sp.GetRequiredService<DbContextOptions<LearnerDbContext>>()));
        services.AddSingleton(Options.Create(pron));
        services.AddSingleton<IConversationOptionsProvider>(new StubConversationOptionsProvider(conv));
        return services.BuildServiceProvider();
    }

    private static AiProvider Row(string code, AiProviderCategory category, bool isActive, int priority)
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = code,
            Name = code,
            Category = category,
            Dialect = AiProviderDialect.OpenAiCompatible,
            BaseUrl = "https://example",
            EncryptedApiKey = string.Empty,
            ApiKeyHint = string.Empty,
            DefaultModel = string.Empty,
            AllowedModelsCsv = string.Empty,
            IsActive = isActive,
            FailoverPriority = priority,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private sealed class StubConversationOptionsProvider(ConversationOptions opts) : IConversationOptionsProvider
    {
        public Task<ConversationOptions> GetAsync(CancellationToken ct = default) => Task.FromResult(opts);
        public void Invalidate() { }
    }
}
