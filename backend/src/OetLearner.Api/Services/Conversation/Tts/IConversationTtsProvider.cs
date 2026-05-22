namespace OetLearner.Api.Services.Conversation.Tts;

public interface IConversationTtsProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct);
}

public sealed record ConversationTtsRequest(
    string Text, string Voice, string Locale, double? Rate = null, double? Pitch = null,
    // Phase Q1 — Qwen3 Voice Studio overrides used by the per-voice preview
    // endpoint and the vocabulary regenerate worker. When non-null they take
    // priority over the admin-configured ConversationOptions for this single
    // synthesis call; other providers ignore them.
    string? ModelVariant = null, string? Instructions = null);

public sealed record ConversationTtsResult(
    byte[] Audio, string MimeType, int DurationMs,
    string ProviderName, string? ProviderResponseSummary);

public sealed class ConversationTtsException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
