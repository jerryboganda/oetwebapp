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
    public void TurnStore_CompletesOrderedChunksAndRejectsDuplicateSequence()
    {
        var store = new ConversationRealtimeTurnStore();
        Assert.True(store.TryBegin("conn-1", "learner-1", "session-1", "stream-1", "audio/webm", "auto", 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _));

        Assert.True(store.TryAppend("conn-1", "session-1", "stream-1", 1, [1, 2], 4, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(250), out var firstResult, out var firstError));
        Assert.False(store.TryAppend("conn-1", "session-1", "stream-1", 1, [3], 4, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(250), out _, out var duplicateError));
        Assert.True(store.TryAppend("conn-1", "session-1", "stream-1", 2, [3, 4], 4, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(250), out var secondResult, out var secondError));
        Assert.True(store.TryComplete("conn-1", "session-1", "stream-1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out var snapshot));

        Assert.Null(firstError);
        Assert.Null(secondError);
        Assert.Equal(2, firstResult?.TotalBytes);
        Assert.Equal(4, secondResult?.TotalBytes);
        Assert.True(firstResult?.ShouldEmitPartial);
        Assert.False(secondResult?.ShouldEmitPartial);
        Assert.Equal("STREAM_SEQUENCE", duplicateError);
        Assert.NotNull(snapshot);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, snapshot.AudioBytes);
        Assert.True(store.TryFinalize("conn-1", "session-1", "stream-1"));
        Assert.False(store.TryComplete("conn-1", "session-1", "stream-1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _));
    }

    [Fact]
    public void TurnStore_DropsStream_WhenTotalBytesExceeded()
    {
        var store = new ConversationRealtimeTurnStore();
        Assert.True(store.TryBegin("conn-1", "learner-1", "session-1", "stream-1", "audio/webm", "auto", 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _));

        Assert.True(store.TryAppend("conn-1", "session-1", "stream-1", 1, [1, 2], 3, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.Zero, out _, out _));
        Assert.False(store.TryAppend("conn-1", "session-1", "stream-1", 2, [3, 4], 3, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.Zero, out _, out var errorCode));

        Assert.Equal("STREAM_SIZE", errorCode);
        Assert.False(store.TryComplete("conn-1", "session-1", "stream-1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _));
    }

    [Fact]
    public void TurnStore_DropsStream_WhenDurationLimitExceeded()
    {
        var store = new ConversationRealtimeTurnStore();
        Assert.True(store.TryBegin("conn-1", "learner-1", "session-1", "stream-1", "audio/webm", "auto", 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _));

        Assert.False(store.TryAppend("conn-1", "session-1", "stream-1", 1, [1], 10, TimeSpan.FromSeconds(30), TimeSpan.Zero, TimeSpan.Zero, out _, out var errorCode));

        Assert.Equal("STREAM_DURATION", errorCode);
        Assert.False(store.TryComplete("conn-1", "session-1", "stream-1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _));
    }

    [Fact]
    public void TurnStore_DropsStream_WhenCompleteExceedsIdleLimit()
    {
        var store = new ConversationRealtimeTurnStore();
        Assert.True(store.TryBegin("conn-1", "learner-1", "session-1", "stream-1", "audio/webm", "auto", 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _));
        Assert.True(store.TryAppend("conn-1", "session-1", "stream-1", 1, [1], 10, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.Zero, out _, out _));

        Assert.False(store.TryComplete("conn-1", "session-1", "stream-1", TimeSpan.Zero, TimeSpan.FromSeconds(60), out _));
        Assert.False(store.TryComplete("conn-1", "session-1", "stream-1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _));
    }

    [Fact]
    public void TurnStore_BeginSweepsExpiredBuffersBeforeConcurrencyCheck()
    {
        var store = new ConversationRealtimeTurnStore();
        Assert.True(store.TryBegin("conn-1", "learner-1", "session-1", "stream-1", "audio/webm", "auto", 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out _));
        Assert.False(store.TryAppend("conn-1", "session-1", "stream-1", 1, [1], 10, TimeSpan.Zero, TimeSpan.FromSeconds(60), TimeSpan.Zero, out _, out _));

        var second = store.TryBegin("conn-2", "learner-1", "session-2", "stream-2", "audio/webm", "auto", 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), out var errorCode);

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
}
