namespace OetLearner.Api.Services.Conversation.Asr;

/// <summary>
/// Conversation STT provider contract. Implementations must return normalised
/// <see cref="ConversationAsrResult"/> so the hub + evaluator never care which
/// engine is live. Admin-swappable via <c>Conversation:AsrProvider</c>.
/// </summary>
public interface IConversationAsrProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<ConversationAsrResult> TranscribeAsync(ConversationAsrRequest request, CancellationToken ct);
}

public sealed record ConversationAsrRequest(
    Stream Audio,
    string AudioMimeType,
    string Locale,
    long? AudioBytes);

public sealed record ConversationAsrResult(
    string Text,
    double Confidence,
    int DurationMs,
    string Language,
    string ProviderName,
    string? ProviderResponseSummary);
