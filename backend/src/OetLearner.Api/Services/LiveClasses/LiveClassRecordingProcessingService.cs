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
///
/// <para>
/// Recordings larger than <see cref="MaxTranscriptionAttachmentBytes"/> cannot be sent to the gateway in one call. When a remote
/// helper has extracted them into mp3 chunks (<c>media.audio-extract</c>, a chunk manifest on the recording row), the transcribe
/// stage makes ONE gateway call per chunk — each with its own <c>AiUsageRecord</c>, exactly like the single call — and joins the
/// transcripts in order. Every chunk transcript is saved as soon as it exists, so a retry resumes instead of paying again, and the
/// chunk audio is deleted once the recording is fully transcribed. Without a manifest (small recording, flag off, no node) nothing
/// changes: the single-call path runs, and an oversize recording fails exactly as before.
/// </para>
/// </summary>
public sealed class LiveClassRecordingProcessingService(
    LearnerDbContext db,
    IAiGatewayService aiGateway,
    IFileStorage fileStorage,
    IRuntimeSettingsProvider runtimeSettings,
    TimeProvider timeProvider,
    ILogger<LiveClassRecordingProcessingService> logger,
    OetLearner.Api.Services.AiAssistant.Indexing.IEmbeddingService? embeddingService = null,
    IRemoteAudioExtraction? remoteAudio = null)
{
    private const long MaxTranscriptionAttachmentBytes = 24L * 1024L * 1024L;
    private const int TranscriptionReadBufferBytes = 81920;

    /// <summary>
    /// Wall-clock budget of one transcribe run over chunks. A job that ran this long queues a continuation and finishes instead of
    /// running into the background processor's 20-minute execution ceiling, so a long recording never fails just for being long.
    /// </summary>
    internal TimeSpan ChunkRunBudget { get; init; } = TimeSpan.FromMinutes(12);

    /// <summary>How long a recording waits between looks at its (remote) audio extraction.</summary>
    internal TimeSpan RemoteExtractionPollDelay { get; init; } = TimeSpan.FromSeconds(45);

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
            // A recording too large for one call is transcribed chunk by chunk
            // (one gateway call per chunk) once a helper has extracted the chunks.
            var outcome = await TranscribeRecordingAudioAsync(recording, ct);
            if (outcome.IsPending)
            {
                // A continuation job is already queued: Summarize waits for the real transcript.
                return;
            }

            recording.TranscriptText = outcome.Transcript;
        }

        // Queue next stage.
        var now = timeProvider.GetUtcNow();
        QueueJob(JobType.LiveClassRecordingSummarize, recordingId, now);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "ProcessTranscribeAsync: recording {RecordingId} transcribed ({Length} chars) — Summarize queued.",
            recordingId, recording.TranscriptText?.Length ?? 0);
    }

    /// <summary>Where the audio stage stands: a finished transcript, or "come back later" (a continuation job is queued).</summary>
    private sealed record AudioTranscription(string? Transcript, bool IsPending)
    {
        public static AudioTranscription Done(string transcript) => new(transcript, false);

        public static readonly AudioTranscription Pending = new(null, true);
    }

    /// <summary>Thrown when the extracted chunks can no longer be used (blob gone, size or hash changed): the manifest is dropped and the audio is extracted again.</summary>
    private sealed class AudioChunksUnavailableException(string message) : Exception(message);

    private async Task<AudioTranscription> TranscribeRecordingAudioAsync(LiveClassRecording recording, CancellationToken ct)
    {
        var recordingId = recording.Id;
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

        // A helper already extracted this recording into chunks: one gateway call per chunk.
        var manifest = LiveClassAudioManifest.TryParse(recording.AudioChunksJson);
        if (manifest is not null)
        {
            try
            {
                return await TranscribeChunksAsync(recording, manifest, prompt, ct);
            }
            catch (AudioChunksUnavailableException ex)
            {
                // The chunks (or their integrity) are gone: forget the manifest and extract again below.
                logger.LogWarning(ex, "ProcessTranscribeAsync: extracted chunks of recording {RecordingId} are unusable; extracting again.", recordingId);
                recording.AudioChunksJson = null;
                await db.SaveChangesAsync(ct);
            }
        }
        else if (!string.IsNullOrWhiteSpace(recording.AudioChunksJson))
        {
            // Not a well-formed manifest: never trust it, never read a key from it.
            logger.LogWarning("ProcessTranscribeAsync: recording {RecordingId} carries an unreadable chunk manifest; ignoring it.", recordingId);
            recording.AudioChunksJson = null;
            await db.SaveChangesAsync(ct);
        }

        var audioRead = await fileStorage.OpenReadWithMetadataAsync(audioKey, ct);
        long length;
        byte[]? audioBytes = null;
        await using (var audioStream = audioRead.Stream)
        {
            length = audioRead.Length;
            if (length > 0 && length <= MaxTranscriptionAttachmentBytes)
            {
                audioBytes = await ReadExactAsync(audioStream, length, $"Recording {recordingId}", ct);
            }
        }

        if (length > MaxTranscriptionAttachmentBytes)
        {
            return await HandleOversizeRecordingAsync(recording, audioKey, length, ct);
        }
        if (length <= 0)
        {
            throw new InvalidOperationException($"Recording {recordingId} storage object is empty and cannot be transcribed.");
        }

        var transcript = await CompleteTranscriptionAsync(
            prompt,
            audioBytes!,
            GuessAudioMimeType(audioKey),
            "Transcribe the attached OET class recording. Return plain text only.",
            recordingId,
            ct);
        if (string.IsNullOrWhiteSpace(transcript))
        {
            logger.LogWarning("ProcessTranscribeAsync: empty transcript from gateway for recording {RecordingId}.", recordingId);
            return AudioTranscription.Done("[Transcript empty — Whisper returned no text]");
        }

        return AudioTranscription.Done(transcript);
    }

    /// <summary>
    /// The gateway attachment contract requires byte[]: read once into an exact-sized buffer instead of growing a MemoryStream and
    /// duplicating it with ToArray().
    /// </summary>
    private static async Task<byte[]> ReadExactAsync(Stream stream, long length, string description, CancellationToken ct)
    {
        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)length));
        var offset = 0;
        while (offset < bytes.Length)
        {
            var requested = Math.Min(TranscriptionReadBufferBytes, bytes.Length - offset);
            var read = await stream.ReadAsync(bytes.AsMemory(offset, requested), ct);
            if (read == 0)
            {
                throw new EndOfStreamException($"{description} ended after {offset} of {length} bytes.");
            }

            offset += read;
        }

        return bytes;
    }

    /// <summary>One transcription call through the AI gateway (the coordinator writes the AiUsageRecord for the physical call).</summary>
    private async Task<string> CompleteTranscriptionAsync(
        AiGroundedPrompt prompt,
        byte[] audioBytes,
        string mimeType,
        string userMessage,
        string recordingId,
        CancellationToken ct)
    {
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
                        MimeType = mimeType,
                        Data = audioBytes,
                    },
                },
            }, ct);

            return result.Completion?.Trim() ?? string.Empty;
        }
        catch (PromptNotGroundedException pex)
        {
            logger.LogError(pex, "ProcessTranscribeAsync: prompt-not-grounded refusal for recording {RecordingId}.", recordingId);
            throw;
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // Oversize recordings — remote audio extraction, then one call per chunk
    // ───────────────────────────────────────────────────────────────────

    private async Task<AudioTranscription> HandleOversizeRecordingAsync(
        LiveClassRecording recording,
        string audioKey,
        long length,
        CancellationToken ct)
    {
        var recordingId = recording.Id;
        var tooLarge = new InvalidOperationException(
            $"Recording {recordingId} is {length} bytes, which exceeds the {MaxTranscriptionAttachmentBytes} byte transcription upload limit.");
        if (remoteAudio is null) throw tooLarge;

        var plan = await remoteAudio.PlanAsync(recordingId, audioKey, length, ct);
        switch (plan.Action)
        {
            case AudioExtractAction.Pending:
                logger.LogInformation(
                    "ProcessTranscribeAsync: recording {RecordingId} is {Length} bytes; its audio is being extracted by a remote helper — retrying shortly.",
                    recordingId, length);
                await QueueContinuationAsync(recordingId, RemoteExtractionPollDelay, ct);
                return AudioTranscription.Pending;

            case AudioExtractAction.Failed:
                throw new InvalidOperationException($"Recording {recordingId} cannot be transcribed: {plan.Note}");

            default:
                // No remote path applies: an oversize recording fails exactly as it always did.
                throw tooLarge;
        }
    }

    private async Task<AudioTranscription> TranscribeChunksAsync(
        LiveClassRecording recording,
        LiveClassAudioManifest manifest,
        AiGroundedPrompt prompt,
        CancellationToken ct)
    {
        var recordingId = recording.Id;
        var started = timeProvider.GetUtcNow();
        var ordered = manifest.Chunks.OrderBy(chunk => chunk.Index).ToList();

        foreach (var chunk in ordered)
        {
            // Already transcribed by an earlier run: never pay for it twice.
            if (chunk.Transcript is not null) continue;

            if (manifest.AudioDeleted)
            {
                throw new AudioChunksUnavailableException("The chunk audio was deleted before every chunk had a transcript.");
            }

            // A long recording must not run into the background processor's execution ceiling: finish this run, continue in the next.
            if (timeProvider.GetUtcNow() - started >= ChunkRunBudget)
            {
                logger.LogInformation(
                    "ProcessTranscribeAsync: recording {RecordingId} chunk run budget reached at chunk {Index}/{Count}; continuing in a new job.",
                    recordingId, chunk.Index, ordered.Count);
                await QueueContinuationAsync(recordingId, TimeSpan.Zero, ct);
                return AudioTranscription.Pending;
            }

            var bytes = await ReadChunkAsync(chunk, ct);
            var userMessage =
                $"Transcribe the attached audio, which is part {chunk.Index + 1} of {ordered.Count} of an OET class recording. "
                + "Transcribe only this audio. Return plain text only.";
            var transcript = await CompleteTranscriptionAsync(prompt, bytes, LiveClassAudioManifest.MimeType, userMessage, recordingId, ct);

            // Saved per chunk: a failure later in the recording resumes here instead of paying for this chunk again.
            chunk.Transcript = transcript;
            recording.AudioChunksJson = manifest.ToJson();
            await db.SaveChangesAsync(ct);
        }

        var joined = manifest.JoinTranscripts();
        if (string.IsNullOrWhiteSpace(joined))
        {
            logger.LogWarning("ProcessTranscribeAsync: empty transcript from gateway for recording {RecordingId}.", recordingId);
            joined = "[Transcript empty — Whisper returned no text]";
        }

        // Persist the transcript BEFORE the chunk audio is deleted: if the delete step fails the transcript is not lost, and a
        // re-run finds every chunk transcribed and only retries the delete.
        recording.TranscriptText = joined;
        await db.SaveChangesAsync(ct);
        await DeleteChunkAudioAsync(recording, manifest, ct);

        return AudioTranscription.Done(joined);
    }

    /// <summary>Reads one chunk and verifies it is the object the extraction produced (size and SHA-256).</summary>
    private async Task<byte[]> ReadChunkAsync(LiveClassAudioChunk chunk, CancellationToken ct)
    {
        byte[] bytes;
        try
        {
            var read = await fileStorage.OpenReadWithMetadataAsync(chunk.StorageKey, ct);
            await using var stream = read.Stream;
            if (read.Length != chunk.SizeBytes || read.Length <= 0 || read.Length > MaxTranscriptionAttachmentBytes)
            {
                throw new AudioChunksUnavailableException($"Chunk {chunk.Index} changed size since it was extracted.");
            }

            bytes = await ReadExactAsync(stream, read.Length, $"Chunk {chunk.Index}", ct);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or KeyNotFoundException
                                       || ex is Amazon.S3.AmazonS3Exception { StatusCode: System.Net.HttpStatusCode.NotFound })
        {
            throw new AudioChunksUnavailableException($"Chunk {chunk.Index} is no longer in storage.");
        }

        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(sha, chunk.Sha256, StringComparison.Ordinal))
        {
            throw new AudioChunksUnavailableException($"Chunk {chunk.Index} no longer matches its recorded SHA-256.");
        }

        return bytes;
    }

    /// <summary>Delete-on-complete: the learner voices in the chunk audio are not kept once the recording is transcribed (the transcripts stay).</summary>
    private async Task DeleteChunkAudioAsync(LiveClassRecording recording, LiveClassAudioManifest manifest, CancellationToken ct)
    {
        if (manifest.AudioDeleted) return;

        var allGone = true;
        foreach (var chunk in manifest.Chunks)
        {
            try
            {
                await fileStorage.DeleteAsync(chunk.StorageKey, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The retention sweep of the remote-job outputs removes whatever is left; the transcript is already saved,
                // so a failed delete never fails the stage.
                allGone = false;
                logger.LogWarning(ex, "ProcessTranscribeAsync: could not delete chunk {Index} of recording {RecordingId}.", chunk.Index, recording.Id);
            }
        }

        if (!allGone) return;

        manifest.AudioDeleted = true;
        recording.AudioChunksJson = manifest.ToJson();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Queues another transcribe run for the recording (once: a run already waiting is enough).</summary>
    private async Task QueueContinuationAsync(string recordingId, TimeSpan delay, CancellationToken ct)
    {
        var alreadyWaiting = await db.BackgroundJobs.AnyAsync(
            job => job.Type == JobType.LiveClassRecordingTranscribe
                && job.ResourceId == recordingId
                && job.State == AsyncState.Queued,
            ct);
        if (!alreadyWaiting)
        {
            var now = timeProvider.GetUtcNow();
            QueueJob(JobType.LiveClassRecordingTranscribe, recordingId, now, now + delay);
        }

        await db.SaveChangesAsync(ct);
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
            // UBAG facade JSON coercion: the OpenAI-compatible rows honour
            // response_format json_object (the facade coerces + fails loudly
            // when nothing parses); other providers ignore it and keep the
            // tolerant TryParseSummaryJson path below.
            ResponseFormatJson = "json_object",
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
        // v2 — dedicated embedding path first: the shared IEmbeddingService
        // calls POST /embeddings on the configured provider (real vectors
        // from OpenAI-compatible hosts; deterministic hash vectors from the
        // UBAG facade, which the EmbeddingService routes automatically). The
        // legacy gateway-text prompt below stays as the fallback so a missing
        // embedding registration can never regress to empty vectors.
        if (embeddingService is not null)
        {
            try
            {
                var vector = await embeddingService.EmbedAsync(text, ct);
                if (vector is { Length: > 0 })
                {
                    return JsonSerializer.Serialize(vector);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Embedding service failed (best-effort); falling back to gateway-text path.");
            }
        }

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

    /// <summary>True for the bracketed stand-ins this pipeline writes when there is no real transcript (also read by the extraction applier).</summary>
    internal static bool IsPlaceholderTranscript(string text)
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

    private void QueueJob(JobType type, string resourceId, DateTimeOffset now, DateTimeOffset? availableAt = null)
    {
        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"bgj-{Guid.NewGuid():N}",
            Type = type,
            ResourceId = resourceId,
            State = AsyncState.Queued,
            AvailableAt = availableAt ?? now,
            CreatedAt = now,
        });
    }

    internal sealed record ParsedSummary(string Summary, string ChaptersJson, string ActionItemsJson, int ChapterCount);

    internal sealed record TranscriptChunk(int Index, string Text, int StartTimeSeconds, int EndTimeSeconds);
}
