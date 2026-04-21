namespace OetLearner.Api.Services.Conversation.Asr;

public sealed class MockConversationAsrProvider : IConversationAsrProvider
{
    public string Name => "mock";
    public bool IsConfigured => true;

    private static readonly string[] Samples =
    {
        "Good morning. My name is one of the doctors here today. How can I help you?",
        "Thank you for explaining that. Could you tell me a little more about when this started?",
        "I understand. Have you noticed any other symptoms along with this?",
        "I'd like to examine you if that's alright. Then we can discuss the next steps.",
        "That sounds worrying. I want to reassure you we will look into this together.",
        "Let me explain what I think is going on, and then we can talk about treatment options.",
        "Is there anything else you'd like to ask before we finish?",
        "If the pain comes back or gets worse, please come back straight away.",
        "I'm going to hand over Mrs Patel in Bay 3. Situation is she was admitted last night with chest pain.",
    };

    public async Task<ConversationAsrResult> TranscribeAsync(ConversationAsrRequest request, CancellationToken ct)
    {
        long bytes = request.AudioBytes ?? 0;
        if (bytes == 0)
        {
            var buf = new byte[8192];
            while (!ct.IsCancellationRequested)
            {
                var read = await request.Audio.ReadAsync(buf, ct);
                if (read == 0) break;
                bytes += read;
            }
        }
        var rng = new Random((int)(bytes % int.MaxValue));
        var text = Samples[rng.Next(Samples.Length)];
        return new ConversationAsrResult(
            text, 0.82 + rng.NextDouble() * 0.14,
            (int)(text.Split(' ').Length * 320 + rng.Next(100, 400)),
            request.Locale, Name, $"mock ({bytes} bytes)");
    }
}
