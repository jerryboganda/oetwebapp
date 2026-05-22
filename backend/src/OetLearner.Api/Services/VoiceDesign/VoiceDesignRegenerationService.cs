using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Vocabulary;

namespace OetLearner.Api.Services.VoiceDesign;

public sealed class VoiceDesignRegenerationService(
    LearnerDbContext db,
    IVocabularyAudioQueue vocabularyQueue,
    ILogger<VoiceDesignRegenerationService> logger) : IVoiceDesignRegenerationService
{
    public async Task<AdminAudioRegenerateBatchResult> EnqueueBulkRegenerationAsync(
        AdminAudioRegenerateRequest request, string requestedBy, CancellationToken ct)
    {
        var batchId = $"vd-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString()[..8]}";
        var modelVariant = request.ModelVariant ?? "flash";
        var voiceId = request.VoiceId ?? "Cherry";
        var isDryRun = request.DryRun ?? false;

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
            var convQuery = db.ConversationTemplates.Where(t => t.Status == "published");
            convQuery = request.Scope switch
            {
                "missing" => convQuery.Where(t => t.OpeningAudioSha == null),
                "different-voice" => convQuery.Where(t => t.TtsVoice != voiceId || t.TtsModelVariant != modelVariant),
                _ => convQuery,
            };
            totalItems += await convQuery.CountAsync(ct);
        }

        if (isDryRun)
        {
            return new AdminAudioRegenerateBatchResult(batchId, request.AudioType, request.Scope,
                totalItems, true, modelVariant, voiceId);
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
                    Instructions: request.Instructions), ct);
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

        return new AdminAudioRegenerateBatchResult(batchId, request.AudioType, request.Scope,
            totalItems, false, modelVariant, voiceId);
    }

    public async Task<IReadOnlyList<AdminAudioBatchDto>> GetBatchesAsync(CancellationToken ct)
    {
        var batches = await db.AudioRegenerationBatches
            .OrderByDescending(b => b.StartedAt)
            .Take(20)
            .ToListAsync(ct);

        return batches.Select(b => new AdminAudioBatchDto(
            b.Id, b.AudioType, b.Scope, b.Status,
            b.TotalItems, b.CompletedItems, b.FailedItems,
            b.VoiceId, b.ModelVariant, b.Speed, b.Pitch, b.Emotion,
            b.StartedAt.ToString("o"), b.CompletedAt?.ToString("o"),
            b.RequestedBy)).ToList();
    }

    public async Task<AdminAudioBatchDto?> GetBatchProgressAsync(string batchId, CancellationToken ct)
    {
        var b = await db.AudioRegenerationBatches.FindAsync([batchId], ct);
        if (b is null) return null;

        // Recompute progress from actual job state
        var vocabCompleted = await db.VocabularyTerms
            .CountAsync(t => t.AudioBatchId == batchId && t.AudioMediaAssetId != null, ct);
        var listeningCompleted = await db.ListeningTtsJobs
            .CountAsync(j => j.BatchId == batchId && j.Status == ListeningTtsJobStatus.Completed, ct);
        var listeningFailed = await db.ListeningTtsJobs
            .CountAsync(j => j.BatchId == batchId && j.Status == ListeningTtsJobStatus.Failed, ct);

        b.CompletedItems = vocabCompleted + listeningCompleted;
        b.FailedItems = listeningFailed;

        if (b.CompletedItems + b.FailedItems >= b.TotalItems && b.Status == "running")
        {
            b.Status = "completed";
            b.CompletedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);

        return new AdminAudioBatchDto(
            b.Id, b.AudioType, b.Scope, b.Status,
            b.TotalItems, b.CompletedItems, b.FailedItems,
            b.VoiceId, b.ModelVariant, b.Speed, b.Pitch, b.Emotion,
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
}
