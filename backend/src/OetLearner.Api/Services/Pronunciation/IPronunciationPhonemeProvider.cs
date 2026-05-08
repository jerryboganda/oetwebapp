namespace OetLearner.Api.Services.Pronunciation;

/// <summary>
/// Phase 6c — interface that isolates the *phoneme-scoring* facet of
/// pronunciation analysis from the broader ASR contract. Today the only
/// implementation is <see cref="AzurePronunciationPhonemeProvider"/>,
/// which delegates to the existing <see cref="AzurePronunciationAsrProvider"/>
/// — Azure's pronunciation-assessment endpoint is the same API call as
/// its ASR endpoint, just with a different header. The split exists so
/// future engines that *only* score phonemes (e.g. forced-aligner-based
/// libraries that do not transcribe) can plug in without subclassing the
/// full ASR contract.
/// <para>
/// IMPORTANT: This interface is currently scaffolding. Live pronunciation
/// scoring still routes through <see cref="IPronunciationAsrProviderSelector"/>
/// → <see cref="IPronunciationAsrProvider"/>. Switching the live grading
/// path is a separate, gated change tracked as Phase 6d.
/// </para>
/// </summary>
public interface IPronunciationPhonemeProvider
{
    string Name { get; }

    bool IsConfigured { get; }

    /// <summary>Score a single attempt and return per-phoneme accuracy.
    /// Implementations re-use the existing <see cref="AsrResult"/> shape
    /// so call sites already familiar with the ASR result do not need a
    /// second projection. Implementations MAY ignore transcript/word
    /// fields when their underlying engine only emits phoneme data.</summary>
    Task<AsrResult> AnalyzePhonemesAsync(AsrRequest request, CancellationToken ct);
}
