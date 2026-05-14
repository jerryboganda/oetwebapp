using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Conversation.Asr;

namespace OetLearner.Api.Tests;

public sealed class ConversationRealtimeElevenLabsLiveSmokeTests
{
    [Fact]
    public async Task ConnectivitySmoke_StartsAndCompletesRealtimeSession_WhenExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ELEVENLABS_REALTIME_STT_LIVE_SMOKE"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var apiKey = Environment.GetEnvironmentVariable("Conversation__ElevenLabsSttApiKey")
            ?? Environment.GetEnvironmentVariable("ELEVENLABS_STT_API_KEY");
        Assert.False(string.IsNullOrWhiteSpace(apiKey));

        var options = new ConversationOptions
        {
            RealtimeSttEnabled = true,
            RealtimeAsrProvider = "elevenlabs-stt",
            RealtimeSttAllowRealProvider = true,
            RealtimeSttRealProviderProductionAuthorized = true,
            RealtimeSttEstimatedCostUsdPerMinute = 0.01m,
            RealtimeSttProviderSessionTopology = "single-instance",
            RealtimeSttRegionId = "live-smoke",
            RealtimeSttAssumeLearnersAdult = true,
            ElevenLabsSttApiKey = apiKey!,
            ElevenLabsSttAudioFormat = "pcm_16000",
            ElevenLabsSttEnableProviderLogging = false,
        };
        var provider = new ElevenLabsConversationRealtimeAsrProvider(
            new StubConversationOptionsProvider(options),
            NullLogger<ElevenLabsConversationRealtimeAsrProvider>.Instance);

        var sink = new CapturingTranscriptSink();
        await using var session = await provider.StartAsync(
            new ConversationRealtimeAsrStartRequest("live-smoke-session", "live-smoke-user", "live-smoke-stream", "audio/pcm", "en-GB", false, 5),
            sink,
            CancellationToken.None);
        await session.SendAudioAsync(new ConversationRealtimeAudioChunk(1, GenerateSilencePcm(TimeSpan.FromMilliseconds(500)), false, 0), CancellationToken.None);
        await session.CompleteAsync(CancellationToken.None);

        Assert.Null(sink.ProviderError);
    }

    private static byte[] GenerateSilencePcm(TimeSpan duration)
    {
        var samples = Math.Max(1, (int)(16000 * duration.TotalSeconds));
        return new byte[samples * 2];
    }

    private sealed class StubConversationOptionsProvider(ConversationOptions options) : IConversationOptionsProvider
    {
        public Task<ConversationOptions> GetAsync(CancellationToken ct = default) => Task.FromResult(options);
        public void Invalidate() { }
    }

    private sealed class CapturingTranscriptSink : IConversationRealtimeTranscriptSink
    {
        public ConversationAsrException? ProviderError { get; private set; }

        public Task OnPartialAsync(ConversationRealtimeTranscriptPartial partial, CancellationToken ct) => Task.CompletedTask;
        public Task OnFinalAsync(ConversationRealtimeTranscriptFinal final, CancellationToken ct) => Task.CompletedTask;
        public Task OnProviderErrorAsync(ConversationAsrException error, CancellationToken ct)
        {
            ProviderError = error;
            return Task.CompletedTask;
        }
    }
}