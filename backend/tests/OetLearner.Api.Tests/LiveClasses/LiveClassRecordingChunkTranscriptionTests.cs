using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.LiveClasses;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Tests.RemoteJobs;

namespace OetLearner.Api.Tests.LiveClasses;

/// <summary>
/// Live Class recordings larger than the 24 MiB single-call cap (OET-RWP/1 section 6.3): once a helper has extracted the audio into mp3
/// chunks, the transcribe stage makes ONE gateway call per chunk, saves each transcript as it exists, and deletes the chunk audio when
/// the recording is done. Small recordings and the flag-off behaviour are untouched; with no remote path an oversize recording fails
/// exactly as it always did.
/// </summary>
public sealed class LiveClassRecordingChunkTranscriptionTests
{
    private const string VideoKey = "live-class-recordings/2027/01/session-1/video.mp4";
    private const long OversizeBytes = 30L * 1024 * 1024;

    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static LiveClassRecordingProcessingService Service(
        LearnerDbContext db,
        IAiGatewayService gateway,
        IFileStorage storage,
        IRemoteAudioExtraction? remote = null,
        TimeProvider? time = null,
        bool aiEnabled = true,
        TimeSpan? budget = null)
        => new(
            db,
            gateway,
            storage,
            TestRuntimeSettingsProvider.WithLiveClassAi(enabled: aiEnabled),
            time ?? TimeProvider.System,
            NullLogger<LiveClassRecordingProcessingService>.Instance,
            remoteAudio: remote)
        {
            ChunkRunBudget = budget ?? TimeSpan.FromMinutes(12),
            RemoteExtractionPollDelay = TimeSpan.FromSeconds(10),
        };

    private static async Task SeedRecordingAsync(
        LearnerDbContext db,
        ChunkStorage storage,
        string? transcript = null,
        string? chunksJson = null,
        bool oversize = true)
    {
        storage.Put(VideoKey, [1, 2, 3, 4]);
        if (oversize) storage.VirtualLengths[VideoKey] = OversizeBytes;
        db.LiveClassRecordings.Add(new LiveClassRecording
        {
            Id = "rec-1",
            ClassSessionId = "session-1",
            S3VideoKey = VideoKey,
            TranscriptText = transcript,
            AudioChunksJson = chunksJson,
            DurationSeconds = 3600,
            RecordedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Puts <paramref name="count"/> chunks in storage (chunk i is made of the byte i) and returns the manifest that describes them.</summary>
    private static LiveClassAudioManifest SeedChunks(ChunkStorage storage, int count)
    {
        var manifest = new LiveClassAudioManifest
        {
            JobId = "rj_test",
            Fence = 1,
            InputSha256 = new string('c', 64),
            DurationMs = count * 600_000L,
            SegmentSeconds = 600,
        };
        for (var i = 0; i < count; i++)
        {
            var bytes = Enumerable.Repeat((byte)i, 100 + i).ToArray();
            var name = $"chunk-{i:D4}.mp3";
            var key = $"remote-jobs/rj_test/1/{name}";
            storage.Put(key, bytes);
            manifest.Chunks.Add(new LiveClassAudioChunk
            {
                Index = i,
                Name = name,
                StorageKey = key,
                StartMs = i * 600_000L,
                DurationMs = 600_000,
                SizeBytes = bytes.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            });
        }

        return manifest;
    }

    /// <summary>A gateway that answers <c>T{n}</c> for the chunk made of the byte n.</summary>
    private static ScriptedGateway ChunkGateway(Action<AiGatewayRequest>? onCall = null)
        => new(request =>
        {
            onCall?.Invoke(request);
            var data = request.AudioAttachments!.Single().Data;
            return Task.FromResult("T" + data[0]);
        });

    private static LiveClassAudioManifest Manifest(LiveClassRecording recording)
        => LiveClassAudioManifest.TryParse(recording.AudioChunksJson)!;

    // ── the chunk path ───────────────────────────────────────────────────────

    [Fact]
    public async Task Transcribe_WithAManifest_MakesOneGatewayCallPerChunk_InOrder_AndJoinsTheTranscripts()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        var chunks = SeedChunks(storage, 3);
        await SeedRecordingAsync(db, storage, chunksJson: chunks.ToJson());
        var gateway = ChunkGateway();
        var remote = new FakeRemoteAudio(AudioExtractPlan.UseLocal);

        await Service(db, gateway, storage, remote).ProcessTranscribeAsync("rec-1", CancellationToken.None);

        Assert.Equal(3, gateway.Requests.Count);
        Assert.All(gateway.Requests, request =>
        {
            Assert.Equal(AiFeatureCodes.ClassRecordingTranscribe, request.FeatureCode);
            Assert.Null(request.UserId);
            Assert.Equal(0.0, request.Temperature);
            Assert.Equal("audio/mpeg", request.AudioAttachments!.Single().MimeType);
        });
        Assert.Equal(new[] { 0, 1, 2 }, gateway.Requests.Select(r => (int)r.AudioAttachments!.Single().Data[0]).ToArray());
        Assert.Contains("part 1 of 3", gateway.Requests[0].UserInput, StringComparison.Ordinal);
        Assert.Contains("part 3 of 3", gateway.Requests[2].UserInput, StringComparison.Ordinal);

        var recording = await db.LiveClassRecordings.SingleAsync();
        Assert.Equal("T0\nT1\nT2", recording.TranscriptText);
        Assert.Single(await db.BackgroundJobs.Where(j => j.Type == JobType.LiveClassRecordingSummarize).ToListAsync());

        // the stored recording was never opened (it is far over the cap) and the remote path was never consulted
        Assert.DoesNotContain(VideoKey, storage.MetadataReads);
        Assert.Equal(0, remote.Calls);
    }

    [Fact]
    public async Task Transcribe_DeletesTheChunkAudioWhenDone_ButKeepsEveryTranscript()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        var chunks = SeedChunks(storage, 2);
        await SeedRecordingAsync(db, storage, chunksJson: chunks.ToJson());

        await Service(db, ChunkGateway(), storage).ProcessTranscribeAsync("rec-1", CancellationToken.None);

        Assert.All(chunks.Chunks, chunk => Assert.False(storage.Has(chunk.StorageKey)));
        var manifest = Manifest(await db.LiveClassRecordings.SingleAsync());
        Assert.True(manifest.AudioDeleted);
        Assert.Equal(new[] { "T0", "T1" }, manifest.Chunks.Select(c => c.Transcript).ToArray());
    }

    [Fact]
    public async Task Transcribe_AFailedDeleteNeverFailsTheStage_AndTheTranscriptIsAlreadySaved()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage { DeleteFailure = new IOException("disk gone") };
        var chunks = SeedChunks(storage, 2);
        await SeedRecordingAsync(db, storage, chunksJson: chunks.ToJson());

        await Service(db, ChunkGateway(), storage).ProcessTranscribeAsync("rec-1", CancellationToken.None);

        var recording = await db.LiveClassRecordings.SingleAsync();
        Assert.Equal("T0\nT1", recording.TranscriptText);
        Assert.False(Manifest(recording).AudioDeleted); // the retention sweep removes what is left
        Assert.Single(await db.BackgroundJobs.Where(j => j.Type == JobType.LiveClassRecordingSummarize).ToListAsync());
    }

    [Fact]
    public async Task Transcribe_AFailureInTheMiddle_KeepsTheTranscribedChunks_AndTheRetryPaysOnlyForTheRest()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        var chunks = SeedChunks(storage, 3);
        await SeedRecordingAsync(db, storage, chunksJson: chunks.ToJson());
        var failOnce = true;
        var gateway = ChunkGateway(request =>
        {
            if (request.AudioAttachments!.Single().Data[0] == 2 && failOnce)
            {
                failOnce = false;
                throw new InvalidOperationException("provider hiccup");
            }
        });
        var service = Service(db, gateway, storage);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProcessTranscribeAsync("rec-1", CancellationToken.None));

        var afterFailure = Manifest(await db.LiveClassRecordings.SingleAsync());
        Assert.Equal(new[] { "T0", "T1", null }, afterFailure.Chunks.Select(c => c.Transcript).ToArray());
        Assert.Equal(3, gateway.Requests.Count);

        await service.ProcessTranscribeAsync("rec-1", CancellationToken.None);

        // only the chunk that had no transcript was sent again
        Assert.Equal(4, gateway.Requests.Count);
        Assert.Equal(2, gateway.Requests[3].AudioAttachments!.Single().Data[0]);
        Assert.Equal("T0\nT1\nT2", (await db.LiveClassRecordings.SingleAsync()).TranscriptText);
    }

    [Fact]
    public async Task Transcribe_ALongRecording_ContinuesInANewJob_InsteadOfRunningIntoTheExecutionCeiling()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        var chunks = SeedChunks(storage, 2);
        await SeedRecordingAsync(db, storage, chunksJson: chunks.ToJson());
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var gateway = ChunkGateway(_ => clock.Advance(TimeSpan.FromMinutes(13))); // each call "takes" 13 minutes
        var service = Service(db, gateway, storage, time: clock, budget: TimeSpan.FromMinutes(12));

        await service.ProcessTranscribeAsync("rec-1", CancellationToken.None);

        // one chunk done, the budget is spent: a continuation is queued and the next stage waits for the real transcript
        Assert.Single(gateway.Requests);
        var recording = await db.LiveClassRecordings.SingleAsync();
        Assert.Null(recording.TranscriptText);
        Assert.Equal(new[] { "T0", null }, Manifest(recording).Chunks.Select(c => c.Transcript).ToArray());
        var continuation = Assert.Single(await db.BackgroundJobs.Where(j => j.Type == JobType.LiveClassRecordingTranscribe).ToListAsync());
        Assert.Equal("rec-1", continuation.ResourceId);
        Assert.Equal(AsyncState.Queued, continuation.State);
        Assert.Empty(await db.BackgroundJobs.Where(j => j.Type == JobType.LiveClassRecordingSummarize).ToListAsync());

        // the continuation finishes the rest
        await service.ProcessTranscribeAsync("rec-1", CancellationToken.None);

        Assert.Equal(2, gateway.Requests.Count);
        Assert.Equal("T0\nT1", (await db.LiveClassRecordings.SingleAsync()).TranscriptText);
        Assert.Single(await db.BackgroundJobs.Where(j => j.Type == JobType.LiveClassRecordingSummarize).ToListAsync());
    }

    /// <summary>A service built the way DI builds it: no explicit run budget, only the (optional) remote-jobs options.</summary>
    private static LiveClassRecordingProcessingService ServiceWithOptions(
        LearnerDbContext db,
        IAiGatewayService gateway,
        IFileStorage storage,
        TimeProvider time,
        OetLearner.Api.Configuration.RemoteJobsOptions? options)
        => new(
            db,
            gateway,
            storage,
            TestRuntimeSettingsProvider.WithLiveClassAi(enabled: true),
            time,
            NullLogger<LiveClassRecordingProcessingService>.Instance,
            remoteJobsOptions: options is null ? null : Microsoft.Extensions.Options.Options.Create(options));

    [Fact]
    public async Task Transcribe_TheRunBudgetIsAFewMinutesNotTwelve_AndFollowsTheConfiguredOption()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        var gateway = ChunkGateway();

        Assert.Equal(TimeSpan.FromMinutes(4), ServiceWithOptions(db, gateway, storage, TimeProvider.System, null).ChunkRunBudget);
        Assert.Equal(TimeSpan.FromMinutes(4), ServiceWithOptions(db, gateway, storage, TimeProvider.System, new OetLearner.Api.Configuration.RemoteJobsOptions()).ChunkRunBudget);
        Assert.Equal(
            TimeSpan.FromMinutes(7),
            ServiceWithOptions(db, gateway, storage, TimeProvider.System, new OetLearner.Api.Configuration.RemoteJobsOptions { LiveClassChunkRunBudgetMinutes = 7 }).ChunkRunBudget);

        // out-of-range values are clamped: never zero, never close to the background processor's 20-minute execution ceiling
        Assert.Equal(
            TimeSpan.FromMinutes(1),
            ServiceWithOptions(db, gateway, storage, TimeProvider.System, new OetLearner.Api.Configuration.RemoteJobsOptions { LiveClassChunkRunBudgetMinutes = 0 }).ChunkRunBudget);
        Assert.Equal(
            TimeSpan.FromMinutes(15),
            ServiceWithOptions(db, gateway, storage, TimeProvider.System, new OetLearner.Api.Configuration.RemoteJobsOptions { LiveClassChunkRunBudgetMinutes = 500 }).ChunkRunBudget);
    }

    [Fact]
    public async Task Transcribe_WithTheDefaultBudget_HandsTheRestToAContinuationAfterTheFirstFiveMinuteCall()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        var chunks = SeedChunks(storage, 3);
        await SeedRecordingAsync(db, storage, chunksJson: chunks.ToJson());
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var gateway = ChunkGateway(_ => clock.Advance(TimeSpan.FromMinutes(5))); // each call "takes" five minutes

        await ServiceWithOptions(db, gateway, storage, clock, null).ProcessTranscribeAsync("rec-1", CancellationToken.None);

        // the former 12-minute budget would have transcribed all three chunks (holding the single-threaded processor for 15 minutes)
        Assert.Single(gateway.Requests);
        Assert.Equal(new[] { "T0", null, null }, Manifest(await db.LiveClassRecordings.SingleAsync()).Chunks.Select(c => c.Transcript).ToArray());
        Assert.Single(await db.BackgroundJobs.Where(j => j.Type == JobType.LiveClassRecordingTranscribe).ToListAsync());
        Assert.Empty(await db.BackgroundJobs.Where(j => j.Type == JobType.LiveClassRecordingSummarize).ToListAsync());
    }

    [Fact]
    public async Task Transcribe_ASilentChunk_IsKeptAsEmptyText_AndIsNotTranscribedTwice()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        var chunks = SeedChunks(storage, 2);
        await SeedRecordingAsync(db, storage, chunksJson: chunks.ToJson());
        var gateway = new ScriptedGateway(request => Task.FromResult(request.AudioAttachments!.Single().Data[0] == 0 ? "   " : "spoken"));

        await Service(db, gateway, storage).ProcessTranscribeAsync("rec-1", CancellationToken.None);

        Assert.Equal(2, gateway.Requests.Count);
        var recording = await db.LiveClassRecordings.SingleAsync();
        Assert.Equal("spoken", recording.TranscriptText); // the empty chunk adds nothing but is still done
        Assert.Equal(new[] { "", "spoken" }, Manifest(recording).Chunks.Select(c => c.Transcript).ToArray());
    }

    [Fact]
    public async Task Transcribe_EveryChunkSilent_IsTheSamePlaceholderTheSingleCallPathWrites()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        var chunks = SeedChunks(storage, 1);
        await SeedRecordingAsync(db, storage, chunksJson: chunks.ToJson());
        var gateway = new ScriptedGateway(_ => Task.FromResult(string.Empty));

        await Service(db, gateway, storage).ProcessTranscribeAsync("rec-1", CancellationToken.None);

        Assert.Equal("[Transcript empty — Whisper returned no text]", (await db.LiveClassRecordings.SingleAsync()).TranscriptText);
    }

    // ── chunks that can no longer be trusted ─────────────────────────────────

    [Fact]
    public async Task Transcribe_AChunkThatVanished_DropsTheManifest_AndAsksForAFreshExtraction()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        var chunks = SeedChunks(storage, 2);
        storage.Remove(chunks.Chunks[1].StorageKey);
        await SeedRecordingAsync(db, storage, chunksJson: chunks.ToJson());
        var remote = new FakeRemoteAudio(new AudioExtractPlan(AudioExtractAction.Pending, "queued", "rj_new"));

        await Service(db, ChunkGateway(), storage, remote).ProcessTranscribeAsync("rec-1", CancellationToken.None);

        var recording = await db.LiveClassRecordings.SingleAsync();
        Assert.Null(recording.AudioChunksJson);
        Assert.Null(recording.TranscriptText);
        Assert.Equal(1, remote.Calls);
        Assert.Equal(("rec-1", VideoKey, OversizeBytes), remote.Last);
        Assert.Single(await db.BackgroundJobs.Where(j => j.Type == JobType.LiveClassRecordingTranscribe).ToListAsync());
    }

    [Fact]
    public async Task Transcribe_AChunkWhoseBytesChanged_IsNeverSentToTheModel()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        var chunks = SeedChunks(storage, 2);
        storage.Put(chunks.Chunks[0].StorageKey, Enumerable.Repeat((byte)99, (int)chunks.Chunks[0].SizeBytes).ToArray()); // same size, other bytes
        await SeedRecordingAsync(db, storage, chunksJson: chunks.ToJson());
        var gateway = ChunkGateway();
        var remote = new FakeRemoteAudio(new AudioExtractPlan(AudioExtractAction.Pending, "queued", "rj_new"));

        await Service(db, gateway, storage, remote).ProcessTranscribeAsync("rec-1", CancellationToken.None);

        Assert.Empty(gateway.Requests);
        Assert.Null((await db.LiveClassRecordings.SingleAsync()).AudioChunksJson);
        Assert.Equal(1, remote.Calls);
    }

    [Fact]
    public async Task Transcribe_AChunkWhoseSizeChanged_IsNeverSentToTheModel()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        var chunks = SeedChunks(storage, 1);
        storage.Put(chunks.Chunks[0].StorageKey, new byte[] { 1, 2, 3 });
        await SeedRecordingAsync(db, storage, chunksJson: chunks.ToJson());
        var gateway = ChunkGateway();

        // no remote path: the oversize recording then fails exactly as it always did
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db, gateway, storage).ProcessTranscribeAsync("rec-1", CancellationToken.None));

        Assert.Contains("exceeds", failure.Message, StringComparison.Ordinal);
        Assert.Empty(gateway.Requests);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("{\"schema\":\"something-else/1\",\"chunks\":[]}")]
    [InlineData("[]")]
    public async Task Transcribe_AnUnreadableManifest_IsIgnoredAndDropped_ASmallRecordingIsTranscribedAsBefore(string damaged)
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        await SeedRecordingAsync(db, storage, chunksJson: damaged, oversize: false);
        var gateway = new ScriptedGateway(_ => Task.FromResult("whole recording"));

        await Service(db, gateway, storage).ProcessTranscribeAsync("rec-1", CancellationToken.None);

        var recording = await db.LiveClassRecordings.SingleAsync();
        Assert.Equal("whole recording", recording.TranscriptText);
        Assert.Null(recording.AudioChunksJson);
        Assert.Single(gateway.Requests);
    }

    [Fact]
    public async Task Transcribe_AManifestNeverLetsTheStageReadAKeyOutsideRemoteJobs()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        var chunks = SeedChunks(storage, 1);
        chunks.Chunks[0].StorageKey = "learner/private/secret.mp3"; // a forged key: not a manifest at all
        storage.Put("learner/private/secret.mp3", [5, 5, 5]);
        await SeedRecordingAsync(db, storage, chunksJson: chunks.ToJson(), oversize: false);
        var gateway = new ScriptedGateway(request => Task.FromResult(request.AudioAttachments!.Single().Data.Length == 4 ? "recording" : "LEAK"));

        await Service(db, gateway, storage).ProcessTranscribeAsync("rec-1", CancellationToken.None);

        Assert.Equal("recording", (await db.LiveClassRecordings.SingleAsync()).TranscriptText);
        Assert.DoesNotContain("learner/private/secret.mp3", storage.MetadataReads);
    }

    // ── oversize recordings without a manifest ───────────────────────────────

    [Fact]
    public async Task Transcribe_OversizeWithNoRemotePath_FailsExactlyAsBefore()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        await SeedRecordingAsync(db, storage);
        var gateway = ChunkGateway();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db, gateway, storage).ProcessTranscribeAsync("rec-1", CancellationToken.None));

        Assert.Equal(
            $"Recording rec-1 is {OversizeBytes} bytes, which exceeds the {24L * 1024 * 1024} byte transcription upload limit.",
            failure.Message);
        Assert.Empty(gateway.Requests);
        Assert.Empty(await db.BackgroundJobs.ToListAsync());
    }

    [Fact]
    public async Task Transcribe_OversizeWhenTheRemotePathHasNothingToOffer_FailsExactlyAsBefore()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        await SeedRecordingAsync(db, storage);
        var remote = new FakeRemoteAudio(AudioExtractPlan.UseLocal);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db, ChunkGateway(), storage, remote).ProcessTranscribeAsync("rec-1", CancellationToken.None));

        Assert.Contains("exceeds the", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, remote.Calls);
        Assert.Equal(("rec-1", VideoKey, OversizeBytes), remote.Last);
        Assert.Empty(await db.BackgroundJobs.ToListAsync());
    }

    [Fact]
    public async Task Transcribe_OversizeWhileAHelperExtracts_WaitsInAContinuationJob_AndNeverBlocksOrFails()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        await SeedRecordingAsync(db, storage);
        var remote = new FakeRemoteAudio(new AudioExtractPlan(AudioExtractAction.Pending, "in progress", "rj_1"));
        var gateway = ChunkGateway();
        var before = DateTimeOffset.UtcNow;

        await Service(db, gateway, storage, remote).ProcessTranscribeAsync("rec-1", CancellationToken.None);

        var job = Assert.Single(await db.BackgroundJobs.ToListAsync());
        Assert.Equal(JobType.LiveClassRecordingTranscribe, job.Type);
        Assert.Equal("rec-1", job.ResourceId);
        Assert.Equal(AsyncState.Queued, job.State);
        Assert.InRange(job.AvailableAt, before + TimeSpan.FromSeconds(9), DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30));
        Assert.Empty(gateway.Requests);
        Assert.Null((await db.LiveClassRecordings.SingleAsync()).TranscriptText);
        Assert.Equal(LiveClassRecordingStatus.Pending, (await db.LiveClassRecordings.SingleAsync()).Status);
    }

    [Fact]
    public async Task Transcribe_WaitingTwice_StillLeavesExactlyOneContinuation()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        await SeedRecordingAsync(db, storage);
        var remote = new FakeRemoteAudio(new AudioExtractPlan(AudioExtractAction.Pending, "in progress", "rj_1"));
        var service = Service(db, ChunkGateway(), storage, remote);

        await service.ProcessTranscribeAsync("rec-1", CancellationToken.None);
        await service.ProcessTranscribeAsync("rec-1", CancellationToken.None);

        Assert.Single(await db.BackgroundJobs.ToListAsync());
        Assert.Equal(2, remote.Calls);
    }

    [Fact]
    public async Task Transcribe_OversizeWhenTheHelperFailedForGood_FailsWithTheReason()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        await SeedRecordingAsync(db, storage);
        var remote = new FakeRemoteAudio(new AudioExtractPlan(AudioExtractAction.Failed, "remote audio extraction failed (no_audio_stream); an admin must requeue it", "rj_1"));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db, ChunkGateway(), storage, remote).ProcessTranscribeAsync("rec-1", CancellationToken.None));

        Assert.Contains("no_audio_stream", failure.Message, StringComparison.Ordinal);
        Assert.Empty(await db.BackgroundJobs.ToListAsync());
    }

    // ── unchanged behaviour ──────────────────────────────────────────────────

    [Fact]
    public async Task Transcribe_ASmallRecording_NeverConsultsTheRemotePath()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        await SeedRecordingAsync(db, storage, oversize: false);
        var remote = new FakeRemoteAudio(new AudioExtractPlan(AudioExtractAction.Pending, "x", "rj"));
        var gateway = new ScriptedGateway(_ => Task.FromResult("whole recording"));

        await Service(db, gateway, storage, remote).ProcessTranscribeAsync("rec-1", CancellationToken.None);

        Assert.Equal(0, remote.Calls);
        Assert.Single(gateway.Requests);
        Assert.Equal("whole recording", (await db.LiveClassRecordings.SingleAsync()).TranscriptText);
        Assert.Single(await db.BackgroundJobs.Where(j => j.Type == JobType.LiveClassRecordingSummarize).ToListAsync());
    }

    [Fact]
    public async Task Transcribe_WithTheAiFlagOff_DoesNothing_NotEvenForAnOversizeRecording()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        var chunks = SeedChunks(storage, 2);
        await SeedRecordingAsync(db, storage, chunksJson: chunks.ToJson());
        var gateway = ChunkGateway();
        var remote = new FakeRemoteAudio(new AudioExtractPlan(AudioExtractAction.Pending, "x", "rj"));

        await Service(db, gateway, storage, remote, aiEnabled: false).ProcessTranscribeAsync("rec-1", CancellationToken.None);

        Assert.Empty(gateway.Requests);
        Assert.Equal(0, remote.Calls);
        Assert.All(chunks.Chunks, chunk => Assert.True(storage.Has(chunk.StorageKey)));
        Assert.Null((await db.LiveClassRecordings.SingleAsync()).TranscriptText);
        Assert.Empty(await db.BackgroundJobs.ToListAsync());
    }

    [Fact]
    public async Task Transcribe_ARecordingThatAlreadyHasATranscript_NeverTouchesTheChunksOrTheGateway()
    {
        await using var db = NewDb();
        var storage = new ChunkStorage();
        var chunks = SeedChunks(storage, 2);
        await SeedRecordingAsync(db, storage, transcript: "Zoom already transcribed this class.", chunksJson: chunks.ToJson());
        var gateway = ChunkGateway();

        await Service(db, gateway, storage).ProcessTranscribeAsync("rec-1", CancellationToken.None);

        Assert.Empty(gateway.Requests);
        Assert.Equal("Zoom already transcribed this class.", (await db.LiveClassRecordings.SingleAsync()).TranscriptText);
        Assert.Single(await db.BackgroundJobs.Where(j => j.Type == JobType.LiveClassRecordingSummarize).ToListAsync());
    }

    // ── the manifest itself ──────────────────────────────────────────────────

    [Fact]
    public void Manifest_RoundTrips_AndIsCamelCaseJson()
    {
        var storage = new ChunkStorage();
        var manifest = SeedChunks(storage, 2);
        manifest.Chunks[1].Transcript = "second";

        var json = manifest.ToJson();
        var parsed = LiveClassAudioManifest.TryParse(json);

        Assert.Contains("\"schema\":\"live-class-audio-chunks/1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"storageKey\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("isTranscribed", json, StringComparison.Ordinal); // derived, never stored
        Assert.NotNull(parsed);
        Assert.Equal(2, parsed!.Chunks.Count);
        Assert.Equal("second", parsed.Chunks[1].Transcript);
        Assert.Null(parsed.Chunks[0].Transcript);
        Assert.False(parsed.IsTranscribed);
        parsed.Chunks[0].Transcript = "first";
        Assert.True(parsed.IsTranscribed);
        Assert.Equal("first\nsecond", parsed.JoinTranscripts());
    }

    [Fact]
    public void Manifest_TryParse_RejectsAnythingThatIsNotAWellFormedManifest()
    {
        var storage = new ChunkStorage();
        LiveClassAudioManifest Good() => SeedChunks(storage, 2);

        Assert.Null(LiveClassAudioManifest.TryParse(null));
        Assert.Null(LiveClassAudioManifest.TryParse("   "));
        Assert.Null(LiveClassAudioManifest.TryParse("{"));

        var wrongSchema = Good();
        wrongSchema.Schema = "other/1";
        Assert.Null(LiveClassAudioManifest.TryParse(wrongSchema.ToJson()));

        var noChunks = Good();
        noChunks.Chunks.Clear();
        Assert.Null(LiveClassAudioManifest.TryParse(noChunks.ToJson()));

        var gap = Good();
        gap.Chunks[1].Index = 5;
        Assert.Null(LiveClassAudioManifest.TryParse(gap.ToJson()));

        var outside = Good();
        outside.Chunks[0].StorageKey = "media/learner/voice.mp3";
        Assert.Null(LiveClassAudioManifest.TryParse(outside.ToJson()));

        var traversal = Good();
        traversal.Chunks[0].StorageKey = "remote-jobs/../media/learner/voice.mp3";
        Assert.Null(LiveClassAudioManifest.TryParse(traversal.ToJson()));

        var empty = Good();
        empty.Chunks[0].SizeBytes = 0;
        Assert.Null(LiveClassAudioManifest.TryParse(empty.ToJson()));

        var tooMany = Good();
        tooMany.Chunks = Enumerable.Range(0, 65)
            .Select(i => new LiveClassAudioChunk { Index = i, Name = $"chunk-{i:D4}.mp3", StorageKey = $"remote-jobs/j/1/chunk-{i:D4}.mp3", SizeBytes = 1, DurationMs = 1 })
            .ToList();
        Assert.Null(LiveClassAudioManifest.TryParse(tooMany.ToJson()));
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeRemoteAudio(AudioExtractPlan plan) : IRemoteAudioExtraction
    {
        public int Calls { get; private set; }
        public (string RecordingId, string StorageKey, long SizeBytes)? Last { get; private set; }

        public Task<AudioExtractPlan> PlanAsync(string recordingId, string storageKey, long sizeBytes, CancellationToken ct)
        {
            Calls++;
            Last = (recordingId, storageKey, sizeBytes);
            return Task.FromResult(plan);
        }
    }

    private sealed class ScriptedGateway(Func<AiGatewayRequest, Task<string>> complete) : IAiGatewayService
    {
        public List<AiGatewayRequest> Requests { get; } = [];

        public async Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            var completion = await complete(request);
            return new AiGatewayResult
            {
                Completion = completion,
                RulebookVersion = "test",
                Metadata = new AiGroundedPromptMetadata
                {
                    RulebookKind = RuleKind.Grammar,
                    Profession = ExamProfession.Medicine,
                    RulebookVersion = "test",
                },
            };
        }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "OET AI — Rulebook-Grounded System Prompt",
                TaskInstruction = "Test",
                Metadata = new AiGroundedPromptMetadata
                {
                    RulebookKind = context.Kind,
                    Profession = context.Profession,
                    RulebookVersion = "test",
                },
            };
    }

    /// <summary>Storage that can pretend one object is far larger than the bytes it holds, and records what the stage opened.</summary>
    private sealed class ChunkStorage : IFileStorage
    {
        private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);

        public Dictionary<string, long> VirtualLengths { get; } = new(StringComparer.Ordinal);
        public List<string> MetadataReads { get; } = [];
        public Exception? DeleteFailure { get; init; }

        public void Put(string key, byte[] bytes) => files[key] = bytes;

        public void Remove(string key) => files.Remove(key);

        public bool Has(string key) => files.ContainsKey(key);

        public Task<FileStorageReadResult> OpenReadWithMetadataAsync(string key, CancellationToken ct)
        {
            MetadataReads.Add(key);
            if (!files.TryGetValue(key, out var bytes)) throw new FileNotFoundException("missing", key);
            var length = VirtualLengths.TryGetValue(key, out var virtualLength) ? virtualLength : bytes.Length;
            return Task.FromResult(new FileStorageReadResult(new MemoryStream(bytes, writable: false), length));
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
        {
            if (!files.TryGetValue(key, out var bytes)) throw new FileNotFoundException("missing", key);
            return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }

        public async Task<long> WriteAsync(string key, Stream source, CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, ct);
            files[key] = buffer.ToArray();
            return files[key].LongLength;
        }

        public Task<Stream> OpenWriteAsync(string key, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> ExistsAsync(string key, CancellationToken ct) => Task.FromResult(files.ContainsKey(key));

        public Task<bool> DeleteAsync(string key, CancellationToken ct)
        {
            if (DeleteFailure is { } failure) throw failure;
            return Task.FromResult(files.Remove(key));
        }

        public Task<long> LengthAsync(string key, CancellationToken ct) => Task.FromResult(files[key].LongLength);

        public Task MoveAsync(string sourceKey, string destKey, bool overwrite, CancellationToken ct) => throw new NotSupportedException();

        public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct) => Task.FromResult(0);

        public string? TryResolveLocalPath(string key) => null;

        public Uri? ResolveReadUrl(string key, TimeSpan ttl) => null;
    }
}
