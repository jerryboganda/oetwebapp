namespace OetLearner.Api.Services.Conversation.Tts;

/// <summary>
/// Text-to-speech provider for the AI partner's spoken replies. Returns a
/// stream of audio bytes with a MIME type. Admin-swappable via
/// <c>Conversation:TtsProvider</c>.
/// </summary>
public interface IConversationTtsProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct);
}

public sealed record ConversationTtsRequest(
    string Text,
    string Voice,
    string Locale,
    double? Rate = null,   // 0.5 – 2.0, 1.0 = natural
    double? Pitch = null); // -0.5 – +0.5 semitones

public sealed record ConversationTtsResult(
    byte[] Audio,
    string MimeType,
    int DurationMs,
    string ProviderName,
    string? ProviderResponseSummary);

public sealed class ConversationTtsException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
