namespace OetLearner.Api.Services.Conversation.Asr;

public interface IConversationAsrProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<ConversationAsrResult> TranscribeAsync(ConversationAsrRequest request, CancellationToken ct);
}

public sealed record ConversationAsrRequest(
    Stream Audio, string AudioMimeType, string Locale, long? AudioBytes, bool EnableDiarization = false);

public sealed record ConversationAsrResult(
    string Text, double Confidence, int DurationMs, string Language,
    string ProviderName, string? ProviderResponseSummary,
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
