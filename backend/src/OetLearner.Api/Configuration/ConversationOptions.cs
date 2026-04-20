namespace OetLearner.Api.Configuration;

/// <summary>
/// AI Conversation subsystem configuration. Controls ASR + TTS provider
/// selection, free-tier entitlement, retention, session shape and AI behaviour.
/// Admin-editable via <c>/v1/admin/conversation/settings</c> (GET/PUT).
/// See docs/CONVERSATION.md.
/// </summary>
public class ConversationOptions
{
    public const string SectionName = "Conversation";

    /// <summary>Kill-switch. When false, the feature is disabled at the entry
    /// points (CreateSession + hub). UI gates via the <c>ai_conversation</c>
    /// feature flag.</summary>
    public bool Enabled { get; set; } = true;

    // ── ASR ─────────────────────────────────────────────────────────────────

    /// <summary>Active speech-to-text provider.
    /// Valid values: <c>azure</c>, <c>whisper</c>, <c>deepgram</c>, <c>mock</c>, <c>auto</c>.
    /// <c>auto</c> prefers Azure, then Whisper, then Deepgram, then Mock.</summary>
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

    // ── TTS ─────────────────────────────────────────────────────────────────

    /// <summary>Active text-to-speech provider.
    /// Valid values: <c>azure</c>, <c>elevenlabs</c>, <c>cosyvoice</c>, <c>chattts</c>, <c>gptsovits</c>, <c>mock</c>, <c>auto</c>, <c>off</c>.
    /// <c>off</c> disables TTS entirely (text-only mode).</summary>
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

    // ── Audio pipeline ──────────────────────────────────────────────────────

    public long MaxAudioBytes { get; set; } = 15 * 1024 * 1024;

    public string[] AllowedMimeTypes { get; set; } =
    {
        "audio/webm",
        "audio/ogg",
        "audio/mpeg",
        "audio/mp4",
        "audio/wav",
        "audio/x-wav",
    };

    /// <summary>Days to retain learner audio + AI-TTS audio before the retention
    /// worker deletes the blobs. Transcript metadata persists forever.</summary>
    public int AudioRetentionDays { get; set; } = 30;

    // ── Session shape ───────────────────────────────────────────────────────

    public int PrepDurationSeconds { get; set; } = 120;
    public int MaxSessionDurationSeconds { get; set; } = 360; // 6 min cap
    public int MaxTurnDurationSeconds { get; set; } = 60;

    /// <summary>Canonical enabled task types. Default reflects business: OET only.</summary>
    public string[] EnabledTaskTypes { get; set; } = { "oet-roleplay", "oet-handover" };

    // ── Entitlement ─────────────────────────────────────────────────────────

    /// <summary>Free-tier sessions per rolling window. -1 disables.</summary>
    public int FreeTierSessionsLimit { get; set; } = 3;
    public int FreeTierWindowDays { get; set; } = 7;

    // ── AI behaviour ────────────────────────────────────────────────────────

    /// <summary>AI partner reply model id. Empty = provider default.</summary>
    public string ReplyModel { get; set; } = "";

    /// <summary>AI evaluation model id. Empty = provider default.</summary>
    public string EvaluationModel { get; set; } = "";

    /// <summary>Temperature for reply generation. Lower = more consistent persona.</summary>
    public double ReplyTemperature { get; set; } = 0.6;

    /// <summary>Temperature for evaluation. Keep low for determinism.</summary>
    public double EvaluationTemperature { get; set; } = 0.1;
}
