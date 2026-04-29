namespace OetLearner.Api.Services.Pronunciation;

/// <summary>
/// ASR + pronunciation-scoring provider contract. Implementations must produce
/// a normalised <see cref="AsrResult"/> regardless of the underlying engine, so
/// the downstream pipeline (assessment persistence, grounded AI feedback,
/// spaced-repetition scheduler) never special-cases a vendor.
///
/// Scoring dimensions are the OET pronunciation composite:
///   - Accuracy     — phoneme articulation fidelity (0-100)
///   - Fluency      — pacing, disfluency penalty (0-100)
///   - Completeness — proportion of the reference text actually uttered (0-100)
///   - Prosody      — stress, intonation, rhythm (0-100)
///
/// Every provider MUST return word-level scores AND at least one entry per
/// distinct phoneme referenced in <see cref="AsrRequest.ReferenceText"/>.
/// Falling back to rough heuristics is acceptable for providers that don't
/// natively expose phoneme data, so long as the emitted output is honest
/// (mark the entries with <c>ErrorType = "NoData"</c>).
/// </summary>
public interface IPronunciationAsrProvider
{
    string Name { get; }

    /// <summary>Is this provider currently configured and ready to serve calls?
    /// The <see cref="PronunciationAsrProviderSelector"/> uses this to decide
    /// between the requested provider and fallbacks.</summary>
    bool IsConfigured { get; }

    Task<AsrResult> AnalyzeAsync(AsrRequest request, CancellationToken ct);
}

public sealed record AsrRequest(
    Stream Audio,
    string AudioMimeType,
    string ReferenceText,
    string TargetPhoneme,
    string Locale,
    string? TargetRuleId,
    string RulebookProfession,
    long? AudioBytes,
    string? UserId = null);

public sealed record AsrResult(
    double AccuracyScore,
    double FluencyScore,
    double CompletenessScore,
    double ProsodyScore,
    double OverallScore,
    IReadOnlyList<WordScore> WordScores,
    IReadOnlyList<PhonemeScore> ProblematicPhonemes,
    FluencyMarkers FluencyMarkers,
    string ProviderName,
    string? ProviderResponseSummary);

public sealed record WordScore(
    string Word,
    double AccuracyScore,
    string ErrorType,      // "None" | "Mispronunciation" | "Omission" | "Insertion" | "NoData"
    IReadOnlyList<PhonemeScore>? Phonemes = null);

public sealed record PhonemeScore(
    string Phoneme,
    double Score,
    int Occurrences,
    string? RuleId = null);

public sealed record FluencyMarkers(
    double SpeechRateWpm,
    int PauseCount,
    int AveragePauseDurationMs);
