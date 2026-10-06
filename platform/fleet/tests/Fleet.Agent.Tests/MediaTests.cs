using System.Text;
using System.Text.Json;

namespace Fleet.Agent.Tests;

/// <summary>Argument vectors and procedures of media.audio-extract / media.speaking-join (protocol 6.3, 6.4; RW-113, RW-114).</summary>
public sealed class MediaTests
{
    private const string Engine = "ffmpeg:7.1.1/audio-extract:1";

    [Fact]
    public void RW113_the_decode_and_encode_vectors_equal_the_normative_procedure()
    {
        Assert.Equal(
            new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-i", "/scratch/job/media.bin", "-map", "0:a:0", "-vn", "-sn", "-dn", "-ac", "1", "-ar", "16000", "-f", "s16le", "pipe:1" },
            FfmpegArgs.DecodeFileToPcm("/scratch/job/media.bin"));
        Assert.Equal(
            new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-f", "s16le", "-ar", "16000", "-ac", "1", "-i", "pipe:0", "-c:a", "libmp3lame", "-b:a", "48k", "-f", "mp3", "pipe:1" },
            FfmpegArgs.EncodePcmToMp3(48));
    }

    [Fact]
    public void RW114_the_speaking_decode_vector_equals_the_one_the_api_transcoder_uses()
    {
        // FfmpegSpeakingAudioTranscoder: -hide_banner -loglevel error -nostdin -i pipe:0 -vn -ac 1 -ar 16000 -f s16le pipe:1
        Assert.Equal(
            new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-i", "pipe:0", "-vn", "-ac", "1", "-ar", "16000", "-f", "s16le", "pipe:1" },
            FfmpegArgs.DecodeClipFromStdin());
    }

    [Theory]
    [InlineData("ffmpeg version 7.1.1-1~deb13u1 Copyright (c) 2000-2025 the FFmpeg developers", "7.1.1")]
    [InlineData("ffmpeg version 6.0 Copyright (c) 2000-2023", "6.0")]
    [InlineData("ffmpeg version N-12345-gabcdef Copyright", null)]
    [InlineData("", null)]
    public void ffmpeg_versions_are_parsed_into_the_engine_string_or_rejected(string line, string? expected)
    {
        Assert.Equal(expected, FfmpegProbe.ParseVersion(line));
    }

    // ---- audio extract -------------------------------------------------------------------------------------

    private static string Params(int segmentSeconds, int maxDuration = 100) =>
        "{\"sampleRateHz\":16000,\"channels\":1,\"codec\":\"mp3\",\"bitrateKbps\":48,\"segmentSeconds\":" + segmentSeconds
        + ",\"overlapMs\":0,\"maxChunkBytes\":20971520,\"maxDurationSeconds\":" + maxDuration + ",\"argsVersion\":\"v1\"}";

    private static ClaimedJob AudioJob(byte[] media, int segmentSeconds, int maxDuration = 100)
    {
        var job = TestJobs.Pdf(TestIds.Job(1), media, JobKinds.MediaAudioExtract, Params(segmentSeconds, maxDuration), engine: Engine);
        job.Inputs[0].Name = "media";
        job.Limits = new JobLimits { Weight = 2, CpuMilli = 2000, MemMiB = 1024, TmpMiB = 1536, TimeoutSeconds = 900, MaxInputBytes = 805_306_368, MaxResultBytes = 262_144, MaxOutputBytes = 134_217_728, MaxOutputs = 64 };
        return job;
    }

    /// <summary>A scripted ffmpeg: decode emits <paramref name="pcmBytes"/> of PCM; encode returns "MP3" + the input length.</summary>
    private static FakeProcessRunner Ffmpeg(int pcmBytes, int decodeExit = 0, string decodeStderr = "")
    {
        var runner = new FakeProcessRunner();
        runner.Handler = async (spec, ct) =>
        {
            if (spec.Arguments.Contains("libmp3lame"))
            {
                using var input = new MemoryStream();
                await spec.Stdin!.CopyToAsync(input, ct);
                await spec.ConsumeStdout!(new MemoryStream(Encoding.ASCII.GetBytes("MP3:" + input.Length)), ct);
                return new ProcessRunResult(0, false, "");
            }

            var pcm = new byte[pcmBytes];
            for (var i = 0; i < pcm.Length; i++) pcm[i] = (byte)(i % 251);
            if (decodeExit == 0) await spec.ConsumeStdout!(new MemoryStream(pcm), ct);
            return new ProcessRunResult(decodeExit, false, decodeStderr);
        };
        return runner;
    }

    private static async Task<(ExecutionResult Result, FakeIo Io)> RunAudio(FakeProcessRunner runner, ClaimedJob job, byte[] media)
    {
        using var dir = new TempDir();
        var io = new FakeIo();
        io.Inputs["media"] = media;
        var lease = TestJobs.Lease(job, new FakeClock());
        var executor = new AudioExtractExecutor(Engine, runner, "ffmpeg", TestLog.Instance);
        var result = await executor.ExecuteAsync(new JobContext { Job = job, Lease = lease, ScratchDirectory = dir.Path, Io = io }, CancellationToken.None);
        return (result, io);
    }

    [Fact]
    public async Task RW113_windows_are_sample_exact_with_contiguous_start_times_and_exact_durations()
    {
        var media = new byte[] { 1, 2, 3 };
        var job = AudioJob(media, segmentSeconds: 1);

        var (result, io) = await RunAudio(Ffmpeg(pcmBytes: 80_000), job, media); // 2.5 s of 16 kHz mono s16le

        Assert.True(result.Success);
        using var json = JsonDocument.Parse(result.ResultUtf8);
        var root = json.RootElement;
        Assert.Equal("media.audio-extract.result/1", root.GetProperty("schema").GetString());
        Assert.Equal(Engine, root.GetProperty("engineVersion").GetString());
        Assert.Equal(TestIds.Sha(media), root.GetProperty("inputSha256").GetString());
        Assert.Equal(2500, root.GetProperty("durationMs").GetInt64());
        Assert.Equal(3, root.GetProperty("chunkCount").GetInt32());
        var chunks = root.GetProperty("chunks").EnumerateArray().ToArray();
        Assert.Equal(new[] { 0L, 1000L, 2000L }, chunks.Select(c => c.GetProperty("startMs").GetInt64()).ToArray());
        Assert.Equal(new[] { 1000L, 1000L, 500L }, chunks.Select(c => c.GetProperty("durationMs").GetInt64()).ToArray());
        Assert.Equal(new[] { "chunk-0000.mp3", "chunk-0001.mp3", "chunk-0002.mp3" }, chunks.Select(c => c.GetProperty("name").GetString()!).ToArray());
        Assert.Equal(2500, chunks.Sum(c => c.GetProperty("durationMs").GetInt64()));
        Assert.Equal(new[] { "chunk-0000.mp3", "chunk-0001.mp3", "chunk-0002.mp3" }, io.Uploaded.Select(u => u.Name).ToArray());
        Assert.Equal(result.Outputs.Sum(o => o.SizeBytes), root.GetProperty("totalOutputBytes").GetInt64());
        // Each window is encoded independently from exactly its own samples: 32,000 + 32,000 + 16,000 bytes of PCM.
        Assert.Equal(new[] { "MP3:32000", "MP3:32000", "MP3:16000" }, io.Uploaded.Select(u => Encoding.ASCII.GetString(u.Bytes)).ToArray());
    }

    [Fact]
    public async Task RW113_two_runs_produce_identical_chunk_hashes_and_a_stray_odd_byte_is_dropped()
    {
        var media = new byte[] { 1, 2, 3 };
        var job = AudioJob(media, segmentSeconds: 1);

        var first = await RunAudio(Ffmpeg(80_001), job, media);
        var second = await RunAudio(Ffmpeg(80_001), job, media);

        Assert.Equal(first.Result.Outputs.Select(o => o.Sha256).ToArray(), second.Result.Outputs.Select(o => o.Sha256).ToArray());
        Assert.Equal("MP3:16000", Encoding.ASCII.GetString(first.Io.Uploaded[2].Bytes));
    }

    [Fact]
    public async Task each_chunk_is_uploaded_with_the_verified_hash_and_the_local_copy_is_deleted()
    {
        var media = new byte[] { 1, 2, 3 };
        var job = AudioJob(media, segmentSeconds: 1);
        using var dir = new TempDir();
        var io = new FakeIo();
        io.Inputs["media"] = media;
        var executor = new AudioExtractExecutor(Engine, Ffmpeg(40_000), "ffmpeg", TestLog.Instance);

        var result = await executor.ExecuteAsync(new JobContext { Job = job, Lease = TestJobs.Lease(job, new FakeClock()), ScratchDirectory = dir.Path, Io = io }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.Outputs.Count);
        Assert.All(result.Outputs, o => Assert.Equal(TestIds.Sha(io.Uploaded.Single(u => u.Name == o.Name).Bytes), o.Sha256));
        Assert.Empty(Directory.GetFiles(dir.Path, "chunk-*.mp3"));
    }

    [Fact]
    public async Task a_recording_longer_than_the_maximum_is_a_terminal_duration_exceeded_failure()
    {
        var media = new byte[] { 1 };
        var job = AudioJob(media, segmentSeconds: 1, maxDuration: 1);

        var failure = await Assert.ThrowsAsync<JobFailureException>(() => RunAudio(Ffmpeg(80_000), job, media));

        Assert.Equal(FailCodes.DurationExceeded, failure.Code);
        Assert.False(failure.Retryable);
    }

    [Fact]
    public async Task no_audio_stream_is_detected_from_the_decoder_and_is_terminal()
    {
        var media = new byte[] { 1 };
        var job = AudioJob(media, segmentSeconds: 1);

        var (result, _) = await RunAudio(Ffmpeg(0, decodeExit: 1, decodeStderr: "Stream map '0:a:0' matches no streams."), job, media);

        Assert.False(result.Success);
        Assert.Equal(FailCodes.NoAudioStream, result.FailCode);
        Assert.False(result.Retryable);
    }

    [Fact]
    public async Task silence_with_zero_pcm_is_also_no_audio_stream()
    {
        var media = new byte[] { 1 };
        var (result, _) = await RunAudio(Ffmpeg(0), AudioJob(media, 1), media);

        Assert.Equal(FailCodes.NoAudioStream, result.FailCode);
    }

    [Fact]
    public async Task a_missing_ffmpeg_is_transcoder_unavailable_and_a_failing_one_too()
    {
        var media = new byte[] { 1 };
        var missing = new FakeProcessRunner { Handler = (_, _) => Task.FromResult(new ProcessRunResult(ProcessRunner.NotStarted, false, "")) };

        var (a, _) = await RunAudio(missing, AudioJob(media, 1), media);
        var (b, _) = await RunAudio(Ffmpeg(0, decodeExit: 1, decodeStderr: "Invalid data found when processing input"), AudioJob(media, 1), media);

        Assert.Equal(FailCodes.TranscoderUnavailable, a.FailCode);
        Assert.Equal(FailCodes.TranscoderUnavailable, b.FailCode);
        Assert.False(a.Retryable);
    }

    [Fact]
    public async Task unsupported_parameters_are_refused_not_guessed()
    {
        var media = new byte[] { 1 };
        var job = AudioJob(media, 1);
        job.Params = TestJobs.Json(Params(1).Replace("\"overlapMs\":0", "\"overlapMs\":250"));

        var failure = await Assert.ThrowsAsync<JobFailureException>(() => RunAudio(Ffmpeg(32_000), job, media));

        Assert.Equal(FailCodes.InternalError, failure.Code);
    }

    [Fact]
    public async Task an_encoder_timeout_is_a_retryable_timeout_not_a_hang()
    {
        var media = new byte[] { 1 };
        var runner = new FakeProcessRunner();
        runner.Handler = async (spec, ct) =>
        {
            if (spec.Arguments.Contains("libmp3lame")) return new ProcessRunResult(-1, true, "");
            await spec.ConsumeStdout!(new MemoryStream(new byte[32_000]), ct);
            return new ProcessRunResult(0, false, "");
        };

        var failure = await Assert.ThrowsAsync<JobFailureException>(() => RunAudio(runner, AudioJob(media, 1), media));

        Assert.Equal(FailCodes.Timeout, failure.Code);
        Assert.True(failure.Retryable);
    }

    // ---- speaking join -------------------------------------------------------------------------------------

    private static ClaimedJob JoinJob(params byte[][] clips)
    {
        var job = TestJobs.Pdf(TestIds.Job(2), clips[0], JobKinds.MediaSpeakingJoin,
            "{\"gapMilliseconds\":600,\"maxAudioSeconds\":360,\"sampleRateHz\":16000,\"bitrateKbps\":48,\"decodeTimeoutSeconds\":60,\"encodeTimeoutSeconds\":90,\"engine\":\"ffmpeg-pcm-join/1\"}",
            engine: "ffmpeg:7.1.1/ffmpeg-pcm-join:1");
        job.Inputs = clips.Select((c, i) => new InputRef { Name = "clip-" + i.ToString("D4"), SizeBytes = c.Length, Sha256 = TestIds.Sha(c), ContentType = "audio/webm" }).ToList();
        job.Limits = new JobLimits { Weight = 1, CpuMilli = 1000, MemMiB = 512, TmpMiB = 192, TimeoutSeconds = 240, MaxInputBytes = 67_108_864, MaxResultBytes = 16_384, MaxOutputBytes = 4_194_304, MaxOutputs = 1 };
        return job;
    }

    [Fact]
    public async Task RW114_clips_are_decoded_in_order_joined_encoded_once_and_uploaded_as_join_mp3()
    {
        var clips = new[] { new byte[] { 1, 1 }, new byte[] { 2, 2, 2 } };
        var job = JoinJob(clips);
        var io = new FakeIo();
        io.Inputs["clip-0000"] = clips[0];
        io.Inputs["clip-0001"] = clips[1];
        var decoded = new List<byte[]>();
        var runner = new FakeProcessRunner();
        runner.Handler = async (spec, ct) =>
        {
            if (spec.Arguments.Contains("libmp3lame"))
            {
                using var input = new MemoryStream();
                await spec.Stdin!.CopyToAsync(input, ct);
                await spec.ConsumeStdout!(new MemoryStream(new byte[] { 0x49, 0x44, 0x33 }), ct);
                return new ProcessRunResult(0, false, "");
            }

            using var clip = new MemoryStream();
            await spec.Stdin!.CopyToAsync(clip, ct);
            var pcm = new byte[clip.Length * 100]; // fake "decoded" audio, length proportional to the clip
            await spec.ConsumeStdout!(new MemoryStream(pcm), ct);
            return new ProcessRunResult(0, false, "");
        };
        using var dir = new TempDir();
        var executor = new SpeakingJoinExecutor("ffmpeg:7.1.1/ffmpeg-pcm-join:1", runner, "ffmpeg", TestLog.Instance, joiner: (parts, gap, max) =>
        {
            decoded.AddRange(parts);
            return new PcmJoinResult(parts.SelectMany(p => p).ToArray(), 1234, false);
        });

        var result = await executor.ExecuteAsync(new JobContext { Job = job, Lease = TestJobs.Lease(job, new FakeClock()), ScratchDirectory = dir.Path, Io = io }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(new[] { 200, 300 }, decoded.Select(d => d.Length).ToArray());
        var upload = Assert.Single(io.Uploaded);
        Assert.Equal("join.mp3", upload.Name);
        using var json = JsonDocument.Parse(result.ResultUtf8);
        var root = json.RootElement;
        Assert.Equal("media.speaking-join.result/1", root.GetProperty("schema").GetString());
        Assert.Equal(2, root.GetProperty("clipCount").GetInt32());
        Assert.Equal(1234, root.GetProperty("durationMs").GetInt32());
        Assert.False(root.GetProperty("truncated").GetBoolean());
        Assert.Equal(3, root.GetProperty("mp3Bytes").GetInt64());
        Assert.Equal(TestIds.Sha(upload.Bytes), root.GetProperty("mp3Sha256").GetString());
        // inputSha256 is the hash of the ORDERED clip hashes joined by comma (6.4).
        Assert.Equal(TestIds.Sha(Encoding.UTF8.GetBytes(TestIds.Sha(clips[0]) + "," + TestIds.Sha(clips[1]))), root.GetProperty("inputSha256").GetString());
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public async Task a_clip_that_cannot_be_decoded_fails_the_whole_join_as_transcoder_unavailable()
    {
        var clip = new byte[] { 1 };
        var job = JoinJob(clip);
        var io = new FakeIo();
        io.Inputs["clip-0000"] = clip;
        var runner = new FakeProcessRunner { Handler = (_, _) => Task.FromResult(new ProcessRunResult(1, false, "x")) };
        using var dir = new TempDir();
        var executor = new SpeakingJoinExecutor("ffmpeg:7.1.1/ffmpeg-pcm-join:1", runner, "ffmpeg", TestLog.Instance, (p, g, m) => new PcmJoinResult([1], 1, false));

        var result = await executor.ExecuteAsync(new JobContext { Job = job, Lease = TestJobs.Lease(job, new FakeClock()), ScratchDirectory = dir.Path, Io = io }, CancellationToken.None);

        Assert.Equal(FailCodes.TranscoderUnavailable, result.FailCode);
        Assert.Empty(io.Uploaded);
    }

    [Fact]
    public async Task clips_with_an_unexpected_content_type_or_size_are_refused()
    {
        var clip = new byte[] { 1 };
        var job = JoinJob(clip);
        job.Inputs[0].ContentType = "application/x-msdownload";
        using var dir = new TempDir();
        var executor = new SpeakingJoinExecutor("ffmpeg:7.1.1/ffmpeg-pcm-join:1", new FakeProcessRunner(), "ffmpeg", TestLog.Instance, (p, g, m) => new PcmJoinResult([1], 1, false));

        var failure = await Assert.ThrowsAsync<JobFailureException>(() => executor.ExecuteAsync(
            new JobContext { Job = job, Lease = TestJobs.Lease(job, new FakeClock()), ScratchDirectory = dir.Path, Io = new FakeIo() }, CancellationToken.None));

        Assert.Equal(FailCodes.InternalError, failure.Code);
    }

    [Theory]
    [InlineData("audio/webm;codecs=opus")]
    [InlineData("audio/webm; codecs=opus")]
    [InlineData(" Audio/WebM ; codecs=\"opus\" ")]
    [InlineData("audio/mp4;codecs=mp4a.40.2")]
    public async Task a_clip_content_type_with_parameters_is_matched_on_its_bare_media_type(string contentType)
    {
        var clip = new byte[] { 1, 1 };
        var job = JoinJob(clip);
        job.Inputs[0].ContentType = contentType;
        var io = new FakeIo();
        io.Inputs["clip-0000"] = clip;
        var runner = new FakeProcessRunner();
        runner.Handler = async (spec, ct) =>
        {
            await spec.Stdin!.CopyToAsync(Stream.Null, ct);
            var produced = spec.Arguments.Contains("libmp3lame") ? new byte[] { 0x49, 0x44, 0x33 } : new byte[200];
            await spec.ConsumeStdout!(new MemoryStream(produced), ct);
            return new ProcessRunResult(0, false, "");
        };
        using var dir = new TempDir();
        var executor = new SpeakingJoinExecutor("ffmpeg:7.1.1/ffmpeg-pcm-join:1", runner, "ffmpeg", TestLog.Instance, (p, g, m) => new PcmJoinResult([1], 1234, false));

        var result = await executor.ExecuteAsync(new JobContext { Job = job, Lease = TestJobs.Lease(job, new FakeClock()), ScratchDirectory = dir.Path, Io = io }, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task a_parameterised_type_that_is_not_on_the_allow_list_is_still_refused()
    {
        var clip = new byte[] { 1 };
        var job = JoinJob(clip);
        job.Inputs[0].ContentType = "application/x-msdownload;codecs=opus";
        using var dir = new TempDir();
        var executor = new SpeakingJoinExecutor("ffmpeg:7.1.1/ffmpeg-pcm-join:1", new FakeProcessRunner(), "ffmpeg", TestLog.Instance, (p, g, m) => new PcmJoinResult([1], 1, false));

        var failure = await Assert.ThrowsAsync<JobFailureException>(() => executor.ExecuteAsync(
            new JobContext { Job = job, Lease = TestJobs.Lease(job, new FakeClock()), ScratchDirectory = dir.Path, Io = new FakeIo() }, CancellationToken.None));

        Assert.Equal(FailCodes.InternalError, failure.Code);
        Assert.Equal("audio/webm", SpeakingJoinExecutor.MediaType("audio/webm;codecs=opus"));
        Assert.Equal("audio/webm", SpeakingJoinExecutor.MediaType(" audio/webm "));
    }

    // ---- companion index prep ------------------------------------------------------------------------------

    private static IReadOnlyList<ChunkRow> OneChunkPerPage(IReadOnlyList<string> pages) =>
        pages.Select((p, i) => new ChunkRow("Page " + (i + 1), i + 1, p)).Where(c => c.Text.Length > 0).ToList();

    [Fact]
    public void companion_result_follows_section_6_2_including_version_and_chunk_hash()
    {
        var pages = new[] { "First page content that is long enough.", "Second page content." };

        var bytes = CompanionPrepCore.BuildFromPages(true, pages, 10, EngineVersions.CompanionIndexPrep, new string('a', 64), OneChunkPerPage);

        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        Assert.Equal("companion.index-prep.result/1", root.GetProperty("schema").GetString());
        Assert.False(root.GetProperty("needsOcr").GetBoolean());
        Assert.Equal(2, root.GetProperty("pageCount").GetInt32());
        Assert.Equal(TestIds.Sha(Encoding.UTF8.GetBytes(string.Join("\n", pages)))[..16], root.GetProperty("version").GetString());
        Assert.Equal(2, root.GetProperty("chunkCount").GetInt32());
        var expectedChunkHash = TestIds.Sha(Encoding.UTF8.GetBytes(string.Join("\n",
            "Page 1|1|" + TestIds.Sha(Encoding.UTF8.GetBytes(pages[0])), "Page 2|2|" + TestIds.Sha(Encoding.UTF8.GetBytes(pages[1])))));
        Assert.Equal(expectedChunkHash, root.GetProperty("chunksSha256").GetString());
    }

    [Fact]
    public void companion_needing_ocr_ships_no_chunks_and_no_version()
    {
        var bytes = CompanionPrepCore.BuildFromPages(true, new[] { "tiny" }, 50, EngineVersions.CompanionIndexPrep, new string('a', 64), OneChunkPerPage);

        using var json = JsonDocument.Parse(bytes);
        Assert.True(json.RootElement.GetProperty("needsOcr").GetBoolean());
        Assert.Equal(0, json.RootElement.GetProperty("chunks").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("version").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("chunksSha256").ValueKind);
    }

    [Fact]
    public void companion_chunks_outside_the_api_bounds_are_never_sent()
    {
        var pages = new[] { "page text long enough" };
        Func<IReadOnlyList<string>, IReadOnlyList<ChunkRow>> tooLong = _ => [new ChunkRow("Page 1", 1, new string('x', CompanionPrepCore.MaxChunkChars + 1))];
        Func<IReadOnlyList<string>, IReadOnlyList<ChunkRow>> badHeading = _ => [new ChunkRow("Chapter 1", 1, "text")];
        Func<IReadOnlyList<string>, IReadOnlyList<ChunkRow>> badPage = _ => [new ChunkRow("Page 3", 3, "text")];
        Func<IReadOnlyList<string>, IReadOnlyList<ChunkRow>> empty = _ => [new ChunkRow("Page 1", 1, "")];

        foreach (var chunker in new[] { tooLong, badHeading, badPage, empty })
        {
            Assert.Throws<UnrepresentableResultException>(() => CompanionPrepCore.BuildFromPages(true, pages, 5, "e", new string('a', 64), chunker));
        }
    }

    [Fact]
    public void companion_requires_the_linked_chunker_source_to_be_offered_at_all()
    {
        // Without CompanionChunker.cs from the API track the adapter reports unavailable and the executor is never advertised.
        Assert.Equal(CompanionChunkAdapter.Available, ExpectedChunkerLinked());
        if (!CompanionChunkAdapter.Available) Assert.Throws<NotSupportedException>(() => CompanionChunkAdapter.Build(new[] { "x" }));
    }

    private static bool ExpectedChunkerLinked() =>
        File.Exists(TestPaths.Combine("backend", "src", "OetLearner.Api", "Services", "Companion", "CompanionChunker.cs"));

    [Fact]
    public void speaking_join_requires_the_linked_joiner_source_to_be_offered_at_all()
    {
        Assert.Equal(PcmJoinAdapter.Available, File.Exists(TestPaths.Combine("backend", "src", "OetLearner.Api", "Services", "Speaking", "PcmJoiner.cs")));
    }
}
