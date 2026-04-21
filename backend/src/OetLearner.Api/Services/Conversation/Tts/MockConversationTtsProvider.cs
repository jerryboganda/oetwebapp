namespace OetLearner.Api.Services.Conversation.Tts;

public sealed class MockConversationTtsProvider : IConversationTtsProvider
{
    public string Name => "mock";
    public bool IsConfigured => true;

    public Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
        => Task.FromResult(new ConversationTtsResult(
            Array.Empty<byte>(), "audio/mpeg",
            request.Text.Split(' ').Length * 300, Name, "mock tts"));
}
