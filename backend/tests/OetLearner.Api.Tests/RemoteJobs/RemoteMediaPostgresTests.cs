using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.LiveClasses;
using OetLearner.Api.Services.RemoteJobs;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// The media kinds end to end on real PostgreSQL (OET-RWP/1 sections 6.3 and 6.4): the producers' decisions, the fenced completion that
/// writes the recording's chunk manifest, the Speaking join that is served once and deleted, the cleanup with the clips, and the
/// reaper's expiry of outputs. RW-066, RW-067, RW-113, RW-114, RW-115.
/// </summary>
[Collection(PostgreSqlExclusiveCollection.Name)]
public sealed class RemoteMediaPostgresTests
{
    private const string AudioEngine = "ffmpeg:7.1.1/audio-extract:1";
    private const string JoinEngine = "ffmpeg:7.1.1/ffmpeg-pcm-join:1";
    private const string RecordingKey = "live-class-recordings/2027/01/session-1/video.mp4";
    private static readonly byte[] RecordingBytes = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray();
    private static readonly byte[] ClipA = Enumerable.Repeat((byte)0xA1, 300).ToArray();
    private static readonly byte[] ClipB = Enumerable.Repeat((byte)0xB2, 500).ToArray();

    // ── shared setup ─────────────────────────────────────────────────────────

    private const string SupportDdl = """
        CREATE TABLE "LiveClassRecordings" (
            "Id"              character varying(64)  NOT NULL PRIMARY KEY,
            "S3VideoKey"      character varying(512) NULL,
            "S3AudioKey"      character varying(512) NULL,
            "TranscriptText"  text                   NULL,
            "DurationSeconds" integer                NOT NULL DEFAULT 0,
            "AudioChunksJson" text                   NULL
        );
        CREATE TABLE "SpeakingRecordings" (
            "Id"                character varying(64)    NOT NULL PRIMARY KEY,
            "SpeakingSessionId" character varying(64)    NOT NULL,
            "MediaAssetId"      character varying(64)    NOT NULL,
            "MimeType"          character varying(96)    NOT NULL DEFAULT 'audio/webm',
            "Sha256"            character varying(64)    NOT NULL DEFAULT '',
            "IsArchived"        boolean                  NOT NULL DEFAULT false,
            "IsWarmup"          boolean                  NOT NULL DEFAULT false,
            "CreatedAt"         timestamp with time zone NOT NULL DEFAULT now()
        );
        CREATE TABLE "SpeakingTranscripts" (
            "Id"                character varying(64)    NOT NULL PRIMARY KEY,
            "SpeakingSessionId" character varying(64)    NOT NULL,
            "SegmentsJson"      text                     NOT NULL DEFAULT '[]',
            "IsLatest"          boolean                  NOT NULL DEFAULT true,
            "GeneratedAt"       timestamp with time zone NOT NULL DEFAULT now()
        );
        CREATE TABLE "FeatureFlags" (
            "Id"      character varying(64)  NOT NULL PRIMARY KEY,
            "Key"     character varying(128) NOT NULL,
            "Enabled" boolean                NOT NULL DEFAULT false
        );
        CREATE TABLE "AiOperations" (
            "Id"           character varying(64)    NOT NULL PRIMARY KEY,
            "FeatureCode"  character varying(64)    NOT NULL,
            "ResourceType" character varying(64)    NULL,
            "ResourceId"   character varying(64)    NULL,
            "State"        integer                  NOT NULL DEFAULT 0,
            "CreatedAt"    timestamp with time zone NOT NULL DEFAULT now()
        );
        """;

    private static async Task SetUpAsync(RemotePgHarness h, bool pinEngines = true, bool flagsOn = true)
    {
        await h.Database.ExecuteAsync(SupportDdl);
        if (pinEngines)
        {
            h.Options.Kinds[RemoteJobKinds.MediaAudioExtract] = new RemoteKindOptions { EngineVersion = AudioEngine };
            h.Options.Kinds[RemoteJobKinds.MediaSpeakingJoin] = new RemoteKindOptions { EngineVersion = JoinEngine };
        }

        if (flagsOn)
        {
            h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.MediaAudioExtract, RemoteJobFlagKeys.MediaSpeakingJoin);
        }
    }

    /// <summary>An Active, freshly heartbeating node that offers (and may run) exactly these kinds at the pinned engines.</summary>
    private static async Task<string> NodeAsync(RemotePgHarness h, params string[] kinds)
    {
        var node = await h.AddNodeAsync(allowedKinds: kinds);
        var offers = kinds.Select(kind => new Dictionary<string, object>
        {
            ["kind"] = kind,
            ["schemaVersions"] = new[] { 1 },
            ["engineVersion"] = RemoteJobKinds.EngineVersion(kind, h.Options)!,
        }).ToArray();
        await h.SqlAsync(
            """UPDATE "RemoteWorkers" SET "KindsJson" = CAST(@k AS jsonb) WHERE "Id" = @id;""",
            ("k", JsonSerializer.Serialize(offers)), ("id", node));
        return node;
    }

    private static async Task<RemoteJobRow> ClaimAsync(RemotePgHarness h, string nodeId, string kind)
    {
        await using var db = h.NewContext();
        var node = await db.RemoteWorkers.AsNoTracking().SingleAsync(w => w.Id == nodeId);
        var request = h.ClaimRequest(nodeId);
        request.Kinds = new List<RemoteKindOfferDto> { RemoteTestData.Offer(kind, RemoteJobKinds.EngineVersion(kind, h.Options)) };
        var outcome = await h.Claims(db).ClaimAsync(node, request, CancellationToken.None);
        Assert.True(outcome.Leased is not null, $"expected a lease but got error {outcome.Error?.Code} / 204 {outcome.NoContentReason}");
        return outcome.Leased!.Job;
    }

    private static IRemoteKindHandler[] Handlers()
        =>
        [
            new AudioExtractKindHandler(NullLogger<AudioExtractKindHandler>.Instance),
            new SpeakingJoinKindHandler(NullLogger<SpeakingJoinKindHandler>.Instance),
        ];

    /// <summary>Stores the files as the job's outputs the way <c>PUT outputs</c> does (object + row) and returns what the node would declare.</summary>
    private static async Task<List<(string Name, long Size, string Sha)>> UploadAsync(
        RemotePgHarness h,
        RemoteJobRow job,
        params (string Name, byte[] Bytes)[] files)
    {
        var declared = new List<(string Name, long Size, string Sha)>();
        foreach (var (name, bytes) in files)
        {
            var key = RemoteInputOutputService.OutputKey(job.Id, job.FenceToken, name);
            await h.Storage.WriteAsync(key, new MemoryStream(bytes), CancellationToken.None);
            var sha = RemoteIds.Sha256Hex(bytes);
            await h.SqlAsync(
                """
                INSERT INTO "RemoteJobOutputs" ("JobId", "Fence", "Name", "Sha256", "SizeBytes", "StorageKey", "CreatedAt")
                VALUES (@job, @fence, @name, @sha, @size, @key, clock_timestamp());
                """,
                ("job", job.Id), ("fence", job.FenceToken), ("name", name), ("sha", sha), ("size", (long)bytes.Length), ("key", key));
            declared.Add((name, bytes.Length, sha));
        }

        return declared;
    }

    private static byte[] Body(long fence, string resultJson, IEnumerable<(string Name, long Size, string Sha)> outputs)
        => JsonSerializer.SerializeToUtf8Bytes(new
        {
            fence,
            resultSha256 = RemoteIds.Sha256Hex(resultJson),
            resultJson,
            outputs = outputs.Select(o => new { name = o.Name, sizeBytes = o.Size, sha256 = o.Sha }).ToArray(),
        });

    private static byte[] Filler(int length, byte seed) => Enumerable.Repeat(seed, length).ToArray();

    // ═════════════════════════════════════════════════════════════════════════
    // media.audio-extract: the producer
    // ═════════════════════════════════════════════════════════════════════════

    private static RemoteAudioExtractionProducer AudioProducer(RemotePgHarness h, LearnerDbContext db, bool headroom = false)
        => new(
            db,
            h.Flags,
            h.Queue(db),
            new RemotePlacement(db, h.Settings, new FixedHeadroom { Value = headroom }, h.Time),
            h.Storage,
            h.Settings,
            h.Time,
            NullLogger<RemoteAudioExtractionProducer>.Instance);

    private static async Task<AudioExtractPlan> PlanAudioAsync(RemotePgHarness h, string key = RecordingKey, long? size = null)
    {
        await using var db = h.NewContext();
        return await AudioProducer(h, db).PlanAsync("rec-1", key, size ?? RecordingBytes.Length, CancellationToken.None);
    }

    private static async Task SeedRecordingAsync(RemotePgHarness h, string key = RecordingKey, string? transcript = null)
    {
        await h.Storage.WriteAsync(key, new MemoryStream(RecordingBytes), CancellationToken.None);
        await h.SqlAsync(
            """INSERT INTO "LiveClassRecordings" ("Id", "S3VideoKey", "TranscriptText") VALUES ('rec-1', @key, @t);""",
            ("key", key), ("t", transcript));
    }

    private static Task<int> JobCountAsync(RemotePgHarness h)
        => h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobs";""");

    [PostgreSqlFact]
    public async Task AudioPlan_IsLocal_WhenTheFlagIsOff_NoEngineIsPinned_OrNoNodeCanTakeIt()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await SetUpAsync(h);
        await SeedRecordingAsync(h);

        // no node at all: there is no local extraction to wait for, so no remote path
        Assert.Equal(AudioExtractAction.Local, (await PlanAudioAsync(h)).Action);
        Assert.Equal(0, await JobCountAsync(h));

        await NodeAsync(h, RemoteJobKinds.MediaAudioExtract);
        h.Flags.Set(RemoteJobFlagKeys.Master); // the kind flag stays off
        Assert.Equal(AudioExtractAction.Local, (await PlanAudioAsync(h)).Action);

        h.Flags.Set(RemoteJobFlagKeys.MediaAudioExtract); // the master switch off
        Assert.Equal(AudioExtractAction.Local, (await PlanAudioAsync(h)).Action);

        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.MediaAudioExtract);
        h.Options.Kinds.Remove(RemoteJobKinds.MediaAudioExtract); // media kinds are never offered without a pinned engine
        Assert.Equal(AudioExtractAction.Local, (await PlanAudioAsync(h)).Action);

        Assert.Equal(0, await JobCountAsync(h));
    }

    [PostgreSqlFact]
    public async Task AudioPlan_OnlyOffersRecordingsTheKindAccepts()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await SetUpAsync(h);
        await SeedRecordingAsync(h);
        await NodeAsync(h, RemoteJobKinds.MediaAudioExtract);

        Assert.Equal(AudioExtractAction.Local, (await PlanAudioAsync(h, "live-class-recordings/x/video.exe")).Action);
        Assert.Equal(AudioExtractAction.Local, (await PlanAudioAsync(h, RecordingKey, size: 0)).Action);
        Assert.Equal(AudioExtractAction.Local, (await PlanAudioAsync(h, RecordingKey, size: 805_306_369)).Action);
        Assert.Equal(0, await JobCountAsync(h));
    }

    [PostgreSqlFact]
    public async Task AudioPlan_EnqueuesOnce_AFingerprintedJobWhoseParamsCarryNoIdentifier()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await SetUpAsync(h);
        await SeedRecordingAsync(h);
        await NodeAsync(h, RemoteJobKinds.MediaAudioExtract);

        var first = await PlanAudioAsync(h);
        var second = await PlanAudioAsync(h);

        Assert.Equal(AudioExtractAction.Pending, first.Action);
        Assert.Equal(AudioExtractAction.Pending, second.Action);
        Assert.Equal(first.JobId, second.JobId);
        Assert.Equal(1, await JobCountAsync(h));

        var job = await h.JobAsync(first.JobId!);
        Assert.Equal("Queued", job.State);
        Assert.Equal(RemoteJobKinds.MediaAudioExtract, job.Kind);
        Assert.Equal("LiveClassRecording", job.ResourceType);
        Assert.Equal("rec-1", job.ResourceId);
        Assert.Equal(RemoteIds.Sha256Hex(RecordingBytes), job.InputSha256);
        Assert.Equal(AudioEngine, job.EngineVersion);
        Assert.Equal(AudioExtractSettings.Hash(), job.SettingsHash);
        Assert.Equal(2, job.Weight);
        Assert.NotNull(job.FallbackAfter);

        var input = Assert.Single(RemoteInputOutputService.ReadManifest(job.InputsJson));
        Assert.Equal("media", input.Name);
        Assert.Equal(RecordingBytes.Length, input.SizeBytes);
        Assert.Equal(RemoteIds.Sha256Hex(RecordingBytes), input.Sha256);
        Assert.Equal("video/mp4", input.ContentType);
        Assert.Equal(RecordingKey, input.StorageKey);

        // params travel to the node as they are: no recording id, no storage key
        Assert.DoesNotContain("rec-1", job.ParamsJson, StringComparison.Ordinal);
        Assert.DoesNotContain(RecordingKey, job.ParamsJson, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task AudioPlan_TracksTheJobThroughItsLifeCycle()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await SetUpAsync(h);
        await SeedRecordingAsync(h);
        var node = await NodeAsync(h, RemoteJobKinds.MediaAudioExtract);
        var queued = await PlanAudioAsync(h);
        var jobId = queued.JobId!;

        // leased: a helper is on it
        await ClaimAsync(h, node, RemoteJobKinds.MediaAudioExtract);
        Assert.Equal(AudioExtractAction.Pending, (await PlanAudioAsync(h)).Action);

        // finished a moment ago with no manifest: the reader raced the applier, so wait
        await SetFinishedAsync(h, jobId, "Succeeded", completedAgo: "0 seconds");
        Assert.Equal(AudioExtractAction.Pending, (await PlanAudioAsync(h)).Action);
        Assert.Equal("Succeeded", await h.StateOfAsync(jobId));

        // finished long ago with no manifest: the chunks vanished with it, so extract again (same job, fence preserved)
        await SetFinishedAsync(h, jobId, "Succeeded", completedAgo: "1 hour");
        var requeued = await PlanAudioAsync(h);
        Assert.Equal(AudioExtractAction.Pending, requeued.Action);
        Assert.Equal(jobId, requeued.JobId);
        Assert.Equal("Queued", await h.StateOfAsync(jobId));
        Assert.Equal(1, await JobCountAsync(h));
        Assert.Equal(1L, await h.ScalarAsync<long>("""SELECT "FenceToken" FROM "RemoteJobs" WHERE "Id" = @id;""", ("id", jobId)));
    }

    [PostgreSqlFact]
    public async Task AudioPlan_AFailedOrQuarantinedJob_IsSurfaced_NeverRunLocally()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await SetUpAsync(h);
        await SeedRecordingAsync(h);
        await NodeAsync(h, RemoteJobKinds.MediaAudioExtract);
        var jobId = (await PlanAudioAsync(h)).JobId!;

        await SetFinishedAsync(h, jobId, "Failed", completedAgo: "1 hour", failureCode: "no_audio_stream");
        var failed = await PlanAudioAsync(h);
        Assert.Equal(AudioExtractAction.Failed, failed.Action);
        Assert.Contains("no_audio_stream", failed.Note, StringComparison.Ordinal);

        await SetFinishedAsync(h, jobId, "Quarantined", completedAgo: "1 hour", failureCode: "lease_expired");
        var quarantined = await PlanAudioAsync(h);
        Assert.Equal(AudioExtractAction.Failed, quarantined.Action);
        Assert.Contains("quarantined", quarantined.Note, StringComparison.Ordinal);
        Assert.Equal(1, await JobCountAsync(h));
    }

    [PostgreSqlFact]
    public async Task AudioPlan_AWithdrawnJob_IsAskedForAgainOnlyWhenANodeCanTakeIt()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await SetUpAsync(h);
        await SeedRecordingAsync(h);
        var node = await NodeAsync(h, RemoteJobKinds.MediaAudioExtract);
        var jobId = (await PlanAudioAsync(h)).JobId!;

        foreach (var state in new[] { "FallbackLocal", "Cancelled" })
        {
            await SetFinishedAsync(h, jobId, state, completedAgo: "1 hour");

            // a node can take it: the SAME job is reset and queued again
            var again = await PlanAudioAsync(h);
            Assert.Equal(AudioExtractAction.Pending, again.Action);
            Assert.Equal(jobId, again.JobId);
            Assert.Equal("Queued", await h.StateOfAsync(jobId));

            // no node can take it: there is no remote path
            await SetFinishedAsync(h, jobId, state, completedAgo: "1 hour");
            await h.SqlAsync("""UPDATE "RemoteWorkers" SET "Status" = 'Disabled' WHERE "Id" = @id;""", ("id", node));
            Assert.Equal(AudioExtractAction.Local, (await PlanAudioAsync(h)).Action);
            Assert.Equal(state, await h.StateOfAsync(jobId));
            await h.SqlAsync("""UPDATE "RemoteWorkers" SET "Status" = 'Active' WHERE "Id" = @id;""", ("id", node));
        }

        Assert.Equal(1, await JobCountAsync(h));
    }

    [PostgreSqlFact]
    public async Task AudioPlan_ARecordingStoredAnewGetsAFreshJob_NotTheStaleOne()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await SetUpAsync(h);
        await SeedRecordingAsync(h);
        await NodeAsync(h, RemoteJobKinds.MediaAudioExtract);
        var first = await PlanAudioAsync(h);

        // the same recording id now points at another file
        var newKey = "live-class-recordings/2027/02/session-1/video.mp4";
        var newBytes = Filler(2000, 9);
        await h.Storage.WriteAsync(newKey, new MemoryStream(newBytes), CancellationToken.None);
        var second = await PlanAudioAsync(h, newKey, newBytes.Length);

        Assert.Equal(AudioExtractAction.Pending, second.Action);
        Assert.NotEqual(first.JobId, second.JobId);
        Assert.Equal(2, await JobCountAsync(h));
        var job = await h.JobAsync(second.JobId!);
        Assert.Equal(RemoteIds.Sha256Hex(newBytes), job.InputSha256);
        Assert.Equal(newKey, Assert.Single(RemoteInputOutputService.ReadManifest(job.InputsJson)).StorageKey);
    }

    [PostgreSqlFact]
    public async Task AudioPlan_ARecordingThatCannotBeRead_StaysLocal_AndCreatesNoJob()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await SetUpAsync(h);
        await NodeAsync(h, RemoteJobKinds.MediaAudioExtract);

        // the object was never stored: hashing fails, and a job without a fingerprint is never created
        var plan = await PlanAudioAsync(h, "live-class-recordings/2027/01/missing/video.mp4", 4096);

        Assert.Equal(AudioExtractAction.Local, plan.Action);
        Assert.Equal(0, await JobCountAsync(h));
    }

    private static Task SetFinishedAsync(RemotePgHarness h, string jobId, string state, string completedAgo, string? failureCode = null)
        => h.SqlAsync(
            $"""
            UPDATE "RemoteJobs" SET "State" = @state, "FailureCode" = @code, "ApplyOutcome" = CASE WHEN @state = 'Succeeded' THEN 'Applied' ELSE NULL END,
                "LeaseOwner" = NULL, "LeaseExpiresAt" = NULL, "DeadlineAt" = NULL,
                "CompletedAt" = clock_timestamp() - interval '{completedAgo}'
            WHERE "Id" = @id;
            """,
            ("state", state), ("code", failureCode), ("id", jobId));

    // ═════════════════════════════════════════════════════════════════════════
    // media.audio-extract: the completion
    // ═════════════════════════════════════════════════════════════════════════

    private static async Task<(string Node, RemoteJobRow Job)> LeasedAudioJobAsync(RemotePgHarness h, string? transcript = null)
    {
        await SetUpAsync(h);
        await SeedRecordingAsync(h, transcript: transcript);
        var node = await NodeAsync(h, RemoteJobKinds.MediaAudioExtract);
        Assert.Equal(AudioExtractAction.Pending, (await PlanAudioAsync(h)).Action);
        return (node, await ClaimAsync(h, node, RemoteJobKinds.MediaAudioExtract));
    }

    private static string AudioResultJson(
        RemoteJobRow job,
        IReadOnlyList<(string Name, long Size, string Sha)> files,
        int[] durationsMs,
        Action<Dictionary<string, object?>>? tamper = null)
    {
        var chunks = new List<Dictionary<string, object?>>();
        long start = 0;
        for (var i = 0; i < files.Count; i++)
        {
            chunks.Add(new Dictionary<string, object?>
            {
                ["index"] = i,
                ["name"] = files[i].Name,
                ["startMs"] = start,
                ["durationMs"] = durationsMs[i],
                ["sizeBytes"] = files[i].Size,
                ["sha256"] = files[i].Sha,
            });
            start += durationsMs[i];
        }

        var body = new Dictionary<string, object?>
        {
            ["schema"] = "media.audio-extract.result/1",
            ["engineVersion"] = job.EngineVersion,
            ["inputSha256"] = job.InputSha256,
            ["durationMs"] = durationsMs.Sum(),
            ["sampleRateHz"] = 16000,
            ["channels"] = 1,
            ["codec"] = "mp3",
            ["bitrateKbps"] = 48,
            ["segmentSeconds"] = 600,
            ["chunkCount"] = files.Count,
            ["chunks"] = chunks,
            ["totalOutputBytes"] = files.Sum(f => f.Size),
        };
        tamper?.Invoke(body);
        return JsonSerializer.Serialize(body);
    }

    [PostgreSqlFact]
    public async Task AudioComplete_WritesTheManifestOnTheRecording_AndSettlesTheJobInOneTransaction()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (node, job) = await LeasedAudioJobAsync(h);
        var files = await UploadAsync(h, job, ("chunk-0000.mp3", Filler(100, 1)), ("chunk-0001.mp3", Filler(60, 2)));

        var outcome = await h.CompleteAsync(node, job, Body(job.FenceToken, AudioResultJson(job, files, [600_000, 180_000]), files), Handlers());

        Assert.Null(outcome.Error);
        Assert.Equal("succeeded", outcome.Status);
        Assert.Equal("Applied", outcome.Outcome);
        var row = await h.JobAsync(job.Id);
        Assert.Equal("Succeeded", row.State);
        Assert.Equal("Applied", row.ApplyOutcome);
        Assert.Equal(job.FenceToken, row.SettledFence);

        var manifest = LiveClassAudioManifest.TryParse(await h.ScalarAsync<string>("""SELECT "AudioChunksJson" FROM "LiveClassRecordings" WHERE "Id" = 'rec-1';"""));
        Assert.NotNull(manifest);
        Assert.Equal(job.Id, manifest!.JobId);
        Assert.Equal(job.FenceToken, manifest.Fence);
        Assert.Equal(job.InputSha256, manifest.InputSha256);
        Assert.Equal(780_000, manifest.DurationMs);
        Assert.Equal(600, manifest.SegmentSeconds);
        Assert.False(manifest.AudioDeleted);
        Assert.Equal(2, manifest.Chunks.Count);
        Assert.Equal(RemoteInputOutputService.OutputKey(job.Id, job.FenceToken, "chunk-0000.mp3"), manifest.Chunks[0].StorageKey);
        Assert.Equal(RemoteInputOutputService.OutputKey(job.Id, job.FenceToken, "chunk-0001.mp3"), manifest.Chunks[1].StorageKey);
        Assert.Equal(600_000, manifest.Chunks[0].DurationMs);
        Assert.Equal(600_000, manifest.Chunks[1].StartMs);
        Assert.Equal(files[1].Sha, manifest.Chunks[1].Sha256);
        Assert.All(manifest.Chunks, chunk => Assert.Null(chunk.Transcript));

        // the recording's length is filled in (it was 0), and the audit row names ids and counts only
        Assert.Equal(780, await h.ScalarAsync<int>("""SELECT "DurationSeconds" FROM "LiveClassRecordings" WHERE "Id" = 'rec-1';"""));
        Assert.Equal(1, await h.AuditCountAsync("RemoteJob.Applied"));
        var details = await h.ScalarAsync<string>("""SELECT "Details" FROM "AuditEvents" WHERE "Action" = 'RemoteJob.Applied';""");
        Assert.DoesNotContain("remote-jobs/", details, StringComparison.Ordinal);
        Assert.DoesNotContain(RecordingKey, details, StringComparison.Ordinal);

        // a job that finished a moment ago is never reset: the planner waits instead of extracting the same recording again
        Assert.Equal(AudioExtractAction.Pending, (await PlanAudioAsync(h)).Action);
        Assert.Equal("Succeeded", await h.StateOfAsync(job.Id));
    }

    [PostgreSqlFact]
    public async Task AudioComplete_AnExistingDurationIsNeverOverwritten()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (node, job) = await LeasedAudioJobAsync(h);
        await h.SqlAsync("""UPDATE "LiveClassRecordings" SET "DurationSeconds" = 4200 WHERE "Id" = 'rec-1';""");
        var files = await UploadAsync(h, job, ("chunk-0000.mp3", Filler(100, 1)));

        var outcome = await h.CompleteAsync(node, job, Body(job.FenceToken, AudioResultJson(job, files, [300_000]), files), Handlers());

        Assert.Equal("Applied", outcome.Outcome);
        Assert.Equal(4200, await h.ScalarAsync<int>("""SELECT "DurationSeconds" FROM "LiveClassRecordings" WHERE "Id" = 'rec-1';"""));
    }

    [PostgreSqlFact]
    public async Task AudioComplete_ARecordingThatMovedOn_IsDiscarded_AndTheDomainIsUntouched()
    {
        // the stored file changed while the helper worked
        await using (var h = await RemotePgHarness.CreateAsync())
        {
            var (node, job) = await LeasedAudioJobAsync(h);
            var files = await UploadAsync(h, job, ("chunk-0000.mp3", Filler(100, 1)));
            await h.SqlAsync("""UPDATE "LiveClassRecordings" SET "S3VideoKey" = 'live-class-recordings/other/video.mp4' WHERE "Id" = 'rec-1';""");

            var outcome = await h.CompleteAsync(node, job, Body(job.FenceToken, AudioResultJson(job, files, [300_000]), files), Handlers());

            Assert.Equal("stale_input", outcome.Error!.Code);
            Assert.Equal("Cancelled", await h.StateOfAsync(job.Id));
            Assert.Null(await h.ScalarAsync<string>("""SELECT "AudioChunksJson" FROM "LiveClassRecordings" WHERE "Id" = 'rec-1';"""));
        }

        // a real transcript arrived meanwhile: the chunks would never be used
        await using (var h = await RemotePgHarness.CreateAsync())
        {
            var (node, job) = await LeasedAudioJobAsync(h, transcript: "Zoom already transcribed this class.");
            var files = await UploadAsync(h, job, ("chunk-0000.mp3", Filler(100, 1)));

            var outcome = await h.CompleteAsync(node, job, Body(job.FenceToken, AudioResultJson(job, files, [300_000]), files), Handlers());

            Assert.Equal("stale_input", outcome.Error!.Code);
            Assert.Equal("Cancelled", await h.StateOfAsync(job.Id));
            Assert.Null(await h.ScalarAsync<string>("""SELECT "AudioChunksJson" FROM "LiveClassRecordings" WHERE "Id" = 'rec-1';"""));
        }

        // the placeholder written while a transcript is pending does NOT count as a transcript
        await using (var h = await RemotePgHarness.CreateAsync())
        {
            var (node, job) = await LeasedAudioJobAsync(h, transcript: "[Transcript processing — Zoom AI Companion transcript will appear here when available]");
            var files = await UploadAsync(h, job, ("chunk-0000.mp3", Filler(100, 1)));

            var outcome = await h.CompleteAsync(node, job, Body(job.FenceToken, AudioResultJson(job, files, [300_000]), files), Handlers());

            Assert.Equal("Applied", outcome.Outcome);
        }

        // the recording was deleted
        await using (var h = await RemotePgHarness.CreateAsync())
        {
            var (node, job) = await LeasedAudioJobAsync(h);
            var files = await UploadAsync(h, job, ("chunk-0000.mp3", Filler(100, 1)));
            await h.SqlAsync("""DELETE FROM "LiveClassRecordings" WHERE "Id" = 'rec-1';""");

            var outcome = await h.CompleteAsync(node, job, Body(job.FenceToken, AudioResultJson(job, files, [300_000]), files), Handlers());

            Assert.Equal("resource_gone", outcome.Error!.Code);
            Assert.Equal("Cancelled", await h.StateOfAsync(job.Id));
        }
    }

    [PostgreSqlFact]
    public async Task AudioComplete_AnUploadThatDisagreesWithTheResult_IsRejected_AndWritesNothing()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (node, job) = await LeasedAudioJobAsync(h);
        var files = await UploadAsync(h, job, ("chunk-0000.mp3", Filler(100, 1)));
        // the result claims a different chunk hash than the object that was uploaded
        var json = AudioResultJson(job, files, [300_000], body => ((List<Dictionary<string, object?>>)body["chunks"]!)[0]["sha256"] = new string('9', 64));

        var outcome = await h.CompleteAsync(node, job, Body(job.FenceToken, json, files), Handlers());

        Assert.Equal("output_hash_mismatch", outcome.Error!.Code);
        Assert.Null(await h.ScalarAsync<string>("""SELECT "AudioChunksJson" FROM "LiveClassRecordings" WHERE "Id" = 'rec-1';"""));
        Assert.Equal("Queued", await h.StateOfAsync(job.Id)); // a rejected result requeues like a retryable failure
        Assert.Equal(0, await h.AuditCountAsync("RemoteJob.Applied"));
    }

    [PostgreSqlFact]
    public async Task AudioComplete_AWholeSecondResultForTheSameFence_IsAReplay_NotASecondManifest()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (node, job) = await LeasedAudioJobAsync(h);
        var files = await UploadAsync(h, job, ("chunk-0000.mp3", Filler(100, 1)));
        var body = Body(job.FenceToken, AudioResultJson(job, files, [300_000]), files);

        await h.CompleteAsync(node, job, body, Handlers());
        var replay = await h.CompleteAsync(node, job, body, Handlers());

        Assert.Null(replay.Error);
        Assert.True(replay.Replayed);
        Assert.Equal(1, await h.AuditCountAsync("RemoteJob.Applied"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // media.speaking-join
    // ═════════════════════════════════════════════════════════════════════════

    private static RemoteSpeakingJoinProducer JoinProducer(RemotePgHarness h, LearnerDbContext db, SpeakingAudioAssessmentOptions? audio = null)
        => new(
            db,
            h.Flags,
            h.Queue(db),
            new RemotePlacement(db, h.Settings, new FixedHeadroom(), h.Time),
            h.Storage,
            h.Settings,
            Microsoft.Extensions.Options.Options.Create(audio ?? new SpeakingAudioAssessmentOptions()),
            NullLogger<RemoteSpeakingJoinProducer>.Instance);

    private static async Task<SpeakingJoinEnqueue> EnqueueJoinAsync(RemotePgHarness h, string session = "session-1")
    {
        await using var db = h.NewContext();
        return await JoinProducer(h, db).EnqueueForSessionAsync(session, CancellationToken.None);
    }

    private static async Task<SpeakingAudioJoin?> ServeJoinAsync(RemotePgHarness h, string session, params string[] shas)
    {
        await using var db = h.NewContext();
        return await JoinProducer(h, db).TryServeAsync(session, shas, CancellationToken.None);
    }

    private static Task EnableAudioStageAsync(RemotePgHarness h)
        => h.SqlAsync(
            """INSERT INTO "FeatureFlags" ("Id", "Key", "Enabled") VALUES ('flag-audio', 'speaking_audio_assessment', true);""");

    private static async Task SeedClipAsync(
        RemotePgHarness h,
        string session,
        string recordingId,
        byte[] bytes,
        string mime = "audio/webm",
        string? assetSha = null,
        string recordingSha = "",
        bool archived = false,
        bool warmup = false,
        bool writeBlob = true)
    {
        var key = $"speaking/sessions/{session}/{recordingId}.bin";
        if (writeBlob) await h.Storage.WriteAsync(key, new MemoryStream(bytes), CancellationToken.None);
        await h.SqlAsync(
            """INSERT INTO "MediaAssets" ("Id", "Format", "StoragePath", "SizeBytes", "Sha256") VALUES (@id, 'bin', @key, @size, @sha);""",
            ("id", "asset-" + recordingId), ("key", key), ("size", (long)bytes.Length), ("sha", assetSha));
        await h.SqlAsync(
            """
            INSERT INTO "SpeakingRecordings" ("Id", "SpeakingSessionId", "MediaAssetId", "MimeType", "Sha256", "IsArchived", "IsWarmup", "CreatedAt")
            VALUES (@id, @session, @asset, @mime, @sha, @archived, @warmup, clock_timestamp());
            """,
            ("id", recordingId), ("session", session), ("asset", "asset-" + recordingId), ("mime", mime), ("sha", recordingSha),
            ("archived", archived), ("warmup", warmup));
    }

    private static Task SeedTranscriptAsync(RemotePgHarness h, string session, params (string Speaker, string? Recording)[] turns)
    {
        var segments = turns.Select((turn, i) =>
        {
            var segment = new Dictionary<string, object?>
            {
                ["speaker"] = turn.Speaker,
                ["startMs"] = i * 5000,
                ["endMs"] = (i * 5000) + 4000,
                ["text"] = turn.Speaker == "candidate" ? "Good morning, I am Doctor Hesham." : "Hello doctor, thanks for seeing me.",
            };
            if (turn.Recording is not null) segment["sourceRecordingId"] = turn.Recording;
            return segment;
        }).ToArray();
        return h.SqlAsync(
            """INSERT INTO "SpeakingTranscripts" ("Id", "SpeakingSessionId", "SegmentsJson") VALUES (@id, @session, @json);""",
            ("id", "tr-" + session), ("session", session), ("json", JsonSerializer.Serialize(segments)));
    }

    /// <summary>Two live-voice clips (rec-a spoken first, then rec-b) with known hashes, a transcript that names them, and a node that offers the kind.</summary>
    private static async Task<string> TwoClipSessionAsync(RemotePgHarness h, string session = "session-1")
    {
        await SeedClipAsync(h, session, "rec-a-" + session, ClipA, assetSha: RemoteIds.Sha256Hex(ClipA));
        await SeedClipAsync(h, session, "rec-b-" + session, ClipB, assetSha: RemoteIds.Sha256Hex(ClipB));
        await SeedTranscriptAsync(h, session, ("candidate", "rec-a-" + session), ("patient", null), ("candidate", "rec-b-" + session));
        return session;
    }

    private static string JoinResultJson(RemoteJobRow job, (string Name, long Size, string Sha) file, int clipCount, int durationMs, bool truncated = false)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["schema"] = "media.speaking-join.result/1",
            ["engineVersion"] = job.EngineVersion,
            ["inputSha256"] = job.InputSha256,
            ["clipCount"] = clipCount,
            ["durationMs"] = durationMs,
            ["truncated"] = truncated,
            ["mp3Bytes"] = file.Size,
            ["mp3Sha256"] = file.Sha,
        });

    /// <summary>A session whose join was prepared and applied: producer, claim, upload, completion. Returns the join bytes.</summary>
    private static async Task<(string Node, RemoteJobRow Job, byte[] Mp3)> AppliedJoinAsync(RemotePgHarness h, string session = "session-1")
    {
        await SetUpAsync(h);
        await EnableAudioStageAsync(h);
        await TwoClipSessionAsync(h, session);
        var node = await NodeAsync(h, RemoteJobKinds.MediaSpeakingJoin);
        Assert.Equal(SpeakingJoinEnqueue.Enqueued, await EnqueueJoinAsync(h, session));

        var job = await ClaimAsync(h, node, RemoteJobKinds.MediaSpeakingJoin);
        var mp3 = Filler(900, 7);
        var files = await UploadAsync(h, job, ("join.mp3", mp3));
        var outcome = await h.CompleteAsync(node, job, Body(job.FenceToken, JoinResultJson(job, files[0], 2, 9_000), files), Handlers());
        Assert.Null(outcome.Error);
        Assert.Equal("Applied", outcome.Outcome);
        return (node, job, mp3);
    }

    private static string[] TwoClipShas(string first = "A") => first == "A"
        ? [RemoteIds.Sha256Hex(ClipA), RemoteIds.Sha256Hex(ClipB)]
        : [RemoteIds.Sha256Hex(ClipB), RemoteIds.Sha256Hex(ClipA)];

    [PostgreSqlFact]
    public async Task JoinEnqueue_StaysDisabled_UntilEveryConditionHolds()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await SetUpAsync(h);
        await TwoClipSessionAsync(h);

        // the Speaking audio stage itself is off
        await NodeAsync(h, RemoteJobKinds.MediaSpeakingJoin);
        Assert.Equal(SpeakingJoinEnqueue.Disabled, await EnqueueJoinAsync(h));

        await EnableAudioStageAsync(h);
        h.Flags.Set(RemoteJobFlagKeys.Master); // the kind flag is off
        Assert.Equal(SpeakingJoinEnqueue.Disabled, await EnqueueJoinAsync(h));

        h.Flags.Set(RemoteJobFlagKeys.MediaSpeakingJoin); // the master switch is off
        Assert.Equal(SpeakingJoinEnqueue.Disabled, await EnqueueJoinAsync(h));

        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.MediaSpeakingJoin);
        h.Options.Kinds.Remove(RemoteJobKinds.MediaSpeakingJoin); // no pinned engine
        Assert.Equal(SpeakingJoinEnqueue.Disabled, await EnqueueJoinAsync(h));

        Assert.Equal(0, await JobCountAsync(h));
    }

    [PostgreSqlFact]
    public async Task JoinEnqueue_NeedsAHealthyNode_AndClipsTheKindCanJoin()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await SetUpAsync(h);
        await EnableAudioStageAsync(h);
        await TwoClipSessionAsync(h);

        // nobody to run it: nothing is enqueued (the grade joins locally)
        Assert.Equal(SpeakingJoinEnqueue.NoEligibleNode, await EnqueueJoinAsync(h));

        await NodeAsync(h, RemoteJobKinds.MediaSpeakingJoin);

        // a session with no clips
        Assert.Equal(SpeakingJoinEnqueue.NotEligible, await EnqueueJoinAsync(h, "session-without-clips"));

        // a clip type the agent is not offered
        await SeedClipAsync(h, "session-odd", "rec-odd", ClipA, mime: "application/octet-stream", assetSha: RemoteIds.Sha256Hex(ClipA));
        await SeedTranscriptAsync(h, "session-odd", ("candidate", "rec-odd"));
        Assert.Equal(SpeakingJoinEnqueue.NotEligible, await EnqueueJoinAsync(h, "session-odd"));

        // a clip whose bytes are gone
        await SeedClipAsync(h, "session-gone", "rec-gone", ClipA, assetSha: RemoteIds.Sha256Hex(ClipA), writeBlob: false);
        await SeedTranscriptAsync(h, "session-gone", ("candidate", "rec-gone"));
        Assert.Equal(SpeakingJoinEnqueue.NotEligible, await EnqueueJoinAsync(h, "session-gone"));

        Assert.Equal(0, await JobCountAsync(h));
    }

    [PostgreSqlFact]
    public async Task JoinEnqueue_EnqueuesTheSpokenClipsInOrder_RecordsMissingHashes_AndNeverShowsTheNodeAKeyOrAnId()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await SetUpAsync(h);
        await EnableAudioStageAsync(h);
        // rec-b is spoken FIRST; its hash is on the asset. rec-a has no hash anywhere (as several Speaking paths leave it).
        await SeedClipAsync(h, "session-1", "rec-a", ClipA, assetSha: null, recordingSha: "");
        await SeedClipAsync(h, "session-1", "rec-b", ClipB, assetSha: RemoteIds.Sha256Hex(ClipB));
        await SeedClipAsync(h, "session-1", "rec-old", ClipA, archived: true, assetSha: RemoteIds.Sha256Hex(ClipA));
        await SeedClipAsync(h, "session-1", "rec-warm", ClipA, warmup: true, assetSha: RemoteIds.Sha256Hex(ClipA));
        await SeedTranscriptAsync(h, "session-1", ("candidate", "rec-b"), ("patient", null), ("candidate", "rec-a"));
        await NodeAsync(h, RemoteJobKinds.MediaSpeakingJoin);

        Assert.Equal(SpeakingJoinEnqueue.Enqueued, await EnqueueJoinAsync(h));

        var job = await h.JobAsync((await h.ScalarAsync<string>("""SELECT "Id" FROM "RemoteJobs";"""))!);
        Assert.Equal("Queued", job.State);
        Assert.Equal(RemoteJobKinds.MediaSpeakingJoin, job.Kind);
        Assert.Equal("SpeakingSession", job.ResourceType);
        Assert.Equal("session-1", job.ResourceId);
        Assert.Equal(JoinEngine, job.EngineVersion);
        Assert.Equal(1, job.Weight);
        Assert.Equal(5, job.Priority);
        Assert.NotNull(job.FallbackAfter);
        Assert.Equal(SpeakingJoinSettings.Hash(600, 360), job.SettingsHash);

        var shaA = RemoteIds.Sha256Hex(ClipA);
        var shaB = RemoteIds.Sha256Hex(ClipB);
        Assert.Equal(SpeakingJoinSettings.InputSha256([shaB, shaA]), job.InputSha256); // spoken order, not storage order

        var inputs = RemoteInputOutputService.ReadManifest(job.InputsJson);
        Assert.Equal(new[] { "clip-0000", "clip-0001" }, inputs.Select(i => i.Name).ToArray());
        Assert.Equal(new[] { shaB, shaA }, inputs.Select(i => i.Sha256).ToArray());
        Assert.Equal(new long[] { ClipB.Length, ClipA.Length }, inputs.Select(i => i.SizeBytes).ToArray());
        Assert.All(inputs, input => Assert.Equal("audio/webm", input.ContentType));

        // the hash nobody recorded was computed once and recorded on the asset, so the audio stage derives the very same key
        Assert.Equal(shaA, await h.ScalarAsync<string>("""SELECT "Sha256" FROM "MediaAssets" WHERE "Id" = 'asset-rec-a';"""));

        // what the node is told: names, sizes, hashes and content types. Never a storage key, a recording id or the session.
        var wire = RemoteJobWire.ToClaimJob(job, 1000, h.Options.Normalized());
        var wireJson = JsonSerializer.Serialize(wire);
        Assert.DoesNotContain("speaking/sessions", wireJson, StringComparison.Ordinal);
        Assert.DoesNotContain("rec-a", wireJson, StringComparison.Ordinal);
        Assert.DoesNotContain("rec-b", wireJson, StringComparison.Ordinal);
        Assert.DoesNotContain("session-1", wireJson, StringComparison.Ordinal);
        Assert.DoesNotContain("recordingId", wireJson, StringComparison.Ordinal);
        Assert.DoesNotContain("storageKey", wireJson, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task JoinEnqueue_IsOncePerSession_WhateverTheStateOfTheEarlierJob()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await SetUpAsync(h);
        await EnableAudioStageAsync(h);
        await TwoClipSessionAsync(h);
        await NodeAsync(h, RemoteJobKinds.MediaSpeakingJoin);

        Assert.Equal(SpeakingJoinEnqueue.Enqueued, await EnqueueJoinAsync(h));
        Assert.Equal(SpeakingJoinEnqueue.AlreadyQueued, await EnqueueJoinAsync(h));
        await h.SqlAsync("""UPDATE "RemoteJobs" SET "State" = 'FallbackLocal';""");
        Assert.Equal(SpeakingJoinEnqueue.AlreadyQueued, await EnqueueJoinAsync(h));

        Assert.Equal(1, await JobCountAsync(h));
    }

    [PostgreSqlFact]
    public async Task JoinServe_ServesTheVerifiedJoinExactlyOnce_AndDeletesItTheMomentItIsUsed()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (_, job, mp3) = await AppliedJoinAsync(h);
        var key = RemoteInputOutputService.OutputKey(job.Id, job.FenceToken, "join.mp3");
        Assert.True(await h.Storage.ExistsAsync(key, CancellationToken.None));

        var join = await ServeJoinAsync(h, "session-1", TwoClipShas());

        Assert.NotNull(join);
        Assert.Equal(mp3, join!.Mp3);
        Assert.Equal(9_000, join.DurationMs);
        Assert.Equal(2, join.ClipCount);
        Assert.False(join.Truncated);

        // delete-on-use: the learner's audio derivative is gone, object and row
        Assert.False(await h.Storage.ExistsAsync(key, CancellationToken.None));
        Assert.Equal(0, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobOutputs";"""));
        Assert.Null(await ServeJoinAsync(h, "session-1", TwoClipShas()));
        Assert.Equal("Succeeded", await h.StateOfAsync(job.Id));
    }

    [PostgreSqlFact]
    public async Task JoinServe_OnlyAnExactMatchIsServed_ThePrefixIsPerSessionAndPerOrder()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await AppliedJoinAsync(h);

        Assert.Null(await ServeJoinAsync(h, "session-1", TwoClipShas("B"))); // the other order
        Assert.Null(await ServeJoinAsync(h, "session-1", RemoteIds.Sha256Hex(ClipA))); // a subset
        Assert.Null(await ServeJoinAsync(h, "session-2", TwoClipShas())); // byte-identical audio of ANOTHER session is never shared
        Assert.Null(await ServeJoinAsync(h, "session-1")); // no clips
        Assert.Null(await ServeJoinAsync(h, "session-1", "not-a-hash", RemoteIds.Sha256Hex(ClipB)));

        // none of those misses consumed it
        Assert.NotNull(await ServeJoinAsync(h, "session-1", TwoClipShas()));
    }

    [PostgreSqlFact]
    public async Task JoinServe_FailsClosed_WhenTheFeatureIsOffOrTheEngineIsNoLongerPinned()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await AppliedJoinAsync(h);

        h.Flags.Set(RemoteJobFlagKeys.Master); // the kind flag off: a result computed earlier is NOT served
        Assert.Null(await ServeJoinAsync(h, "session-1", TwoClipShas()));

        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.MediaSpeakingJoin);
        h.Options.Kinds.Remove(RemoteJobKinds.MediaSpeakingJoin);
        Assert.Null(await ServeJoinAsync(h, "session-1", TwoClipShas()));

        h.Options.Kinds[RemoteJobKinds.MediaSpeakingJoin] = new RemoteKindOptions { EngineVersion = JoinEngine };
        Assert.NotNull(await ServeJoinAsync(h, "session-1", TwoClipShas()));
    }

    [PostgreSqlFact]
    public async Task JoinServe_ADamagedOrSwappedObject_IsNeverJudged()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (_, job, mp3) = await AppliedJoinAsync(h);
        var key = RemoteInputOutputService.OutputKey(job.Id, job.FenceToken, "join.mp3");

        // same size, other bytes
        await h.Storage.WriteAsync(key, new MemoryStream(Filler(mp3.Length, 1)), CancellationToken.None);
        Assert.Null(await ServeJoinAsync(h, "session-1", TwoClipShas()));

        // another size
        await h.Storage.WriteAsync(key, new MemoryStream(Filler(10, 7)), CancellationToken.None);
        Assert.Null(await ServeJoinAsync(h, "session-1", TwoClipShas()));

        // gone
        await h.Storage.DeleteAsync(key, CancellationToken.None);
        Assert.Null(await ServeJoinAsync(h, "session-1", TwoClipShas()));
    }

    [PostgreSqlFact]
    public async Task JoinServe_ARowThatSaysOtherwiseThanTheJobsOwnSummary_IsNotTrusted()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (_, job, _) = await AppliedJoinAsync(h);

        await h.SqlAsync("""UPDATE "RemoteJobOutputs" SET "SizeBytes" = "SizeBytes" + 1 WHERE "JobId" = @id;""", ("id", job.Id));

        Assert.Null(await ServeJoinAsync(h, "session-1", TwoClipShas()));
    }

    [PostgreSqlFact]
    public async Task JoinDelete_DeletesThoseSessionsDerivatives_AndWithdrawsTheirUnclaimedJobs_AndNoOthers()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (node, job, _) = await AppliedJoinAsync(h, "session-1");
        var key = RemoteInputOutputService.OutputKey(job.Id, job.FenceToken, "join.mp3");

        // a second session whose job nobody has claimed, and a third that must be left alone
        await TwoClipSessionAsync(h, "session-2");
        await TwoClipSessionAsync(h, "session-3");
        Assert.Equal(SpeakingJoinEnqueue.Enqueued, await EnqueueJoinAsync(h, "session-2"));
        Assert.Equal(SpeakingJoinEnqueue.Enqueued, await EnqueueJoinAsync(h, "session-3"));

        await using (var db = h.NewContext())
        {
            await JoinProducer(h, db).DeleteForSessionsAsync(["session-1", "session-2", "  ", "session-1"], CancellationToken.None);
        }

        Assert.False(await h.Storage.ExistsAsync(key, CancellationToken.None));
        Assert.Equal(0, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobOutputs";"""));
        Assert.Equal("Cancelled", await h.ScalarAsync<string>("""SELECT "State" FROM "RemoteJobs" WHERE "ResourceId" = 'session-2';"""));
        Assert.Equal("resource_gone", await h.ScalarAsync<string>("""SELECT "FailureCode" FROM "RemoteJobs" WHERE "ResourceId" = 'session-2';"""));
        Assert.Equal("Queued", await h.ScalarAsync<string>("""SELECT "State" FROM "RemoteJobs" WHERE "ResourceId" = 'session-3';"""));
        Assert.Equal("Succeeded", await h.StateOfAsync(job.Id)); // the finished job row stays as the (content-free) record
        Assert.Null(await ServeJoinAsync(h, "session-1", TwoClipShas()));

        // it can be called again and with nothing at all
        await using var again = h.NewContext();
        await JoinProducer(h, again).DeleteForSessionsAsync(["session-1"], CancellationToken.None);
        await JoinProducer(h, again).DeleteForSessionsAsync([], CancellationToken.None);
        Assert.NotEmpty(node);
    }

    [PostgreSqlFact]
    public async Task JoinComplete_RefusesAJoinWhoseClipsWereArchivedWhileTheHelperWorked()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await SetUpAsync(h);
        await EnableAudioStageAsync(h);
        await TwoClipSessionAsync(h);
        var node = await NodeAsync(h, RemoteJobKinds.MediaSpeakingJoin);
        Assert.Equal(SpeakingJoinEnqueue.Enqueued, await EnqueueJoinAsync(h));
        var job = await ClaimAsync(h, node, RemoteJobKinds.MediaSpeakingJoin);
        var files = await UploadAsync(h, job, ("join.mp3", Filler(900, 7)));

        // the learner erased a recording (or retention swept it) while the helper was joining
        await h.SqlAsync("""UPDATE "SpeakingRecordings" SET "IsArchived" = true WHERE "Id" = 'rec-b-session-1';""");
        var outcome = await h.CompleteAsync(node, job, Body(job.FenceToken, JoinResultJson(job, files[0], 2, 9_000), files), Handlers());

        Assert.Equal("resource_gone", outcome.Error!.Code);
        Assert.Equal("Cancelled", await h.StateOfAsync(job.Id));
        Assert.Equal(0, await h.AuditCountAsync("RemoteJob.Applied"));

        // and nothing can serve it: the job is not Succeeded
        Assert.Null(await ServeJoinAsync(h, "session-1", TwoClipShas()));
    }

    [PostgreSqlFact]
    public async Task JoinComplete_AnUploadThatIsNotExactlyOneJoinMp3_IsRejected()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await SetUpAsync(h);
        await EnableAudioStageAsync(h);
        await TwoClipSessionAsync(h);
        var node = await NodeAsync(h, RemoteJobKinds.MediaSpeakingJoin);
        await EnqueueJoinAsync(h);
        var job = await ClaimAsync(h, node, RemoteJobKinds.MediaSpeakingJoin);
        var files = await UploadAsync(h, job, ("join.mp3", Filler(900, 7)));
        var json = JoinResultJson(job, (files[0].Name, files[0].Size + 1, files[0].Sha), 2, 9_000);

        var outcome = await h.CompleteAsync(node, job, Body(job.FenceToken, json, files), Handlers());

        Assert.Equal("output_hash_mismatch", outcome.Error!.Code);
        Assert.Equal("Queued", await h.StateOfAsync(job.Id));
    }

    // ── the sweeper that hands waiting grades to the precompute ──────────────

    private static ServiceProvider SweeperServices(RemotePgHarness h)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRemoteJobFlags>(h.Flags);
        services.AddScoped(_ => h.NewContext());
        services.AddScoped<IRemoteSpeakingJoin>(provider => JoinProducer(h, provider.GetRequiredService<LearnerDbContext>()));
        return services.BuildServiceProvider();
    }

    private static Task AddGradeOperationAsync(RemotePgHarness h, string id, string session, int state = 0, string feature = "speaking.grade", string resourceType = "speaking_session", string age = "1 minute")
        => h.SqlAsync(
            $"""
            INSERT INTO "AiOperations" ("Id", "FeatureCode", "ResourceType", "ResourceId", "State", "CreatedAt")
            VALUES (@id, @feature, @type, @session, @state, clock_timestamp() - interval '{age}');
            """,
            ("id", id), ("feature", feature), ("type", resourceType), ("session", session), ("state", state));

    [PostgreSqlFact]
    public async Task JoinSweeper_HandsOnlyWaitingSpeakingGradesToThePrecompute_AndOnlyOnceEveryFlagIsOn()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await SetUpAsync(h);
        await EnableAudioStageAsync(h);
        await NodeAsync(h, RemoteJobKinds.MediaSpeakingJoin);
        foreach (var session in new[] { "session-wait", "session-lease", "session-retry", "session-done", "session-other", "session-old", "session-exam" })
        {
            await TwoClipSessionAsync(h, session);
        }

        await AddGradeOperationAsync(h, "op-1", "session-wait", state: 0);
        await AddGradeOperationAsync(h, "op-2", "session-lease", state: 1);
        await AddGradeOperationAsync(h, "op-3", "session-retry", state: 4);
        await AddGradeOperationAsync(h, "op-4", "session-done", state: 3);               // Completed: nothing is waiting
        await AddGradeOperationAsync(h, "op-5", "session-other", feature: "writing.grade"); // another feature
        await AddGradeOperationAsync(h, "op-6", "session-old", age: "3 hours");           // far too old to still be waiting
        await AddGradeOperationAsync(h, "op-7", "session-exam", resourceType: "speaking_exam");
        await using var provider = SweeperServices(h);
        var sweeper = new RemoteSpeakingJoinSweeper(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<RemoteSpeakingJoinSweeper>.Instance);

        h.Flags.Set(RemoteJobFlagKeys.Master); // the kind flag is off: the pass costs nothing and enqueues nothing
        Assert.Equal(0, await sweeper.RunOnceAsync(CancellationToken.None));
        Assert.Equal(0, await JobCountAsync(h));

        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.MediaSpeakingJoin);
        Assert.Equal(3, await sweeper.RunOnceAsync(CancellationToken.None));
        Assert.Equal(
            new[] { "session-lease", "session-retry", "session-wait" },
            (await h.ScalarAsync<string>("""SELECT string_agg("ResourceId", ',' ORDER BY "ResourceId") FROM "RemoteJobs";"""))!.Split(','));

        // the next pass finds them already handled
        Assert.Equal(0, await sweeper.RunOnceAsync(CancellationToken.None));
        Assert.Equal(3, await JobCountAsync(h));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // the reaper: expiry of outputs
    // ═════════════════════════════════════════════════════════════════════════

    private static async Task<string> FinishedJobWithOutputAsync(RemotePgHarness h, string resource, string kind, string age, string name, byte[] bytes)
    {
        var job = (await h.EnqueueAsync(resourceId: resource, kind: kind)).JobId;
        await h.SqlAsync(
            $"""
            UPDATE "RemoteJobs" SET "State" = 'Succeeded', "ApplyOutcome" = 'Applied', "SettledFence" = 1, "FenceToken" = 1,
                "CompletedAt" = clock_timestamp() - interval '{age}', "UpdatedAt" = clock_timestamp() - interval '{age}'
            WHERE "Id" = @id;
            """,
            ("id", job));
        var key = RemoteInputOutputService.OutputKey(job, 1, name);
        await h.Storage.WriteAsync(key, new MemoryStream(bytes), CancellationToken.None);
        await h.SqlAsync(
            """
            INSERT INTO "RemoteJobOutputs" ("JobId", "Fence", "Name", "Sha256", "SizeBytes", "StorageKey", "CreatedAt")
            VALUES (@job, 1, @name, @sha, @size, @key, clock_timestamp());
            """,
            ("job", job), ("name", name), ("sha", RemoteIds.Sha256Hex(bytes)), ("size", (long)bytes.Length), ("key", key));
        return key;
    }

    [PostgreSqlFact]
    public async Task Reaper_ExpiresASpeakingJoinAfterItsShortTtl_AndOtherOutputsOnlyWithTheJobRow()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await SetUpAsync(h);
        var staleJoin = await FinishedJobWithOutputAsync(h, "session-old", RemoteJobKinds.MediaSpeakingJoin, "30 hours", "join.mp3", Filler(50, 1));
        var freshJoin = await FinishedJobWithOutputAsync(h, "session-new", RemoteJobKinds.MediaSpeakingJoin, "2 hours", "join.mp3", Filler(50, 2));
        var recentChunk = await FinishedJobWithOutputAsync(h, "rec-recent", RemoteJobKinds.MediaAudioExtract, "2 days", "chunk-0000.mp3", Filler(50, 3));
        var oldChunk = await FinishedJobWithOutputAsync(h, "rec-old", RemoteJobKinds.MediaAudioExtract, "40 days", "chunk-0000.mp3", Filler(50, 4));

        await using var db = h.NewContext();
        var expired = await h.Sweeper(db).SweepExpiredOutputsAsync(h.Settings.Current, CancellationToken.None);

        Assert.Equal(2, expired);
        Assert.False(await h.Storage.ExistsAsync(staleJoin, CancellationToken.None));
        Assert.True(await h.Storage.ExistsAsync(freshJoin, CancellationToken.None));
        Assert.True(await h.Storage.ExistsAsync(recentChunk, CancellationToken.None));
        Assert.False(await h.Storage.ExistsAsync(oldChunk, CancellationToken.None));
        Assert.Equal(2, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobOutputs";"""));
    }

    [PostgreSqlFact]
    public async Task Reaper_TheTtlIsTheConfiguredOne_AndRunsInTheFullSweepBeforeThePurge()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await SetUpAsync(h);
        h.Options.SpeakingJoinOutputTtlHours = 1;
        var oneHourOld = await FinishedJobWithOutputAsync(h, "session-a", RemoteJobKinds.MediaSpeakingJoin, "2 hours", "join.mp3", Filler(50, 1));
        var veryOld = await FinishedJobWithOutputAsync(h, "rec-ancient", RemoteJobKinds.MediaAudioExtract, "45 days", "chunk-0000.mp3", Filler(50, 2));

        await using var db = h.NewContext();
        var result = await h.Sweeper(db).SweepAsync(CancellationToken.None);

        Assert.Equal(2, result.ExpiredOutputSets);
        Assert.False(await h.Storage.ExistsAsync(oneHourOld, CancellationToken.None));
        Assert.False(await h.Storage.ExistsAsync(veryOld, CancellationToken.None));
        // the 45-day-old job row was purged in the SAME pass, but only after its outputs were gone
        Assert.Equal(1, result.Purged);
        Assert.Equal(0, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobOutputs";"""));
    }

    [PostgreSqlFact]
    public async Task Reaper_NeverPurgesAJobRowWhoseOutputsAreStillThere_SoNoObjectIsLeftWithoutARowThatNamesIt()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await SetUpAsync(h);
        var key = await FinishedJobWithOutputAsync(h, "rec-ancient", RemoteJobKinds.MediaAudioExtract, "45 days", "chunk-0000.mp3", Filler(50, 2));

        await using (var db = h.NewContext())
        {
            var (purged, _) = await h.Sweeper(db).PurgeAsync(h.Settings.Current, CancellationToken.None);
            Assert.Equal(0, purged);
        }

        Assert.True(await h.Storage.ExistsAsync(key, CancellationToken.None));
        Assert.Equal(1, await JobCountAsync(h));

        // once the outputs are expired, the row goes
        await using (var db = h.NewContext())
        {
            await h.Sweeper(db).SweepExpiredOutputsAsync(h.Settings.Current, CancellationToken.None);
            var (purged, _) = await h.Sweeper(db).PurgeAsync(h.Settings.Current, CancellationToken.None);
            Assert.Equal(1, purged);
        }

        Assert.False(await h.Storage.ExistsAsync(key, CancellationToken.None));
        Assert.Equal(0, await JobCountAsync(h));
    }

    [PostgreSqlFact]
    public async Task Reaper_AFailedStorageDelete_KeepsTheRowSoTheNextPassRetries()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await SetUpAsync(h);
        var key = await FinishedJobWithOutputAsync(h, "session-a", RemoteJobKinds.MediaSpeakingJoin, "30 hours", "join.mp3", Filler(50, 1));
        var failing = new PrefixDeleteFailingStorage(h.Storage);

        await using (var db = h.NewContext())
        {
            var sweeper = new RemoteJobSweeper(db, h.Flags, failing, h.Settings, h.Time, NullLogger<RemoteJobSweeper>.Instance);
            await sweeper.SweepExpiredOutputsAsync(h.Settings.Current, CancellationToken.None);
        }

        Assert.True(await h.Storage.ExistsAsync(key, CancellationToken.None));
        Assert.Equal(1, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobOutputs";"""));

        await using (var db = h.NewContext())
        {
            await h.Sweeper(db).SweepExpiredOutputsAsync(h.Settings.Current, CancellationToken.None);
        }

        Assert.False(await h.Storage.ExistsAsync(key, CancellationToken.None));
        Assert.Equal(0, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobOutputs";"""));
    }

    /// <summary>A storage whose prefix delete always fails with an I/O error (everything else passes through to the real double).</summary>
    private sealed class PrefixDeleteFailingStorage(IFileStorage inner) : IFileStorage
    {
        public Task<long> WriteAsync(string key, Stream source, CancellationToken ct) => inner.WriteAsync(key, source, ct);

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => inner.OpenReadAsync(key, ct);

        public Task<Stream> OpenWriteAsync(string key, CancellationToken ct) => inner.OpenWriteAsync(key, ct);

        public Task<bool> ExistsAsync(string key, CancellationToken ct) => inner.ExistsAsync(key, ct);

        public Task<bool> DeleteAsync(string key, CancellationToken ct) => inner.DeleteAsync(key, ct);

        public Task<long> LengthAsync(string key, CancellationToken ct) => inner.LengthAsync(key, ct);

        public Task MoveAsync(string sourceKey, string destKey, bool overwrite, CancellationToken ct) => inner.MoveAsync(sourceKey, destKey, overwrite, ct);

        public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct) => throw new IOException("object store unavailable");

        public string? TryResolveLocalPath(string key) => inner.TryResolveLocalPath(key);

        public Uri? ResolveReadUrl(string key, TimeSpan ttl) => inner.ResolveReadUrl(key, ttl);
    }
}
