using Microsoft.Extensions.Configuration;
using OetWithDrHesham.Api.Configuration;

namespace OetWithDrHesham.Api.Security;

public static class ProductionProviderSafetyValidator
{
    public const string AllowMockProvidersKey = "Features:AllowProductionMockProviders";
    private static readonly HashSet<string> AllowedRealtimeTopologies = new(StringComparer.OrdinalIgnoreCase)
    {
        "single-instance",
        "single-region-sticky",
        "distributed",
    };

    public static void Validate(IConfiguration configuration, bool isDevelopment)
    {
        if (isDevelopment) return;
        if (configuration.GetValue<bool>(AllowMockProvidersKey)) return;

        ValidateOptions(
            configuration.GetSection(PronunciationOptions.SectionName).Get<PronunciationOptions>() ?? new PronunciationOptions(),
            configuration.GetSection(ConversationOptions.SectionName).Get<ConversationOptions>() ?? new ConversationOptions(),
            configuration.GetSection(AiProviderOptions.SectionName).Get<AiProviderOptions>() ?? new AiProviderOptions());
    }

    public static void ValidateOptions(
        PronunciationOptions pronunciation,
        ConversationOptions conversation,
        AiProviderOptions aiProvider)
    {
        ValidateAiProvider(aiProvider);

        ValidatePronunciation(pronunciation);
        if (conversation.Enabled)
        {
            ValidateConversationAsr(conversation);
            ValidateConversationTts(conversation);
            ValidateRealtimeConversationAsr(conversation);
        }
    }

    private static void ValidateAiProvider(AiProviderOptions aiProvider)
    {
        if (string.IsNullOrWhiteSpace(aiProvider.ProviderId)
            || string.IsNullOrWhiteSpace(aiProvider.DefaultModel)
            || IsMock(aiProvider.ProviderId)
            || IsMock(aiProvider.DefaultModel))
        {
            throw new InvalidOperationException(
                $"Production cannot use the mock AI provider or mock AI model. Configure {AiProviderOptions.SectionName}:ProviderId/{AiProviderOptions.SectionName}:DefaultModel with a real provider, or set {AllowMockProvidersKey}=true only for an approved emergency.");
        }

        // ApiKey + BaseUrl are admin-configurable post-boot via /admin/settings (Phase 2 Runtime Settings).
        // Empty/missing values are tolerated at startup so admins can fill them through the UI; the
        // grounded AI gateway itself refuses calls until a valid key/URL is provided.
        if (string.IsNullOrWhiteSpace(aiProvider.ApiKey))
        {
            Console.Error.WriteLine($"[ProductionProviderSafetyValidator] WARN: {AiProviderOptions.SectionName}:ApiKey is empty. Configure via /admin/settings before AI features will function.");
        }

        if (!string.IsNullOrWhiteSpace(aiProvider.BaseUrl))
        {
            if (!Uri.TryCreate(aiProvider.BaseUrl, UriKind.Absolute, out var baseUri)
                || baseUri.Scheme != Uri.UriSchemeHttps
                || baseUri.IsLoopback
                || string.Equals(baseUri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Production requires {AiProviderOptions.SectionName}:BaseUrl to be an external https:// endpoint when set.");
            }
        }
    }

    private static void ValidatePronunciation(PronunciationOptions options)
    {
        var requested = Normalize(options.Provider, "auto");
        if (requested == "mock")
            throw MockNotAllowed("Pronunciation ASR", "Pronunciation:Provider");

        var hasAzure = HasAzureSpeech(options.AzureSpeechKey, options.AzureSpeechRegion);
        var hasGemini = HasPair(options.GeminiBaseUrl, options.GeminiApiKey);
        var hasWhisper = HasPair(options.WhisperBaseUrl, options.WhisperApiKey);
        var configured = requested switch
        {
            "auto" => hasAzure || hasGemini || hasWhisper,
            "azure" => hasAzure,
            "gemini" => hasGemini,
            "whisper" => hasWhisper,
            _ => throw new InvalidOperationException($"Unsupported Pronunciation:Provider value '{options.Provider}'.")
        };

        if (!configured)
        {
            Console.Error.WriteLine("[ProductionProviderSafetyValidator] WARN: Pronunciation ASR provider credentials missing. Configure via /admin/settings before pronunciation grading will function.");
        }
    }

    private static void ValidateConversationAsr(ConversationOptions options)
    {
        var requested = Normalize(options.AsrProvider, "auto");
        if (requested == "mock")
            throw MockNotAllowed("Conversation ASR", "Conversation:AsrProvider");

        var hasAzure = HasAzureSpeech(options.AzureSpeechKey, options.AzureSpeechRegion);
        var hasWhisper = HasPair(options.WhisperBaseUrl, options.WhisperApiKey);
        var hasDeepgram = !string.IsNullOrWhiteSpace(options.DeepgramApiKey);
        var configured = requested switch
        {
            "auto" => hasAzure || hasWhisper || hasDeepgram,
            "azure" => hasAzure,
            "whisper" => hasWhisper,
            "deepgram" => hasDeepgram,
            _ => throw new InvalidOperationException($"Unsupported Conversation:AsrProvider value '{options.AsrProvider}'.")
        };

        if (!configured)
        {
            Console.Error.WriteLine("[ProductionProviderSafetyValidator] WARN: Conversation ASR provider credentials missing. Configure via /admin/settings before conversation ASR will function.");
        }
    }

    private static void ValidateConversationTts(ConversationOptions options)
    {
        var requested = Normalize(options.TtsProvider, "auto");
        if (requested == "off") return;
        if (requested == "mock")
            throw MockNotAllowed("Conversation TTS", "Conversation:TtsProvider");

        // ElevenLabs is the only supported TTS provider. "auto"/"elevenlabs"
        // resolve to ElevenLabs; any other (legacy) value warns rather than
        // throwing so a stale env var cannot crash production boot.
        var hasElevenLabs = !string.IsNullOrWhiteSpace(options.ElevenLabsApiKey);
        if (requested is not ("auto" or "elevenlabs"))
        {
            Console.Error.WriteLine($"[ProductionProviderSafetyValidator] WARN: Conversation:TtsProvider value '{options.TtsProvider}' is no longer supported — ElevenLabs is the only TTS provider. Treating as 'elevenlabs'. Set Conversation:TtsProvider=elevenlabs or off to silence this warning.");
        }

        if (!hasElevenLabs)
        {
            Console.Error.WriteLine("[ProductionProviderSafetyValidator] WARN: ElevenLabs TTS API key missing. Configure via /admin/voice-design or set Conversation:TtsProvider=off.");
        }
    }

    private static void ValidateRealtimeConversationAsr(ConversationOptions options)
    {
        if (options.RealtimeSttAllowManagedLearnerRealProvider)
        {
            throw new InvalidOperationException("Production realtime Conversation ASR cannot enable managed-learner real-provider speech processing until sponsor/school/minor privacy approval is complete.");
        }

        if (!options.RealtimeSttEnabled) return;

        var requested = Normalize(options.RealtimeAsrProvider, "mock");
        if (requested == "mock")
            throw MockNotAllowed("Conversation realtime ASR", "Conversation:RealtimeAsrProvider");

        var isElevenLabs = requested is "elevenlabs" or "elevenlabs-stt" or "elevenlabs-scribe";
        if (!isElevenLabs)
            throw new InvalidOperationException($"Unsupported Conversation:RealtimeAsrProvider value '{options.RealtimeAsrProvider}'.");

        if (string.IsNullOrWhiteSpace(options.ElevenLabsSttApiKey)
            || !options.RealtimeSttAllowRealProvider
            || !options.RealtimeSttRealProviderProductionAuthorized
            || options.RealtimeSttMonthlyBudgetCapUsd <= 0
            || options.RealtimeSttDailyAudioSecondsPerUser <= 0
            || options.RealtimeSttEstimatedCostUsdPerMinute <= 0
            || !options.RealtimeSttAssumeLearnersAdult
            || string.IsNullOrWhiteSpace(options.RealtimeSttRegionId)
            || !AllowedRealtimeTopologies.Contains(Normalize(options.RealtimeSttProviderSessionTopology, string.Empty)))
        {
            throw new InvalidOperationException("Production realtime Conversation ASR requires ElevenLabs STT credentials, approved provider topology, and all real-provider readiness gates.");
        }
    }

    private static InvalidOperationException MockNotAllowed(string subsystem, string configKey)
        => new($"{subsystem} cannot use mock provider in production. Configure a real provider for {configKey}, or set {AllowMockProvidersKey}=true only for an approved emergency.");

    private static string Normalize(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();

    private static bool IsMock(string? value)
        => string.Equals(value?.Trim(), "mock", StringComparison.OrdinalIgnoreCase);

    private static bool HasAzureSpeech(string? key, string? region)
        => !string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(region);

    private static bool HasPair(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second);
}
