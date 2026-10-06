using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Seeding;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// Verifies <see cref="CoreAiProviderSeeder"/> seeds the canonical anthropic /
/// mistral-ocr / whisper-asr / openai-audio / typesafe-jev / opencode rows additively and
/// never overwrites an existing row (so admin keys + the env-derived whisper row survive).
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
    public void BuildSeeds_EmitsSixCanonicalRows()
    {
        var seeds = CoreAiProviderSeeder.BuildSeeds();

        Assert.Equal(6, seeds.Count);
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
        // The Speaking acoustic judge: an OpenAI-compatible audio chat model, Asr category (never in a
        // text failover list), keyless (its key falls back to the live-voice OpenAI account), low priority.
        Assert.Contains(seeds, s => s.Code == "openai-audio"
            && s.Dialect == AiProviderDialect.OpenAiCompatible
            && s.Category == AiProviderCategory.Asr
            && s.BaseUrl == "https://api.openai.com/v1"
            && s.DefaultModel == "gpt-audio-1.5"
            && s.FailoverPriority == 95
            && s.IsActive);
        Assert.Contains(seeds, s => s.Code == "typesafe-jev"
            && s.Dialect == AiProviderDialect.TypeSafeJev
            && s.Category == AiProviderCategory.Judgment
            && s.BaseUrl == "https://api.typesafe.ai"
            && s.DefaultModel == "jev-1.13.0"
            && !s.IsActive);
        Assert.Contains(seeds, s => s.Code == OpenCodeProviderDefaults.ProviderCode
            && s.Dialect == AiProviderDialect.OpenAiCompatible
            && s.Category == AiProviderCategory.TextChat
            && s.BaseUrl == OpenCodeProviderDefaults.ZenBaseUrl
            && s.DefaultModel == OpenCodeProviderDefaults.DefaultModel
            && s.FailoverPriority == 900
            && !s.IsActive);
    }

    [Fact]
    public void OpenCodeSeed_CarriesTheDocumentedPricesAllowedModelsAndReasoningEffort()
    {
        var seed = CoreAiProviderSeeder.BuildSeeds().Single(s => s.Code == OpenCodeProviderDefaults.ProviderCode);

        Assert.Equal(OpenCodeProviderDefaults.DefaultPricePer1kPromptTokens, seed.PricePer1kPromptTokens);
        Assert.Equal(OpenCodeProviderDefaults.DefaultPricePer1kCompletionTokens, seed.PricePer1kCompletionTokens);
        Assert.Equal(OpenCodeProviderDefaults.AllowedModelsCsv, seed.AllowedModelsCsv);
        Assert.Equal(OpenCodeProviderDefaults.DefaultReasoningEffort, seed.ReasoningEffort);
        Assert.Equal("max", seed.ReasoningEffort);
        Assert.False(seed.IsActive);
    }

    [Fact]
    public void OpenCodeSeed_BaseUrlPassesTheSsrfGuard_AndIsAnOpenCodeHost()
    {
        var seed = CoreAiProviderSeeder.BuildSeeds().Single(s => s.Code == OpenCodeProviderDefaults.ProviderCode);

        Assert.Null(AiProviderConnectionTester.GetUnsafeBaseUrlReason(seed.BaseUrl));
        Assert.True(OpenCodeProviderDefaults.IsOpenCodeBaseUrl(seed.BaseUrl));
    }

    [Fact]
    public void OpenCodeSeed_IsExplicitOnly_SoItCanNeverBeAnImplicitDefault()
    {
        var seed = CoreAiProviderSeeder.BuildSeeds().Single(s => s.Code == OpenCodeProviderDefaults.ProviderCode);

        Assert.True(AiProviderDefaultEligibility.IsExplicitOnlyCode(seed.Code));
        // Even if an admin activates it and pastes a real key, it still is not default-eligible.
        Assert.False(AiProviderDefaultEligibility.IsDefaultEligible(new AiProvider
        {
            Code = seed.Code,
            EncryptedApiKey = "ciphertext-of-a-real-key",
            IsActive = true,
        }));
    }

    [Fact]
    public void OnlyTheAnthropicAndOpenCodeSeeds_CarryAnAllowedModelsList()
    {
        var withModels = CoreAiProviderSeeder.BuildSeeds()
            .Where(s => !string.IsNullOrEmpty(s.AllowedModelsCsv))
            .Select(s => s.Code)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "anthropic", "opencode" }, withModels);
        Assert.Equal(
            CoreAiProviderSeeder.AnthropicAllowedModelsCsv,
            CoreAiProviderSeeder.BuildSeeds().Single(s => s.Code == "anthropic").AllowedModelsCsv);
    }

    [Fact]
    public void JevSeed_CodeMatchesTypeSafeOptions_AndBaseUrlPassesTheSsrfGuard()
    {
        var jev = CoreAiProviderSeeder.BuildSeeds().Single(s => s.Dialect == AiProviderDialect.TypeSafeJev);

        Assert.Equal(TypeSafeOptions.ProviderCode, jev.Code);
        Assert.Null(AiProviderConnectionTester.GetUnsafeBaseUrlReason(jev.BaseUrl));
    }

    [Fact]
    public async Task StartAsync_InsertsAllMissingRows_Keyless()
    {
        var sp = BuildServiceProvider();
        var seeder = new CoreAiProviderSeeder(sp, NullLogger<CoreAiProviderSeeder>.Instance);

        await seeder.StartAsync(default);

        await using var db = new LearnerDbContext(_options);
        var rows = await db.AiProviders.AsNoTracking().ToListAsync();
        Assert.Equal(6, rows.Count);
        Assert.All(rows, r => Assert.Equal(string.Empty, r.EncryptedApiKey));
        // Every canonical row is live except the Jev judgment row (off until an admin pastes the key
        // and the connection test passes) and the OpenCode gateway row (off until the owner clears
        // the OpenCode terms and TV-029 and ticks Active).
        Assert.All(
            rows.Where(r => r.Code != "typesafe-jev" && r.Code != OpenCodeProviderDefaults.ProviderCode),
            r => Assert.True(r.IsActive));
        var jev = rows.Single(r => r.Code == "typesafe-jev");
        Assert.False(jev.IsActive);
        Assert.Equal(AiProviderDialect.TypeSafeJev, jev.Dialect);
        Assert.Equal(AiProviderCategory.Judgment, jev.Category);
        Assert.Equal(
            CoreAiProviderSeeder.AnthropicAllowedModelsCsv,
            rows.Single(r => r.Code == "anthropic").AllowedModelsCsv);
    }

    [Fact]
    public async Task StartAsync_InsertsTheOpenCodeRow_KeylessInactiveAndConfigured()
    {
        var seeder = new CoreAiProviderSeeder(BuildServiceProvider(), NullLogger<CoreAiProviderSeeder>.Instance);

        await seeder.StartAsync(default);

        await using var db = new LearnerDbContext(_options);
        var row = await db.AiProviders.AsNoTracking().SingleAsync(p => p.Code == OpenCodeProviderDefaults.ProviderCode);
        Assert.Equal(string.Empty, row.EncryptedApiKey);
        Assert.Equal(string.Empty, row.ApiKeyHint);
        Assert.False(row.IsActive);
        Assert.Equal(AiProviderDialect.OpenAiCompatible, row.Dialect);
        Assert.Equal(AiProviderCategory.TextChat, row.Category);
        Assert.Equal(OpenCodeProviderDefaults.ZenBaseUrl, row.BaseUrl);
        Assert.Equal(OpenCodeProviderDefaults.DefaultModel, row.DefaultModel);
        Assert.Equal(900, row.FailoverPriority);
        Assert.Equal(OpenCodeProviderDefaults.DefaultReasoningEffort, row.ReasoningEffort);
        Assert.Equal("max", row.ReasoningEffort);
        Assert.Equal(OpenCodeProviderDefaults.AllowedModelsCsv, row.AllowedModelsCsv);
        Assert.Equal(OpenCodeProviderDefaults.DefaultPricePer1kPromptTokens, row.PricePer1kPromptTokens);
        Assert.Equal(OpenCodeProviderDefaults.DefaultPricePer1kCompletionTokens, row.PricePer1kCompletionTokens);
        // Rows seeded for the other providers carry no reasoning-effort override.
        Assert.All(
            await db.AiProviders.AsNoTracking().Where(p => p.Code != OpenCodeProviderDefaults.ProviderCode).ToListAsync(),
            other => Assert.Null(other.ReasoningEffort));
    }

    [Fact]
    public async Task StartAsync_NeverOverwritesAnAdminEditedOpenCodeRow()
    {
        await using (var seed = new LearnerDbContext(_options))
        {
            seed.AiProviders.Add(new AiProvider
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = OpenCodeProviderDefaults.ProviderCode,
                Name = "OpenCode (admin renamed)",
                Dialect = AiProviderDialect.OpenAiCompatible,
                Category = AiProviderCategory.TextChat,
                BaseUrl = OpenCodeProviderDefaults.GoBaseUrl,
                EncryptedApiKey = "existing-cipher",
                ApiKeyHint = "…key",
                DefaultModel = "glm-5.3",
                ReasoningEffort = "high",
                AllowedModelsCsv = "glm-5.3",
                IsActive = true,
                FailoverPriority = 850,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        var seeder = new CoreAiProviderSeeder(BuildServiceProvider(), NullLogger<CoreAiProviderSeeder>.Instance);
        await seeder.StartAsync(default);

        await using var db = new LearnerDbContext(_options);
        var row = await db.AiProviders.AsNoTracking().SingleAsync(p => p.Code == OpenCodeProviderDefaults.ProviderCode);
        Assert.Equal("existing-cipher", row.EncryptedApiKey);   // key untouched
        Assert.True(row.IsActive);                              // the owner's activation untouched
        Assert.Equal("OpenCode (admin renamed)", row.Name);
        Assert.Equal(OpenCodeProviderDefaults.GoBaseUrl, row.BaseUrl);
        Assert.Equal("glm-5.3", row.DefaultModel);
        Assert.Equal("high", row.ReasoningEffort);
        Assert.Equal("glm-5.3", row.AllowedModelsCsv);
        Assert.Equal(850, row.FailoverPriority);
        Assert.Equal(6, await db.AiProviders.CountAsync());
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
        // The other five canonical rows are still backfilled.
        Assert.Equal(6, await db.AiProviders.CountAsync());
    }

    [Fact]
    public async Task StartAsync_NeverOverwritesAdminEditedJevRow()
    {
        await using (var seed = new LearnerDbContext(_options))
        {
            seed.AiProviders.Add(new AiProvider
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = "typesafe-jev",
                Name = "Jev (admin renamed)",
                Dialect = AiProviderDialect.TypeSafeJev,
                Category = AiProviderCategory.Judgment,
                BaseUrl = "https://jev.example.com",
                EncryptedApiKey = "existing-cipher",
                ApiKeyHint = "…key",
                DefaultModel = "jev-1.99.0",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        var seeder = new CoreAiProviderSeeder(BuildServiceProvider(), NullLogger<CoreAiProviderSeeder>.Instance);
        await seeder.StartAsync(default);

        await using var db = new LearnerDbContext(_options);
        var jev = await db.AiProviders.AsNoTracking().FirstAsync(p => p.Code == "typesafe-jev");
        Assert.Equal("existing-cipher", jev.EncryptedApiKey);   // key untouched
        Assert.True(jev.IsActive);                              // admin's activation untouched
        Assert.Equal("Jev (admin renamed)", jev.Name);
        Assert.Equal("https://jev.example.com", jev.BaseUrl);
        Assert.Equal("jev-1.99.0", jev.DefaultModel);
        Assert.Equal(6, await db.AiProviders.CountAsync());
    }

    [Fact]
    public async Task StartAsync_IsIdempotent()
    {
        var sp = BuildServiceProvider();
        var seeder = new CoreAiProviderSeeder(sp, NullLogger<CoreAiProviderSeeder>.Instance);
        await seeder.StartAsync(default);
        await seeder.StartAsync(default);

        await using var db = new LearnerDbContext(_options);
        Assert.Equal(6, await db.AiProviders.CountAsync());
    }

    private IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_options);
        services.AddScoped(sp => new LearnerDbContext(sp.GetRequiredService<DbContextOptions<LearnerDbContext>>()));
        return services.BuildServiceProvider();
    }
}
