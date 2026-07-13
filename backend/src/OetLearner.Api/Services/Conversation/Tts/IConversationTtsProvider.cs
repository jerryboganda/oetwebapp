namespace OetLearner.Api.Services.Conversation.Tts;

public interface IConversationTtsProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<bool> IsConfiguredAsync(CancellationToken ct = default)
        => Task.FromResult(IsConfigured);
    Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct);
}

public sealed record ConversationTtsRequest(
    string Text, string Voice, string Locale, double? Rate = null, double? Pitch = null,
    // Per-call overrides. ModelVariant selects the ElevenLabs model id for this
    // single synthesis call (falling back to the admin-configured model when
    // null); Instructions is reserved and currently unused by ElevenLabs.
    string? ModelVariant = null, string? Instructions = null);

public sealed record ConversationTtsResult(
    byte[] Audio, string MimeType, int DurationMs,
    string ProviderName, string? ProviderResponseSummary);

public sealed class ConversationTtsException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
