using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// Idempotent startup hook that guarantees the three canonical AI provider
/// rows exist so an admin only has to paste a key in <c>/admin/ai-providers</c>
/// for the integration to start working — no hand-creating rows with magic
/// codes, no redeploy.
/// <list type="bullet">
///   <item><c>anthropic</c> — Claude Sonnet 4.6, the default contextual-
///   understanding model across every LLM feature route.</item>
///   <item><c>mistral-ocr</c> — document OCR used everywhere OCR is needed
///   (Listening Part A extraction + scanned-PDF fallback for all imports).</item>
///   <item><c>whisper-asr</c> — one STT key covering Speaking, Pronunciation,
///   and Conversation transcription.</item>
/// </list>
/// <para>
/// Safety: strictly additive. Rows are seeded <b>keyless</b>
/// (<see cref="AiProvider.EncryptedApiKey"/> empty) and are NEVER overwritten
/// once present, so an admin-pasted key or a row already created by
/// <see cref="OetLearner.Api.Services.Voice.AiVoiceProviderSeeder"/> (which may
/// have created <c>whisper-asr</c> from conversation env options) is preserved.
/// Register this hook AFTER the voice seeder so the env-derived whisper row
/// wins creation.
/// </para>
/// <para>
/// Tolerates DB unavailable / migration pending — logs a warning and lets the
/// API boot, mirroring <see cref="OetLearner.Api.Services.Voice.AiVoiceProviderSeeder"/>.
/// </para>
/// </summary>
public sealed class CoreAiProviderSeeder(
    IServiceProvider services,
    ILogger<CoreAiProviderSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

            var seeds = BuildSeeds();
            var existingCodes = await db.AiProviders.AsNoTracking()
                .Where(p => seeds.Select(s => s.Code).Contains(p.Code))
                .Select(p => p.Code)
                .ToListAsync(cancellationToken);
            var existing = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);

            var now = DateTimeOffset.UtcNow;
            var inserted = 0;
            foreach (var s in seeds)
            {
                if (existing.Contains(s.Code)) continue;
                db.AiProviders.Add(new AiProvider
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Code = s.Code,
                    Name = s.Name,
                    Dialect = s.Dialect,
                    Category = s.Category,
                    BaseUrl = s.BaseUrl,
                    DefaultModel = s.DefaultModel,
                    EncryptedApiKey = string.Empty,   // admin pastes the key in the UI
                    ApiKeyHint = string.Empty,
                    AllowedModelsCsv = string.Empty,
                    PricePer1kPromptTokens = s.PricePer1kPromptTokens,
                    PricePer1kCompletionTokens = s.PricePer1kCompletionTokens,
                    RetryCount = 2,
                    CircuitBreakerThreshold = 5,
                    CircuitBreakerWindowSeconds = 30,
                    FailoverPriority = s.FailoverPriority,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                inserted++;
            }

            if (inserted > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation(
                    "CoreAiProviderSeeder: inserted {Count} canonical provider rows ({Codes}).",
                    inserted, string.Join(",", seeds.Where(s => !existing.Contains(s.Code)).Select(s => s.Code)));
            }
            else
            {
                logger.LogInformation("CoreAiProviderSeeder: all canonical provider rows already exist.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CoreAiProviderSeeder: skipped (DB unavailable or migration pending).");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Pure seed list — public for unit testing, no DB/DI.</summary>
    public static IReadOnlyList<CoreProviderSeed> BuildSeeds() => new[]
    {
        new CoreProviderSeed(
            Code: "anthropic",
            Name: "Anthropic (Claude)",
            Category: AiProviderCategory.TextChat,
            Dialect: AiProviderDialect.Anthropic,
            BaseUrl: "https://api.anthropic.com/v1",
            DefaultModel: "claude-sonnet-5",
            PricePer1kPromptTokens: 0.003m,
            PricePer1kCompletionTokens: 0.015m,
            FailoverPriority: 20),
        new CoreProviderSeed(
            Code: "mistral-ocr",
            Name: "Mistral OCR",
            Category: AiProviderCategory.Ocr,
            Dialect: AiProviderDialect.OpenAiCompatible,
            BaseUrl: "https://api.mistral.ai",
            DefaultModel: "mistral-ocr-latest",
            PricePer1kPromptTokens: 0m,
            PricePer1kCompletionTokens: 0m,
            FailoverPriority: 25),
        new CoreProviderSeed(
            Code: "whisper-asr",
            Name: "Whisper (Speech-to-Text)",
            Category: AiProviderCategory.Asr,
            Dialect: AiProviderDialect.WhisperAsr,
            BaseUrl: "https://api.openai.com/v1",
            DefaultModel: "whisper-1",
            PricePer1kPromptTokens: 0m,
            PricePer1kCompletionTokens: 0m,
            FailoverPriority: 27),
    };
}

/// <summary>Pure DTO describing a canonical AI provider row to seed.</summary>
public sealed record CoreProviderSeed(
    string Code,
    string Name,
    AiProviderCategory Category,
    AiProviderDialect Dialect,
    string BaseUrl,
    string DefaultModel,
    decimal PricePer1kPromptTokens,
    decimal PricePer1kCompletionTokens,
    int FailoverPriority);
