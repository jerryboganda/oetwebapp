namespace OetLearner.Api.Services.Pronunciation;

/// <summary>
/// Phase 6c — Azure-backed phoneme-scoring provider. Thin adapter over
/// the existing <see cref="AzurePronunciationAsrProvider"/>: Azure's
/// pronunciation-assessment endpoint already returns phoneme-level
/// accuracy as part of the ASR response, so we delegate rather than
/// duplicating the HTTP call. The only real value the phoneme contract
/// adds today is type-level documentation that the consumer wants
/// phoneme detail (vs the full transcript+phoneme response).
/// <para>
/// Honors the same registry-first / options-fallback configuration as
/// <see cref="AzurePronunciationAsrProvider"/> via
/// <see cref="IPronunciationCredentialResolver"/>.
/// </para>
/// </summary>
public sealed class AzurePronunciationPhonemeProvider(
    AzurePronunciationAsrProvider azureAsr) : IPronunciationPhonemeProvider
{
    public string Name => "azure-phoneme";

    public bool IsConfigured => azureAsr.IsConfigured;

    public Task<AsrResult> AnalyzePhonemesAsync(AsrRequest request, CancellationToken ct)
        => azureAsr.AnalyzeAsync(request, ct);
}
