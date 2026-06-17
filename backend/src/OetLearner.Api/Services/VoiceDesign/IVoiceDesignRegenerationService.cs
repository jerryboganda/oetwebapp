using OetLearner.Api.Contracts;

namespace OetLearner.Api.Services.VoiceDesign;

public interface IVoiceDesignRegenerationService
{
    Task<AdminAudioRegenerateBatchResult> EnqueueBulkRegenerationAsync(
        AdminAudioRegenerateRequest request, string requestedBy, CancellationToken ct);
    Task<IReadOnlyList<AdminAudioBatchDto>> GetBatchesAsync(CancellationToken ct);
    Task<AdminAudioBatchDto?> GetBatchProgressAsync(string batchId, CancellationToken ct);
    Task<bool> CancelBatchAsync(string batchId, CancellationToken ct);
    Task<AdminAudioBatchDto?> RetryFailedBatchAsync(string batchId, string requestedBy, CancellationToken ct);

    /// <summary>
    /// Delete audio storage objects under recalls/audio and vocabulary/audio that
    /// are not referenced by any MediaAsset row. Pass <paramref name="dryRun"/> =
    /// true to report what would be deleted without touching storage.
    /// </summary>
    Task<AdminAudioOrphanCleanupResult> CleanupOrphanedAudioAsync(bool dryRun, CancellationToken ct);
}
