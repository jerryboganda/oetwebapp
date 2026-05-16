using Microsoft.Extensions.Configuration;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Security;

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

        if (string.IsNullOrWhiteSpace(aiProvider.ApiKey))
        {
            throw new InvalidOperationException(
                $"Production requires {AiProviderOptions.SectionName}:ApiKey so the grounded AI provider can be synchronized into the registry.");
        }

        if (!Uri.TryCreate(aiProvider.BaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttps
            || baseUri.IsLoopback
            || string.Equals(baseUri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Production requires {AiProviderOptions.SectionName}:BaseUrl to be an external https:// endpoint.");
        }
    }

    private static void ValidatePronunciation(PronunciationOptions options)
    {
        var requested = Normalize(options.Provider, "auto");
        if (requested == "mock")
            throw MockNotAllowed("Pronunciation ASR", "Pronunciation:Provider");

        var hasAzure = HasAzureSpeech(options.AzureSpeechKey, options.AzureSpeechRegion);
        var hasWhisper = HasPair(options.WhisperBaseUrl, options.WhisperApiKey);
        var configured = requested switch
        {
            "auto" => hasAzure || hasWhisper,
            "azure" => hasAzure,
            "whisper" => hasWhisper,
            _ => throw new InvalidOperationException($"Unsupported Pronunciation:Provider value '{options.Provider}'.")
        };

        if (!configured)
            throw new InvalidOperationException("Production requires a real Pronunciation ASR provider. Configure Pronunciation:AzureSpeechKey plus Pronunciation:AzureSpeechRegion, or Pronunciation:WhisperBaseUrl plus Pronunciation:WhisperApiKey.");
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
            throw new InvalidOperationException("Production requires a real Conversation ASR provider. Configure Conversation Azure Speech, Whisper, or Deepgram credentials.");
    }

    private static void ValidateConversationTts(ConversationOptions options)
    {
        var requested = Normalize(options.TtsProvider, "auto");
        if (requested == "off") return;
        if (requested == "mock")
            throw MockNotAllowed("Conversation TTS", "Conversation:TtsProvider");

        var hasAzure = HasAzureSpeech(options.AzureSpeechKey, options.AzureSpeechRegion);
        var hasElevenLabs = !string.IsNullOrWhiteSpace(options.ElevenLabsApiKey);
        var hasCosyVoice = HasPair(options.CosyVoiceBaseUrl, options.CosyVoiceApiKey);
        var hasChatTts = HasPair(options.ChatTtsBaseUrl, options.ChatTtsApiKey);
        var hasGptSoVits = HasPair(options.GptSoVitsBaseUrl, options.GptSoVitsApiKey);
        var configured = requested switch
        {
            "auto" => hasAzure || hasElevenLabs || hasCosyVoice || hasChatTts || hasGptSoVits,
            "azure" => hasAzure,
            "elevenlabs" => hasElevenLabs,
            "cosyvoice" => hasCosyVoice,
            "chattts" => hasChatTts,
            "gptsovits" => hasGptSoVits,
            _ => throw new InvalidOperationException($"Unsupported Conversation:TtsProvider value '{options.TtsProvider}'.")
        };

        if (!configured)
            throw new InvalidOperationException("Production requires a real Conversation TTS provider or Conversation:TtsProvider=off. Configure Azure Speech, ElevenLabs, CosyVoice, ChatTTS, or GPT-SoVITS credentials.");
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
