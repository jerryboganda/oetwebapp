using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Seeding;

namespace OetLearner.Api.Tests;

/// <summary>
/// Guards the OET end of the facade model-catalog contract: the seeder's
/// allowlist must mirror UBAG's adapter manifests (target|choice-value per
/// choice-kind setting) plus the transcription alias. The UBAG gateway test
/// TestFacadeChoiceModelIDsMatchResolver guards the UBAG end; a drift on
/// either side re-opens the "admin board offers IDs the facade rejects"
/// mismatch. Toggle-kind settings (gemini thinking, deepseek deepthink)
/// carry no labelled values and are intentionally absent. The board-curated
/// ChatGPT composite (GPT-5.6 Sol + Medium, bound as ONE pick) is absent too:
/// the allowlist gates free-form model strings, while the composite is a
/// board+facade contract pinned by the board test, not this list.
/// </summary>
public sealed class UbagProviderSeederTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;
    private readonly EphemeralDataProtectionProvider _dp = new();

    public UbagProviderSeederTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public void AllowedModelsCsv_MirrorsLiveFacadeCatalog()
    {
        var ids = UbagProviderRouteDefaults.AllowedModelsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Bare targets (operator defaults).
        foreach (var target in new[]
                 {
                     "mock", "deepseek_web", "chatgpt_web", "claude_web", "gemini_web",
                     "mistral_lechat", "perplexity_web", "duckai_web",
                     "generic_chat", "generic_form",
                 })
        {
            Assert.Contains(target, ids);
        }

        // Choice-kind settings incl. thinking levels (sorted setting keys,
        // manifest value order).
        foreach (var id in new[]
                 {
                     "mock|mock-fast", "mock|mock-deep", "mock|standard", "mock|extended",
                     "deepseek_web|Expert", "deepseek_web|Instant", "deepseek_web|Vision",
                     "chatgpt_web|GPT-5.6 Sol", "chatgpt_web|GPT-5.5", "chatgpt_web|GPT-5.4",
                     "chatgpt_web|GPT-5.3", "chatgpt_web|o3",
                     "chatgpt_web|Instant", "chatgpt_web|Medium", "chatgpt_web|High",
                     "gemini_web|3.8 Flash", "gemini_web|3.7 Flash", "gemini_web|3.6 Flash",
                     "gemini_web|3.5 Flash", "gemini_web|3.1 Flash-Lite", "gemini_web|3.1 Pro",
                     "duckai_web|GPT-5.6 Luna", "duckai_web|GPT-5.4 mini",
                     "duckai_web|Claude Haiku 4.5", "duckai_web|Mistral Small 4",
                     "duckai_web|gpt-oss 120B", "duckai_web|Gemma 4 31B",
                     "duckai_web|Fast", "duckai_web|Reasoning",
                     "whisper-1",
                 })
        {
            Assert.Contains(id, ids);
        }

        // Stale entries the facade rejects must stay out. The board-curated
        // composite is a board+facade contract, not an allowlist entry.
        Assert.DoesNotContain("gemini_web|3.1 Flash", ids);
        Assert.DoesNotContain("duckai_web|GPT-5.4 Mini", ids);
        Assert.DoesNotContain("chatgpt_web|GPT-5.6 Sol + Medium", ids);
    }

    [Fact]
    public async Task SeedAsync_RefreshesStaleAllowlistWithoutTouchingAdminFields()
    {
        await using (var db = new LearnerDbContext(_options))
        {
            await UbagProviderSeeder.SeedAsync(db, _dp);
        }

        await using (var db = new LearnerDbContext(_options))
        {
            var provider = await db.AiProviders
                .FirstOrDefaultAsync(p => p.Code == UbagProviderRouteDefaults.ProviderCode);
            Assert.NotNull(provider);
            provider!.AllowedModelsCsv = "mock,stale-entry";
            provider.DefaultModel = "deepseek_web|Instant";
            await db.SaveChangesAsync();
        }

        await using (var db = new LearnerDbContext(_options))
        {
            await UbagProviderSeeder.SeedAsync(db, _dp);
        }

        await using (var db = new LearnerDbContext(_options))
        {
            var provider = await db.AiProviders
                .FirstOrDefaultAsync(p => p.Code == UbagProviderRouteDefaults.ProviderCode);
            Assert.NotNull(provider);
            Assert.Equal(UbagProviderRouteDefaults.AllowedModelsCsv, provider!.AllowedModelsCsv);
            Assert.Equal("deepseek_web|Instant", provider.DefaultModel);
        }
    }
}
