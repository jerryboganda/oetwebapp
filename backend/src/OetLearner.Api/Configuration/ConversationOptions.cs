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
