using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

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
