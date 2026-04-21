namespace OetLearner.Api.Services.Conversation.Asr;

public interface IConversationAsrProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<ConversationAsrResult> TranscribeAsync(ConversationAsrRequest request, CancellationToken ct);
}

public sealed record ConversationAsrRequest(
    Stream Audio, string AudioMimeType, string Locale, long? AudioBytes);

public sealed record ConversationAsrResult(
    string Text, double Confidence, int DurationMs, string Language,
    string ProviderName, string? ProviderResponseSummary);

public sealed class ConversationAsrException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
