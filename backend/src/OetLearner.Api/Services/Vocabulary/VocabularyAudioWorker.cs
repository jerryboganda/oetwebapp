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
        if (string.IsNullOrWhiteSpace(job.Text))
        {
            logger.LogInformation("Vocab audio job {TermId} skipped — empty text", job.TermId);
            return;
        }

        if (Encoding.UTF8.GetByteCount(job.Text) > MaxTextBytes)
        {
            logger.LogWarning("Vocab audio job {TermId} skipped — text exceeds {Max} bytes", job.TermId, MaxTextBytes);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<LearnerDbContext>();
        var selector = sp.GetRequiredService<IConversationTtsProviderSelector>();
        var storage = sp.GetRequiredService<IFileStorage>();

        if (await selector.IsTtsDisabledAsync(ct).ConfigureAwait(false))
        {
            logger.LogInformation("Vocab audio job {TermId} dropped — TTS provider is off", job.TermId);
            return;
        }

        var provider = await selector.TrySelectAsync(ct).ConfigureAwait(false);
        if (provider is null)
        {
            logger.LogInformation("Vocab audio job {TermId} dropped — no TTS provider available", job.TermId);
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

        ConversationTtsResult result;
        try
        {
            result = await provider.SynthesizeAsync(
                new ConversationTtsRequest(job.Text, job.Voice ?? string.Empty, job.Locale ?? "en-GB"),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await HandleFailureAsync(db, job, ex, ct).ConfigureAwait(false);
            return;
        }
        // Pace successful calls to stay under DigitalOcean Inference rate limits.
        try { await Task.Delay(InterJobDelayMs, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }

        var audio = result.Audio ?? Array.Empty<byte>();
        var sha = Convert.ToHexString(SHA256.HashData(audio)).ToLowerInvariant();
        var mime = string.IsNullOrWhiteSpace(result.MimeType) ? "audio/mpeg" : result.MimeType;
        var ext = mime.Contains("wav", StringComparison.OrdinalIgnoreCase) ? "wav"
                  : mime.Contains("ogg", StringComparison.OrdinalIgnoreCase) ? "ogg"
                  : "mp3";
        var key = ContentAddressed.PublishedKey("vocabulary/audio", sha, ext);

        if (!storage.Exists(key))
        {
            await using var ms = new MemoryStream(audio);
            await storage.WriteAsync(key, ms, ct).ConfigureAwait(false);
        }

        var term = await db.VocabularyTerms.FirstOrDefaultAsync(t => t.Id == job.TermId, ct).ConfigureAwait(false);
        if (term is null)
        {
            logger.LogWarning("Vocab audio job {TermId} dropped — term not found", job.TermId);
            return;
        }

        var asset = await db.MediaAssets.FirstOrDefaultAsync(m => m.Sha256 == sha, ct).ConfigureAwait(false);
        if (asset is null)
        {
            asset = new MediaAsset
            {
                Id = "MA-" + Guid.NewGuid().ToString("N")[..12],
                OriginalFilename = $"{Slug(job.Text)}.{ext}",
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
        var url = storage.ResolveReadUrl(key, TimeSpan.FromDays(7))?.ToString();
        if (!string.IsNullOrWhiteSpace(url))
        {
            term.AudioUrl = url.Length > MaxAudioUrlChars ? url[..MaxAudioUrlChars] : url;
        }
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

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        logger.LogInformation(
            "Vocab audio job {TermId} stored ({Bytes} bytes, sha {Sha}) via {Provider}",
            job.TermId, audio.LongLength, sha[..8], provider.Name);
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
        logger.LogError(ex, "Vocab audio job {TermId} permanently failed after {Attempts} attempts",
            job.TermId, nextAttempt);
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
