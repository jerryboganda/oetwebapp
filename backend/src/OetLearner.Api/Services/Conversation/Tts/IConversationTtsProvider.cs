namespace OetLearner.Api.Services.Conversation.Tts;

public interface IConversationTtsProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct);
}

public sealed record ConversationTtsRequest(
    string Text, string Voice, string Locale, double? Rate = null, double? Pitch = null);

public sealed record ConversationTtsResult(
    byte[] Audio, string MimeType, int DurationMs,
    string ProviderName, string? ProviderResponseSummary);

public sealed class ConversationTtsException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
