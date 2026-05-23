using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Conversation.Tts;

namespace OetLearner.Api.Services.Vocabulary;

/// <summary>
/// Drains <see cref="IVocabularyAudioQueue"/>, synthesises MP3 audio via
/// the configured <see cref="IConversationTtsProvider"/>, persists the bytes
/// through <see cref="IFileStorage"/> (content-addressed), upserts a
/// <see cref="MediaAsset"/>, and patches the originating
/// <see cref="VocabularyTerm"/> with <c>AudioMediaAssetId</c> + <c>AudioUrl</c>.
///
/// Bounded to 3 in-flight jobs to avoid overwhelming third-party TTS APIs.
/// Per-job failures are retried up to 5 times with exponential backoff
/// (cap 60s); a terminal failure emits a <c>VocabAudioFailed</c> audit row.
/// </summary>
public sealed class VocabularyAudioWorker(
    IVocabularyAudioQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<VocabularyAudioWorker> logger) : BackgroundService
{
    private const int MaxConcurrency = 1;
    private const int MaxAttempts = 8;
    private const int MaxTextBytes = 4 * 1024;
    private const int InterJobDelayMs = 1100;
    private const int MaxAudioUrlChars = 256;

    private readonly SemaphoreSlim _slots = new(MaxConcurrency, MaxConcurrency);
    // Global cooldown — when the upstream provider returns HTTP 429 we delay
    // ALL subsequent calls until this timestamp so the worker stops flooding.
    private DateTimeOffset _nextAllowedAt = DateTimeOffset.MinValue;
    private readonly object _gateLock = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ResumeRunningRecallBatchesAsync(stoppingToken).ConfigureAwait(false);

            await foreach (var job in queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await _slots.WaitAsync(stoppingToken).ConfigureAwait(false);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessOneAsync(job, stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Vocabulary audio job {TermId} crashed unexpectedly", job.TermId);
                    }
                    finally
                    {
                        _slots.Release();
                    }
                }, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
    }

    /// <summary>
    /// Process a single job. Public so tests can drive the worker without
    /// starting the hosted-service loop.
    /// </summary>
    public async Task ProcessOneAsync(VocabularyAudioJob job, CancellationToken ct)
    {
        try
        {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<LearnerDbContext>();
        var selector = sp.GetRequiredService<IConversationTtsProviderSelector>();
        var storage = sp.GetRequiredService<IFileStorage>();

        var isRecallBatch = await IsRecallBatchAsync(db, job.BatchId, ct).ConfigureAwait(false);
        if (isRecallBatch && !string.Equals(job.ProviderName, "elevenlabs", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Vocab audio job {TermId} skipped — recall batch {BatchId} requires ElevenLabs", job.TermId, job.BatchId);
            await MarkBatchFailureAsync(db, job, "invalid_recall_audio_provider", ct).ConfigureAwait(false);
            return;
        }

        if (await IsBatchCancelledAsync(db, job.BatchId, ct).ConfigureAwait(false))
        {
            logger.LogInformation("Vocab audio job {TermId} skipped — batch {BatchId} is cancelled", job.TermId, job.BatchId);
            return;
        }

        if (string.IsNullOrWhiteSpace(job.Text))
        {
            logger.LogInformation("Vocab audio job {TermId} skipped — empty text", job.TermId);
            await MarkBatchFailureAsync(db, job, "empty_text", ct).ConfigureAwait(false);
            return;
        }

        if (Encoding.UTF8.GetByteCount(job.Text) > MaxTextBytes)
        {
            logger.LogWarning("Vocab audio job {TermId} skipped — text exceeds {Max} bytes", job.TermId, MaxTextBytes);
            await MarkBatchFailureAsync(db, job, "text_too_large", ct).ConfigureAwait(false);
            return;
        }

        var term = await db.VocabularyTerms.FirstOrDefaultAsync(t => t.Id == job.TermId, ct).ConfigureAwait(false);
        if (term is null)
        {
            logger.LogWarning("Vocab audio job {TermId} dropped — term not found", job.TermId);
            await MarkBatchFailureAsync(db, job, "term_not_found", ct).ConfigureAwait(false);
            return;
        }

        var isRecallTerm = IsRecallTerm(term);
        var isRecallAudioJob = isRecallBatch || isRecallTerm;
        if (isRecallAudioJob && !string.Equals(job.ProviderName, "elevenlabs", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Vocab audio job {TermId} skipped — recall terms require ElevenLabs", job.TermId);
            await MarkBatchFailureAsync(db, job, "invalid_recall_audio_provider", ct).ConfigureAwait(false);
            return;
        }

        if (!job.ForceRegenerate
            && !string.IsNullOrWhiteSpace(term.AudioMediaAssetId)
            && !string.IsNullOrWhiteSpace(term.AudioUrl)
            && IsStoredAudioKey(term.AudioUrl)
            && storage.Exists(term.AudioUrl)
            && CanReuseExistingAudio(term, job, isRecallAudioJob))
        {
            term.AudioBatchId = job.BatchId;
            term.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            logger.LogInformation("Vocab audio job {TermId} skipped — stored audio already exists", job.TermId);
            return;
        }

        IConversationTtsProvider? provider;
        if (!string.IsNullOrWhiteSpace(job.ProviderName))
        {
            provider = await selector.TrySelectAsync(job.ProviderName, ct).ConfigureAwait(false);
        }
        else
        {
            if (await selector.IsTtsDisabledAsync(ct).ConfigureAwait(false))
            {
                logger.LogInformation("Vocab audio job {TermId} dropped — TTS provider is off", job.TermId);
                await MarkBatchFailureAsync(db, job, "tts_provider_off", ct).ConfigureAwait(false);
                return;
            }

            provider = await selector.TrySelectAsync(ct).ConfigureAwait(false);
        }

        if (provider is null)
        {
            logger.LogInformation("Vocab audio job {TermId} dropped — no TTS provider available", job.TermId);
            await MarkBatchFailureAsync(db, job, "tts_provider_unavailable", ct).ConfigureAwait(false);
            return;
        }

        if (isRecallAudioJob && !string.Equals(provider.Name, "elevenlabs", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Vocab audio job {TermId} skipped — selected provider {Provider} is not allowed for recall audio", job.TermId, provider.Name);
            await MarkBatchFailureAsync(db, job, "invalid_recall_audio_provider", ct).ConfigureAwait(false);
            return;
        }

        if (string.Equals(provider.Name, "mock", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Vocab audio job {TermId} using mock TTS provider (placeholder audio)", job.TermId);
        }

        // Respect global cooldown set by a previous 429 response.
        DateTimeOffset gate;
        lock (_gateLock) { gate = _nextAllowedAt; }
        var waitMs = (int)Math.Max(0, (gate - DateTimeOffset.UtcNow).TotalMilliseconds);
        if (waitMs > 0)
        {
            try { await Task.Delay(waitMs, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
        }

        if (await IsBatchCancelledAsync(db, job.BatchId, ct).ConfigureAwait(false))
        {
            logger.LogInformation("Vocab audio job {TermId} skipped — batch {BatchId} was cancelled before synthesis", job.TermId, job.BatchId);
            return;
        }

        ConversationTtsResult result;
        try
        {
            result = await provider.SynthesizeAsync(
                new ConversationTtsRequest(
                    job.Text,
                    job.Voice ?? string.Empty,
                    job.Locale ?? "en-GB",
                    Rate: null,
                    Pitch: null,
                    ModelVariant: job.ModelVariant,
                    Instructions: job.Instructions),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await HandleFailureAsync(db, job, ex, ct).ConfigureAwait(false);
            return;
        }
        // Pace successful calls to stay under DigitalOcean Inference rate limits.
        try { await Task.Delay(InterJobDelayMs, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }

        if (await IsBatchCancelledAsync(db, job.BatchId, ct).ConfigureAwait(false))
        {
            logger.LogInformation("Vocab audio job {TermId} skipped — batch {BatchId} was cancelled before storage", job.TermId, job.BatchId);
            return;
        }

        var audio = result.Audio ?? Array.Empty<byte>();
        var sha = Convert.ToHexString(SHA256.HashData(audio)).ToLowerInvariant();
        var mime = string.IsNullOrWhiteSpace(result.MimeType) ? "audio/mpeg" : result.MimeType;
        var ext = mime.Contains("wav", StringComparison.OrdinalIgnoreCase) ? "wav"
                  : mime.Contains("ogg", StringComparison.OrdinalIgnoreCase) ? "ogg"
                  : "mp3";
        var recallCode = isRecallAudioJob ? FirstRecallSetCode(term.RecallSetCodesJson) : null;
        var key = isRecallAudioJob && string.Equals(provider.Name, "elevenlabs", StringComparison.OrdinalIgnoreCase)
            ? $"recalls/audio/{Slug(recallCode ?? "recall")}-{Slug(job.TermId)}-{sha[..16]}.{ext}"
            : ContentAddressed.PublishedKey("vocabulary/audio", sha, ext);

        if (!storage.Exists(key))
        {
            await using var ms = new MemoryStream(audio);
            await storage.WriteAsync(key, ms, ct).ConfigureAwait(false);
        }

        // Capture the previous asset id BEFORE we reassign so we can
        // ref-count and hard-delete it once the new asset is committed and
        // no other rows still point at it. Critical for Voice Studio
        // regeneration runs — otherwise old (bad-quality) audio bytes
        // remain in storage indefinitely.
        var previousAssetId = term.AudioMediaAssetId;

        var asset = await db.MediaAssets.FirstOrDefaultAsync(m => m.StoragePath == key, ct).ConfigureAwait(false);
        if (asset is null)
        {
            asset = new MediaAsset
            {
                Id = "MA-" + Guid.NewGuid().ToString("N")[..12],
                OriginalFilename = isRecallAudioJob && string.Equals(provider.Name, "elevenlabs", StringComparison.OrdinalIgnoreCase)
                    ? $"{Slug(recallCode ?? "recall")}-{job.TermId}.{ext}"
                    : $"{Slug(job.Text)}.{ext}",
                MimeType = mime,
                Format = ext,
                SizeBytes = audio.LongLength,
                DurationSeconds = (int)Math.Ceiling(Math.Max(0, result.DurationMs) / 1000.0),
                StoragePath = key,
                Status = MediaAssetStatus.Ready,
                Sha256 = sha,
                MediaKind = "audio",
                UploadedBy = "system-vocab-tts",
                UploadedAt = DateTimeOffset.UtcNow,
                ProcessedAt = DateTimeOffset.UtcNow,
            };
            db.MediaAssets.Add(asset);
        }

        term.AudioMediaAssetId = asset.Id;
        term.AudioUrl = key.Length > MaxAudioUrlChars ? key[..MaxAudioUrlChars] : key;
        term.AudioBatchId = job.BatchId;
        // Provenance — lets the admin filter "regenerate where voice ≠ current"
        // without re-listening to every term. AudioVoice for Qwen3 voicedesign
        // is a SHA-8 of the instructions prompt so prompt changes register as
        // a different voice.
        term.AudioProvider = provider.Name;
        term.AudioModelVariant = job.ModelVariant;
        if (string.Equals(job.ModelVariant, "voicedesign", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(job.Instructions))
        {
            var promptHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(job.Instructions!))).ToLowerInvariant();
            term.AudioVoice = "vd-" + promptHash[..8];
        }
        else
        {
            term.AudioVoice = job.Voice;
        }
        term.AudioGeneratedAt = DateTimeOffset.UtcNow;
        term.UpdatedAt = DateTimeOffset.UtcNow;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = "system",
            ActorName = "vocab-audio-worker",
            Action = "VocabAudioGenerated",
            ResourceType = "VocabularyTerm",
            ResourceId = job.TermId,
            Details = JsonSerializer.Serialize(new
            {
                provider = provider.Name,
                sha256 = sha,
                sizeBytes = audio.LongLength,
                durationMs = result.DurationMs,
                batchId = job.BatchId,
                attempt = job.AttemptCount + 1,
            }),
        });

        if (await IsBatchCancelledAsync(db, job.BatchId, ct).ConfigureAwait(false))
        {
            storage.Delete(key);
            logger.LogInformation("Vocab audio job {TermId} generated audio was discarded because batch {BatchId} was cancelled", job.TermId, job.BatchId);
            return;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        logger.LogInformation(
            "Vocab audio job {TermId} stored ({Bytes} bytes, sha {Sha}) via {Provider}",
            job.TermId, audio.LongLength, sha[..8], provider.Name);

        // Best-effort orphaned-asset sweep — if the term used to point at a
        // DIFFERENT MediaAsset and nothing else still references it, hard-delete
        // both the DB row and the underlying storage object. Mirrors the
        // canonical refcount check in MediaEndpoints.DeleteAsync().
        if (!string.IsNullOrWhiteSpace(previousAssetId) && previousAssetId != asset.Id)
        {
            try
            {
                await TryDeleteOrphanedAssetAsync(db, storage, previousAssetId!, job.TermId, job.BatchId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Storage cleanup is non-fatal — log and continue. The new
                // audio is already live and audible to learners.
                logger.LogWarning(ex, "Vocab audio job {TermId}: failed to sweep previous asset {Old}", job.TermId, previousAssetId);
            }
        }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await HandleFailureAsync(db, job, ex, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Delete a MediaAsset row + storage object IF nothing else still
    /// references it. Mirrors the refcount tables checked by the public
    /// /v1/media/{id} DELETE endpoint, plus the vocabulary cross-check.
    /// </summary>
    private async Task TryDeleteOrphanedAssetAsync(
        LearnerDbContext db, IFileStorage storage, string assetId, string sourceTermId, string batchId, CancellationToken ct)
    {
        var asset = await db.MediaAssets.FirstOrDefaultAsync(m => m.Id == assetId, ct).ConfigureAwait(false);
        if (asset is null) return;

        var stillUsed = await db.VocabularyTerms.AnyAsync(t => t.AudioMediaAssetId == assetId, ct).ConfigureAwait(false)
            || await db.ContentPaperAssets.AnyAsync(link => link.MediaAssetId == assetId, ct).ConfigureAwait(false)
            || await db.WritingAttemptAssets.AnyAsync(link => link.MediaAssetId == assetId, ct).ConfigureAwait(false)
            || await db.ReviewVoiceNotes.AnyAsync(note => note.MediaAssetId == assetId, ct).ConfigureAwait(false)
            || await db.RulebookVersions.AnyAsync(rb => rb.ReferencePdfAssetId == assetId, ct).ConfigureAwait(false)
            || await db.ResultTemplateAssets.AnyAsync(rt => rt.MediaAssetId == assetId, ct).ConfigureAwait(false)
            || await db.SpeakingSharedResources.AnyAsync(sr => sr.MediaAssetId == assetId, ct).ConfigureAwait(false);

        if (stillUsed)
        {
            logger.LogDebug("Vocab audio orphan-sweep: asset {AssetId} still referenced — keeping", assetId);
            return;
        }

        var storagePath = asset.StoragePath;
        db.MediaAssets.Remove(asset);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = "system",
            ActorName = "vocab-audio-worker",
            Action = "VocabAudioMediaAssetDeleted",
            ResourceType = "MediaAsset",
            ResourceId = assetId,
            Details = JsonSerializer.Serialize(new
            {
                reason = "orphaned_after_regenerate",
                supersededByTerm = sourceTermId,
                batchId,
                storagePath,
            }),
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        try { storage.Delete(storagePath); }
        catch (Exception ex) { logger.LogWarning(ex, "Vocab audio orphan-sweep: storage delete failed for {Path}", storagePath); }
    }

    private async Task HandleFailureAsync(
        LearnerDbContext db, VocabularyAudioJob job, Exception ex, CancellationToken ct)
    {
        // Treat upstream HTTP 429 as a transient throttle: long fixed backoff
        // and DO NOT count it against MaxAttempts, otherwise a flood of 429s
        // permanently fails legitimate jobs.
        var isRateLimit = (ex.Message ?? string.Empty).Contains("429", StringComparison.Ordinal);
        if (isRateLimit)
        {
            // Set/extend a global cooldown so other in-flight workers stop
            // hammering the provider. 60s is enough to clear a typical token
            // bucket without permanently starving the queue.
            var cooldown = DateTimeOffset.UtcNow.AddSeconds(60);
            lock (_gateLock)
            {
                if (cooldown > _nextAllowedAt) _nextAllowedAt = cooldown;
            }
            logger.LogWarning(
                "Vocab audio job {TermId} rate-limited (429); global cooldown until {When:O}, requeueing without counting attempt",
                job.TermId, _nextAllowedAt);
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                    await queue.EnqueueAsync(job, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception inner)
                {
                    logger.LogError(inner, "Failed to re-enqueue rate-limited vocab audio job {TermId}", job.TermId);
                }
            }, ct);
            return;
        }
        var nextAttempt = job.AttemptCount + 1;
        if (nextAttempt < MaxAttempts)
        {
            var delaySeconds = Math.Min(60, Math.Pow(2, nextAttempt));
            logger.LogWarning(ex,
                "Vocab audio job {TermId} attempt {Attempt} failed; retrying in {Delay}s",
                job.TermId, nextAttempt, delaySeconds);
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct).ConfigureAwait(false);
                    await queue.EnqueueAsync(job with { AttemptCount = nextAttempt }, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception inner)
                {
                    logger.LogError(inner, "Failed to re-enqueue vocab audio job {TermId}", job.TermId);
                }
            }, ct);
            return;
        }

        var msg = ex.Message ?? string.Empty;
        if (msg.Length > 1024) msg = msg[..1024];

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = "system",
            ActorName = "vocab-audio-worker",
            Action = "VocabAudioFailed",
            ResourceType = "VocabularyTerm",
            ResourceId = job.TermId,
            Details = JsonSerializer.Serialize(new
            {
                batchId = job.BatchId,
                attempt = nextAttempt,
                error = msg,
            }),
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await MarkBatchFailureAsync(db, job, "provider_error", ct).ConfigureAwait(false);
        logger.LogError(ex, "Vocab audio job {TermId} permanently failed after {Attempts} attempts",
            job.TermId, nextAttempt);
    }

    private static bool IsStoredAudioKey(string value)
        => !string.IsNullOrWhiteSpace(value)
           && !value.StartsWith("/", StringComparison.Ordinal)
           && !Uri.TryCreate(value, UriKind.Absolute, out _);

    private async Task ResumeRunningRecallBatchesAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var batches = await db.AudioRegenerationBatches
            .AsNoTracking()
            .Where(batch => batch.AudioType == "recalls" && batch.Status == "running")
            .OrderBy(batch => batch.StartedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var batch in batches)
        {
            var terms = await BuildRecallResumeQuery(db, batch)
                .OrderBy(term => term.Id)
                .Take(10_000)
                .Select(term => new { term.Id, term.Term })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var enqueued = 0;
            foreach (var term in terms)
            {
                if (string.IsNullOrWhiteSpace(term.Term)) continue;
                await queue.EnqueueAsync(new VocabularyAudioJob(
                    TermId: term.Id,
                    Text: term.Term,
                    Voice: batch.VoiceId,
                    Locale: "en-GB",
                    BatchId: batch.Id,
                    ModelVariant: batch.ModelVariant,
                    Instructions: batch.Instructions,
                    ProviderName: "elevenlabs"), ct).ConfigureAwait(false);
                enqueued++;
            }

            if (enqueued > 0)
            {
                logger.LogInformation("Re-enqueued {Count} unfinished ElevenLabs recall audio jobs for batch {BatchId}", enqueued, batch.Id);
            }
        }
    }

    private static IQueryable<VocabularyTerm> BuildRecallResumeQuery(LearnerDbContext db, AudioRegenerationBatch batch)
    {
        var query = db.VocabularyTerms.AsNoTracking()
            .Where(term => term.Status != "archived"
                && term.RecallSetCodesJson != null
                && term.RecallSetCodesJson != ""
                && term.RecallSetCodesJson != "[]");

        var scope = (batch.Scope ?? "missing").Trim().ToLowerInvariant();
        if (scope == "different-voice")
        {
            query = query.Where(term =>
                term.AudioProvider != "elevenlabs"
                || term.AudioModelVariant != batch.ModelVariant
                || term.AudioVoice != batch.VoiceId
                || term.AudioVoice == null);
        }
        else if (scope != "all")
        {
            query = query.Where(term =>
                term.AudioMediaAssetId == null
                || term.AudioUrl == null
                || term.AudioUrl == ""
                || !db.MediaAssets.Any(asset => asset.Id == term.AudioMediaAssetId && asset.Status == MediaAssetStatus.Ready));
        }

        return query.Where(term =>
            term.AudioBatchId != batch.Id
            || term.AudioMediaAssetId == null
            || term.AudioUrl == null
            || term.AudioUrl == "");
    }

    private static bool IsRecallTerm(VocabularyTerm term)
        => !string.IsNullOrWhiteSpace(term.RecallSetCodesJson)
           && term.RecallSetCodesJson != "[]";

    private static bool CanReuseExistingAudio(VocabularyTerm term, VocabularyAudioJob job, bool isRecallAudioJob)
    {
        if (!isRecallAudioJob) return true;

        if (!string.Equals(term.AudioProvider, "elevenlabs", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(job.ModelVariant)
            && !string.Equals(term.AudioModelVariant, job.ModelVariant, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(job.Voice)
            && !string.Equals(term.AudioVoice, job.Voice, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static string? FirstRecallSetCode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return null;
        try
        {
            var codes = JsonSerializer.Deserialize<string[]>(json);
            return codes?.FirstOrDefault(code => !string.IsNullOrWhiteSpace(code));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<bool> IsBatchCancelledAsync(LearnerDbContext db, string batchId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(batchId)) return false;
        return await db.AudioRegenerationBatches
            .AnyAsync(b => b.Id == batchId && b.Status == "cancelled", ct)
            .ConfigureAwait(false);
    }

    private static async Task<bool> IsRecallBatchAsync(LearnerDbContext db, string batchId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(batchId)) return false;
        return await db.AudioRegenerationBatches
            .AnyAsync(b => b.Id == batchId && b.AudioType == "recalls", ct)
            .ConfigureAwait(false);
    }

    private static async Task MarkBatchFailureAsync(
        LearnerDbContext db, VocabularyAudioJob job, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.BatchId)) return;

        var batch = await db.AudioRegenerationBatches
            .FirstOrDefaultAsync(b => b.Id == job.BatchId, ct)
            .ConfigureAwait(false);
        if (batch is null || batch.Status != "running") return;

        batch.FailedItems += 1;
        if (batch.CompletedItems + batch.FailedItems >= batch.TotalItems)
        {
            batch.Status = "completed";
            batch.CompletedAt = DateTime.UtcNow;
        }
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = "system",
            ActorName = "vocab-audio-worker",
            Action = "VocabAudioSkipped",
            ResourceType = "VocabularyTerm",
            ResourceId = job.TermId,
            Details = JsonSerializer.Serialize(new { batchId = job.BatchId, reason }),
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static string Slug(string s)
    {
        var sb = new StringBuilder(Math.Min(s.Length, 48));
        foreach (var ch in s.Trim().ToLowerInvariant())
        {
            if (sb.Length >= 48) break;
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch is ' ' or '-' or '_') sb.Append('-');
        }
        var result = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(result) ? "term" : result;
    }
}
