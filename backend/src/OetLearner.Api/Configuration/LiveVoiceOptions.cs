namespace OetLearner.Api.Configuration;

/// <summary>
/// Server-only configuration for the native realtime Speaking providers.
/// One provider session serves one attempt. The provider for a NEW attempt is
/// health-ordered (<see cref="PrimaryProvider"/> first, the other configured
/// provider when the primary's circuit is open) and a failed creation may be
/// retried by the browser on the next candidate. Provider errors are returned
/// to the caller and are never converted into a text, mock, or
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

    /// <summary>Kept so existing configuration still binds; it no longer has any effect. A Gemini
    /// token always lives until the role-play's hard stop plus 15 s (at most 1800 s from minting), so
    /// no value here can cut a conversation short (a 90 s lifetime did on 25 Sep 2026), and a larger
    /// one can never outlive the hard stop.</summary>
    public int GeminiTokenLifetimeSeconds { get; set; } = 900;
    public int GeminiNewSessionLifetimeSeconds { get; set; } = 60;

    /// <summary>Timeout for one provider session-creation call (clamped 2..20). The learner is
    /// already inside the timed role-play, so a slow provider must give way to the next candidate
    /// quickly, and the ceiling stays below the browser's own 22 s create-call timeout so a slow
    /// provider always ends in the server's 503 (fail over) and never in a client-side timeout.</summary>
    public int ProviderRequestTimeoutSeconds { get; set; } = 10;

    /// <summary>The provider session-creation timeout actually applied: <see cref="ProviderRequestTimeoutSeconds"/>
    /// clamped to 2..20 s.</summary>
    public TimeSpan ProviderRequestTimeout() => TimeSpan.FromSeconds(Math.Clamp(ProviderRequestTimeoutSeconds, 2, 20));

    /// <summary>Server-side ceiling on one role-play (clamped 180..1800 s). A card's own
    /// <c>RolePlayTimeSeconds</c> above this is capped to it.</summary>
    public int MaxRoleplaySeconds { get; set; } = 600;

    /// <summary>Slack after the role-play deadline before the server force-ends the session and
    /// closes provider sessions (clamped 0..120 s). Covers the client's own stop and flush.</summary>
    public int HardStopGraceSeconds { get; set; } = 30;

    /// <summary>How long after a role-play ended (or passed its hard stop) a late turn, transcript
    /// or recording is still accepted (clamped 60..3600 s), until grading freezes the transcript.</summary>
    public int TranscriptFlushGraceSeconds { get; set; } = 900;

    /// <summary>Provider sessions one role-play may open: retries, reloads and failover all count
    /// (clamped 1..10). A creation the provider refused is never recorded, so it does not count.</summary>
    public int MaxProviderSessionsPerRolePlay { get; set; } = 3;

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

    /// <summary>Both providers, <see cref="PrimaryProvider"/> first. A blank or unknown primary
    /// orders OpenAI first.</summary>
    public IReadOnlyList<string> ProviderOrder()
        => NormalizeProvider(PrimaryProvider) == LiveVoiceProviders.Gemini
            ? new[] { LiveVoiceProviders.Gemini, LiveVoiceProviders.OpenAi }
            : new[] { LiveVoiceProviders.OpenAi, LiveVoiceProviders.Gemini };

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

    public static IReadOnlyList<string> All { get; } = new[] { OpenAi, Gemini };
}
