using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Conversation;

namespace OetLearner.Api.Services.Voice;

/// <summary>
/// Phase 6b — Idempotent startup hook that backfills the
/// <c>AiProviders</c> table with one row per *configured* voice
/// provider (TTS / ASR / Phoneme). Runs once on host start.
/// <para>
/// Why: Phase 6 added the <see cref="AiProviderCategory"/> column and
/// the admin <c>/admin/ai-providers</c> tab can now filter by capability,
/// but until rows actually exist for the voice services there is nothing
/// to show. This seeder bridges the gap by reading existing
/// <see cref="ConversationOptions"/> and <see cref="PronunciationOptions"/>
/// configuration and writing matching rows so admins can immediately
/// see and (later) edit the voice providers from the same registry UI.
/// </para>
/// <para>
/// Safety: Strictly additive. Never overwrites an existing row keyed by
/// <see cref="AiProvider.Code"/>. Never touches live conversation /
/// pronunciation traffic — selectors continue to read credentials from
/// their existing options sources. The registry rows seeded here are
/// purely visibility scaffolding for the admin UI; selector refactor
/// to consume them lives in a follow-up phase.
/// </para>
/// <para>
/// Tolerates DB unavailable or migration not yet applied — logs a
/// warning and lets the API boot anyway, mirroring
/// <see cref="OetLearner.Api.Services.AiTools.AiToolCatalogSeederHostedService"/>.
/// </para>
/// </summary>
public sealed class AiVoiceProviderSeeder(
    IServiceProvider services,
    ILogger<AiVoiceProviderSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var convOptions = await scope.ServiceProvider
                .GetRequiredService<IConversationOptionsProvider>()
                .GetAsync(cancellationToken);
            var pronOptions = scope.ServiceProvider
                .GetRequiredService<IOptions<PronunciationOptions>>().Value;

            var seeds = BuildSeeds(convOptions, pronOptions);
            if (seeds.Count == 0)
            {
                logger.LogInformation("AiVoiceProviderSeeder: no configured voice providers to seed.");
                return;
            }

            var existingCodes = await db.AiProviders.AsNoTracking()
                .Where(p => seeds.Select(s => s.Code).Contains(p.Code))
                .Select(p => p.Code)
                .ToListAsync(cancellationToken);
            var existing = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);

            var now = DateTimeOffset.UtcNow;
            int inserted = 0;
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
                    EncryptedApiKey = string.Empty,  // Credentials remain in options for now.
                    ApiKeyHint = string.Empty,
                    AllowedModelsCsv = string.Empty,
                    PricePer1kPromptTokens = 0m,
                    PricePer1kCompletionTokens = 0m,
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
                    "AiVoiceProviderSeeder: inserted {Count} voice provider rows ({Codes}).",
                    inserted, string.Join(",", seeds.Where(s => !existing.Contains(s.Code)).Select(s => s.Code)));
            }
            else
            {
                logger.LogInformation("AiVoiceProviderSeeder: all configured voice rows already exist.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AiVoiceProviderSeeder: skipped (DB unavailable, options missing, or migration pending).");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Build the seed list from the current option values. Public for
    /// unit testing — pure function, no DB / DI dependencies.
    /// </summary>
    public static List<VoiceProviderSeed> BuildSeeds(
        ConversationOptions conversation,
        PronunciationOptions pronunciation)
    {
        var seeds = new List<VoiceProviderSeed>();

        // Conversation TTS — Azure
        if (!string.IsNullOrWhiteSpace(conversation.AzureSpeechKey)
            && !string.IsNullOrWhiteSpace(conversation.AzureSpeechRegion))
        {
            seeds.Add(new VoiceProviderSeed(
                Code: "azure-tts",
                Name: "Azure Speech (TTS)",
                Category: AiProviderCategory.Tts,
                Dialect: AiProviderDialect.AzureTts,
                BaseUrl: $"https://{conversation.AzureSpeechRegion}.tts.speech.microsoft.com",
                DefaultModel: conversation.AzureTtsDefaultVoice,
                FailoverPriority: 0));
        }

        // Conversation TTS — ElevenLabs
        if (!string.IsNullOrWhiteSpace(conversation.ElevenLabsApiKey))
        {
            seeds.Add(new VoiceProviderSeed(
                Code: "elevenlabs-tts",
                Name: "ElevenLabs (TTS)",
                Category: AiProviderCategory.Tts,
                Dialect: AiProviderDialect.ElevenLabsTts,
                BaseUrl: "https://api.elevenlabs.io/v1",
                DefaultModel: conversation.ElevenLabsModel,
                FailoverPriority: 1));
        }

        // Conversation ASR — Azure (shares speech key with TTS)
        if (!string.IsNullOrWhiteSpace(conversation.AzureSpeechKey)
            && !string.IsNullOrWhiteSpace(conversation.AzureSpeechRegion))
        {
            seeds.Add(new VoiceProviderSeed(
                Code: "azure-asr",
                Name: "Azure Speech (ASR)",
                Category: AiProviderCategory.Asr,
                Dialect: AiProviderDialect.AzureAsr,
                BaseUrl: $"https://{conversation.AzureSpeechRegion}.stt.speech.microsoft.com",
                DefaultModel: conversation.AzureLocale,
                FailoverPriority: 0));
        }

        // Conversation ASR — Whisper
        if (!string.IsNullOrWhiteSpace(conversation.WhisperApiKey)
            && !string.IsNullOrWhiteSpace(conversation.WhisperBaseUrl))
        {
            seeds.Add(new VoiceProviderSeed(
                Code: "whisper-asr",
                Name: "Whisper (ASR)",
                Category: AiProviderCategory.Asr,
                Dialect: AiProviderDialect.WhisperAsr,
                BaseUrl: conversation.WhisperBaseUrl,
                DefaultModel: conversation.WhisperModel,
                FailoverPriority: 1));
        }

        // Conversation realtime STT — ElevenLabs Scribe.
        if (!string.IsNullOrWhiteSpace(conversation.ElevenLabsSttApiKey))
        {
            seeds.Add(new VoiceProviderSeed(
                Code: "elevenlabs-stt",
                Name: "ElevenLabs Scribe (STT)",
                Category: AiProviderCategory.Asr,
                Dialect: AiProviderDialect.ElevenLabsStt,
                BaseUrl: string.IsNullOrWhiteSpace(conversation.ElevenLabsSttBaseUrl)
                    ? "https://api.elevenlabs.io/v1"
                    : conversation.ElevenLabsSttBaseUrl,
                DefaultModel: conversation.ElevenLabsSttModel,
                FailoverPriority: 2));
        }

        // Pronunciation — Azure phoneme assessment (separate code because
        // it uses Azure SDK pronunciation assessment, not plain ASR).
        if (!string.IsNullOrWhiteSpace(pronunciation.AzureSpeechKey)
            && !string.IsNullOrWhiteSpace(pronunciation.AzureSpeechRegion))
        {
            seeds.Add(new VoiceProviderSeed(
                Code: "azure-phoneme",
                Name: "Azure Pronunciation Assessment",
                Category: AiProviderCategory.Phoneme,
                Dialect: AiProviderDialect.AzurePhoneme,
                BaseUrl: $"https://{pronunciation.AzureSpeechRegion}.api.cognitive.microsoft.com",
                DefaultModel: pronunciation.AzureLocale,
                FailoverPriority: 0));
        }

        return seeds;
    }
}

/// <summary>
/// Pure DTO describing a single voice-provider row to seed.
/// </summary>
public sealed record VoiceProviderSeed(
    string Code,
    string Name,
    AiProviderCategory Category,
    AiProviderDialect Dialect,
    string BaseUrl,
    string DefaultModel,
    int FailoverPriority);
