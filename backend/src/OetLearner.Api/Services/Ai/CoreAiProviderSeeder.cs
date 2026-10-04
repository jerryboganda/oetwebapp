using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Seeding;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// Idempotent startup hook that guarantees the canonical AI provider
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
///   <item><c>typesafe-jev</c> — TypeSafe Jev typed-judgment API (Judgment
///   category, seeded INACTIVE; activate after the key + connection test pass).</item>
///   <item><c>opencode</c> — OpenCode inference gateway (OpenAI-compatible text chat, seeded
///   INACTIVE and keyless; explicit-only, reached solely through the learner chatbot's model
///   picker — see <see cref="AiProviderDefaultEligibility"/>).</item>
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
    /// <summary>
    /// Single source of truth for the "no <c>AiProvider.DefaultModel</c>
    /// configured" fallback used by every direct-call Anthropic caller
    /// (Listening Part A/B/C extraction + advisory scoring). Was previously
    /// duplicated as a private literal in three separate files — kept here so
    /// all three definitively agree with the value this seeder itself writes
    /// into new <c>anthropic</c> rows. This is a safe configured fallback
    /// (used only when a row legitimately has a blank <c>DefaultModel</c>),
    /// not a hard-coded route the registry/pricing configuration could
    /// otherwise resolve — it is deliberately NOT removed.
    /// </summary>
    public const string AnthropicDefaultModel = "claude-sonnet-5";

    /// <summary>Claude API ids the chatbot picker may offer. Kept next to
    /// the default so seeder + picker cannot drift.</summary>
    public const string AnthropicAllowedModelsCsv =
        "claude-sonnet-5,claude-haiku-5,claude-fable-5,claude-opus-4-8,claude-haiku-4-5-20251001";

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
                    AllowedModelsCsv = s.AllowedModelsCsv,
                    ReasoningEffort = s.ReasoningEffort,
                    PricePer1kPromptTokens = s.PricePer1kPromptTokens,
                    PricePer1kCompletionTokens = s.PricePer1kCompletionTokens,
                    RetryCount = 2,
                    CircuitBreakerThreshold = 5,
                    CircuitBreakerWindowSeconds = 30,
                    FailoverPriority = s.FailoverPriority,
                    IsActive = s.IsActive,
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
            BaseUrl: AnthropicProvider.DefaultBaseUrl + "/v1",
            DefaultModel: AnthropicDefaultModel,
            PricePer1kPromptTokens: 0.003m,
            PricePer1kCompletionTokens: 0.015m,
            FailoverPriority: 20,
            AllowedModelsCsv: AnthropicAllowedModelsCsv),
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
        // Speaking acoustic judge (owner spec 4 Oct 2026): an OpenAI audio-chat model hears the candidate's
        // clips. Keyless on purpose — AiProviderRegistry.GetPlatformKeyAsync falls back to the OpenAI account
        // that already funds live voice (LIVEVOICE__OPENAIAPIKEY); a key pasted here overrides it. Category
        // Asr keeps it out of every text-chat failover list; it is reached only by an explicit pin. The
        // model is editable in the admin UI. Prices are blended text+audio per 1k prompt tokens.
        new CoreProviderSeed(
            Code: AiProviderRegistry.SpeakingAudioProviderCode,
            Name: "OpenAI audio (Speaking acoustic judge)",
            Category: AiProviderCategory.Asr,
            Dialect: AiProviderDialect.OpenAiCompatible,
            BaseUrl: "https://api.openai.com/v1",
            DefaultModel: "gpt-audio-1.5",
            PricePer1kPromptTokens: 0.01m,
            PricePer1kCompletionTokens: 0.01m,
            FailoverPriority: 95),
        // TypeSafe Jev (typed judgments): keyless AND inactive. Not a chat provider
        // (Judgment category, no IAiModelProvider adapter); the row only holds the
        // admin-pasted key (read via GetPlatformKeyAsync, which ignores inactive rows
        // and falls back to TypeSafe__ApiKey) so the admin activates it after the
        // key + connection test pass. Code must equal TypeSafeOptions.ProviderCode.
        new CoreProviderSeed(
            Code: OetLearner.Api.Configuration.TypeSafeOptions.ProviderCode,
            Name: "TypeSafe Jev (typed judgments)",
            Category: AiProviderCategory.Judgment,
            Dialect: AiProviderDialect.TypeSafeJev,
            BaseUrl: "https://api.typesafe.ai",
            DefaultModel: "jev-1.13.0",
            PricePer1kPromptTokens: 0m,
            PricePer1kCompletionTokens: 0m,
            FailoverPriority: 28,
            IsActive: false),
        // OpenCode inference gateway (owner directive 2026-10-04, docs/AI-USAGE-POLICY.md §21): keyless AND
        // inactive. The OpenCode terms ("own internal use only") and the TV-029 provider inventory are owner
        // gates, so the admin pastes the key and ticks Active only after both. Even once active it is never
        // an implicit default (AiProviderDefaultEligibility): it is reached only through the learner
        // chatbot's per-thread model picker. Zen base URL; the admin applies the Go preset if the connection
        // test says the key belongs to that plan. AllowedModelsCsv is informational (the picker catalog
        // is code-owned).
        new CoreProviderSeed(
            Code: OpenCodeProviderDefaults.ProviderCode,
            Name: OpenCodeProviderDefaults.ProviderName,
            Category: AiProviderCategory.TextChat,
            Dialect: AiProviderDialect.OpenAiCompatible,
            BaseUrl: OpenCodeProviderDefaults.ZenBaseUrl,
            DefaultModel: OpenCodeProviderDefaults.DefaultModel,
            PricePer1kPromptTokens: OpenCodeProviderDefaults.DefaultPricePer1kPromptTokens,
            PricePer1kCompletionTokens: OpenCodeProviderDefaults.DefaultPricePer1kCompletionTokens,
            FailoverPriority: OpenCodeProviderDefaults.FailoverPriority,
            IsActive: false,
            AllowedModelsCsv: OpenCodeProviderDefaults.AllowedModelsCsv,
            ReasoningEffort: OpenCodeProviderDefaults.DefaultReasoningEffort),
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
    int FailoverPriority,
    bool IsActive = true,
    string AllowedModelsCsv = "",
    string? ReasoningEffort = null);
