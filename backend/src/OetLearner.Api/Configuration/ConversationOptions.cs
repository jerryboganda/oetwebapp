namespace OetLearner.Api.Configuration;

public class ConversationOptions
{
    public const string SectionName = "Conversation";

    public bool Enabled { get; set; } = true;

    // ASR
    public string AsrProvider { get; set; } = "auto";
    public string AzureSpeechKey { get; set; } = string.Empty;
    public string AzureSpeechRegion { get; set; } = string.Empty;
    public string AzureLocale { get; set; } = "en-GB";
    public string WhisperBaseUrl { get; set; } = string.Empty;
    public string WhisperApiKey { get; set; } = string.Empty;
    public string WhisperModel { get; set; } = "whisper-1";
    public string DeepgramApiKey { get; set; } = string.Empty;
    public string DeepgramModel { get; set; } = "nova-2-medical";
    public string DeepgramLanguage { get; set; } = "en-GB";

    // Realtime STT
    public bool RealtimeSttEnabled { get; set; } = false;
    public string RealtimeAsrProvider { get; set; } = "mock";
    public bool RealtimeSttFallbackToBatch { get; set; } = true;
    public int RealtimeSttMaxChunkBytes { get; set; } = 256 * 1024;
    public int RealtimeSttPartialMinIntervalMs { get; set; } = 350;
    public int RealtimeSttTurnIdleTimeoutSeconds { get; set; } = 15;
    public int RealtimeSttMaxConcurrentStreamsPerUser { get; set; } = 1;
    public int RealtimeSttMaxAudioSecondsPerSession { get; set; } = 360;
    public int RealtimeSttDailyAudioSecondsPerUser { get; set; } = 3600;
    public decimal RealtimeSttMonthlyBudgetCapUsd { get; set; } = 100m;
    public string RealtimeSttConsentVersion { get; set; } = "realtime-stt-v1-2026-05-14";
    public string RealtimeSttRollbackMode { get; set; } = "disable-conversation-audio";
    public string ElevenLabsSttApiKey { get; set; } = string.Empty;
    public string ElevenLabsSttBaseUrl { get; set; } = "https://api.elevenlabs.io/v1";
    public string ElevenLabsSttModel { get; set; } = "scribe_v2_realtime";
    public string ElevenLabsSttLanguage { get; set; } = "auto";
    public string ElevenLabsSttAudioFormat { get; set; } = "pcm_s16le_16";
    public string ElevenLabsSttCommitStrategy { get; set; } = "manual";
    public string ElevenLabsSttKeytermsCsv { get; set; } = string.Empty;
    public bool ElevenLabsSttEnableProviderLogging { get; set; } = false;
    public int ElevenLabsSttTokenTtlSeconds { get; set; } = 900;

    // TTS
    public string TtsProvider { get; set; } = "auto";
    public string AzureTtsDefaultVoice { get; set; } = "en-GB-SoniaNeural";
    public string ElevenLabsApiKey { get; set; } = string.Empty;
    public string ElevenLabsDefaultVoiceId { get; set; } = "21m00Tcm4TlvDq8ikWAM";
    public string ElevenLabsModel { get; set; } = "eleven_multilingual_v2";
    public string CosyVoiceBaseUrl { get; set; } = string.Empty;
    public string CosyVoiceApiKey { get; set; } = string.Empty;
    public string CosyVoiceDefaultVoice { get; set; } = "default";
    public string ChatTtsBaseUrl { get; set; } = string.Empty;
    public string ChatTtsApiKey { get; set; } = string.Empty;
    public string ChatTtsDefaultVoice { get; set; } = "default";
    public string GptSoVitsBaseUrl { get; set; } = string.Empty;
    public string GptSoVitsApiKey { get; set; } = string.Empty;
    public string GptSoVitsDefaultVoice { get; set; } = "default";

    // Audio
    public long MaxAudioBytes { get; set; } = 15 * 1024 * 1024;
    public string[] AllowedMimeTypes { get; set; } =
    {
        "audio/webm", "audio/ogg", "audio/mpeg", "audio/mp4", "audio/wav", "audio/x-wav",
    };
    public int AudioRetentionDays { get; set; } = 30;

    // Session shape
    public int PrepDurationSeconds { get; set; } = 120;
    public int MaxSessionDurationSeconds { get; set; } = 360;
    public int MaxTurnDurationSeconds { get; set; } = 60;
    public string[] EnabledTaskTypes { get; set; } = { "oet-roleplay", "oet-handover" };

    // Entitlement
    public int FreeTierSessionsLimit { get; set; } = 3;
    public int FreeTierWindowDays { get; set; } = 7;

    // AI behaviour
    public string ReplyModel { get; set; } = "";
    public string EvaluationModel { get; set; } = "";
    public double ReplyTemperature { get; set; } = 0.6;
    public double EvaluationTemperature { get; set; } = 0.1;
}
