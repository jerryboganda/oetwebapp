using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Conversation;

/// <summary>
/// Resolves the effective <see cref="ConversationOptions"/> by merging the
/// env-var defaults (from <c>AiProviderOptions</c>-equivalent appsettings
/// binding) with DB overrides from the <c>ConversationSettings</c> singleton
/// row. Admins edit the DB row; requests see the merged view.
///
/// Cached in <see cref="IMemoryCache"/> for 30 seconds so a single
/// request burst does not hit the DB for every AI call. The admin PUT
/// endpoint invalidates the cache on save.
/// </summary>
public interface IConversationOptionsProvider
{
    Task<ConversationOptions> GetAsync(CancellationToken ct = default);
    void Invalidate();
}

public sealed class ConversationOptionsProvider(
    IOptions<ConversationOptions> baseOptions,
    IServiceScopeFactory scopeFactory,
    IMemoryCache cache,
    IDataProtectionProvider dp) : IConversationOptionsProvider
{
    private const string CacheKey = "conversation:effective-options:v1";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private readonly IDataProtector _protector = dp.CreateProtector("ConversationOptions.Secret.v1");

    public async Task<ConversationOptions> GetAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue<ConversationOptions>(CacheKey, out var cached) && cached is not null)
            return cached;

        var merged = Clone(baseOptions.Value);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var row = await db.ConversationSettings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == "default", ct);
        if (row is not null) ApplyOverrides(merged, row);

        // Phase 6c: registry-first voice credential overrides. After
        // ConversationSettings DB-row overrides are applied, consult the
        // unified AiProviders registry for any seeded voice rows that
        // carry an encrypted API key. When present, those credentials win
        // — this is the contract that lets admins rotate Azure / Whisper /
        // ElevenLabs keys from /admin/ai-providers without touching
        // .env.production. Rows with empty EncryptedApiKey are skipped so
        // existing options-only deployments keep working unchanged.
        var registry = scope.ServiceProvider.GetRequiredService<IAiProviderRegistry>();
        await ApplyRegistryVoiceOverridesAsync(merged, registry, ct);

        cache.Set(CacheKey, merged, CacheTtl);
        return merged;
    }

    public void Invalidate() => cache.Remove(CacheKey);

    private string? Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return null;
        try { return _protector.Unprotect(cipher); }
        catch { return null; }
    }

    public string Protect(string plain) => _protector.Protect(plain);

    private void ApplyOverrides(ConversationOptions o, ConversationSettingsRow r)
    {
        if (r.Enabled.HasValue) o.Enabled = r.Enabled.Value;
        if (!string.IsNullOrWhiteSpace(r.AsrProvider)) o.AsrProvider = r.AsrProvider;
        if (!string.IsNullOrWhiteSpace(r.TtsProvider)) o.TtsProvider = r.TtsProvider;

        var azureKey = Unprotect(r.AzureSpeechKeyEncrypted);
        if (!string.IsNullOrEmpty(azureKey)) o.AzureSpeechKey = azureKey;
        if (!string.IsNullOrWhiteSpace(r.AzureSpeechRegion)) o.AzureSpeechRegion = r.AzureSpeechRegion;
        if (!string.IsNullOrWhiteSpace(r.AzureLocale)) o.AzureLocale = r.AzureLocale;
        if (!string.IsNullOrWhiteSpace(r.AzureTtsDefaultVoice)) o.AzureTtsDefaultVoice = r.AzureTtsDefaultVoice;

        if (!string.IsNullOrWhiteSpace(r.WhisperBaseUrl)) o.WhisperBaseUrl = r.WhisperBaseUrl;
        var whisperKey = Unprotect(r.WhisperApiKeyEncrypted);
        if (!string.IsNullOrEmpty(whisperKey)) o.WhisperApiKey = whisperKey;
        if (!string.IsNullOrWhiteSpace(r.WhisperModel)) o.WhisperModel = r.WhisperModel;

        var deepgramKey = Unprotect(r.DeepgramApiKeyEncrypted);
        if (!string.IsNullOrEmpty(deepgramKey)) o.DeepgramApiKey = deepgramKey;
        if (!string.IsNullOrWhiteSpace(r.DeepgramModel)) o.DeepgramModel = r.DeepgramModel;
        if (!string.IsNullOrWhiteSpace(r.DeepgramLanguage)) o.DeepgramLanguage = r.DeepgramLanguage;

        if (r.RealtimeSttEnabled.HasValue) o.RealtimeSttEnabled = r.RealtimeSttEnabled.Value;
        if (!string.IsNullOrWhiteSpace(r.RealtimeAsrProvider)) o.RealtimeAsrProvider = r.RealtimeAsrProvider;
        if (r.RealtimeSttAllowRealProvider.HasValue) o.RealtimeSttAllowRealProvider = r.RealtimeSttAllowRealProvider.Value;
        if (r.RealtimeSttRealProviderProductionAuthorized.HasValue) o.RealtimeSttRealProviderProductionAuthorized = r.RealtimeSttRealProviderProductionAuthorized.Value;
        if (r.RealtimeSttFallbackToBatch.HasValue) o.RealtimeSttFallbackToBatch = r.RealtimeSttFallbackToBatch.Value;
        if (r.RealtimeSttProviderConnectTimeoutSeconds.HasValue && r.RealtimeSttProviderConnectTimeoutSeconds.Value > 0) o.RealtimeSttProviderConnectTimeoutSeconds = r.RealtimeSttProviderConnectTimeoutSeconds.Value;
        if (r.RealtimeSttMaxChunkBytes.HasValue && r.RealtimeSttMaxChunkBytes.Value > 0) o.RealtimeSttMaxChunkBytes = r.RealtimeSttMaxChunkBytes.Value;
        if (r.RealtimeSttPartialMinIntervalMs.HasValue && r.RealtimeSttPartialMinIntervalMs.Value > 0) o.RealtimeSttPartialMinIntervalMs = r.RealtimeSttPartialMinIntervalMs.Value;
        if (r.RealtimeSttTurnIdleTimeoutSeconds.HasValue && r.RealtimeSttTurnIdleTimeoutSeconds.Value > 0) o.RealtimeSttTurnIdleTimeoutSeconds = r.RealtimeSttTurnIdleTimeoutSeconds.Value;
        if (r.RealtimeSttMaxConcurrentStreamsPerUser.HasValue && r.RealtimeSttMaxConcurrentStreamsPerUser.Value > 0) o.RealtimeSttMaxConcurrentStreamsPerUser = r.RealtimeSttMaxConcurrentStreamsPerUser.Value;
        if (r.RealtimeSttMaxAudioSecondsPerSession.HasValue && r.RealtimeSttMaxAudioSecondsPerSession.Value > 0) o.RealtimeSttMaxAudioSecondsPerSession = r.RealtimeSttMaxAudioSecondsPerSession.Value;
        if (r.RealtimeSttDailyAudioSecondsPerUser.HasValue && r.RealtimeSttDailyAudioSecondsPerUser.Value > 0) o.RealtimeSttDailyAudioSecondsPerUser = r.RealtimeSttDailyAudioSecondsPerUser.Value;
        if (r.RealtimeSttMonthlyBudgetCapUsd.HasValue && r.RealtimeSttMonthlyBudgetCapUsd.Value > 0) o.RealtimeSttMonthlyBudgetCapUsd = r.RealtimeSttMonthlyBudgetCapUsd.Value;
        if (r.RealtimeSttEstimatedCostUsdPerMinute.HasValue && r.RealtimeSttEstimatedCostUsdPerMinute.Value > 0) o.RealtimeSttEstimatedCostUsdPerMinute = r.RealtimeSttEstimatedCostUsdPerMinute.Value;
        if (!string.IsNullOrWhiteSpace(r.RealtimeSttProviderSessionTopology)) o.RealtimeSttProviderSessionTopology = r.RealtimeSttProviderSessionTopology;
        if (!string.IsNullOrWhiteSpace(r.RealtimeSttRegionId)) o.RealtimeSttRegionId = r.RealtimeSttRegionId;
        if (r.RealtimeSttAssumeLearnersAdult.HasValue) o.RealtimeSttAssumeLearnersAdult = r.RealtimeSttAssumeLearnersAdult.Value;
        if (r.RealtimeSttAllowManagedLearnerRealProvider.HasValue) o.RealtimeSttAllowManagedLearnerRealProvider = r.RealtimeSttAllowManagedLearnerRealProvider.Value;
        if (!string.IsNullOrWhiteSpace(r.RealtimeSttConsentVersion)) o.RealtimeSttConsentVersion = r.RealtimeSttConsentVersion;
        if (!string.IsNullOrWhiteSpace(r.RealtimeSttRollbackMode)) o.RealtimeSttRollbackMode = r.RealtimeSttRollbackMode;
        if (!string.IsNullOrWhiteSpace(r.RealtimeSttAllowedMimeTypesCsv))
        {
            var arr = r.RealtimeSttAllowedMimeTypesCsv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (arr.Length > 0) o.RealtimeSttAllowedMimeTypes = arr;
        }
        var elevenSttKey = Unprotect(r.ElevenLabsSttApiKeyEncrypted);
        if (!string.IsNullOrEmpty(elevenSttKey)) o.ElevenLabsSttApiKey = elevenSttKey;
        if (!string.IsNullOrWhiteSpace(r.ElevenLabsSttBaseUrl)) o.ElevenLabsSttBaseUrl = r.ElevenLabsSttBaseUrl;
        if (!string.IsNullOrWhiteSpace(r.ElevenLabsSttModel)) o.ElevenLabsSttModel = r.ElevenLabsSttModel;
        if (!string.IsNullOrWhiteSpace(r.ElevenLabsSttLanguage)) o.ElevenLabsSttLanguage = r.ElevenLabsSttLanguage;
        if (!string.IsNullOrWhiteSpace(r.ElevenLabsSttAudioFormat)) o.ElevenLabsSttAudioFormat = r.ElevenLabsSttAudioFormat;
        if (!string.IsNullOrWhiteSpace(r.ElevenLabsSttCommitStrategy)) o.ElevenLabsSttCommitStrategy = r.ElevenLabsSttCommitStrategy;
        if (!string.IsNullOrWhiteSpace(r.ElevenLabsSttKeytermsCsv)) o.ElevenLabsSttKeytermsCsv = r.ElevenLabsSttKeytermsCsv;
        if (r.ElevenLabsSttEnableProviderLogging.HasValue) o.ElevenLabsSttEnableProviderLogging = r.ElevenLabsSttEnableProviderLogging.Value;
        if (r.ElevenLabsSttTokenTtlSeconds.HasValue && r.ElevenLabsSttTokenTtlSeconds.Value > 0) o.ElevenLabsSttTokenTtlSeconds = r.ElevenLabsSttTokenTtlSeconds.Value;

        var elevenKey = Unprotect(r.ElevenLabsApiKeyEncrypted);
        if (!string.IsNullOrEmpty(elevenKey)) o.ElevenLabsApiKey = elevenKey;
        if (!string.IsNullOrWhiteSpace(r.ElevenLabsDefaultVoiceId)) o.ElevenLabsDefaultVoiceId = r.ElevenLabsDefaultVoiceId;
        if (!string.IsNullOrWhiteSpace(r.ElevenLabsModel)) o.ElevenLabsModel = r.ElevenLabsModel;

        if (!string.IsNullOrWhiteSpace(r.CosyVoiceBaseUrl)) o.CosyVoiceBaseUrl = r.CosyVoiceBaseUrl;
        var cosyKey = Unprotect(r.CosyVoiceApiKeyEncrypted);
        if (!string.IsNullOrEmpty(cosyKey)) o.CosyVoiceApiKey = cosyKey;
        if (!string.IsNullOrWhiteSpace(r.CosyVoiceDefaultVoice)) o.CosyVoiceDefaultVoice = r.CosyVoiceDefaultVoice;

        if (!string.IsNullOrWhiteSpace(r.ChatTtsBaseUrl)) o.ChatTtsBaseUrl = r.ChatTtsBaseUrl;
        var chatKey = Unprotect(r.ChatTtsApiKeyEncrypted);
        if (!string.IsNullOrEmpty(chatKey)) o.ChatTtsApiKey = chatKey;
        if (!string.IsNullOrWhiteSpace(r.ChatTtsDefaultVoice)) o.ChatTtsDefaultVoice = r.ChatTtsDefaultVoice;

        if (!string.IsNullOrWhiteSpace(r.GptSoVitsBaseUrl)) o.GptSoVitsBaseUrl = r.GptSoVitsBaseUrl;
        var gptsovitsKey = Unprotect(r.GptSoVitsApiKeyEncrypted);
        if (!string.IsNullOrEmpty(gptsovitsKey)) o.GptSoVitsApiKey = gptsovitsKey;
        if (!string.IsNullOrWhiteSpace(r.GptSoVitsDefaultVoice)) o.GptSoVitsDefaultVoice = r.GptSoVitsDefaultVoice;

        if (r.MaxAudioBytes.HasValue && r.MaxAudioBytes.Value > 0) o.MaxAudioBytes = r.MaxAudioBytes.Value;
        if (r.AudioRetentionDays.HasValue && r.AudioRetentionDays.Value > 0) o.AudioRetentionDays = r.AudioRetentionDays.Value;
        if (r.PrepDurationSeconds.HasValue && r.PrepDurationSeconds.Value > 0) o.PrepDurationSeconds = r.PrepDurationSeconds.Value;
        if (r.MaxSessionDurationSeconds.HasValue && r.MaxSessionDurationSeconds.Value > 0) o.MaxSessionDurationSeconds = r.MaxSessionDurationSeconds.Value;
        if (r.MaxTurnDurationSeconds.HasValue && r.MaxTurnDurationSeconds.Value > 0) o.MaxTurnDurationSeconds = r.MaxTurnDurationSeconds.Value;

        if (!string.IsNullOrWhiteSpace(r.EnabledTaskTypesCsv))
        {
            var arr = r.EnabledTaskTypesCsv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (arr.Length > 0) o.EnabledTaskTypes = arr;
        }

        if (r.FreeTierSessionsLimit.HasValue) o.FreeTierSessionsLimit = r.FreeTierSessionsLimit.Value;
        if (r.FreeTierWindowDays.HasValue && r.FreeTierWindowDays.Value > 0) o.FreeTierWindowDays = r.FreeTierWindowDays.Value;

        if (!string.IsNullOrWhiteSpace(r.ReplyModel)) o.ReplyModel = r.ReplyModel;
        if (!string.IsNullOrWhiteSpace(r.EvaluationModel)) o.EvaluationModel = r.EvaluationModel;
        if (r.ReplyTemperature.HasValue) o.ReplyTemperature = r.ReplyTemperature.Value;
        if (r.EvaluationTemperature.HasValue) o.EvaluationTemperature = r.EvaluationTemperature.Value;
    }

    /// <summary>
    /// Phase 6c — apply registry-first overrides for the four conversation
    /// voice provider codes (<c>azure-tts</c>, <c>azure-asr</c>,
    /// <c>elevenlabs-tts</c>, <c>whisper-asr</c>). Each lookup is skipped
    /// silently if the row is missing, inactive, or has an empty
    /// EncryptedApiKey, so this method is strictly additive and cannot
    /// break a deployment that has not yet seeded voice credentials.
    /// </summary>
    private static async Task ApplyRegistryVoiceOverridesAsync(
        ConversationOptions o, IAiProviderRegistry registry, CancellationToken ct)
    {
        // Azure TTS (region used for both ASR + TTS — Azure speech key is shared).
        var azureTts = await registry.FindByCodeAsync("azure-tts", ct);
        if (azureTts is { IsActive: true } && !string.IsNullOrEmpty(azureTts.EncryptedApiKey))
        {
            var key = await registry.GetPlatformKeyAsync(azureTts.Code, ct);
            if (!string.IsNullOrEmpty(key))
            {
                o.AzureSpeechKey = key;
                if (!string.IsNullOrWhiteSpace(azureTts.DefaultModel)) o.AzureTtsDefaultVoice = azureTts.DefaultModel;
                var region = ExtractAzureRegion(azureTts.BaseUrl);
                if (!string.IsNullOrWhiteSpace(region)) o.AzureSpeechRegion = region;
            }
        }

        // Azure ASR — separate row code, but in practice shares the same
        // Azure Speech subscription key. Override only when this row carries
        // a key (operator may genuinely have two distinct subscriptions).
        var azureAsr = await registry.FindByCodeAsync("azure-asr", ct);
        if (azureAsr is { IsActive: true } && !string.IsNullOrEmpty(azureAsr.EncryptedApiKey))
        {
            var key = await registry.GetPlatformKeyAsync(azureAsr.Code, ct);
            if (!string.IsNullOrEmpty(key))
            {
                o.AzureSpeechKey = key;
                if (!string.IsNullOrWhiteSpace(azureAsr.DefaultModel)) o.AzureLocale = azureAsr.DefaultModel;
                var region = ExtractAzureRegion(azureAsr.BaseUrl);
                if (!string.IsNullOrWhiteSpace(region)) o.AzureSpeechRegion = region;
            }
        }

        // ElevenLabs TTS.
        var eleven = await registry.FindByCodeAsync("elevenlabs-tts", ct);
        if (eleven is { IsActive: true } && !string.IsNullOrEmpty(eleven.EncryptedApiKey))
        {
            var key = await registry.GetPlatformKeyAsync(eleven.Code, ct);
            if (!string.IsNullOrEmpty(key))
            {
                o.ElevenLabsApiKey = key;
                if (!string.IsNullOrWhiteSpace(eleven.DefaultModel)) o.ElevenLabsModel = eleven.DefaultModel;
            }
        }

        // Whisper ASR — registry BaseUrl wins when set so admins can swap
        // OpenAI/Groq/Together endpoints without redeploying.
        var whisper = await registry.FindByCodeAsync("whisper-asr", ct);
        if (whisper is { IsActive: true } && !string.IsNullOrEmpty(whisper.EncryptedApiKey))
        {
            var key = await registry.GetPlatformKeyAsync(whisper.Code, ct);
            if (!string.IsNullOrEmpty(key))
            {
                o.WhisperApiKey = key;
                if (!string.IsNullOrWhiteSpace(whisper.BaseUrl)) o.WhisperBaseUrl = whisper.BaseUrl;
                if (!string.IsNullOrWhiteSpace(whisper.DefaultModel)) o.WhisperModel = whisper.DefaultModel;
            }
        }

        var elevenStt = await registry.FindByCodeAsync("elevenlabs-stt", ct);
        if (elevenStt is { IsActive: true } && !string.IsNullOrEmpty(elevenStt.EncryptedApiKey))
        {
            var key = await registry.GetPlatformKeyAsync(elevenStt.Code, ct);
            if (!string.IsNullOrEmpty(key))
            {
                o.ElevenLabsSttApiKey = key;
                if (!string.IsNullOrWhiteSpace(elevenStt.BaseUrl)) o.ElevenLabsSttBaseUrl = elevenStt.BaseUrl;
                if (!string.IsNullOrWhiteSpace(elevenStt.DefaultModel)) o.ElevenLabsSttModel = elevenStt.DefaultModel;
            }
        }
    }

    /// <summary>
    /// Extract the Azure region from a stored BaseUrl such as
    /// <c>https://uksouth.tts.speech.microsoft.com</c>. Returns null when
    /// the host does not match the Azure speech URL shape, leaving the
    /// option-supplied region intact.
    /// </summary>
    private static string? ExtractAzureRegion(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host;
        var dot = host.IndexOf('.');
        if (dot <= 0) return null;
        return host[..dot];
    }

    private static ConversationOptions Clone(ConversationOptions src) => new()
    {
        Enabled = src.Enabled,
        AsrProvider = src.AsrProvider,
        AzureSpeechKey = src.AzureSpeechKey,
        AzureSpeechRegion = src.AzureSpeechRegion,
        AzureLocale = src.AzureLocale,
        WhisperBaseUrl = src.WhisperBaseUrl,
        WhisperApiKey = src.WhisperApiKey,
        WhisperModel = src.WhisperModel,
        DeepgramApiKey = src.DeepgramApiKey,
        DeepgramModel = src.DeepgramModel,
        DeepgramLanguage = src.DeepgramLanguage,
        RealtimeSttEnabled = src.RealtimeSttEnabled,
        RealtimeAsrProvider = src.RealtimeAsrProvider,
        RealtimeSttAllowRealProvider = src.RealtimeSttAllowRealProvider,
        RealtimeSttRealProviderProductionAuthorized = src.RealtimeSttRealProviderProductionAuthorized,
        RealtimeSttFallbackToBatch = src.RealtimeSttFallbackToBatch,
        RealtimeSttProviderConnectTimeoutSeconds = src.RealtimeSttProviderConnectTimeoutSeconds,
        RealtimeSttMaxChunkBytes = src.RealtimeSttMaxChunkBytes,
        RealtimeSttPartialMinIntervalMs = src.RealtimeSttPartialMinIntervalMs,
        RealtimeSttTurnIdleTimeoutSeconds = src.RealtimeSttTurnIdleTimeoutSeconds,
        RealtimeSttMaxConcurrentStreamsPerUser = src.RealtimeSttMaxConcurrentStreamsPerUser,
        RealtimeSttMaxAudioSecondsPerSession = src.RealtimeSttMaxAudioSecondsPerSession,
        RealtimeSttDailyAudioSecondsPerUser = src.RealtimeSttDailyAudioSecondsPerUser,
        RealtimeSttMonthlyBudgetCapUsd = src.RealtimeSttMonthlyBudgetCapUsd,
        RealtimeSttEstimatedCostUsdPerMinute = src.RealtimeSttEstimatedCostUsdPerMinute,
        RealtimeSttProviderSessionTopology = src.RealtimeSttProviderSessionTopology,
        RealtimeSttRegionId = src.RealtimeSttRegionId,
        RealtimeSttAssumeLearnersAdult = src.RealtimeSttAssumeLearnersAdult,
        RealtimeSttAllowManagedLearnerRealProvider = src.RealtimeSttAllowManagedLearnerRealProvider,
        RealtimeSttConsentVersion = src.RealtimeSttConsentVersion,
        RealtimeSttRollbackMode = src.RealtimeSttRollbackMode,
        RealtimeSttAllowedMimeTypes = src.RealtimeSttAllowedMimeTypes.ToArray(),
        ElevenLabsSttApiKey = src.ElevenLabsSttApiKey,
        ElevenLabsSttBaseUrl = src.ElevenLabsSttBaseUrl,
        ElevenLabsSttModel = src.ElevenLabsSttModel,
        ElevenLabsSttLanguage = src.ElevenLabsSttLanguage,
        ElevenLabsSttAudioFormat = src.ElevenLabsSttAudioFormat,
        ElevenLabsSttCommitStrategy = src.ElevenLabsSttCommitStrategy,
        ElevenLabsSttKeytermsCsv = src.ElevenLabsSttKeytermsCsv,
        ElevenLabsSttEnableProviderLogging = src.ElevenLabsSttEnableProviderLogging,
        ElevenLabsSttTokenTtlSeconds = src.ElevenLabsSttTokenTtlSeconds,
        TtsProvider = src.TtsProvider,
        AzureTtsDefaultVoice = src.AzureTtsDefaultVoice,
        ElevenLabsApiKey = src.ElevenLabsApiKey,
        ElevenLabsDefaultVoiceId = src.ElevenLabsDefaultVoiceId,
        ElevenLabsModel = src.ElevenLabsModel,
        CosyVoiceBaseUrl = src.CosyVoiceBaseUrl,
        CosyVoiceApiKey = src.CosyVoiceApiKey,
        CosyVoiceDefaultVoice = src.CosyVoiceDefaultVoice,
        ChatTtsBaseUrl = src.ChatTtsBaseUrl,
        ChatTtsApiKey = src.ChatTtsApiKey,
        ChatTtsDefaultVoice = src.ChatTtsDefaultVoice,
        GptSoVitsBaseUrl = src.GptSoVitsBaseUrl,
        GptSoVitsApiKey = src.GptSoVitsApiKey,
        GptSoVitsDefaultVoice = src.GptSoVitsDefaultVoice,
        MaxAudioBytes = src.MaxAudioBytes,
        AllowedMimeTypes = src.AllowedMimeTypes,
        AudioRetentionDays = src.AudioRetentionDays,
        PrepDurationSeconds = src.PrepDurationSeconds,
        MaxSessionDurationSeconds = src.MaxSessionDurationSeconds,
        MaxTurnDurationSeconds = src.MaxTurnDurationSeconds,
        EnabledTaskTypes = src.EnabledTaskTypes,
        FreeTierSessionsLimit = src.FreeTierSessionsLimit,
        FreeTierWindowDays = src.FreeTierWindowDays,
        ReplyModel = src.ReplyModel,
        EvaluationModel = src.EvaluationModel,
        ReplyTemperature = src.ReplyTemperature,
        EvaluationTemperature = src.EvaluationTemperature,
    };
}
