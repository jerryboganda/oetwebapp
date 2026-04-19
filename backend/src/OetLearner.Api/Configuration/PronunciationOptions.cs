namespace OetLearner.Api.Configuration;

/// <summary>
/// Pronunciation subsystem configuration. Controls ASR provider selection,
/// free-tier entitlement, and upload retention. See docs/PRONUNCIATION.md §6.
/// </summary>
public class PronunciationOptions
{
    public const string SectionName = "Pronunciation";

    /// <summary>
    /// Which ASR provider services pronunciation scoring. Valid values:
    /// - "azure"   : Azure Speech SDK Pronunciation Assessment (best phoneme detail)
    /// - "whisper" : OpenAI/Groq Whisper word-level transcript + grounded AI phoneme scoring
    /// - "mock"    : deterministic stub that returns plausible scores from reference text
    ///               density. Used when no real provider is configured.
    /// - "auto"    : try azure first, fall back to whisper if azure credentials missing
    ///               or azure returns an error.
    /// </summary>
    public string Provider { get; set; } = "auto";

    /// <summary>Azure Speech service subscription key (blank = disabled).</summary>
    public string AzureSpeechKey { get; set; } = string.Empty;

    /// <summary>Azure Speech service region, e.g. "uksouth", "westeurope".</summary>
    public string AzureSpeechRegion { get; set; } = string.Empty;

    /// <summary>Azure locale for ASR. OET uses BrE "en-GB" by default; AmE candidates
    /// can be configured per user.</summary>
    public string AzureLocale { get; set; } = "en-GB";

    /// <summary>Whisper provider base URL (OpenAI = https://api.openai.com/v1,
    /// Groq = https://api.groq.com/openai/v1). Blank = disabled.</summary>
    public string WhisperBaseUrl { get; set; } = string.Empty;

    /// <summary>Whisper API key (blank = disabled).</summary>
    public string WhisperApiKey { get; set; } = string.Empty;

    /// <summary>Whisper model name (e.g. "whisper-1", "whisper-large-v3").</summary>
    public string WhisperModel { get; set; } = "whisper-1";

    /// <summary>Maximum allowed audio upload size per attempt, in bytes. Default 15 MB.</summary>
    public long MaxAudioBytes { get; set; } = 15 * 1024 * 1024;

    /// <summary>Permitted audio MIME types for learner uploads.</summary>
    public string[] AllowedMimeTypes { get; set; } =
    {
        "audio/webm",
        "audio/ogg",
        "audio/mpeg",
        "audio/mp4",
        "audio/wav",
        "audio/x-wav",
        "audio/aac",
    };

    /// <summary>How many days to retain learner audio before the cleanup worker deletes it.
    /// Scoring results persist forever; the raw recording is only needed transiently.</summary>
    public int AudioRetentionDays { get; set; } = 45;

    /// <summary>Free-tier weekly attempt limit across all drills. Set to -1 to disable throttling.</summary>
    public int FreeTierWeeklyAttemptLimit { get; set; } = 20;

    /// <summary>Rolling window (days) that <see cref="FreeTierWeeklyAttemptLimit"/> applies over.</summary>
    public int FreeTierWindowDays { get; set; } = 7;
}
