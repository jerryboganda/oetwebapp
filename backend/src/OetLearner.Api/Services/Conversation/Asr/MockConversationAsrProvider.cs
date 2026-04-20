namespace OetLearner.Api.Services.Conversation.Asr;

/// <summary>
/// Deterministic mock STT. Returns a plausible transcript for dev/test when
/// no real ASR is configured. Marks output as mocked in the response summary
/// so the UI can show a "simulated transcript" banner if desired.
/// </summary>
public sealed class MockConversationAsrProvider : IConversationAsrProvider
{
    public string Name => "mock";
    public bool IsConfigured => true;

    private static readonly string[] SampleTranscriptions =
    {
        "Good morning. My name is one of the doctors here today. How can I help you?",
        "Thank you for explaining that. Could you tell me a little more about when this started?",
        "I understand. Have you noticed any other symptoms along with this?",
        "I'd like to examine you if that's alright. Then we can discuss what the next steps might be.",
        "That sounds worrying. I want to reassure you we will look into this together.",
        "Let me explain what I think is going on, and then we can talk about treatment options.",
        "Is there anything else you'd like to ask before we finish?",
        "If the pain comes back or gets worse, please come back straight away.",
        "Thank you. Just to check I've explained this well — could you tell me in your own words what the plan is?",
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
        var seed = (int)(bytes % int.MaxValue);
        var rng = new Random(seed);
        var text = SampleTranscriptions[rng.Next(SampleTranscriptions.Length)];
        return new ConversationAsrResult(
            Text: text,
            Confidence: 0.82 + rng.NextDouble() * 0.14,
            DurationMs: (int)(text.Split(' ').Length * 320 + rng.Next(100, 400)),
            Language: request.Locale,
            ProviderName: Name,
            ProviderResponseSummary: $"mock transcription ({bytes} audio bytes)");
    }
}
