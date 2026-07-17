using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OetWithDrHesham.Api.Services.Speaking;

/// <summary>
/// Development / test stub for <see cref="ISpeakingTranscriptionProvider"/>.
///
/// Returns a deterministic fake transcript with three segments and
/// confidence 0.95 so the rest of the speaking transcription pipeline
/// (queueing, state machine, persistence, endpoints) is exercisable
/// without provisioning a real ASR engine. Intentionally synchronous and
/// allocation-light — never call this in production.
///
/// Wire-up: Agent W2-A registers this concrete implementation against
/// <see cref="ISpeakingTranscriptionProvider"/> in <c>Program.cs</c> for
/// non-prod environments.
/// </summary>
public sealed class MockSpeakingTranscriptionProvider(
    ILogger<MockSpeakingTranscriptionProvider> logger) : ISpeakingTranscriptionProvider
{
    public string ProviderCode => "mock";

    public Task<SpeakingTranscriptionProviderResult> TranscribeAsync(
        string mediaAssetUrl,
        string language,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mediaAssetUrl))
        {
            throw new ArgumentException("Media asset URL is required.", nameof(mediaAssetUrl));
        }

        ct.ThrowIfCancellationRequested();

        var resolvedLanguage = string.IsNullOrWhiteSpace(language) ? "en" : language;

        var segments = new[]
        {
            new
            {
                speaker = "candidate",
                startMs = 0,
                endMs = 4_500,
                text = "Good morning, my name is the candidate and I'll be your nurse today.",
                confidence = 0.95,
                words = Array.Empty<object>(),
            },
            new
            {
                speaker = "interlocutor",
                startMs = 4_600,
                endMs = 8_900,
                text = "Hello, I've been having chest pain since yesterday evening.",
                confidence = 0.95,
                words = Array.Empty<object>(),
            },
            new
            {
                speaker = "candidate",
                startMs = 9_000,
                endMs = 14_200,
                text = "I'm sorry to hear that. Could you describe the pain in a bit more detail?",
                confidence = 0.95,
                words = Array.Empty<object>(),
            },
        };

        var segmentsJson = JsonSerializer.Serialize(segments, JsonSupport.Options);
        var wordCount = segments.Sum(s => s.text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);

        logger.LogDebug(
            "Mock ASR transcribing {MediaAssetUrl} (lang={Language}) → {WordCount} words across {SegmentCount} segments.",
            mediaAssetUrl,
            resolvedLanguage,
            wordCount,
            segments.Length);

        var result = new SpeakingTranscriptionProviderResult
        {
            Provider = ProviderCode,
            Language = resolvedLanguage,
            SegmentsJson = segmentsJson,
            WordCount = wordCount,
            MeanConfidence = 0.95,
            Model = "mock-stub-v1",
        };

        return Task.FromResult(result);
    }
}
