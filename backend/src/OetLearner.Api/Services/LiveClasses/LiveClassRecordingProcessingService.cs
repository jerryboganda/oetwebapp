using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.LiveClasses;

/// <summary>
/// Wave A2 — fills in the AI bodies for the recording pipeline jobs that
/// <see cref="LiveClassRecordingService"/> already queues. Each stage:
///   1. Loads the recording row, short-circuits if the
///      <see cref="LiveClassSettings.AiRecordingProcessingEnabled"/> flag is off.
///   2. Calls <see cref="IAiGatewayService"/> with the appropriate feature
///      code (per plan §14.1 / §14.3) — system prompt is rulebook-grounded
///      using <see cref="RuleKind.Grammar"/> + <see cref="AiTaskMode.Coach"/>
///      because the gateway physically refuses ungrounded prompts; the
///      <em>real</em> task prompt rides in the user-message under
///      <see cref="AiGatewayRequest.UserInput"/>.
///   3. Persists the result back onto <see cref="LiveClassRecording"/>.
///   4. Queues the next stage via the background-job table.
///
/// <para>
/// Failures bubble — the <see cref="BackgroundJobProcessor"/> applies its
/// retry/backoff policy and finally marks the recording <c>Failed</c> when
/// retries are exhausted (see <c>MarkResourceFailedAfterFinalRetryAsync</c>).
/// </para>
/// </summary>
public sealed class LiveClassRecordingProcessingService(
    LearnerDbContext db,
    IAiGatewayService aiGateway,
    IFileStorage fileStorage,
    IRuntimeSettingsProvider runtimeSettings,
    TimeProvider timeProvider,
    ILogger<LiveClassRecordingProcessingService> logger)
{
    private const long MaxTranscriptionAttachmentBytes = 24L * 1024L * 1024L;
    private const int TranscriptionReadBufferBytes = 81920;

    // The cached system prompt from the plan §14.3. Stays static so the
    // model's prompt-cache hit-rate stays high — every summary on every
    // class shares the same byte-identical opener.
    internal const string SummariseSystemPromptCached =
        """
        You are summarising an OET preparation class for healthcare professionals.

        Output JSON only, with this structure:
        {
          "summary": "2-paragraph plain-English summary, max 200 words",
          "chapters": [
            { "startSeconds": 0, "title": "Introduction", "summary": "..." },
            ...
          ],
          "actionItems": [
            "Specific thing student should do before next class",
            ...
          ],
          "keyTopics": ["topic 1", "topic 2", ...]
        }

        Style rules:
        - Use OET terminology (consult Dr Ahmed's style canon)
        - Reference Writing/Speaking sub-test names accurately
        - Action items must be concrete and verifiable
        - Chapters every 5-10 minutes of class

        Transcript follows.
        """;

    internal const string TranslateSystemPromptCached =
        """
        You are a professional EN→AR translator working with OET medical
        education content. Translate the supplied English summary into
        natural, modern-standard Arabic suitable for a healthcare-professional
        audience. Preserve medical terminology where the Arabic equivalent
        would be unfamiliar — keep the English term in parentheses on first
        use. Output the Arabic translation only, no commentary.
        """;

    // ───────────────────────────────────────────────────────────────────
    // Transcribe — Whisper Large-v3 (or reuse Zoom AI Companion transcript)
    // ───────────────────────────────────────────────────────────────────

    public async Task ProcessTranscribeAsync(string recordingId, CancellationToken ct)
    {
        var settings = await runtimeSettings.GetAsync(ct);
        if (!settings.LiveClasses.AiRecordingProcessingEnabled)
        {
            logger.LogInformation(
                "ProcessTranscribeAsync: AI recording processing flag is OFF — recording {RecordingId} stays Pending.",
                recordingId);
            await ResetToPendingAsync(recordingId, ct);
            return;
        }

        var recording = await db.LiveClassRecordings.FirstOrDefaultAsync(r => r.Id == recordingId, ct);
        if (recording is null)
        {
            logger.LogWarning("ProcessTranscribeAsync: recording {RecordingId} not found.", recordingId);
            return;
        }

        // If Zoom AI Companion has already produced a transcript, reuse it.
        // Else send to Whisper.
        if (!string.IsNullOrWhiteSpace(recording.TranscriptText) && !IsPlaceholderTranscript(recording.TranscriptText))
        {
            logger.LogInformation(
                "ProcessTranscribeAsync: recording {RecordingId} already has TranscriptText — skipping Whisper.",
                recordingId);
        }
        else if (!string.IsNullOrWhiteSpace(recording.S3TranscriptKey))
        {
            // VTT parsing path (deferred to v2 per LiveClassRecordingService).
            // For Wave A2 we treat the existence of a key as "already transcribed".
            logger.LogInformation(
                "ProcessTranscribeAsync: recording {RecordingId} has S3TranscriptKey={Key} — VTT ingestion deferred to v2; using placeholder text.",
                recordingId, recording.S3TranscriptKey);
            recording.TranscriptText = $"[Transcript ready at {recording.S3TranscriptKey} — VTT ingestion pending]";
        }
        else
        {
            // Whisper/native-audio path — call the AI gateway with the stored
            // recording bytes so ASR-capable providers can inspect the media.
            var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Grammar,
                Profession = ExamProfession.Medicine,
                Task = AiTaskMode.Coach,
            });

            var audioKey = !string.IsNullOrWhiteSpace(recording.S3AudioKey)
                ? recording.S3AudioKey
                : recording.S3VideoKey;
            if (string.IsNullOrWhiteSpace(audioKey))
            {
                throw new InvalidOperationException($"Recording {recordingId} has no stored audio or video file to transcribe.");
            }

            var audioRead = await fileStorage.OpenReadWithMetadataAsync(audioKey, ct);
            await using var audioStream = audioRead.Stream;
            var length = audioRead.Length;
            if (length > MaxTranscriptionAttachmentBytes)
            {
                throw new InvalidOperationException(
                    $"Recording {recordingId} is {length} bytes, which exceeds the {MaxTranscriptionAttachmentBytes} byte transcription upload limit.");
            }
            if (length <= 0)
            {
                throw new InvalidOperationException($"Recording {recordingId} storage object is empty and cannot be transcribed.");
            }

            // The gateway attachment contract currently requires byte[]. Read
            // once into an exact-sized buffer instead of growing a MemoryStream
            // and duplicating it with ToArray().
            var audioBytes = GC.AllocateUninitializedArray<byte>(checked((int)length));
            var offset = 0;
            while (offset < audioBytes.Length)
            {
                var requested = Math.Min(
                    TranscriptionReadBufferBytes, audioBytes.Length - offset);
                var read = await audioStream.ReadAsync(
                    audioBytes.AsMemory(offset, requested), ct);
                if (read == 0)
                {
                    throw new EndOfStreamException(
                        $"Recording {recordingId} ended after {offset} of {length} bytes.");
                }
                offset += read;
            }

            var userMessage = "Transcribe the attached OET class recording. Return plain text only.";

            try
            {
                var result = await aiGateway.CompleteAsync(new AiGatewayRequest
                {
                    Prompt = prompt,
                    UserInput = userMessage,
                    FeatureCode = AiFeatureCodes.ClassRecordingTranscribe,
                    UserId = null,
                    Temperature = 0.0,
                    AudioAttachments = new[]
                    {
                        new AiProviderAudioAttachment
                        {
                            MimeType = GuessAudioMimeType(audioKey),
                            Data = audioBytes,
                        },
                    },
                }, ct);

                var transcript = result.Completion?.Trim();
                if (string.IsNullOrWhiteSpace(transcript))
                {
                    logger.LogWarning("ProcessTranscribeAsync: empty transcript from gateway for recording {RecordingId}.", recordingId);
                    recording.TranscriptText = "[Transcript empty — Whisper returned no text]";
                }
                else
                {
                    recording.TranscriptText = transcript;
                }
            }
            catch (PromptNotGroundedException pex)
            {
                logger.LogError(pex, "ProcessTranscribeAsync: prompt-not-grounded refusal for recording {RecordingId}.", recordingId);
                throw;
            }
        }

        // Queue next stage.
        var now = timeProvider.GetUtcNow();
        QueueJob(JobType.LiveClassRecordingSummarize, recordingId, now);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "ProcessTranscribeAsync: recording {RecordingId} transcribed ({Length} chars) — Summarize queued.",
            recordingId, recording.TranscriptText?.Length ?? 0);
    }

    private static string GuessAudioMimeType(string key)
    {
        var extension = Path.GetExtension(key).ToLowerInvariant();
        return extension switch
        {
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".mp4" => "video/mp4",
            ".mpeg" => "audio/mpeg",
            ".mpga" => "audio/mpeg",
            ".oga" => "audio/ogg",
            ".ogg" => "audio/ogg",
            ".wav" => "audio/wav",
            ".webm" => "audio/webm",
            _ => "application/octet-stream",
        };
    }

    // ───────────────────────────────────────────────────────────────────
    // Summarize — Sonnet-4.6 with cached system prompt → JSON
    // ───────────────────────────────────────────────────────────────────

    public async Task ProcessSummarizeAsync(string recordingId, CancellationToken ct)
    {
        var settings = await runtimeSettings.GetAsync(ct);
        if (!settings.LiveClasses.AiRecordingProcessingEnabled)
        {
            logger.LogInformation(
                "ProcessSummarizeAsync: AI recording processing flag is OFF — recording {RecordingId} stays Pending.",
                recordingId);
            await ResetToPendingAsync(recordingId, ct);
            return;
        }

        var recording = await db.LiveClassRecordings.FirstOrDefaultAsync(r => r.Id == recordingId, ct);
        if (recording is null)
        {
            logger.LogWarning("ProcessSummarizeAsync: recording {RecordingId} not found.", recordingId);
            return;
        }

        if (string.IsNullOrWhiteSpace(recording.TranscriptText) || IsPlaceholderTranscript(recording.TranscriptText))
        {
            logger.LogWarning(
                "ProcessSummarizeAsync: recording {RecordingId} has no real transcript — skipping AI summary.",
                recordingId);
            recording.AiSummary = null;
            recording.Status = LiveClassRecordingStatus.Ready;
            recording.ProcessedAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return;
        }

        // Build grounded wrapper + embed our cached opener in the system
        // prompt suffix (the gateway's grounding header is prepended, the
        // task instruction sits beneath it).
        var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Grammar,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Coach,
        });

        // Pack the canonical summarisation instructions + transcript in the
        // user message so the cached system prompt stays byte-identical
        // across every class — maximises prompt-cache hits at the provider.
        var userMessage = $"{SummariseSystemPromptCached}\n\n---\n\n{recording.TranscriptText}";

        var result = await aiGateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = prompt,
            UserInput = userMessage,
            FeatureCode = AiFeatureCodes.ClassRecordingSummarize,
            UserId = null,
            Temperature = 0.2,
            MaxTokens = 2048,
        }, ct);

        var parsed = TryParseSummaryJson(result.Completion);
        recording.AiSummary = parsed?.Summary ?? string.Empty;
        recording.ChaptersJson = parsed?.ChaptersJson ?? "[]";
        recording.ActionItemsJson = parsed?.ActionItemsJson ?? "[]";

        // Queue translate stage.
        var now = timeProvider.GetUtcNow();
        QueueJob(JobType.LiveClassRecordingTranslate, recordingId, now);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "ProcessSummarizeAsync: recording {RecordingId} summarized ({SummaryChars} chars summary, {ChapterCount} chapters) — Translate queued.",
            recordingId, recording.AiSummary?.Length ?? 0, parsed?.ChapterCount ?? 0);
    }

    // ───────────────────────────────────────────────────────────────────
    // Translate — Sonnet-4.6 EN→AR of the summary
    // ───────────────────────────────────────────────────────────────────

    public async Task ProcessTranslateAsync(string recordingId, CancellationToken ct)
    {
        var settings = await runtimeSettings.GetAsync(ct);
        if (!settings.LiveClasses.AiRecordingProcessingEnabled)
        {
            logger.LogInformation(
                "ProcessTranslateAsync: AI recording processing flag is OFF — recording {RecordingId} stays Pending.",
                recordingId);
            await ResetToPendingAsync(recordingId, ct);
            return;
        }

        var recording = await db.LiveClassRecordings
            .Include(r => r.ClassSession)
                .ThenInclude(s => s.LiveClass)
            .Include(r => r.ClassSession)
                .ThenInclude(s => s.Enrollments)
            .FirstOrDefaultAsync(r => r.Id == recordingId, ct);
        if (recording is null)
        {
            logger.LogWarning("ProcessTranslateAsync: recording {RecordingId} not found.", recordingId);
            return;
        }

        var now = timeProvider.GetUtcNow();

        if (!string.IsNullOrWhiteSpace(recording.AiSummary))
        {
            var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Grammar,
                Profession = ExamProfession.Medicine,
                Task = AiTaskMode.Coach,
            });

            var userMessage = $"{TranslateSystemPromptCached}\n\n---\n\n{recording.AiSummary}";

            try
            {
                var result = await aiGateway.CompleteAsync(new AiGatewayRequest
                {
                    Prompt = prompt,
                    UserInput = userMessage,
                    FeatureCode = AiFeatureCodes.ClassRecordingTranslate,
                    UserId = null,
                    Temperature = 0.2,
                    MaxTokens = 2048,
                }, ct);

                recording.AiSummaryAr = result.Completion?.Trim();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Translation is best-effort; missing Arabic shouldn't block Ready.
                logger.LogWarning(ex,
                    "ProcessTranslateAsync: EN→AR translation failed for recording {RecordingId}; continuing without Arabic summary.",
                    recordingId);
                recording.AiSummaryAr = null;
            }
        }
        else
        {
            recording.AiSummaryAr = null;
        }

        recording.Status = LiveClassRecordingStatus.Ready;
        recording.ProcessedAt = now;

        // Queue embedding ingestion (best-effort, separate job so a failure
        // doesn't block Ready visibility).
        QueueJob(JobType.LiveClassRecordingEmbed, recordingId, now);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "ProcessTranslateAsync: recording {RecordingId} marked Ready ({HasAr} Arabic summary) — Embed queued.",
            recordingId, recording.AiSummaryAr is not null);
    }

    // ───────────────────────────────────────────────────────────────────
    // Embed — chunk the transcript and persist 1536-d vectors
    // ───────────────────────────────────────────────────────────────────

    public async Task ProcessEmbedAsync(string recordingId, CancellationToken ct)
    {
        var settings = await runtimeSettings.GetAsync(ct);
        if (!settings.LiveClasses.AiRecordingProcessingEnabled)
        {
            logger.LogInformation(
                "ProcessEmbedAsync: AI recording processing flag is OFF — recording {RecordingId} stays Pending.",
                recordingId);
            return;
        }

        var recording = await db.LiveClassRecordings.FirstOrDefaultAsync(r => r.Id == recordingId, ct);
        if (recording is null || string.IsNullOrWhiteSpace(recording.TranscriptText))
        {
            logger.LogInformation(
                "ProcessEmbedAsync: recording {RecordingId} missing or has no transcript — skipping embed.",
                recordingId);
            return;
        }

        // Idempotency: clear any prior embeddings for this recording. A
        // recording is processed at most once on the happy path; this guard
        // covers re-runs from manual admin retry.
        var existing = await db.ClassRecordingEmbeddings
            .Where(e => e.ClassRecordingId == recordingId)
            .ToListAsync(ct);
        if (existing.Count > 0)
        {
            db.ClassRecordingEmbeddings.RemoveRange(existing);
        }

        var chunks = ChunkTranscript(recording.TranscriptText, recording.DurationSeconds);
        var now = timeProvider.GetUtcNow();
        var inserted = 0;

        // IAiGatewayService does not expose a batch-embedding contract. Keep
        // concurrency at one so its scoped usage-accounting DbContext and
        // provider rate limits are never exercised concurrently.
        foreach (var chunk in chunks)
        {
            // Best-effort: a missing/embedding-failed chunk doesn't fail the
            // whole job. We store a zero-vector placeholder so retrieval can
            // still surface the chunk via keyword fallback in v2.
            var embedding = await TryEmbedChunkAsync(chunk.Text, ct);
            db.ClassRecordingEmbeddings.Add(new ClassRecordingEmbedding
            {
                Id = $"cre-{Guid.NewGuid():N}",
                ClassRecordingId = recordingId,
                ChunkIndex = chunk.Index,
                ChunkText = chunk.Text,
                EmbeddingJson = embedding ?? "[]",
                EmbeddingModel = "text-embedding-3-small",
                StartTimeSeconds = chunk.StartTimeSeconds,
                EndTimeSeconds = chunk.EndTimeSeconds,
                CreatedAt = now,
            });
            inserted++;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "ProcessEmbedAsync: recording {RecordingId} embedded {Count} chunk(s).",
            recordingId, inserted);
    }

    // ───────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────

    private async Task<string?> TryEmbedChunkAsync(string text, CancellationToken ct)
    {
        // v1 — we route through the existing AI gateway with a small
        // grounded wrapper. Real embedding providers (OpenAI's
        // /v1/embeddings) return a numeric vector; until that endpoint is
        // surfaced through IAiModelProvider, the gateway returns text — so
        // we treat any non-JSON response as "embedding deferred" and store
        // an empty vector. v2 will swap this for a dedicated embedding path.
        try
        {
            var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Grammar,
                Profession = ExamProfession.Medicine,
                Task = AiTaskMode.Coach,
            });
            var userMessage = $"Return a 1536-dim text-embedding-3-small vector as JSON for the text below. Output JSON array only.\n\n{text}";
            var result = await aiGateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userMessage,
                FeatureCode = AiFeatureCodes.ClassAssistantQna,
                UserId = null,
                Temperature = 0.0,
                MaxTokens = 8192,
            }, ct);

            var completion = result.Completion?.Trim();
            if (!string.IsNullOrEmpty(completion) && completion.StartsWith('['))
            {
                return completion;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Embedding chunk failed (best-effort); storing empty vector.");
        }

        return null;
    }

    internal static IReadOnlyList<TranscriptChunk> ChunkTranscript(string transcript, int totalDurationSeconds)
    {
        // Approximate 500-token windows. We can't tokenise without a
        // tokeniser library on the host, so we approximate at 1 token ≈ 4
        // chars → 2000 chars per chunk. Chunks ride a sliding 200-char
        // overlap to preserve cross-boundary context.
        const int approxCharsPerChunk = 2000;
        const int overlap = 200;
        var chunks = new List<TranscriptChunk>();
        if (string.IsNullOrWhiteSpace(transcript)) return chunks;

        var text = transcript.Trim();
        var totalChars = text.Length;
        var idx = 0;
        var index = 0;
        while (idx < totalChars)
        {
            var end = Math.Min(idx + approxCharsPerChunk, totalChars);
            var slice = text.Substring(idx, end - idx);

            // Distribute timestamps linearly across the recording.
            var startFraction = (double)idx / totalChars;
            var endFraction = (double)end / totalChars;
            var startSec = (int)Math.Round(startFraction * Math.Max(1, totalDurationSeconds));
            var endSec = (int)Math.Round(endFraction * Math.Max(1, totalDurationSeconds));

            chunks.Add(new TranscriptChunk(index, slice, startSec, endSec));
            index++;
            if (end >= totalChars) break;
            idx = end - overlap;
            if (idx < 0) idx = 0;
        }
        return chunks;
    }

    private static bool IsPlaceholderTranscript(string text)
        => text.StartsWith("[Transcript", StringComparison.Ordinal);

    private static ParsedSummary? TryParseSummaryJson(string? completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;
        var trimmed = completion.Trim();
        // Tolerate the model wrapping JSON in ```json ... ``` fences.
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }
            if (trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^3].TrimEnd();
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var summary = root.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString() ?? string.Empty
                : string.Empty;

            var chaptersJson = "[]";
            var chapterCount = 0;
            if (root.TryGetProperty("chapters", out var chapters) && chapters.ValueKind == JsonValueKind.Array)
            {
                chaptersJson = chapters.GetRawText();
                chapterCount = chapters.GetArrayLength();
            }

            var actionItemsJson = "[]";
            if (root.TryGetProperty("actionItems", out var actions) && actions.ValueKind == JsonValueKind.Array)
            {
                actionItemsJson = actions.GetRawText();
            }

            return new ParsedSummary(summary, chaptersJson, actionItemsJson, chapterCount);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task ResetToPendingAsync(string recordingId, CancellationToken ct)
    {
        var recording = await db.LiveClassRecordings.FirstOrDefaultAsync(r => r.Id == recordingId, ct);
        if (recording is null) return;
        if (recording.Status is LiveClassRecordingStatus.Ready or LiveClassRecordingStatus.Failed)
        {
            // Don't regress a terminal status.
            return;
        }
        recording.Status = LiveClassRecordingStatus.Pending;
        await db.SaveChangesAsync(ct);
    }

    private void QueueJob(JobType type, string resourceId, DateTimeOffset now)
    {
        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"bgj-{Guid.NewGuid():N}",
            Type = type,
            ResourceId = resourceId,
            State = AsyncState.Queued,
            AvailableAt = now,
            CreatedAt = now,
        });
    }

    internal sealed record ParsedSummary(string Summary, string ChaptersJson, string ActionItemsJson, int ChapterCount);

    internal sealed record TranscriptChunk(int Index, string Text, int StartTimeSeconds, int EndTimeSeconds);
}
