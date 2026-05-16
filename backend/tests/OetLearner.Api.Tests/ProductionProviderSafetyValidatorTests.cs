using Microsoft.Extensions.Configuration;
using OetLearner.Api.Configuration;
using OetLearner.Api.Security;
using Xunit;

namespace OetLearner.Api.Tests;

public sealed class ProductionProviderSafetyValidatorTests
{
    [Fact]
    public void Validate_AllowsDevelopmentDefaults()
    {
        var config = new ConfigurationBuilder().Build();

        ProductionProviderSafetyValidator.Validate(config, isDevelopment: true);
    }

    [Fact]
    public void Validate_AllowsExplicitEmergencyOverride()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ProductionProviderSafetyValidator.AllowMockProvidersKey] = "true"
            })
            .Build();

        ProductionProviderSafetyValidator.Validate(config, isDevelopment: false);
    }

    [Fact]
    public void ValidateOptions_RejectsProductionPronunciationMockProvider()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProductionProviderSafetyValidator.ValidateOptions(
                ValidPronunciation(provider: "mock"),
                ValidConversation(),
                ValidAiProvider()));

        Assert.Contains("Pronunciation ASR cannot use mock provider", ex.Message);
    }

    [Fact]
    public void ValidateOptions_RejectsProductionConversationTtsMockProvider()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProductionProviderSafetyValidator.ValidateOptions(
                ValidPronunciation(),
                ValidConversation(ttsProvider: "mock"),
                ValidAiProvider()));

        Assert.Contains("Conversation TTS cannot use mock provider", ex.Message);
    }

    [Fact]
    public void ValidateOptions_RejectsProductionConversationAsrMockProvider()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProductionProviderSafetyValidator.ValidateOptions(
                ValidPronunciation(),
                ValidConversation(asrProvider: "mock"),
                ValidAiProvider()));

        Assert.Contains("Conversation ASR cannot use mock provider", ex.Message);
    }

    [Fact]
    public void ValidateOptions_RejectsProductionRealtimeConversationAsrMockProvider()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProductionProviderSafetyValidator.ValidateOptions(
                ValidPronunciation(),
                ValidConversation(realtimeEnabled: true, realtimeProvider: "mock"),
                ValidAiProvider()));

        Assert.Contains("Conversation realtime ASR cannot use mock provider", ex.Message);
    }

    [Fact]
    public void ValidateOptions_RejectsProductionRealtimeConversationAsrWithoutReadinessGates()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProductionProviderSafetyValidator.ValidateOptions(
                ValidPronunciation(),
                ValidConversation(realtimeEnabled: true, realtimeProvider: "elevenlabs-stt"),
                ValidAiProvider()));

        Assert.Contains("Production realtime Conversation ASR requires ElevenLabs STT credentials", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("round-robin")]
    [InlineData("unconfigured")]
    public void ValidateOptions_RejectsProductionRealtimeConversationAsrWithoutApprovedTopology(string topology)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProductionProviderSafetyValidator.ValidateOptions(
                ValidPronunciation(),
                ValidConversation(realtimeEnabled: true, realtimeReady: true, realtimeTopology: topology),
                ValidAiProvider()));

        Assert.Contains("approved provider topology", ex.Message);
    }

    [Fact]
    public void ValidateOptions_RejectsProductionManagedLearnerRealtimeProviderExposure()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProductionProviderSafetyValidator.ValidateOptions(
                ValidPronunciation(),
                ValidConversation(allowManagedLearnerRealtime: true),
                ValidAiProvider()));

        Assert.Contains("managed-learner real-provider speech processing", ex.Message);
    }

    [Fact]
    public void ValidateOptions_RejectsProductionMockAiProvider()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProductionProviderSafetyValidator.ValidateOptions(
                ValidPronunciation(),
                ValidConversation(),
                ValidAiProvider(providerId: "mock")));

        Assert.Contains("mock AI provider", ex.Message);
    }

    [Fact]
    public void ValidateOptions_RejectsProductionAiProviderWithoutApiKey()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProductionProviderSafetyValidator.ValidateOptions(
                ValidPronunciation(),
                ValidConversation(),
                ValidAiProvider(apiKey: "")));

        Assert.Contains("AI:ApiKey", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://inference.example.com/v1")]
    [InlineData("https://localhost:1234/v1")]
    public void ValidateOptions_RejectsProductionAiProviderWithoutExternalHttpsBaseUrl(string baseUrl)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProductionProviderSafetyValidator.ValidateOptions(
                ValidPronunciation(),
                ValidConversation(),
                ValidAiProvider(baseUrl: baseUrl)));

        Assert.Contains("AI:BaseUrl", ex.Message);
    }

    [Fact]
    public void ValidateOptions_AllowsRealSpeechProviders()
    {
        ProductionProviderSafetyValidator.ValidateOptions(
            ValidPronunciation(),
            ValidConversation(),
            ValidAiProvider());
    }

    [Theory]
    [InlineData("single-instance")]
    [InlineData("single-region-sticky")]
    [InlineData("distributed")]
    public void ValidateOptions_AllowsProductionRealtimeConversationAsrWithAllReadinessGates(string topology)
    {
        ProductionProviderSafetyValidator.ValidateOptions(
            ValidPronunciation(),
            ValidConversation(realtimeEnabled: true, realtimeReady: true, realtimeTopology: topology),
            ValidAiProvider());
    }

    private static PronunciationOptions ValidPronunciation(string provider = "azure") => new()
    {
        Provider = provider,
        AzureSpeechKey = "azure-key",
        AzureSpeechRegion = "uksouth",
    };

    private static ConversationOptions ValidConversation(
        string asrProvider = "deepgram",
        string ttsProvider = "elevenlabs",
        bool realtimeEnabled = false,
        string realtimeProvider = "elevenlabs-stt",
        bool allowManagedLearnerRealtime = false,
        bool realtimeReady = false,
        string realtimeTopology = "single-instance") => new()
    {
        Enabled = true,
        AsrProvider = asrProvider,
        DeepgramApiKey = "deepgram-key",
        TtsProvider = ttsProvider,
        ElevenLabsApiKey = "elevenlabs-key",
        RealtimeSttEnabled = realtimeEnabled,
        RealtimeAsrProvider = realtimeProvider,
        RealtimeSttAllowManagedLearnerRealProvider = allowManagedLearnerRealtime,
        ElevenLabsSttApiKey = realtimeReady ? "elevenlabs-stt-key" : string.Empty,
        RealtimeSttAllowRealProvider = realtimeReady,
        RealtimeSttRealProviderProductionAuthorized = realtimeReady,
        RealtimeSttEstimatedCostUsdPerMinute = realtimeReady ? 0.02m : 0m,
        RealtimeSttAssumeLearnersAdult = realtimeReady,
        RealtimeSttProviderSessionTopology = realtimeTopology,
        RealtimeSttRegionId = realtimeReady ? "eu-west-1" : string.Empty,
    };

    private static AiProviderOptions ValidAiProvider(
        string providerId = "digitalocean-serverless",
        string apiKey = "real-ai-provider-key",
        string baseUrl = "https://inference.do-ai.run/v1") => new()
    {
        ProviderId = providerId,
        BaseUrl = baseUrl,
        ApiKey = apiKey,
        DefaultModel = "glm-5",
    };
}
