using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Runtime overrides for <see cref="Configuration.ConversationOptions"/>.
/// Singleton row (Id = "default"): admins edit this via
/// <c>PUT /v1/admin/conversation/settings</c>. At request time, the
/// <c>ConversationOptionsProvider</c> merges these overrides on top of the
/// env-var defaults and returns a computed <c>ConversationOptions</c>.
///
/// Secret fields (API keys) are encrypted via ASP.NET Data Protection with
/// the purpose <c>ConversationOptions.Secret.v1</c> — never stored plain.
/// </summary>
public class ConversationSettingsRow
{
    [Key]
    [MaxLength(32)]
    public string Id { get; set; } = "default";

    public bool? Enabled { get; set; }

    [MaxLength(32)]
    public string? AsrProvider { get; set; }

    [MaxLength(32)]
    public string? TtsProvider { get; set; }

    // Azure Speech (both ASR + TTS)
    public string? AzureSpeechKeyEncrypted { get; set; }
    [MaxLength(64)] public string? AzureSpeechRegion { get; set; }
    [MaxLength(16)] public string? AzureLocale { get; set; }
    [MaxLength(128)] public string? AzureTtsDefaultVoice { get; set; }

    // Whisper (OpenAI-compatible)
    [MaxLength(256)] public string? WhisperBaseUrl { get; set; }
    public string? WhisperApiKeyEncrypted { get; set; }
    [MaxLength(64)] public string? WhisperModel { get; set; }

    // Deepgram
    public string? DeepgramApiKeyEncrypted { get; set; }
    [MaxLength(64)] public string? DeepgramModel { get; set; }
    [MaxLength(16)] public string? DeepgramLanguage { get; set; }

    // Realtime STT
    public bool? RealtimeSttEnabled { get; set; }
    [MaxLength(64)] public string? RealtimeAsrProvider { get; set; }
    public bool? RealtimeSttAllowRealProvider { get; set; }
    public bool? RealtimeSttRealProviderProductionAuthorized { get; set; }
    public bool? RealtimeSttFallbackToBatch { get; set; }
    public int? RealtimeSttProviderConnectTimeoutSeconds { get; set; }
    public int? RealtimeSttMaxChunkBytes { get; set; }
    public int? RealtimeSttPartialMinIntervalMs { get; set; }
    public int? RealtimeSttTurnIdleTimeoutSeconds { get; set; }
    public int? RealtimeSttMaxConcurrentStreamsPerUser { get; set; }
    public int? RealtimeSttMaxAudioSecondsPerSession { get; set; }
    public int? RealtimeSttDailyAudioSecondsPerUser { get; set; }
    public decimal? RealtimeSttMonthlyBudgetCapUsd { get; set; }
    public decimal? RealtimeSttEstimatedCostUsdPerMinute { get; set; }
    [MaxLength(64)] public string? RealtimeSttProviderSessionTopology { get; set; }
    [MaxLength(96)] public string? RealtimeSttRegionId { get; set; }
    public bool? RealtimeSttAssumeLearnersAdult { get; set; }
    public bool? RealtimeSttAllowManagedLearnerRealProvider { get; set; }
    [MaxLength(96)] public string? RealtimeSttConsentVersion { get; set; }
    [MaxLength(64)] public string? RealtimeSttRollbackMode { get; set; }
    [MaxLength(512)] public string? RealtimeSttAllowedMimeTypesCsv { get; set; }
    public string? ElevenLabsSttApiKeyEncrypted { get; set; }
    [MaxLength(256)] public string? ElevenLabsSttBaseUrl { get; set; }
    [MaxLength(64)] public string? ElevenLabsSttModel { get; set; }
    [MaxLength(16)] public string? ElevenLabsSttLanguage { get; set; }
    [MaxLength(64)] public string? ElevenLabsSttAudioFormat { get; set; }
    [MaxLength(32)] public string? ElevenLabsSttCommitStrategy { get; set; }
    [MaxLength(1024)] public string? ElevenLabsSttKeytermsCsv { get; set; }
    public bool? ElevenLabsSttEnableProviderLogging { get; set; }
    public int? ElevenLabsSttTokenTtlSeconds { get; set; }

    // ElevenLabs
    public string? ElevenLabsApiKeyEncrypted { get; set; }
    [MaxLength(64)] public string? ElevenLabsDefaultVoiceId { get; set; }
    [MaxLength(64)] public string? ElevenLabsModel { get; set; }

    // CosyVoice
    [MaxLength(256)] public string? CosyVoiceBaseUrl { get; set; }
    public string? CosyVoiceApiKeyEncrypted { get; set; }
    [MaxLength(64)] public string? CosyVoiceDefaultVoice { get; set; }

    // ChatTTS
    [MaxLength(256)] public string? ChatTtsBaseUrl { get; set; }
    public string? ChatTtsApiKeyEncrypted { get; set; }
    [MaxLength(64)] public string? ChatTtsDefaultVoice { get; set; }

    // GPT-SoVITS
    [MaxLength(256)] public string? GptSoVitsBaseUrl { get; set; }
    public string? GptSoVitsApiKeyEncrypted { get; set; }
    [MaxLength(64)] public string? GptSoVitsDefaultVoice { get; set; }

    // Audio pipeline
    public long? MaxAudioBytes { get; set; }
    public int? AudioRetentionDays { get; set; }

    // Session shape
    public int? PrepDurationSeconds { get; set; }
    public int? MaxSessionDurationSeconds { get; set; }
    public int? MaxTurnDurationSeconds { get; set; }

    /// <summary>CSV of enabled task types, e.g. <c>oet-roleplay,oet-handover</c>.</summary>
    [MaxLength(256)]
    public string? EnabledTaskTypesCsv { get; set; }

    // Entitlement
    public int? FreeTierSessionsLimit { get; set; }
    public int? FreeTierWindowDays { get; set; }

    // AI behaviour
    [MaxLength(128)] public string? ReplyModel { get; set; }
    [MaxLength(128)] public string? EvaluationModel { get; set; }
    public double? ReplyTemperature { get; set; }
    public double? EvaluationTemperature { get; set; }

    // Audit
    [MaxLength(64)]
    public string? UpdatedByUserId { get; set; }

    [MaxLength(128)]
    public string? UpdatedByUserName { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
