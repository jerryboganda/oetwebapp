namespace OetLearner.Api.Configuration;

/// <summary>
/// Server-only configuration for the native realtime Speaking providers.
/// A session is bound to exactly one provider selected here. Provider errors
/// are returned to the caller and are never converted into a text, mock, or
/// batch-transcription fallback.
/// </summary>
public sealed class LiveVoiceOptions
{
    public const string SectionName = "LiveVoice";

    public string PrimaryProvider { get; set; } = "openai";

    public string OpenAiApiKey { get; set; } = string.Empty;
    public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1/live/sessions";
    public string OpenAiModelsBaseUrl { get; set; } = "https://api.openai.com/v1/models";
    public string OpenAiModel { get; set; } = "gpt-live-1";

    public string GeminiApiKey { get; set; } = string.Empty;
    public string GeminiBaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/auth_tokens";
    public string GeminiModelsBaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/models";
    public string GeminiWebSocketBaseUrl { get; set; } =
        "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContentConstrained";
    public string GeminiModel { get; set; } = "models/gemini-3.8-live";

    public int GeminiTokenLifetimeSeconds { get; set; } = 90;
    public int GeminiNewSessionLifetimeSeconds { get; set; } = 60;

    /// <summary>Days that provider transcript and connection audit metadata are retained.</summary>
    public int RetentionDays { get; set; } = 30;

    public bool IsOpenAiConfigured => !string.IsNullOrWhiteSpace(OpenAiApiKey)
        && Uri.TryCreate(OpenAiBaseUrl, UriKind.Absolute, out _)
        && Uri.TryCreate(OpenAiModelsBaseUrl, UriKind.Absolute, out _)
        && !string.IsNullOrWhiteSpace(OpenAiModel);

    public bool IsGeminiConfigured => !string.IsNullOrWhiteSpace(GeminiApiKey)
        && Uri.TryCreate(GeminiBaseUrl, UriKind.Absolute, out _)
        && Uri.TryCreate(GeminiModelsBaseUrl, UriKind.Absolute, out _)
        && Uri.TryCreate(GeminiWebSocketBaseUrl, UriKind.Absolute, out _)
        && !string.IsNullOrWhiteSpace(GeminiModel);

    public bool IsConfigured(string provider) => NormalizeProvider(provider) switch
    {
        LiveVoiceProviders.OpenAi => IsOpenAiConfigured,
        LiveVoiceProviders.Gemini => IsGeminiConfigured,
        _ => false,
    };

    public static string NormalizeProvider(string? provider)
        => provider?.Trim().ToLowerInvariant() switch
        {
            LiveVoiceProviders.OpenAi => LiveVoiceProviders.OpenAi,
            "openai-realtime" => LiveVoiceProviders.OpenAi,
            "realtime" => LiveVoiceProviders.OpenAi,
            LiveVoiceProviders.Gemini => LiveVoiceProviders.Gemini,
            "gemini-live" => LiveVoiceProviders.Gemini,
            _ => string.Empty,
        };
}

public static class LiveVoiceProviders
{
    public const string OpenAi = "openai";
    public const string Gemini = "gemini";
}
