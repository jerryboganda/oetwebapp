namespace OetLearner.Api.Services.Conversation.Asr;

public interface IConversationAsrProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<ConversationAsrResult> TranscribeAsync(ConversationAsrRequest request, CancellationToken ct);
}

public interface IConversationRealtimeAsrProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<IConversationRealtimeAsrSession> StartAsync(
        ConversationRealtimeAsrStartRequest request,
        IConversationRealtimeTranscriptSink sink,
        CancellationToken ct);
}

public interface IConversationRealtimeAsrSession : IAsyncDisposable
{
    Task SendAudioAsync(ConversationRealtimeAudioChunk chunk, CancellationToken ct);
    Task CompleteAsync(CancellationToken ct);
    Task AbortAsync(string reason, CancellationToken ct);
}

public interface IConversationRealtimeTranscriptSink
{
    Task OnPartialAsync(ConversationRealtimeTranscriptPartial partial, CancellationToken ct);
    Task OnFinalAsync(ConversationRealtimeTranscriptFinal final, CancellationToken ct);
    Task OnProviderErrorAsync(ConversationAsrException error, CancellationToken ct);
}

public sealed record ConversationAsrRequest(
    Stream Audio, string AudioMimeType, string Locale, long? AudioBytes, bool EnableDiarization = false);

public sealed record ConversationAsrResult(
    string Text, double Confidence, int DurationMs, string Language,
    string ProviderName, string? ProviderResponseSummary,
    IReadOnlyList<ConversationSpeakerSegment>? SpeakerSegments = null);

public sealed record ConversationRealtimeAsrStartRequest(
    string SessionId,
    string UserId,
    string StreamId,
    string AudioMimeType,
    string Locale,
    bool EnableDiarization,
    int MaxTurnDurationSeconds);

public sealed record ConversationRealtimeAudioChunk(
    int Sequence,
    ReadOnlyMemory<byte> Audio,
    bool IsFinal,
    int? ClientOffsetMs);

public sealed record ConversationRealtimeTranscriptPartial(
    string StreamId,
    string Text,
    double? Confidence,
    int? StartMs,
    int? EndMs,
    int Sequence);

public sealed record ConversationRealtimeTranscriptFinal(
    string StreamId,
    string Text,
    double Confidence,
    int DurationMs,
    string ProviderName,
    string? ProviderEventId,
    string? ProviderResponseSummary,
    IReadOnlyList<ConversationSpeakerSegment>? SpeakerSegments = null);

public sealed record ConversationSpeakerSegment(
    string Speaker,
    string Text,
    int StartMs,
    int EndMs,
    double? Confidence);

public sealed class ConversationAsrException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
