namespace OetLearner.Api.Services.Conversation.Tts;

/// <summary>
/// Mock TTS — emits a tiny silent MP3 so callers + tests get deterministic
/// output without any external service. Never used in production when a real
/// provider is configured.
/// </summary>
public sealed class MockConversationTtsProvider : IConversationTtsProvider
{
    public string Name => "mock";
    public bool IsConfigured => true;

    // Minimum-valid silent MP3 header (just enough for a browser to decode to 0 samples).
    // If decoding fails in the UI, the UI falls back to text-only for mock env.
    private static readonly byte[] Silence = Array.Empty<byte>();

    public Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
        => Task.FromResult(new ConversationTtsResult(
            Audio: Silence,
            MimeType: "audio/mpeg",
            DurationMs: request.Text.Split(' ').Length * 300,
            ProviderName: Name,
            ProviderResponseSummary: "mock tts — no audio produced"));
}
