namespace OetLearner.Api.Services;

/// <summary>
/// Speech-to-text transcription service.
/// Mock implementation returns simulated transcription results.
/// Production: swap for Deepgram WebSocket API or Azure Speech SDK.
/// </summary>
public class SpeechToTextService
{
    private static readonly string[] SampleTranscriptions = new[]
    {
        "Good morning, I'm here to discuss your discharge plan and follow-up care.",
        "The patient has been recovering well since the procedure yesterday.",
        "I'd like to go through your medication changes and what to expect at home.",
        "Could you tell me a bit more about the pain you've been experiencing?",
        "I understand your concerns. Let me explain the next steps in your treatment.",
        "We'll need to arrange a follow-up appointment within two weeks.",
        "The test results show improvement in your condition.",
        "I'm going to hand over the care of this patient to the night shift team.",
        "Let me check your vital signs before we proceed with the assessment.",
        "The physiotherapy team will work with you on mobility exercises tomorrow."
    };

    /// <summary>
    /// Transcribe an audio chunk to text.
    /// In production, this calls Deepgram/Azure Speech SDK.
    /// </summary>
    public Task<SpeechTranscriptionResult> TranscribeAudioChunkAsync(string audioBase64, string sessionId, CancellationToken ct = default)
    {
        // Mock: return a realistic transcription based on session context
        var index = Math.Abs(sessionId.GetHashCode()) % SampleTranscriptions.Length;
        var text = SampleTranscriptions[index];

        var result = new SpeechTranscriptionResult
        {
            Text = text,
            Confidence = 0.85 + Random.Shared.NextDouble() * 0.14,  // 0.85–0.99
            DurationMs = (int)(text.Split(' ').Length * 320 + Random.Shared.Next(100, 500)),
            IsFinal = true,
            Language = "en"
        };

        return Task.FromResult(result);
    }
}

public class SpeechTranscriptionResult
{
    public string Text { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public int DurationMs { get; set; }
    public bool IsFinal { get; set; }
    public string Language { get; set; } = "en";
}
