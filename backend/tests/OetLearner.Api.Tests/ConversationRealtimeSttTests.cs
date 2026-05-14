using System.Net.WebSockets;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Conversation.Asr;

namespace OetLearner.Api.Tests;

public sealed class ConversationRealtimeSttTests
{
    [Fact]
    public async Task RealtimeSelector_ReturnsNull_WhenFeatureDisabled()
    {
        var selector = BuildSelector(new ConversationOptions { RealtimeSttEnabled = false });

        var provider = await selector.TrySelectRealtimeAsync();

        Assert.Null(provider);
    }

    [Fact]
    public async Task RealtimeSelector_ReturnsMock_WhenFeatureEnabled()
    {
        var selector = BuildSelector(new ConversationOptions
        {
            RealtimeSttEnabled = true,
            RealtimeAsrProvider = "mock",
        });

        var provider = await selector.TrySelectRealtimeAsync();

        Assert.NotNull(provider);
        Assert.Equal("mock", provider.Name);
    }

    [Theory]
    [InlineData("elevenlabs")]
    [InlineData("elevenlabs-stt")]
    [InlineData("elevenlabs-scribe")]
    public async Task RealtimeSelector_ReturnsNull_ForElevenLabsAliasesUntilProviderRegistered(string providerName)
    {
        var selector = BuildSelector(new ConversationOptions
        {
            RealtimeSttEnabled = true,
            RealtimeAsrProvider = providerName,
        });

        var provider = await selector.TrySelectRealtimeAsync();

        Assert.Null(provider);
    }

    [Theory]
    [InlineData("elevenlabs")]
    [InlineData("elevenlabs-stt")]
    [InlineData("elevenlabs-scribe")]
    public async Task RealtimeSelector_ReturnsNull_ForElevenLabsUntilReadinessGateEnabled(string providerName)
    {
        var options = new ConversationOptions
        {
            RealtimeSttEnabled = true,
            RealtimeAsrProvider = providerName,
            ElevenLabsSttApiKey = "dev-only-test-key",
        };
        var optionsProvider = new StubConversationOptionsProvider(options);
        var selector = new ConversationAsrProviderSelector(
            [new MockConversationAsrProvider()],
            [new MockConversationRealtimeAsrProvider(), new ElevenLabsConversationRealtimeAsrProvider(optionsProvider, NullLogger<ElevenLabsConversationRealtimeAsrProvider>.Instance)],
            optionsProvider,
            NullLogger<ConversationAsrProviderSelector>.Instance);

        var provider = await selector.TrySelectRealtimeAsync();

        Assert.Null(provider);
    }

    [Theory]
    [InlineData("elevenlabs")]
    [InlineData("elevenlabs-stt")]
    [InlineData("elevenlabs-scribe")]
    public async Task RealtimeSelector_ReturnsElevenLabs_WhenProviderRegisteredConfiguredAndReadinessGateEnabled(string providerName)
    {
        var options = new ConversationOptions
        {
            RealtimeSttEnabled = true,
            RealtimeAsrProvider = providerName,
            RealtimeSttAllowRealProvider = true,
            RealtimeSttRealProviderProductionAuthorized = true,
            RealtimeSttEstimatedCostUsdPerMinute = 0.01m,
            RealtimeSttProviderSessionTopology = "single-instance",
            RealtimeSttRegionId = "test-region",
            RealtimeSttAssumeLearnersAdult = true,
            ElevenLabsSttApiKey = "dev-only-test-key",
        };
        var optionsProvider = new StubConversationOptionsProvider(options);
        var selector = new ConversationAsrProviderSelector(
            [new MockConversationAsrProvider()],
            [new MockConversationRealtimeAsrProvider(), new ElevenLabsConversationRealtimeAsrProvider(optionsProvider, NullLogger<ElevenLabsConversationRealtimeAsrProvider>.Instance)],
            optionsProvider,
            NullLogger<ConversationAsrProviderSelector>.Instance);

        var provider = await selector.TrySelectRealtimeAsync();

        Assert.NotNull(provider);
        Assert.Equal("elevenlabs-stt", provider.Name);
    }

    [Fact]
    public async Task RealtimeSelector_ReturnsNull_ForRealProvider_WhenProductionAuthorizationMissing()
    {
        var options = new ConversationOptions
        {
            RealtimeSttEnabled = true,
            RealtimeAsrProvider = "elevenlabs-stt",
            RealtimeSttAllowRealProvider = true,
            RealtimeSttEstimatedCostUsdPerMinute = 0.01m,
            RealtimeSttProviderSessionTopology = "single-instance",
            RealtimeSttRegionId = "test-region",
            RealtimeSttAssumeLearnersAdult = true,
            ElevenLabsSttApiKey = "dev-only-test-key",
        };
        var optionsProvider = new StubConversationOptionsProvider(options);
        var selector = new ConversationAsrProviderSelector(
            [new MockConversationAsrProvider()],
            [new MockConversationRealtimeAsrProvider(), new ElevenLabsConversationRealtimeAsrProvider(optionsProvider, NullLogger<ElevenLabsConversationRealtimeAsrProvider>.Instance)],
            optionsProvider,
            NullLogger<ConversationAsrProviderSelector>.Instance);

        var provider = await selector.TrySelectRealtimeAsync();

        Assert.Null(provider);
    }

    [Fact]
    public async Task RealtimeSelector_ReturnsNull_ForRealProvider_WhenTopologyUnconfigured()
    {
        var options = new ConversationOptions
        {
            RealtimeSttEnabled = true,
            RealtimeAsrProvider = "elevenlabs-stt",
            RealtimeSttAllowRealProvider = true,
            RealtimeSttRealProviderProductionAuthorized = true,
            RealtimeSttEstimatedCostUsdPerMinute = 0.01m,
            RealtimeSttAssumeLearnersAdult = true,
            ElevenLabsSttApiKey = "dev-only-test-key",
        };
        var optionsProvider = new StubConversationOptionsProvider(options);
        var selector = new ConversationAsrProviderSelector(
            [new MockConversationAsrProvider()],
            [new MockConversationRealtimeAsrProvider(), new ElevenLabsConversationRealtimeAsrProvider(optionsProvider, NullLogger<ElevenLabsConversationRealtimeAsrProvider>.Instance)],
            optionsProvider,
            NullLogger<ConversationAsrProviderSelector>.Instance);

        var provider = await selector.TrySelectRealtimeAsync();

        Assert.Null(provider);
    }

    [Fact]
    public async Task RealtimeSelector_ReturnsNull_ForRealProvider_WhenPricingUnconfigured()
    {
        var options = new ConversationOptions
        {
            RealtimeSttEnabled = true,
            RealtimeAsrProvider = "elevenlabs-stt",
            RealtimeSttAllowRealProvider = true,
            RealtimeSttRealProviderProductionAuthorized = true,
            RealtimeSttProviderSessionTopology = "single-instance",
            RealtimeSttRegionId = "test-region",
            RealtimeSttAssumeLearnersAdult = true,
            ElevenLabsSttApiKey = "dev-only-test-key",
        };
        var optionsProvider = new StubConversationOptionsProvider(options);
        var selector = new ConversationAsrProviderSelector(
            [new MockConversationAsrProvider()],
            [new MockConversationRealtimeAsrProvider(), new ElevenLabsConversationRealtimeAsrProvider(optionsProvider, NullLogger<ElevenLabsConversationRealtimeAsrProvider>.Instance)],
            optionsProvider,
            NullLogger<ConversationAsrProviderSelector>.Instance);

        var provider = await selector.TrySelectRealtimeAsync();

        Assert.Null(provider);
    }

    [Fact]
    public async Task RealtimeSelector_AutoNeverReturnsRealProviderUntilAllGatesPass()
    {
        var options = new ConversationOptions
        {
            RealtimeSttEnabled = true,
            RealtimeAsrProvider = "auto",
            RealtimeSttAllowRealProvider = true,
            RealtimeSttRealProviderProductionAuthorized = true,
            ElevenLabsSttApiKey = "dev-only-test-key",
        };
        var optionsProvider = new StubConversationOptionsProvider(options);
        var selector = new ConversationAsrProviderSelector(
            [new MockConversationAsrProvider()],
            [new ElevenLabsConversationRealtimeAsrProvider(optionsProvider, NullLogger<ElevenLabsConversationRealtimeAsrProvider>.Instance)],
            optionsProvider,
            NullLogger<ConversationAsrProviderSelector>.Instance);

        var provider = await selector.TrySelectRealtimeAsync();

        Assert.Null(provider);
    }

    [Fact]
    public async Task ElevenLabsRealtimeProvider_RejectsBrowserContainerAudioWhenPcmIsRequired()
    {
        var provider = new ElevenLabsConversationRealtimeAsrProvider(
            new StubConversationOptionsProvider(new ConversationOptions
            {
                ElevenLabsSttApiKey = "dev-only-test-key",
                ElevenLabsSttAudioFormat = "pcm_16000",
            }),
            NullLogger<ElevenLabsConversationRealtimeAsrProvider>.Instance);

        var error = await Assert.ThrowsAsync<ConversationAsrException>(() => provider.StartAsync(
            new ConversationRealtimeAsrStartRequest(
                "session-1",
                "learner-1",
                "stream-1",
                "audio/webm",
                "en-GB",
                false,
                60),
            new NoopTranscriptSink(),
            CancellationToken.None));

        Assert.Equal("elevenlabs_audio_format_unsupported", error.Code);
    }

    [Fact]
    public async Task ElevenLabsRealtimeProvider_AllowsRawPcmMimeTypeBeforeConnecting()
    {
        var provider = new ElevenLabsConversationRealtimeAsrProvider(
            new StubConversationOptionsProvider(new ConversationOptions
            {
                ElevenLabsSttApiKey = "dev-only-test-key",
                ElevenLabsSttBaseUrl = "https://example.com/v1",
                ElevenLabsSttAudioFormat = "pcm_16000",
            }),
            NullLogger<ElevenLabsConversationRealtimeAsrProvider>.Instance);

        var error = await Assert.ThrowsAsync<ConversationAsrException>(() => provider.StartAsync(
            new ConversationRealtimeAsrStartRequest(
                "session-1",
                "learner-1",
                "stream-1",
                "audio/pcm",
                "en-GB",
                false,
                60),
            new NoopTranscriptSink(),
            CancellationToken.None));

        Assert.Equal("elevenlabs_realtime_base_url", error.Code);
    }

    [Fact]
    public async Task ElevenLabsRealtimeProvider_RejectsUnapprovedBaseUrlBeforeConnecting()
    {
        var provider = new ElevenLabsConversationRealtimeAsrProvider(
            new StubConversationOptionsProvider(new ConversationOptions
            {
                ElevenLabsSttApiKey = "dev-only-test-key",
                ElevenLabsSttBaseUrl = "https://example.com/v1",
                ElevenLabsSttAudioFormat = "pcm_16000",
            }),
            NullLogger<ElevenLabsConversationRealtimeAsrProvider>.Instance);

        var error = await Assert.ThrowsAsync<ConversationAsrException>(() => provider.StartAsync(
            new ConversationRealtimeAsrStartRequest(
                "session-1",
                "learner-1",
                "stream-1",
                "audio/pcm",
                "en-GB",
                false,
                60),
            new NoopTranscriptSink(),
            CancellationToken.None));

        Assert.Equal("elevenlabs_realtime_base_url", error.Code);
    }

    [Fact]
    public async Task ElevenLabsRealtimeSession_ClosesWhenProviderReturnsErrorMessage()
    {
        var sessionType = typeof(ElevenLabsConversationRealtimeAsrProvider)
            .GetNestedType("ElevenLabsRealtimeAsrSession", BindingFlags.NonPublic);
        Assert.NotNull(sessionType);

        using var socket = new ClientWebSocket();
        var sink = new CapturingTranscriptSink();
        var request = new ConversationRealtimeAsrStartRequest(
            "session-1",
            "learner-1",
            "stream-1",
            "audio/pcm",
            "en-GB",
            false,
            60);
        var session = Activator.CreateInstance(
            sessionType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [socket, request, sink, NullLogger<ElevenLabsConversationRealtimeAsrProvider>.Instance],
            culture: null);
        Assert.NotNull(session);
        var handleMessage = sessionType.GetMethod("HandleMessageAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        var closedField = sessionType.GetField("_closed", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(handleMessage);
        Assert.NotNull(closedField);

        var task = (Task?)handleMessage.Invoke(session, ["{\"message_type\":\"scribe_error\"}", CancellationToken.None]);
        Assert.NotNull(task);
        await task;

        Assert.Equal("scribe_error", sink.ProviderErrorCode);
        Assert.True((bool?)closedField.GetValue(session));
        await ((IAsyncDisposable)session).DisposeAsync();
    }

    [Fact]
    public void TurnStore_RejectsConcurrentStreamForSameUser()
    {
        var store = new ConversationRealtimeTurnStore();

        var first = store.TryBegin("conn-1", "learner-1", "session-1", "stream-1", "audio/webm", "auto", 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out var firstError);
        var second = store.TryBegin("conn-2", "learner-1", "session-2", "stream-2", "audio/webm", "auto", 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out var secondError);

        Assert.True(first);
        Assert.Null(firstError);
        Assert.False(second);
        Assert.Equal("REALTIME_CONCURRENCY", secondError);
    }

    [Fact]
    public async Task TurnStore_AllowsOnlyOneConcurrentBeginPerUser()
    {
        var store = new ConversationRealtimeTurnStore();
        using var gate = new ManualResetEventSlim(false);
        var attempts = Enumerable.Range(0, 32)
            .Select(index => Task.Run(() =>
            {
                gate.Wait();
                var started = store.TryBegin($"conn-{index}", "learner-1", $"session-{index}", $"stream-{index}", "audio/webm", "auto", 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out var errorCode);
                return (started, errorCode);
            }))
            .ToArray();

        gate.Set();
        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(result => result.started));
        Assert.All(results.Where(result => !result.started), result => Assert.Equal("REALTIME_CONCURRENCY", result.errorCode));
    }

    [Fact]
    public void TurnStore_CompletesOrderedChunksAndRejectsDuplicateSequence()
    {
        var store = new ConversationRealtimeTurnStore();
        Assert.True(store.TryBegin("conn-1", "learner-1", "session-1", "stream-1", "audio/webm", "auto", 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _));

        Assert.True(store.TryAppend("conn-1", "session-1", "stream-1", 1, [1, 2], 4, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(250), out var firstResult, out var firstError));
        Assert.False(store.TryAppend("conn-1", "session-1", "stream-1", 1, [3], 4, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(250), out _, out var duplicateError));
        Assert.True(store.TryAppend("conn-1", "session-1", "stream-1", 2, [3, 4], 4, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(250), out var secondResult, out var secondError));
        Assert.True(store.TryComplete("conn-1", "session-1", "stream-1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out var snapshot, out var completeError));

        Assert.Null(firstError);
        Assert.Null(secondError);
        Assert.Equal(2, firstResult?.TotalBytes);
        Assert.Equal(4, secondResult?.TotalBytes);
        Assert.True(firstResult?.ShouldEmitPartial);
        Assert.False(secondResult?.ShouldEmitPartial);
        Assert.Equal("STREAM_SEQUENCE", duplicateError);
        Assert.Null(completeError);
        Assert.NotNull(snapshot);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, snapshot.AudioBytes);
        Assert.True(store.TryFinalize("conn-1", "session-1", "stream-1"));
        Assert.False(store.TryComplete("conn-1", "session-1", "stream-1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _, out var missingError));
        Assert.Equal("STREAM_NOT_FOUND", missingError);
    }

    [Fact]
    public void TurnStore_CompleteClaimsStreamUntilFinalizeOrCancel()
    {
        var store = new ConversationRealtimeTurnStore();
        Assert.True(store.TryBegin("conn-1", "learner-1", "session-1", "stream-1", "audio/webm", "auto", 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _));
        Assert.True(store.TryAppend("conn-1", "session-1", "stream-1", 1, [1, 2], 10, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.Zero, out _, out _));

        Assert.True(store.TryComplete("conn-1", "session-1", "stream-1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out var firstSnapshot, out var firstError));
        Assert.False(store.TryComplete("conn-1", "session-1", "stream-1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out var secondSnapshot, out var secondError));

        Assert.NotNull(firstSnapshot);
        Assert.Null(firstError);
        Assert.Null(secondSnapshot);
        Assert.Equal("STREAM_COMMITTING", secondError);
        Assert.True(store.TryFinalize("conn-1", "session-1", "stream-1"));
    }

    [Fact]
    public void TurnStore_TracksProviderSessionAndFinalTranscript()
    {
        var store = new ConversationRealtimeTurnStore();
        var session = new NoopRealtimeSession();
        var final = new ConversationRealtimeTranscriptFinal(
            "stream-1",
            "I have chest pain",
            0.91,
            3200,
            "mock",
            "provider-final-1",
            "mock realtime final");

        Assert.True(store.TryBegin("conn-1", "learner-1", "session-1", "stream-1", "audio/webm", "auto", 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _));
        Assert.True(store.TryAttachProviderSession("conn-1", "session-1", "stream-1", session));
        Assert.True(store.TryGetProviderSession("conn-1", "session-1", "stream-1", out var storedSession));
        Assert.Same(session, storedSession);

        Assert.True(store.TrySetProviderFinal("conn-1", "session-1", "stream-1", final));
        Assert.True(store.TryGetProviderFinal("conn-1", "session-1", "stream-1", out var storedFinal));
        Assert.Equal("I have chest pain", storedFinal?.Text);
        Assert.Equal("provider-final-1", storedFinal?.ProviderEventId);
    }

    [Fact]
    public void TurnStore_DetachesExpiredProviderSessionsBeforeConcurrencyCheck()
    {
        var store = new ConversationRealtimeTurnStore();
        var session = new NoopRealtimeSession();

        Assert.True(store.TryBegin("conn-1", "learner-1", "session-1", "stream-1", "audio/webm", "auto", 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _));
        Assert.True(store.TryAttachProviderSession("conn-1", "session-1", "stream-1", session));

        var detached = store.DetachExpiredProviderSessions(TimeSpan.Zero, TimeSpan.FromSeconds(60));
        var second = store.TryBegin("conn-2", "learner-1", "session-2", "stream-2", "audio/webm", "auto", 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out var errorCode);

        Assert.Single(detached);
        Assert.Same(session, detached[0]);
        Assert.True(second);
        Assert.Null(errorCode);
    }

    [Fact]
    public void TurnStore_ReportsSizeLimitWithoutDroppingStream()
    {
        var store = new ConversationRealtimeTurnStore();
        Assert.True(store.TryBegin("conn-1", "learner-1", "session-1", "stream-1", "audio/webm", "auto", 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _));

        Assert.True(store.TryAppend("conn-1", "session-1", "stream-1", 1, [1, 2], 3, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.Zero, out _, out _));
        Assert.False(store.TryAppend("conn-1", "session-1", "stream-1", 2, [3, 4], 3, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.Zero, out _, out var errorCode));

        Assert.Equal("STREAM_SIZE", errorCode);
        Assert.True(store.TryComplete("conn-1", "session-1", "stream-1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out var snapshot, out _));
        Assert.Equal(new byte[] { 1, 2 }, snapshot?.AudioBytes);
    }

    [Fact]
    public void TurnStore_ReportsDurationLimitWithoutDroppingStream()
    {
        var store = new ConversationRealtimeTurnStore();
        Assert.True(store.TryBegin("conn-1", "learner-1", "session-1", "stream-1", "audio/webm", "auto", 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _));

        Assert.False(store.TryAppend("conn-1", "session-1", "stream-1", 1, [1], 10, TimeSpan.FromSeconds(30), TimeSpan.Zero, TimeSpan.Zero, out _, out var errorCode));

        Assert.Equal("STREAM_DURATION", errorCode);
        Assert.True(store.TryComplete("conn-1", "session-1", "stream-1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _, out _));
    }

    [Fact]
    public void TurnStore_DropsStream_WhenCompleteExceedsIdleLimit()
    {
        var store = new ConversationRealtimeTurnStore();
        Assert.True(store.TryBegin("conn-1", "learner-1", "session-1", "stream-1", "audio/webm", "auto", 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _));
        Assert.True(store.TryAppend("conn-1", "session-1", "stream-1", 1, [1], 10, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.Zero, out _, out _));

        Assert.False(store.TryComplete("conn-1", "session-1", "stream-1", TimeSpan.Zero, TimeSpan.FromSeconds(60), out _, out var idleError));
        Assert.Equal("STREAM_IDLE_TIMEOUT", idleError);
        Assert.True(store.TryComplete("conn-1", "session-1", "stream-1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _, out _));
    }

    [Fact]
    public void TurnStore_BeginSweepsExpiredBuffersBeforeConcurrencyCheck()
    {
        var store = new ConversationRealtimeTurnStore();
        Assert.True(store.TryBegin("conn-1", "learner-1", "session-1", "stream-1", "audio/webm", "auto", 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _));
        Assert.False(store.TryAppend("conn-1", "session-1", "stream-1", 1, [1], 10, TimeSpan.Zero, TimeSpan.FromSeconds(60), TimeSpan.Zero, out _, out _));

        var second = store.TryBegin("conn-2", "learner-1", "session-2", "stream-2", "audio/webm", "auto", 1, TimeSpan.Zero, TimeSpan.FromSeconds(60), out var errorCode);

        Assert.True(second);
        Assert.Null(errorCode);
    }

    private static ConversationAsrProviderSelector BuildSelector(ConversationOptions options)
        => new(
            [new MockConversationAsrProvider()],
            [new MockConversationRealtimeAsrProvider()],
            new StubConversationOptionsProvider(options),
            NullLogger<ConversationAsrProviderSelector>.Instance);

    private sealed class StubConversationOptionsProvider(ConversationOptions options) : IConversationOptionsProvider
    {
        public Task<ConversationOptions> GetAsync(CancellationToken ct = default) => Task.FromResult(options);
        public void Invalidate() { }
    }

    private sealed class NoopRealtimeSession : IConversationRealtimeAsrSession
    {
        public Task SendAudioAsync(ConversationRealtimeAudioChunk chunk, CancellationToken ct) => Task.CompletedTask;
        public Task CompleteAsync(CancellationToken ct) => Task.CompletedTask;
        public Task AbortAsync(string reason, CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoopTranscriptSink : IConversationRealtimeTranscriptSink
    {
        public Task OnPartialAsync(ConversationRealtimeTranscriptPartial partial, CancellationToken ct) => Task.CompletedTask;
        public Task OnFinalAsync(ConversationRealtimeTranscriptFinal final, CancellationToken ct) => Task.CompletedTask;
        public Task OnProviderErrorAsync(ConversationAsrException error, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class CapturingTranscriptSink : IConversationRealtimeTranscriptSink
    {
        public string? ProviderErrorCode { get; private set; }
        public Task OnPartialAsync(ConversationRealtimeTranscriptPartial partial, CancellationToken ct) => Task.CompletedTask;
        public Task OnFinalAsync(ConversationRealtimeTranscriptFinal final, CancellationToken ct) => Task.CompletedTask;

        public Task OnProviderErrorAsync(ConversationAsrException error, CancellationToken ct)
        {
            ProviderErrorCode = error.Code;
            return Task.CompletedTask;
        }
    }
}
