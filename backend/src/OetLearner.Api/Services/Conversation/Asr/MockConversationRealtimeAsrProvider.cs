namespace OetLearner.Api.Services.Conversation.Asr;

public sealed class MockConversationRealtimeAsrProvider : IConversationRealtimeAsrProvider
{
    public string Name => "mock";
    public bool IsConfigured => true;

    public Task<IConversationRealtimeAsrSession> StartAsync(
        ConversationRealtimeAsrStartRequest request,
        IConversationRealtimeTranscriptSink sink,
        CancellationToken ct)
        => Task.FromResult<IConversationRealtimeAsrSession>(new MockConversationRealtimeAsrSession(request, sink));

    private sealed class MockConversationRealtimeAsrSession(
        ConversationRealtimeAsrStartRequest request,
        IConversationRealtimeTranscriptSink sink) : IConversationRealtimeAsrSession
    {
        private long _bytes;
        private int _sequence;
        private bool _closed;

        public async Task SendAudioAsync(ConversationRealtimeAudioChunk chunk, CancellationToken ct)
        {
            if (_closed) return;
            _bytes += chunk.Audio.Length;
            _sequence = Math.Max(_sequence, chunk.Sequence);
            if (_bytes > 0)
            {
                await sink.OnPartialAsync(new ConversationRealtimeTranscriptPartial(
                    request.StreamId,
                    "Listening...",
                    0.75,
                    0,
                    null,
                    _sequence), ct);
            }
        }

        public async Task CompleteAsync(CancellationToken ct)
        {
            if (_closed) return;
            _closed = true;
            var text = _bytes > 0
                ? "Thank you. I would like to explain what has been happening and ask a few more questions."
                : string.Empty;
            await sink.OnFinalAsync(new ConversationRealtimeTranscriptFinal(
                request.StreamId,
                text,
                _bytes > 0 ? 0.86 : 0,
                _bytes > 0 ? Math.Min(request.MaxTurnDurationSeconds * 1000, 4000 + (int)(_bytes % 3000)) : 0,
                "mock",
                $"mock-{request.StreamId}",
                $"mock realtime ({_bytes} bytes)",
                _bytes > 0
                    ? [new ConversationSpeakerSegment("learner", text, 0, 4000, 0.86)]
                    : []), ct);
        }

        public Task AbortAsync(string reason, CancellationToken ct)
        {
            _closed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _closed = true;
            return ValueTask.CompletedTask;
        }
    }
}
