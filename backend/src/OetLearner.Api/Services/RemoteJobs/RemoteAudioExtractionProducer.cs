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
/// With the flag off, no pinned engine, no healthy node, a size or type the kind does not accept, or any doubt, the answer is
/// <see cref="AudioExtractAction.Local"/> and the oversize recording behaves exactly as it always did.
/// </summary>
public sealed class RemoteAudioExtractionProducer(
    LearnerDbContext db,
    IRemoteJobFlags flags,
    IRemoteJobQueue queue,
    RemotePlacement placement,
    IFileStorage storage,
    RemoteJobsSettings settings,
    TimeProvider timeProvider,
    ILogger<RemoteAudioExtractionProducer> logger) : IRemoteAudioExtraction
{
    public const string Enqueuer = "system:live-class-recording";

    /// <summary>A job that finished this recently with no manifest on the recording is a read race, not a vanished manifest.</summary>
    private static readonly TimeSpan JustFinished = TimeSpan.FromMinutes(2);

    public async Task<AudioExtractPlan> PlanAsync(string recordingId, string storageKey, long sizeBytes, CancellationToken ct)
    {
        try
        {
            return await PlanCoreAsync(recordingId, storageKey, sizeBytes, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The remote path may never make a recording worse: any failure means "no remote path" (today's behaviour).
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
            case RemoteJobState.Leased:
                return new AudioExtractPlan(AudioExtractAction.Pending, "remote audio extraction in progress", job.Id);

            case RemoteJobState.Succeeded:
                // The applier writes the manifest in the SAME transaction that settles the job, so a finished job with no manifest
                // means the manifest was dropped (its chunks vanished) or the caller read the row a moment too early.
                if (job.CompletedAt is { } completed && timeProvider.GetUtcNow() - completed < JustFinished)
                {
                    return new AudioExtractPlan(AudioExtractAction.Pending, "remote audio extraction just finished", job.Id);
                }

                return await RequeueAsync(job, recordingId, storageKey, sizeBytes, contentType, engine, spec, ct);

            case RemoteJobState.FallbackLocal:
            case RemoteJobState.Cancelled:
                // Withdrawn (no node claimed it, the master flag was off, the recording changed): ask again if a node can take it now.
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

    /// <summary>Resets a withdrawn or consumed job when a node can take it now; otherwise there is no remote path.</summary>
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

        if (await placement.DecideAsync(RemoteJobKinds.MediaAudioExtract, spec.Limits.Weight, ct) != RemotePlacementDecision.Remote)
        {
            return AudioExtractPlan.UseLocal;
        }

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
        // Never create a job nobody can take: there is no local extraction to wait for, so no node means no remote path.
        if (await placement.DecideAsync(RemoteJobKinds.MediaAudioExtract, spec.Limits.Weight, ct) != RemotePlacementDecision.Remote)
        {
            return AudioExtractPlan.UseLocal;
        }

        var sha = await HashAsync(storageKey, ct);
        if (sha is null) return AudioExtractPlan.UseLocal;

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
