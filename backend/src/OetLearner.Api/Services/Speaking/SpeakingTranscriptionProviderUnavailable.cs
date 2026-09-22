namespace OetLearner.Api.Services.Speaking;

public sealed class SpeakingTranscriptionProviderUnavailable : ISpeakingTranscriptionProvider
{
    public string ProviderCode => "unavailable";

    public Task<SpeakingTranscriptionProviderResult> TranscribeAsync(
        string mediaAssetReference,
        string language,
        CancellationToken ct)
        => Task.FromException<SpeakingTranscriptionProviderResult>(
            new InvalidOperationException(
                "A production Speaking transcription provider is not configured. Configure OpenAI Whisper before grading recorded Speaking audio."));
}
