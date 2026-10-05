using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>
/// The producer, consumer and cleanup of <c>media.speaking-join</c> (OET-RWP/1 section 6.4). The producer enqueues one idempotent job per
/// session while a grade is waiting; the audio stage later asks for a join that matches its clips exactly and otherwise joins locally. It
/// never waits for a remote job, never calls a provider, and every failure of this path means "join locally": the local
/// <see cref="FfmpegSpeakingAudioTranscoder"/> is always the fallback.
///
/// <para>
/// Learner-audio hygiene (owner decision 2026-10-05, in addition to the helper rules of section 10): the job and its output are keyed by
/// SESSION (never content-addressed, so no derivative is ever shared between learners); the derivative is deleted the moment it is used
/// (<see cref="TryServeAsync"/>), after <c>RemoteJobs:SpeakingJoinOutputTtlHours</c> by the reaper, and together with the clips it derives
/// from (<see cref="DeleteForSessionsAsync"/>, called by the audio retention sweep and by a learner's erasure).
/// </para>
/// </summary>
public sealed class RemoteSpeakingJoinProducer(
    LearnerDbContext db,
    IRemoteJobFlags flags,
    IRemoteJobQueue queue,
    RemotePlacement placement,
    IFileStorage storage,
    RemoteJobsSettings settings,
    IOptions<SpeakingAudioAssessmentOptions> audioOptions,
    ILogger<RemoteSpeakingJoinProducer> logger) : IRemoteSpeakingJoin
{
    public const string Enqueuer = "system:speaking-join";

    /// <summary>A grade is waiting on this: ahead of background work such as PDF extraction.</summary>
    private const int Priority = 5;

    // ── producer ─────────────────────────────────────────────────────────────

    public async Task<SpeakingJoinEnqueue> EnqueueForSessionAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            return await EnqueueCoreAsync(sessionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Remote Speaking join planning failed for session {SessionId}; the grade joins locally.", sessionId);
            return SpeakingJoinEnqueue.NotEligible;
        }
    }

    private async Task<SpeakingJoinEnqueue> EnqueueCoreAsync(string sessionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return SpeakingJoinEnqueue.NotEligible;

        var snapshot = await flags.GetAsync(ct);
        if (!snapshot.KindEnabled(RemoteJobKinds.MediaSpeakingJoin, RemoteJobPurpose.Apply)) return SpeakingJoinEnqueue.Disabled;

        var spec = RemoteJobKinds.Find(RemoteJobKinds.MediaSpeakingJoin);
        var engine = RemoteJobKinds.EngineVersion(RemoteJobKinds.MediaSpeakingJoin, settings.Current);
        if (spec is null || engine is null) return SpeakingJoinEnqueue.Disabled;

        // The audio stage is an admin flag of its own: with it off no grade ever asks for a join, so there is nothing to precompute.
        if (!await db.FeatureFlags.AsNoTracking().AnyAsync(f => f.Key == SpeakingAudioAssessmentOptions.FeatureFlagKey && f.Enabled, ct))
        {
            return SpeakingJoinEnqueue.Disabled;
        }

        // One precompute per session: a job in ANY state means this session was already handled (a consumed or withdrawn join is not redone).
        if (await JobExistsAsync(sessionId, ct)) return SpeakingJoinEnqueue.AlreadyQueued;

        if (!await placement.HasEligibleNodeAsync(RemoteJobKinds.MediaSpeakingJoin, spec.Limits.Weight, ct)) return SpeakingJoinEnqueue.NoEligibleNode;

        // The SAME clips in the SAME order the audio stage will use (it reads the same transcript, with the same chatter removed).
        var segments = await db.SpeakingTranscripts.AsNoTracking()
            .Where(t => t.SpeakingSessionId == sessionId && t.IsLatest)
            .OrderByDescending(t => t.GeneratedAt)
            .Select(t => t.SegmentsJson)
            .FirstOrDefaultAsync(ct);

        // No transcript yet: the grade picks a live-voice session's clips by TURN (minus chatter) from the transcript it is graded with,
        // so the "every clip, oldest first" fallback used below could differ from it, and a job keyed by the wrong list would block the
        // correct enqueue for ever (one job per session). Try again on a later pass, once the real clip order exists.
        if (segments is null) return SpeakingJoinEnqueue.NotEligible;

        var turns = SpeakingAudioEvidenceService.ReadCandidateTurns(SpeakingTranscriptEvidence.StripConnectivityChatter(segments));
        var clips = await SpeakingAudioClips.LoadAsync(db, sessionId, turns, ct);
        if (clips.Count is 0 or > SpeakingJoinSettings.MaxClips) return SpeakingJoinEnqueue.NotEligible;

        var entries = new List<Dictionary<string, object>>(clips.Count);
        var shas = new List<string>(clips.Count);
        long total = 0;
        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            var contentType = SpeakingJoinSettings.NormalizeContentType(clip.MimeType);
            if (contentType is null || string.IsNullOrWhiteSpace(clip.StoragePath)) return SpeakingJoinEnqueue.NotEligible;

            long length;
            try
            {
                if (!await storage.ExistsAsync(clip.StoragePath, ct)) return SpeakingJoinEnqueue.NotEligible;
                length = await storage.LengthAsync(clip.StoragePath, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KeyNotFoundException)
            {
                return SpeakingJoinEnqueue.NotEligible;
            }

            total += length;
            if (length <= 0 || length > SpeakingJoinSettings.MaxClipBytes || total > SpeakingJoinSettings.MaxTotalBytes) return SpeakingJoinEnqueue.NotEligible;

            var sha = clip.Sha256 ?? await HashAndRecordAsync(clip, ct);
            if (sha is null) return SpeakingJoinEnqueue.NotEligible;

            shas.Add(sha);
            entries.Add(new Dictionary<string, object>
            {
                ["name"] = SpeakingJoinSettings.InputName(i),
                ["sizeBytes"] = length,
                ["sha256"] = sha,
                ["contentType"] = contentType,
                ["storageKey"] = clip.StoragePath,
                // Server-side only (the claim response never carries it): the applier checks these recordings are still alive.
                ["recordingId"] = clip.Id,
            });
        }

        var audio = audioOptions.Value;
        var request = new RemoteEnqueueRequest(
            RemoteJobKinds.MediaSpeakingJoin,
            spec.SchemaVersion,
            RemoteJobPurpose.Apply,
            SpeakingJoinSettings.ResourceType,
            sessionId,
            SpeakingJoinSettings.InputSha256(shas),
            engine,
            SpeakingJoinSettings.Hash(audio.GapMilliseconds, audio.MaxAudioSeconds),
            SpeakingJoinSettings.ParamsJson(audio.GapMilliseconds, audio.MaxAudioSeconds),
            JsonSerializer.Serialize(entries),
            spec.Limits,
            Enqueuer,
            Priority);

        var result = await queue.EnqueueAsync(request, force: false, ct);
        return result.Created ? SpeakingJoinEnqueue.Enqueued : SpeakingJoinEnqueue.AlreadyQueued;
    }

    private async Task<bool> JobExistsAsync(string sessionId, CancellationToken ct)
    {
        var exists = await RemoteDb.ScalarAsync(
            db,
            """
            SELECT EXISTS (SELECT 1 FROM "RemoteJobs" WHERE "Kind" = @kind AND "ResourceType" = @resourceType AND "ResourceId" = @id);
            """,
            parameters =>
            {
                parameters.AddWithValue("kind", RemoteJobKinds.MediaSpeakingJoin);
                parameters.AddWithValue("resourceType", SpeakingJoinSettings.ResourceType);
                parameters.AddWithValue("id", sessionId);
            },
            ct);
        return exists is true;
    }

    /// <summary>
    /// The clip's SHA-256 when no creation site recorded one (several Speaking paths leave it empty): streamed once and recorded on the
    /// recording that owns the clip (<c>SpeakingRecording.Sha256</c>, the hash the audio stage reads back through
    /// <see cref="SpeakingClipRow.Sha256"/>, so it derives the very same key). It is deliberately NOT written to
    /// <c>MediaAssets.Sha256</c>: that column is the cross-asset dedupe key other upload paths look up WITHOUT regard to owner, so a
    /// learner's private clip would become a candidate for reuse by a later byte-identical upload. A recording that already carries a
    /// (non-standard) value is left untouched, never overwritten, and its session stays local. Null when the hash cannot be determined
    /// or recorded (the session then stays local).
    /// </summary>
    private async Task<string?> HashAndRecordAsync(SpeakingClipRow clip, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clip.StoragePath) || !string.IsNullOrEmpty(clip.RecordingSha256)) return null;

        try
        {
            await using var stream = await storage.OpenReadAsync(clip.StoragePath, ct);
            var (_, sha) = await StreamingSha256.ComputeAsync([stream], null, ct);
            var recordingId = clip.Id;
            var recorded = await db.SpeakingRecordings
                .Where(r => r.Id == recordingId && r.Sha256 == string.Empty)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Sha256, _ => sha), ct);

            // Not recorded (another process got there first, or the row is gone): the audio stage could not derive this key.
            return recorded == 1 ? sha : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KeyNotFoundException)
        {
            logger.LogWarning(ex, "Could not hash a Speaking clip for the remote join; the session stays on the local path.");
            return null;
        }
    }

    // ── consumer ─────────────────────────────────────────────────────────────

    public async Task<SpeakingAudioJoin?> TryServeAsync(string sessionId, IReadOnlyList<string> clipSha256s, CancellationToken ct)
    {
        try
        {
            return await ServeCoreAsync(sessionId, clipSha256s, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "A precomputed Speaking join of session {SessionId} could not be used; joining locally.", sessionId);
            return null;
        }
    }

    private async Task<SpeakingAudioJoin?> ServeCoreAsync(string sessionId, IReadOnlyList<string> clipSha256s, CancellationToken ct)
    {
        if (clipSha256s.Count == 0 || clipSha256s.Any(sha => !RemoteIds.IsSha256Hex(sha))) return null;

        // Off means off: a result computed while the feature was on is not served once it is switched off.
        var snapshot = await flags.GetAsync(ct);
        if (!snapshot.KindEnabled(RemoteJobKinds.MediaSpeakingJoin, RemoteJobPurpose.Apply)) return null;
        var engine = RemoteJobKinds.EngineVersion(RemoteJobKinds.MediaSpeakingJoin, settings.Current);
        if (engine is null) return null;

        var audio = audioOptions.Value;
        var key = RemoteJobKeys.IdempotencyKey(
            RemoteJobKinds.MediaSpeakingJoin,
            RemoteJobPurpose.Apply,
            SpeakingJoinSettings.ResourceType,
            sessionId,
            SpeakingJoinSettings.InputSha256(clipSha256s),
            engine,
            SpeakingJoinSettings.Hash(audio.GapMilliseconds, audio.MaxAudioSeconds));
        var job = await queue.FindByKeyAsync(key, ct);
        if (job is null || job.State != RemoteJobState.Succeeded || job.ApplyOutcome != "Applied"
            || job.SettledFence is not { } fence || job.ResultSummaryJson is null)
        {
            return null;
        }

        if (!TryReadSummary(job.ResultSummaryJson, out var summary)) return null;

        var output = await RemoteDb.QueryFirstAsync(
            db,
            """
            SELECT "StorageKey", "SizeBytes", "Sha256" FROM "RemoteJobOutputs"
            WHERE "JobId" = @id AND "Fence" = @fence AND "Name" = @name;
            """,
            parameters =>
            {
                parameters.AddWithValue("id", job.Id);
                parameters.AddWithValue("fence", fence);
                parameters.AddWithValue("name", SpeakingJoinSettings.OutputName);
            },
            reader => new StoredJoin(RemoteDb.Str(reader, "StorageKey"), RemoteDb.Long(reader, "SizeBytes"), RemoteDb.Str(reader, "Sha256")),
            ct);
        var limit = RemoteJobKinds.Find(RemoteJobKinds.MediaSpeakingJoin)?.Limits.MaxOutputBytes ?? 4_194_304;
        if (output is null || output.Size < 1 || output.Size > limit
            || output.Size != summary.Mp3Bytes || !string.Equals(output.Sha256, summary.Mp3Sha256, StringComparison.Ordinal))
        {
            return null;
        }

        byte[] mp3;
        try
        {
            var read = await storage.OpenReadWithMetadataAsync(output.StorageKey, ct);
            await using var stream = read.Stream;
            if (read.Length != output.Size) return null;
            mp3 = new byte[(int)read.Length];
            await stream.ReadExactlyAsync(mp3, ct);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or KeyNotFoundException or EndOfStreamException
                                       || ex is Amazon.S3.AmazonS3Exception { StatusCode: System.Net.HttpStatusCode.NotFound })
        {
            return null;
        }

        // Verified against the hash the API itself validated at completion: a swapped or damaged object is never judged.
        if (!string.Equals(RemoteIds.Sha256Hex(mp3), output.Sha256, StringComparison.Ordinal)) return null;

        // Delete-on-use: the derivative is read exactly once.
        await DeleteOutputAsync(job.Id, fence, output.StorageKey, ct);
        return new SpeakingAudioJoin(mp3, summary.DurationMs, summary.ClipCount, summary.Truncated);
    }

    private sealed record StoredJoin(string StorageKey, long Size, string Sha256);

    private sealed record JoinSummary(int ClipCount, int DurationMs, bool Truncated, int Mp3Bytes, string Mp3Sha256);

    private static bool TryReadSummary(string summaryJson, out JoinSummary summary)
    {
        summary = new JoinSummary(0, 0, false, 0, string.Empty);
        try
        {
            using var document = JsonDocument.Parse(summaryJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            if (!root.TryGetProperty("clipCount", out var clips) || !clips.TryGetInt32(out var clipCount)) return false;
            if (!root.TryGetProperty("durationMs", out var duration) || !duration.TryGetInt32(out var durationMs)) return false;
            if (!root.TryGetProperty("truncated", out var cut) || cut.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            if (!root.TryGetProperty("mp3Bytes", out var bytes) || !bytes.TryGetInt32(out var mp3Bytes)) return false;
            if (!root.TryGetProperty("mp3Sha256", out var sha) || sha.ValueKind != JsonValueKind.String) return false;

            summary = new JoinSummary(clipCount, durationMs, cut.GetBoolean(), mp3Bytes, sha.GetString() ?? string.Empty);
            return clipCount > 0 && durationMs > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task DeleteOutputAsync(string jobId, long fence, string storageKey, CancellationToken ct)
    {
        try
        {
            await storage.DeleteAsync(storageKey, ct);
            await RemoteDb.ExecuteAsync(
                db,
                """DELETE FROM "RemoteJobOutputs" WHERE "JobId" = @id AND "Fence" = @fence AND "Name" = @name;""",
                parameters =>
                {
                    parameters.AddWithValue("id", jobId);
                    parameters.AddWithValue("fence", fence);
                    parameters.AddWithValue("name", SpeakingJoinSettings.OutputName);
                },
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The reaper's TTL sweep removes whatever is left; the join was already read.
            logger.LogWarning(ex, "Could not delete a used Speaking join output of remote job {JobId}.", jobId);
        }
    }

    // ── cleanup with the clips ───────────────────────────────────────────────

    public async Task DeleteForSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct)
    {
        var ids = sessionIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return;

        try
        {
            // A job nobody has claimed yet is withdrawn; one already leased is refused by its applier (its clips are archived by now).
            await RemoteDb.ExecuteAsync(
                db,
                """
                UPDATE "RemoteJobs" SET "State" = 'Cancelled', "FailureCode" = 'resource_gone', "UpdatedAt" = clock_timestamp()
                WHERE "Kind" = @kind AND "ResourceType" = @resourceType AND "ResourceId" = ANY(@ids) AND "State" = 'Queued';
                """,
                parameters =>
                {
                    parameters.AddWithValue("kind", RemoteJobKinds.MediaSpeakingJoin);
                    parameters.AddWithValue("resourceType", SpeakingJoinSettings.ResourceType);
                    parameters.AddWithValue("ids", ids);
                },
                ct);

            var sets = await RemoteDb.QueryAsync(
                db,
                """
                SELECT o."JobId", o."Fence"
                FROM "RemoteJobOutputs" o
                JOIN "RemoteJobs" j ON j."Id" = o."JobId"
                WHERE j."Kind" = @kind AND j."ResourceType" = @resourceType AND j."ResourceId" = ANY(@ids)
                GROUP BY o."JobId", o."Fence";
                """,
                parameters =>
                {
                    parameters.AddWithValue("kind", RemoteJobKinds.MediaSpeakingJoin);
                    parameters.AddWithValue("resourceType", SpeakingJoinSettings.ResourceType);
                    parameters.AddWithValue("ids", ids);
                },
                reader => (JobId: RemoteDb.Str(reader, "JobId"), Fence: RemoteDb.Long(reader, "Fence")),
                ct);

            foreach (var (jobId, fence) in sets)
            {
                // The trailing slash matters: fence 2 must never match the keys of fence 20 (object stores match by raw prefix).
                await storage.DeletePrefixAsync(RemoteInputOutputService.OutputKey(jobId, fence, string.Empty), ct);
                await RemoteDb.ExecuteAsync(
                    db,
                    """DELETE FROM "RemoteJobOutputs" WHERE "JobId" = @id AND "Fence" = @fence;""",
                    parameters =>
                    {
                        parameters.AddWithValue("id", jobId);
                        parameters.AddWithValue("fence", fence);
                    },
                    ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never fail a retention sweep or an erasure because of a derivative: the TTL sweep is the backstop.
            logger.LogWarning(ex, "Could not delete the remote Speaking join derivatives of {Count} session(s).", ids.Length);
        }
    }
}

/// <summary>
/// Hands the sessions whose grade is waiting (queued or scheduled for retry, never one already running) to
/// <see cref="IRemoteSpeakingJoin"/> so a helper can prepare their joined audio while the grade queues. Newest first, skipping
/// sessions that already have a join job. Runs only on the <c>ai-worker</c> (the process that executes grades), never changes a grade, and does nothing at all
/// until the remote-job master flag, the <c>media.speaking-join</c> flag and the Speaking audio-stage flag are all on and an engine is pinned.
/// </summary>
public sealed class RemoteSpeakingJoinSweeper(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<RemoteSpeakingJoinSweeper> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    /// <summary>A grade operation older than this is no longer waiting for anything worth precomputing.</summary>
    internal static readonly TimeSpan MaxOperationAge = TimeSpan.FromHours(2);

    private const int PerPass = 25;

    /// <summary>How many waiting grades one pass looks at before sessions that were already handled are filtered out.</summary>
    private const int CandidateWindow = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(20, 45)), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Remote Speaking join pass failed."); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>One pass in a fresh scope; returns how many sessions were enqueued.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        if (!db.Database.IsNpgsql()) return 0;

        // The cheap, cached gate first: with the feature off this pass costs nothing but a flag-cache read.
        var flags = scope.ServiceProvider.GetRequiredService<IRemoteJobFlags>();
        if (!(await flags.GetAsync(ct)).KindEnabled(RemoteJobKinds.MediaSpeakingJoin, RemoteJobPurpose.Apply)) return 0;

        // Only grades that have NOT started: a leased one is already inside the audio stage, which joins locally within seconds, so a
        // remote job started then is almost never consumed (the helper would still download the learner's audio for nothing).
        // Newest first, and sessions that already have a join job are skipped, so grades that were handled (or are stuck in a
        // retry loop) never use up the budget of a pass while newer sessions wait.
        var since = timeProvider.GetUtcNow() - MaxOperationAge;
        var candidates = (await db.AiOperations.AsNoTracking()
                .Where(o => o.FeatureCode == AiFeatureCodes.SpeakingGrade
                    && o.ResourceType == "speaking_session"
                    && o.ResourceId != null
                    && o.CreatedAt > since
                    && (o.State == AiOperationState.Queued || o.State == AiOperationState.RetryScheduled))
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => o.ResourceId!)
                .Take(CandidateWindow)
                .ToListAsync(ct))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0) return 0;

        var handled = (await RemoteDb.QueryAsync(
                db,
                """SELECT DISTINCT "ResourceId" FROM "RemoteJobs" WHERE "Kind" = @kind AND "ResourceType" = @resourceType AND "ResourceId" = ANY(@ids);""",
                parameters =>
                {
                    parameters.AddWithValue("kind", RemoteJobKinds.MediaSpeakingJoin);
                    parameters.AddWithValue("resourceType", SpeakingJoinSettings.ResourceType);
                    parameters.AddWithValue("ids", candidates);
                },
                reader => RemoteDb.Str(reader, "ResourceId"),
                ct))
            .ToHashSet(StringComparer.Ordinal);
        var sessions = candidates.Where(id => !handled.Contains(id)).Take(PerPass).ToList();

        var producer = scope.ServiceProvider.GetRequiredService<IRemoteSpeakingJoin>();
        var enqueued = 0;
        foreach (var sessionId in sessions)
        {
            if (await producer.EnqueueForSessionAsync(sessionId, ct) == SpeakingJoinEnqueue.Enqueued) enqueued++;
        }

        return enqueued;
    }
}
