using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.LiveClasses;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>
/// The <c>media.audio-extract</c> producer used by the Live Class transcription stage (OET-RWP/1 section 6.3). It is consulted ONLY for a
/// recording that is too large to transcribe in one call and has no usable chunk manifest; it never touches a recording the existing path
/// can handle. It enqueues one idempotent job per (recording, content) when a healthy node offers the kind, and reports where that job
/// stands. Transcription is never done here or on the helper: the helper only decodes and chunks, and every AI call stays on the API.
/// With the flag off, no pinned engine, no node whose policy could ever run the job, a size or type the kind does not accept, or any
/// doubt, the answer is <see cref="AudioExtractAction.Local"/> and the oversize recording behaves exactly as it always did.
///
/// <para>
/// "Nobody can take it" is judged by a node's CAPACITY CEILING (<see cref="RemotePlacement.HasNodeThatCouldRunAsync"/>), never by what is
/// free this second: a node that exists but is busy with another job (the kind weighs 2, so any other leased job on a default node blocks
/// it) only delays the extraction, and reading that as "no remote path" would fail the recording for good after the transcribe job's
/// three quick retries. A job that stays unclaimed for <c>RemoteJobs:FallbackHardAfterMinutes</c> across the reaper's re-queues ends the
/// wait with a clear failure instead of polling for ever. That failure is sticky for the transcribe job's own quick retries, and the
/// wait window restarts once nobody has asked for a while (an administrator's retry).
/// </para>
/// </summary>
public sealed class RemoteAudioExtractionProducer(
    LearnerDbContext db,
    IRemoteJobFlags flags,
    IRemoteJobQueue queue,
    RemotePlacement placement,
    IFileStorage storage,
    RemoteJobsSettings settings,
    RemoteLocalWaitTracker waits,
    TimeProvider timeProvider,
    ILogger<RemoteAudioExtractionProducer> logger) : IRemoteAudioExtraction
{
    public const string Enqueuer = "system:live-class-recording";

    /// <summary>A job that finished this recently with no manifest on the recording is a read race, not a vanished manifest.</summary>
    private static readonly TimeSpan JustFinished = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The transcribe stage asks again every 45 s (and its own failure retries come within half a minute), so a wait nobody asked about
    /// for this long ended somewhere else (another process finished it, an administrator retried the recording much later): its clock
    /// restarts, instead of failing the new wait on the old one.
    /// </summary>
    private static readonly TimeSpan WaitIdleReset = TimeSpan.FromMinutes(15);

    public async Task<AudioExtractPlan> PlanAsync(string recordingId, string storageKey, long sizeBytes, CancellationToken ct)
    {
        try
        {
            var plan = await PlanCoreAsync(recordingId, storageKey, sizeBytes, ct);

            // No remote path at all ends the wait. A failure (including the wait limit itself) deliberately does NOT clear it: the
            // transcribe job retries within seconds and must meet the same answer, not a fresh window.
            if (plan.Action == AudioExtractAction.Local) waits.Clear(WaitKey(recordingId));
            return plan;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The remote path may never make a recording worse: any failure means "no remote path" (today's behaviour).
            waits.Clear(WaitKey(recordingId));
            logger.LogWarning(ex, "Remote audio extraction planning failed for recording {RecordingId}; using the local path.", recordingId);
            return AudioExtractPlan.UseLocal;
        }
    }

    private async Task<AudioExtractPlan> PlanCoreAsync(string recordingId, string storageKey, long sizeBytes, CancellationToken ct)
    {
        var flagSnapshot = await flags.GetAsync(ct);
        if (!flagSnapshot.KindEnabled(RemoteJobKinds.MediaAudioExtract, RemoteJobPurpose.Apply)) return AudioExtractPlan.UseLocal;

        var options = settings.Current;
        var spec = RemoteJobKinds.Find(RemoteJobKinds.MediaAudioExtract);
        var engine = RemoteJobKinds.EngineVersion(RemoteJobKinds.MediaAudioExtract, options);
        if (spec is null || engine is null) return AudioExtractPlan.UseLocal;

        if (sizeBytes <= 0 || sizeBytes > spec.Limits.MaxInputBytes) return AudioExtractPlan.UseLocal;
        var contentType = AudioExtractSettings.ContentTypeFor(storageKey);
        if (contentType is null) return AudioExtractPlan.UseLocal;

        var job = await FindLatestJobAsync(recordingId, ct);
        if (job is not null && !MatchesInput(job, storageKey, sizeBytes))
        {
            // The recording was replaced since that job was made: it describes another file.
            job = null;
        }

        if (job is null)
        {
            return await EnqueueNewAsync(recordingId, storageKey, sizeBytes, contentType, engine, spec, ct);
        }

        switch (job.State)
        {
            case RemoteJobState.Queued:
                // Nobody has started it yet (a busy node, a node restarting, the retry backoff after a lost lease): keep waiting, but
                // not for ever.
                return GiveUpIfWaitedTooLong(recordingId, job.Id)
                    ?? new AudioExtractPlan(AudioExtractAction.Pending, "remote audio extraction queued", job.Id);

            case RemoteJobState.Leased:
                // A helper is on it: the wait for a node is over.
                waits.Clear(WaitKey(recordingId));
                return new AudioExtractPlan(AudioExtractAction.Pending, "remote audio extraction in progress", job.Id);

            case RemoteJobState.Succeeded:
                // The applier writes the manifest in the SAME transaction that settles the job, so a finished job with no manifest
                // means the manifest was dropped (its chunks vanished) or the caller read the row a moment too early.
                if (job.CompletedAt is { } completed && timeProvider.GetUtcNow() - completed < JustFinished)
                {
                    waits.Clear(WaitKey(recordingId));
                    return new AudioExtractPlan(AudioExtractAction.Pending, "remote audio extraction just finished", job.Id);
                }

                return await RequeueAsync(job, recordingId, storageKey, sizeBytes, contentType, engine, spec, ct);

            case RemoteJobState.FallbackLocal:
            case RemoteJobState.Cancelled:
                // Withdrawn (no node claimed it in time, the master flag was off, the recording changed): ask again while a node
                // could take it.
                return await RequeueAsync(job, recordingId, storageKey, sizeBytes, contentType, engine, spec, ct);

            case RemoteJobState.Failed:
                return new AudioExtractPlan(
                    AudioExtractAction.Failed,
                    $"remote audio extraction failed ({job.FailureCode}); an admin must requeue it",
                    job.Id);

            case RemoteJobState.Quarantined:
                return new AudioExtractPlan(
                    AudioExtractAction.Failed,
                    "remote audio extraction is quarantined; an admin must requeue it",
                    job.Id);

            default:
                return AudioExtractPlan.UseLocal;
        }
    }

    private static string WaitKey(string recordingId) => "audio-extract:" + recordingId;

    /// <summary>
    /// Null while the extraction may keep waiting for a node. Once it has waited <c>FallbackHardAfterMinutes</c> without a helper starting
    /// it, a failure with a clear reason. The clock is per process and best effort, like every wait of the remote path, and it restarts
    /// after <see cref="WaitIdleReset"/> without a question, so a retry of the recording much later gets a fresh window.
    /// </summary>
    private AudioExtractPlan? GiveUpIfWaitedTooLong(string recordingId, string? jobId)
    {
        var minutes = settings.Current.FallbackHardAfterMinutes;
        if (!waits.HardDeadlinePassed(WaitKey(recordingId), TimeSpan.FromMinutes(minutes), WaitIdleReset)) return null;

        logger.LogWarning("Remote audio extraction of recording {RecordingId} was not started by any helper within {Minutes} minutes.", recordingId, minutes);
        return new AudioExtractPlan(
            AudioExtractAction.Failed,
            $"no helper started the audio extraction within {minutes} minutes; retry the recording once a helper is online",
            jobId);
    }

    /// <summary>Resets a withdrawn or consumed job while a node could take it; otherwise there is no remote path.</summary>
    private async Task<AudioExtractPlan> RequeueAsync(
        RemoteJobRow job,
        string recordingId,
        string storageKey,
        long sizeBytes,
        string contentType,
        string engine,
        RemoteKindSpec spec,
        CancellationToken ct)
    {
        // A different engine or settings fingerprint is a different job (its key differs), never a reset of this one.
        if (!string.Equals(job.EngineVersion, engine, StringComparison.Ordinal)
            || !string.Equals(job.SettingsHash, AudioExtractSettings.Hash(), StringComparison.Ordinal))
        {
            return await EnqueueNewAsync(recordingId, storageKey, sizeBytes, contentType, engine, spec, ct);
        }

        if (!await placement.HasNodeThatCouldRunAsync(RemoteJobKinds.MediaAudioExtract, spec.Limits.Weight, ct))
        {
            return AudioExtractPlan.UseLocal;
        }

        if (GiveUpIfWaitedTooLong(recordingId, job.Id) is { } giveUp) return giveUp;

        var result = await queue.EnqueueAsync(BuildRequest(recordingId, storageKey, sizeBytes, contentType, job.InputSha256, engine, spec), force: true, ct);
        return new AudioExtractPlan(AudioExtractAction.Pending, "remote audio extraction queued", result.JobId);
    }

    private async Task<AudioExtractPlan> EnqueueNewAsync(
        string recordingId,
        string storageKey,
        long sizeBytes,
        string contentType,
        string engine,
        RemoteKindSpec spec,
        CancellationToken ct)
    {
        // Never create a job no node could ever take: there is no local extraction to wait for, so without such a node there is no
        // remote path. A node that exists but is busy right now is NOT that case: the job simply waits its turn.
        if (!await placement.HasNodeThatCouldRunAsync(RemoteJobKinds.MediaAudioExtract, spec.Limits.Weight, ct))
        {
            return AudioExtractPlan.UseLocal;
        }

        var sha = await HashAsync(storageKey, ct);
        if (sha is null) return AudioExtractPlan.UseLocal;

        if (GiveUpIfWaitedTooLong(recordingId, null) is { } giveUp) return giveUp;

        // force: a finished job with the very same key but a different stored location (the recording was re-stored) is reset with the
        // new manifest; a Queued or Leased one is returned unchanged either way.
        var result = await queue.EnqueueAsync(BuildRequest(recordingId, storageKey, sizeBytes, contentType, sha, engine, spec), force: true, ct);
        return new AudioExtractPlan(AudioExtractAction.Pending, "remote audio extraction queued", result.JobId);
    }

    private static RemoteEnqueueRequest BuildRequest(
        string recordingId,
        string storageKey,
        long sizeBytes,
        string contentType,
        string sha,
        string engine,
        RemoteKindSpec spec)
    {
        var inputsJson = JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, object>
            {
                ["name"] = AudioExtractSettings.InputName,
                ["sizeBytes"] = sizeBytes,
                ["sha256"] = sha,
                ["contentType"] = contentType,
                ["storageKey"] = storageKey,
            },
        });

        return new RemoteEnqueueRequest(
            RemoteJobKinds.MediaAudioExtract,
            spec.SchemaVersion,
            RemoteJobPurpose.Apply,
            AudioExtractSettings.ResourceType,
            recordingId,
            sha,
            engine,
            AudioExtractSettings.Hash(),
            AudioExtractSettings.ParamsJson(),
            inputsJson,
            spec.Limits,
            Enqueuer);
    }

    private static bool MatchesInput(RemoteJobRow job, string storageKey, long sizeBytes)
    {
        var input = RemoteInputOutputService.ReadManifest(job.InputsJson)
            .FirstOrDefault(entry => string.Equals(entry.Name, AudioExtractSettings.InputName, StringComparison.Ordinal));
        return input is not null
            && string.Equals(input.StorageKey, storageKey, StringComparison.Ordinal)
            && input.SizeBytes == sizeBytes;
    }

    /// <summary>The newest apply job for this recording, whatever its state.</summary>
    private Task<RemoteJobRow?> FindLatestJobAsync(string recordingId, CancellationToken ct)
        => RemoteDb.QueryFirstAsync(
            db,
            "SELECT " + RemoteJobRow.Columns("j") + " FROM \"RemoteJobs\" j"
            + " WHERE j.\"Kind\" = @kind AND j.\"ResourceType\" = @resourceType AND j.\"ResourceId\" = @id AND j.\"Purpose\" = 'apply'"
            + " ORDER BY j.\"CreatedAt\" DESC LIMIT 1;",
            parameters =>
            {
                parameters.AddWithValue("kind", RemoteJobKinds.MediaAudioExtract);
                parameters.AddWithValue("resourceType", AudioExtractSettings.ResourceType);
                parameters.AddWithValue("id", recordingId);
            },
            RemoteJobRow.Read,
            ct);

    /// <summary>SHA-256 of the stored recording (streamed once; the node verifies every byte against it). Null when it cannot be read.</summary>
    private async Task<string?> HashAsync(string storageKey, CancellationToken ct)
    {
        try
        {
            await using var stream = await storage.OpenReadAsync(storageKey, ct);
            var (_, sha) = await StreamingSha256.ComputeAsync([stream], null, ct);
            return sha;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KeyNotFoundException)
        {
            logger.LogWarning(ex, "Could not hash a Live Class recording for remote extraction; it stays on the local path.");
            return null;
        }
    }
}
