namespace OetLearner.Api.Services.Vocabulary;

/// <summary>
/// Background job describing a vocabulary term that needs TTS audio
/// synthesised and persisted. Enqueued from the bulk-import commit path
/// and the admin backfill endpoint; consumed by
/// <see cref="VocabularyAudioWorker"/>.
/// </summary>
public sealed record VocabularyAudioJob(
    string TermId,
    string Text,
    string? Voice,
    string Locale,
    string BatchId,
    int AttemptCount = 0,
    // When set, overrides the admin-configured ConversationOptions for this
    // specific synthesis. ModelVariant selects the ElevenLabs model id; used by
    // the bulk "regenerate audio" path so a single run can pin a model/voice.
    string? ModelVariant = null,
    string? Instructions = null,
    string? ProviderName = null,
    bool ForceRegenerate = false);

/// <summary>
/// Unbounded multi-writer / multi-reader queue (Channel-backed) used to
/// decouple the import commit transaction from the per-term TTS round-trip.
/// </summary>
public interface IVocabularyAudioQueue
{
    ValueTask EnqueueAsync(VocabularyAudioJob job, CancellationToken ct = default);
    IAsyncEnumerable<VocabularyAudioJob> ReadAllAsync(CancellationToken ct);
    int PendingCount { get; }
}
