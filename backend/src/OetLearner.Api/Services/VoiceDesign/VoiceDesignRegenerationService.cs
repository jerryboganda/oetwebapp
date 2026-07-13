using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Vocabulary;

namespace OetLearner.Api.Services.VoiceDesign;

public sealed class VoiceDesignRegenerationService(
    LearnerDbContext db,
    IVocabularyAudioQueue vocabularyQueue,
    IConversationOptionsProvider optionsProvider,
    IFileStorage storage,
    ILogger<VoiceDesignRegenerationService> logger) : IVoiceDesignRegenerationService
{
    public async Task<AdminAudioRegenerateBatchResult> EnqueueBulkRegenerationAsync(
        AdminAudioRegenerateRequest request, string requestedBy, CancellationToken ct)
    {
        var batchId = $"vd-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString()[..8]}";
        if (string.Equals(request.AudioType, "conversation", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "conversation_audio_regeneration_not_supported",
                "Conversation template audio regeneration is not supported by this batch worker yet.");
        }

        // ElevenLabs is the only TTS provider — every audio type generates
        // through it, using the admin-configured model + default voice unless
        // the request overrides them.
        var options = await optionsProvider.GetAsync(ct);
        const string providerName = "elevenlabs";
        var modelVariant = request.ModelVariant ?? options.ElevenLabsModel;
        var voiceId = request.VoiceId ?? options.ElevenLabsDefaultVoiceId;
        var isDryRun = request.DryRun ?? false;
        var forceRegenerate = request.ForceRegenerate ?? false;

        if (string.IsNullOrWhiteSpace(options.ElevenLabsApiKey))
        {
            throw ApiException.Validation(
                "elevenlabs_api_key_required",
                "Save an ElevenLabs API key in Admin Voice Design before starting audio generation.");
        }

        int totalItems = 0;

        // Count items based on audio type and scope
        if (request.AudioType is "all" or "vocabulary")
        {
            var vocabQuery = db.VocabularyTerms.Where(t => t.Status == "active");
            vocabQuery = request.Scope switch
            {
                "missing" => vocabQuery.Where(t => t.AudioMediaAssetId == null),
                "different-voice" => vocabQuery.Where(t => t.AudioVoice != voiceId || t.AudioModelVariant != modelVariant),
                _ => vocabQuery,
            };
            totalItems += await vocabQuery.CountAsync(ct);
        }

        if (request.AudioType is "all" or "listening")
        {
            var listeningQuery = db.ListeningExtracts.AsQueryable();
            listeningQuery = request.Scope switch
            {
                "missing" => listeningQuery.Where(e => e.AudioContentSha == null),
                "different-voice" => listeningQuery.Where(e => e.TtsVoice != voiceId || e.TtsModelVariant != modelVariant),
                _ => listeningQuery,
            };
            totalItems += await listeningQuery.CountAsync(ct);
        }

        if (request.AudioType is "all" or "conversation")
        {
            // Intentionally excluded until conversation audio has a dedicated
            // queue/worker. Counting it here would create batches that cannot finish.
        }

        if (request.AudioType is "recalls")
        {
            totalItems += (await GetRecallAudioCandidatesAsync(request.Scope, voiceId, modelVariant, providerName, retryOnly: false, batchId: null, ct)).Count;
        }

        if (isDryRun)
        {
            return new AdminAudioRegenerateBatchResult(batchId, request.AudioType, request.Scope,
                totalItems, true, modelVariant, voiceId, providerName);
        }

        // Persist the batch record
        var batch = new AudioRegenerationBatch
        {
            Id = batchId,
            AudioType = request.AudioType,
            Scope = request.Scope,
            Status = "running",
            TotalItems = totalItems,
            CompletedItems = 0,
            FailedItems = 0,
            VoiceId = voiceId,
            ModelVariant = modelVariant,
            ProviderName = providerName,
            Speed = request.Speed ?? 1.0,
            Pitch = request.Pitch ?? 0,
            Emotion = request.Emotion ?? "",
            Instructions = request.Instructions ?? "",
            RequestedBy = requestedBy,
            StartedAt = DateTime.UtcNow,
        };
        db.AudioRegenerationBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        // Enqueue vocabulary items
        if (request.AudioType is "all" or "vocabulary")
        {
            var vocabTerms = await db.VocabularyTerms
                .Where(t => t.Status == "active")
                .Where(t => request.Scope == "missing"
                    ? t.AudioMediaAssetId == null
                    : request.Scope == "different-voice"
                        ? (t.AudioVoice != voiceId || t.AudioModelVariant != modelVariant)
                        : true)
                .Select(t => new { t.Id, t.Term })
                .Take(50_000)
                .ToListAsync(ct);

            foreach (var term in vocabTerms)
            {
                await vocabularyQueue.EnqueueAsync(new VocabularyAudioJob(
                    TermId: term.Id,
                    Text: term.Term,
                    Voice: voiceId,
                    Locale: "en-GB",
                    BatchId: batchId,
                    ModelVariant: modelVariant,
                    Instructions: request.Instructions,
                    ProviderName: providerName,
                    ForceRegenerate: forceRegenerate), ct);
            }
            logger.LogInformation("Enqueued {Count} vocabulary terms for batch {BatchId}", vocabTerms.Count, batchId);
        }

        // Enqueue listening items
        if (request.AudioType is "all" or "listening")
        {
            var extracts = await db.ListeningExtracts
                .AsQueryable()
                .Where(e => request.Scope == "missing"
                    ? e.AudioContentSha == null
                    : request.Scope == "different-voice"
                        ? (e.TtsVoice != voiceId || e.TtsModelVariant != modelVariant)
                        : true)
                .Select(e => e.Id)
                .Take(50_000)
                .ToListAsync(ct);

            foreach (var extractId in extracts)
            {
                db.ListeningTtsJobs.Add(new ListeningTtsJob
                {
                    Id = Guid.NewGuid().ToString(),
                    ExtractId = extractId,
                    RequestedBy = requestedBy,
                    Status = ListeningTtsJobStatus.Pending,
                    BatchId = batchId,
                    VoiceOverride = voiceId,
                    ModelVariantOverride = modelVariant,
                    InstructionsOverride = request.Instructions,
                    SpeedOverride = request.Speed,
                    PitchOverride = request.Pitch,
                });
            }
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Created {Count} listening TTS jobs for batch {BatchId}", extracts.Count, batchId);
        }

        if (request.AudioType is "recalls")
        {
            var recallTerms = await GetRecallAudioCandidatesAsync(request.Scope, voiceId, modelVariant, providerName, retryOnly: false, batchId: null, ct);

            foreach (var term in recallTerms)
            {
                await vocabularyQueue.EnqueueAsync(new VocabularyAudioJob(
                    TermId: term.Id,
                    Text: term.Term,
                    Voice: voiceId,
                    Locale: "en-GB",
                    BatchId: batchId,
                    ModelVariant: modelVariant,
                    Instructions: request.Instructions,
                    ProviderName: providerName,
                    ForceRegenerate: forceRegenerate), ct);
            }
            logger.LogInformation("Enqueued {Count} recall terms for ElevenLabs batch {BatchId}", recallTerms.Count, batchId);
        }

        return new AdminAudioRegenerateBatchResult(batchId, request.AudioType, request.Scope,
            totalItems, false, modelVariant, voiceId, providerName);
    }

    public async Task<IReadOnlyList<AdminAudioBatchDto>> GetBatchesAsync(CancellationToken ct)
    {
        var batches = await db.AudioRegenerationBatches
            .OrderByDescending(b => b.StartedAt)
            .Take(20)
            .ToListAsync(ct);

        foreach (var batch in batches)
        {
            await RecomputeProgressAsync(batch, ct);
        }

        return batches.Select(b => new AdminAudioBatchDto(
            b.Id, b.AudioType, b.Scope, b.Status,
            b.TotalItems, b.CompletedItems, b.FailedItems,
            b.VoiceId, b.ModelVariant, b.ProviderName, b.Speed, b.Pitch, b.Emotion,
            b.StartedAt.ToString("o"), b.CompletedAt?.ToString("o"),
            b.RequestedBy)).ToList();
    }

    public async Task<AdminAudioBatchDto?> GetBatchProgressAsync(string batchId, CancellationToken ct)
    {
        var b = await db.AudioRegenerationBatches.FindAsync([batchId], ct);
        if (b is null) return null;

        await RecomputeProgressAsync(b, ct);

        return new AdminAudioBatchDto(
            b.Id, b.AudioType, b.Scope, b.Status,
            b.TotalItems, b.CompletedItems, b.FailedItems,
            b.VoiceId, b.ModelVariant, b.ProviderName, b.Speed, b.Pitch, b.Emotion,
            b.StartedAt.ToString("o"), b.CompletedAt?.ToString("o"),
            b.RequestedBy);
    }

    public async Task<bool> CancelBatchAsync(string batchId, CancellationToken ct)
    {
        var batch = await db.AudioRegenerationBatches.FindAsync([batchId], ct);
        if (batch is null || batch.Status != "running") return false;

        batch.Status = "cancelled";
        batch.CompletedAt = DateTime.UtcNow;

        // Cancel pending listening jobs in this batch
        await db.ListeningTtsJobs
            .Where(j => j.BatchId == batchId && j.Status == ListeningTtsJobStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, ListeningTtsJobStatus.Failed)
                .SetProperty(j => j.ErrorMessage, "Batch cancelled by admin"), ct);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Cancelled batch {BatchId}", batchId);
        return true;
    }

    public async Task<AdminAudioBatchDto?> RetryFailedBatchAsync(string batchId, string requestedBy, CancellationToken ct)
    {
        var batch = await db.AudioRegenerationBatches.FindAsync([batchId], ct);
        if (batch is null || batch.AudioType != "recalls") return null;

        await RecomputeProgressAsync(batch, ct);

        var retryTerms = await GetRecallAudioCandidatesAsync(
            batch.Scope,
            batch.VoiceId,
            batch.ModelVariant,
            batch.ProviderName ?? "elevenlabs",
            retryOnly: true,
            batchId: batch.Id,
            ct);

        batch.Status = "running";
        batch.CompletedAt = null;
        batch.FailedItems = 0;
        batch.TotalItems = batch.CompletedItems + retryTerms.Count;
        batch.RequestedBy = requestedBy;
        await db.SaveChangesAsync(ct);

        foreach (var term in retryTerms)
        {
            await vocabularyQueue.EnqueueAsync(new VocabularyAudioJob(
                TermId: term.Id,
                Text: term.Term,
                Voice: batch.VoiceId,
                Locale: "en-GB",
                BatchId: batch.Id,
                ModelVariant: batch.ModelVariant,
                Instructions: batch.Instructions,
                ProviderName: "elevenlabs",
                ForceRegenerate: false), ct);
        }

        logger.LogInformation("Re-enqueued {Count} failed/missing recall terms for batch {BatchId}", retryTerms.Count, batch.Id);
        return await GetBatchProgressAsync(batch.Id, ct);
    }

    public async Task<AdminAudioOrphanCleanupResult> CleanupOrphanedAudioAsync(bool dryRun, CancellationToken ct)
    {
        // Audio prefixes written by the vocab/recall pipeline.
        var prefixes = new[] { "recalls/audio", "vocabulary/audio" };

        // Every storage path still referenced by a MediaAsset row — anything not
        // in this set under the audio prefixes is an orphan safe to delete.
        var referenced = new HashSet<string>(
            await db.MediaAssets
                .Select(m => m.StoragePath)
                .Where(p => p != null)
                .ToListAsync(ct)
                .ConfigureAwait(false),
            StringComparer.Ordinal);

        var orphans = new List<string>();
        foreach (var prefix in prefixes)
        {
            await foreach (var key in storage.ListKeysAsync(prefix, ct))
            {
                ct.ThrowIfCancellationRequested();
                if (!referenced.Contains(key))
                    orphans.Add(key);
            }
        }

        var deleted = 0;
        long bytes = 0;
        if (!dryRun)
        {
            foreach (var key in orphans)
            {
                try
                {
                    var size = await storage.LengthAsync(key, ct);
                    if (await storage.DeleteAsync(key, ct)) { deleted++; bytes += Math.Max(0, size); }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Orphan-audio cleanup: failed to delete {Key}", key);
                }
            }
        }

        logger.LogInformation(
            "Orphan-audio cleanup: {Found} orphan(s), {Deleted} deleted, {Bytes} bytes (dryRun={DryRun})",
            orphans.Count, deleted, bytes, dryRun);

        return new AdminAudioOrphanCleanupResult(
            OrphansFound: orphans.Count,
            Deleted: deleted,
            BytesReclaimed: bytes,
            DryRun: dryRun,
            Sample: orphans.Take(50).ToList());
    }

    private IQueryable<VocabularyTerm> RecallTermsForGeneration()
        => db.VocabularyTerms.Where(t =>
            t.Status != "archived"
            && t.RecallSetCodesJson != null
            && t.RecallSetCodesJson != ""
            && t.RecallSetCodesJson != "[]");

    private async Task<List<RecallAudioCandidate>> GetRecallAudioCandidatesAsync(
        string? scope,
        string? voiceId,
        string? modelVariant,
        string? providerName,
        bool retryOnly,
        string? batchId,
        CancellationToken ct)
    {
        var rows = await RecallTermsForGeneration()
            .OrderBy(t => t.Id)
            .Take(50_000)
            .Select(t => new RecallAudioCandidateRow(
                t.Id,
                t.Term,
                t.AudioUrl,
                t.AudioMediaAssetId,
                t.AudioProvider,
                t.AudioVoice,
                t.AudioModelVariant,
                t.AudioBatchId,
                t.AudioMediaAssetId != null && db.MediaAssets.Any(asset => asset.Id == t.AudioMediaAssetId && asset.Status == MediaAssetStatus.Ready)))
            .ToListAsync(ct);

        var normalizedScope = string.IsNullOrWhiteSpace(scope) ? "missing" : scope.Trim().ToLowerInvariant();
        var candidates = new List<RecallAudioCandidateRow>();
        foreach (var row in rows)
        {
            var matchesScope = normalizedScope switch
            {
                "all" => true,
                "different-voice" => await IsMissingStoredAudioAsync(row, ct)
                    || !MatchesRecallAudioProvenance(row, voiceId, modelVariant, providerName),
                _ => await IsMissingStoredAudioAsync(row, ct),
            };
            if (!matchesScope)
            {
                continue;
            }

            if (retryOnly
                && !await IsMissingStoredAudioAsync(row, ct)
                && MatchesRecallAudioProvenance(row, voiceId, modelVariant, providerName)
                && string.Equals(row.AudioBatchId, batchId, StringComparison.Ordinal))
            {
                continue;
            }

            candidates.Add(row);
        }

        return candidates
            .Where(row => !string.IsNullOrWhiteSpace(row.Term))
            .Select(row => new RecallAudioCandidate(row.Id, row.Term))
            .ToList();
    }

    private async Task<bool> IsMissingStoredAudioAsync(
        RecallAudioCandidateRow row,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(row.AudioMediaAssetId)
            || string.IsNullOrWhiteSpace(row.AudioUrl)
            || !row.MediaAssetReady
            || !IsStoredAudioKey(row.AudioUrl))
        {
            return true;
        }

        return !await storage.ExistsAsync(row.AudioUrl, ct);
    }

    private static bool MatchesRecallAudioProvenance(
        RecallAudioCandidateRow row,
        string? voiceId,
        string? modelVariant,
        string? providerName)
        => string.Equals(row.AudioProvider, providerName ?? "elevenlabs", StringComparison.OrdinalIgnoreCase)
           && (string.IsNullOrWhiteSpace(modelVariant) || string.Equals(row.AudioModelVariant, modelVariant, StringComparison.OrdinalIgnoreCase))
           && (string.IsNullOrWhiteSpace(voiceId) || string.Equals(row.AudioVoice, voiceId, StringComparison.OrdinalIgnoreCase));

    private static bool IsStoredAudioKey(string value)
        => !string.IsNullOrWhiteSpace(value)
           && !value.StartsWith("/", StringComparison.Ordinal)
           && !Uri.TryCreate(value, UriKind.Absolute, out _);

    private sealed record RecallAudioCandidate(string Id, string Term);

    private sealed record RecallAudioCandidateRow(
        string Id,
        string Term,
        string? AudioUrl,
        string? AudioMediaAssetId,
        string? AudioProvider,
        string? AudioVoice,
        string? AudioModelVariant,
        string? AudioBatchId,
        bool MediaAssetReady);

    private async Task RecomputeProgressAsync(AudioRegenerationBatch batch, CancellationToken ct)
    {
        var vocabCompleted = await db.VocabularyTerms
            .CountAsync(t => t.AudioBatchId == batch.Id
                             && t.AudioMediaAssetId != null
                             && db.MediaAssets.Any(asset => asset.Id == t.AudioMediaAssetId
                                                            && asset.Status == MediaAssetStatus.Ready), ct);
        var listeningCompleted = await db.ListeningTtsJobs
            .CountAsync(j => j.BatchId == batch.Id && j.Status == ListeningTtsJobStatus.Completed, ct);
        var listeningFailed = await db.ListeningTtsJobs
            .CountAsync(j => j.BatchId == batch.Id && j.Status == ListeningTtsJobStatus.Failed, ct);

        batch.CompletedItems = vocabCompleted + listeningCompleted;
        batch.FailedItems = Math.Max(batch.FailedItems, listeningFailed);

        if (batch.CompletedItems + batch.FailedItems >= batch.TotalItems && batch.Status == "running")
        {
            batch.Status = "completed";
            batch.CompletedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
