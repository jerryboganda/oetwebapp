using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.RemoteJobs;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// The pure halves of the media kinds (OET-RWP/1 sections 6.3 and 6.4): strict parsing, the contiguous sample-exact manifest of
/// <c>media.audio-extract</c>, the single-output contract of <c>media.speaking-join</c>, and the constants that fingerprint them.
/// RW-113, RW-114, RW-115.
/// </summary>
public sealed class RemoteMediaKindValidatorTests
{
    private const string AudioEngine = "ffmpeg:7.1.1/audio-extract:1";
    private const string JoinEngine = "ffmpeg:7.1.1/ffmpeg-pcm-join:1";
    private static readonly string MediaSha = new('c', 64);
    private static readonly string ClipShaA = new('1', 64);
    private static readonly string ClipShaB = new('2', 64);

    // ── media.audio-extract ──────────────────────────────────────────────────

    private static RemoteJobRow AudioJob(string? paramsJson = null)
        => RemoteTestData.Row(
            kind: RemoteJobKinds.MediaAudioExtract,
            engine: AudioEngine,
            inputSha: MediaSha,
            paramsJson: paramsJson ?? AudioExtractSettings.ParamsJson(),
            inputsJson: "[{\"name\":\"media\",\"sizeBytes\":100,\"sha256\":\"" + MediaSha + "\",\"contentType\":\"video/mp4\",\"storageKey\":\"live-class-recordings/x/video.mp4\"}]",
            settingsHash: AudioExtractSettings.Hash());

    private static (string Json, List<RemoteOutputRow> Outputs) AudioResult(
        RemoteJobRow job,
        int[] durationsMs,
        Action<Dictionary<string, object?>>? tamper = null)
    {
        var chunks = new List<Dictionary<string, object?>>();
        var outputs = new List<RemoteOutputRow>();
        var start = 0;
        var total = 0;
        for (var i = 0; i < durationsMs.Length; i++)
        {
            var size = 1_000_000 + i;
            var sha = RemoteIds.Sha256Hex("chunk" + i);
            var name = AudioExtractValidator.ChunkName(i);
            chunks.Add(new Dictionary<string, object?>
            {
                ["index"] = i,
                ["name"] = name,
                ["startMs"] = start,
                ["durationMs"] = durationsMs[i],
                ["sizeBytes"] = size,
                ["sha256"] = sha,
            });
            outputs.Add(new RemoteOutputRow(name, size, sha, $"remote-jobs/{job.Id}/1/{name}"));
            start += durationsMs[i];
            total += size;
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
            ["chunkCount"] = durationsMs.Length,
            ["chunks"] = chunks,
            ["totalOutputBytes"] = total,
        };
        tamper?.Invoke(body);
        return (JsonSerializer.Serialize(body), outputs);
    }

    private static RemoteResultValidation ValidateAudio(RemoteJobRow job, string json, IReadOnlyList<RemoteOutputRow> outputs)
        => new AudioExtractKindHandler(NullLogger<AudioExtractKindHandler>.Instance).Validate(job, json, outputs, new RemoteJobsOptions());

    private static Dictionary<string, object?> Chunk(Dictionary<string, object?> body, int index)
        => ((List<Dictionary<string, object?>>)body["chunks"]!)[index];

    [Fact]
    public void AudioExtract_ACleanContiguousResult_IsAccepted_AndSummarisedWithoutContent()
    {
        var job = AudioJob();
        var (json, outputs) = AudioResult(job, [600_000, 600_000, 180_000]);

        var validation = ValidateAudio(job, json, outputs);

        Assert.True(validation.IsOk, validation.Message);
        var parsed = Assert.IsType<AudioExtractResult>(validation.Parsed);
        Assert.Equal(3, parsed.ChunkCount);
        Assert.Equal(1_380_000, parsed.DurationMs);
        using var summary = JsonDocument.Parse(validation.SummaryJson!);
        Assert.Equal(3, summary.RootElement.GetProperty("chunkCount").GetInt32());
        Assert.Equal(1_380_000, summary.RootElement.GetProperty("durationMs").GetInt32());
        Assert.DoesNotContain("remote-jobs", validation.SummaryJson, StringComparison.Ordinal);
    }

    [Fact]
    public void AudioExtract_AShortSingleChunkRecording_IsAccepted()
    {
        var job = AudioJob();
        var (json, outputs) = AudioResult(job, [42_500]);

        Assert.True(ValidateAudio(job, json, outputs).IsOk);
    }

    [Fact]
    public void AudioExtract_EveryBrokenManifestRule_IsInvalid_NotAcceptedAndNeverContent()
    {
        var job = AudioJob();
        var cases = new Dictionary<string, Action<Dictionary<string, object?>>>
        {
            ["schema"] = body => body["schema"] = "media.audio-extract.result/2",
            ["inputSha256"] = body => body["inputSha256"] = new string('d', 64),
            ["sampleRate"] = body => body["sampleRateHz"] = 44100,
            ["channels"] = body => body["channels"] = 2,
            ["codec"] = body => body["codec"] = "aac",
            ["bitrate"] = body => body["bitrateKbps"] = 128,
            ["segmentSeconds"] = body => body["segmentSeconds"] = 300,
            ["chunkCount"] = body => body["chunkCount"] = 5,
            ["firstIndex"] = body => Chunk(body, 0)["index"] = 1,
            ["name"] = body => Chunk(body, 1)["name"] = "chunk-0009.mp3",
            ["gap"] = body => Chunk(body, 1)["startMs"] = 600_001,
            ["shortMiddleChunk"] = body => Chunk(body, 0)["durationMs"] = 599_000,
            ["lastChunkLongerThanASegment"] = body => Chunk(body, 2)["durationMs"] = 600_001,
            ["emptyChunk"] = body => Chunk(body, 2)["sizeBytes"] = 0,
            ["oversizedChunk"] = body => Chunk(body, 0)["sizeBytes"] = AudioExtractSettings.MaxChunkBytes + 1,
            ["chunkHash"] = body => Chunk(body, 0)["sha256"] = "NOT-A-HASH",
            ["totalDuration"] = body => body["durationMs"] = 1_380_001,
            ["totalBytes"] = body => body["totalOutputBytes"] = 1,
            ["tooLong"] = body => body["durationMs"] = (AudioExtractSettings.MaxDurationSeconds + 1) * 1000,
        };

        foreach (var (name, tamper) in cases)
        {
            var (json, outputs) = AudioResult(job, [600_000, 600_000, 180_000], tamper);
            var validation = ValidateAudio(job, json, outputs);

            Assert.True(validation.Status == RemoteValidationStatus.Invalid, $"case '{name}' was {validation.Status}");
        }
    }

    [Fact]
    public void AudioExtract_AnEngineThatIsNotTheJobsEngine_IsAnEngineMismatch()
    {
        var job = AudioJob();
        var (json, outputs) = AudioResult(job, [600_000], body => body["engineVersion"] = "ffmpeg:6.0/audio-extract:1");

        Assert.Equal(RemoteValidationStatus.EngineMismatch, ValidateAudio(job, json, outputs).Status);
    }

    [Fact]
    public void AudioExtract_MoreThanSixtyFourChunks_IsInvalid()
    {
        var job = AudioJob();
        var (json, outputs) = AudioResult(job, Enumerable.Repeat(600_000, 65).ToArray());

        Assert.Equal(RemoteValidationStatus.Invalid, ValidateAudio(job, json, outputs).Status);
    }

    [Fact]
    public void AudioExtract_UploadsThatDisagreeWithTheResult_AreAnOutputMismatch()
    {
        var job = AudioJob();
        var (json, outputs) = AudioResult(job, [600_000, 180_000]);

        // a chunk that was never uploaded
        Assert.Equal(RemoteValidationStatus.OutputMismatch, ValidateAudio(job, json, outputs.Take(1).ToList()).Status);

        // an upload of a different size, and one of a different hash
        var resized = outputs.Select((o, i) => i == 0 ? o with { SizeBytes = o.SizeBytes + 1 } : o).ToList();
        Assert.Equal(RemoteValidationStatus.OutputMismatch, ValidateAudio(job, json, resized).Status);
        var rehashed = outputs.Select((o, i) => i == 1 ? o with { Sha256 = new string('e', 64) } : o).ToList();
        Assert.Equal(RemoteValidationStatus.OutputMismatch, ValidateAudio(job, json, rehashed).Status);

        // an extra object the result does not mention is never kept
        var extra = outputs.Concat(new[] { new RemoteOutputRow("chunk-0002.mp3", 5, new string('f', 64), "remote-jobs/x/1/chunk-0002.mp3") }).ToList();
        Assert.Equal(RemoteValidationStatus.OutputMismatch, ValidateAudio(job, json, extra).Status);
    }

    [Fact]
    public void AudioExtract_TryParse_RejectsAnythingThatIsNotAWellFormedResult()
    {
        Assert.False(AudioExtractValidator.TryParse("[]", out _, out var notObject));
        Assert.NotNull(notObject);
        Assert.False(AudioExtractValidator.TryParse("{", out _, out var notJson));
        Assert.NotNull(notJson);
        Assert.False(AudioExtractValidator.TryParse("{\"schema\":\"media.audio-extract.result/1\"}", out _, out var missing));
        Assert.NotNull(missing);

        var job = AudioJob();
        var (json, _) = AudioResult(job, [600_000]);
        var wrongType = json.Replace("\"durationMs\":600000,\"sampleRateHz\"", "\"durationMs\":\"600000\",\"sampleRateHz\"", StringComparison.Ordinal);
        Assert.False(AudioExtractValidator.TryParse(wrongType, out _, out var typeError));
        Assert.NotNull(typeError);

        Assert.True(AudioExtractValidator.TryParse(json, out var parsed, out _));
        Assert.Equal("chunk-0000.mp3", parsed!.Chunks[0].Name);
    }

    [Fact]
    public void AudioExtract_ChunkNames_AreZeroPaddedToFourDigits()
    {
        Assert.Equal("chunk-0000.mp3", AudioExtractValidator.ChunkName(0));
        Assert.Equal("chunk-0012.mp3", AudioExtractValidator.ChunkName(12));
        Assert.Equal("chunk-0063.mp3", AudioExtractValidator.ChunkName(63));
    }

    [Fact]
    public void AudioExtract_TheFingerprintAndParamsAreTheSpecValues_AndCarryNoIdentifier()
    {
        // The exact string the settings hash covers (sorted keys), so a change to any constant is a deliberate, visible edit.
        const string Expected =
            "argsVersion=v1;bitrateKbps=48;channels=1;codec=mp3;maxChunkBytes=20971520;maxDurationSeconds=14400;overlapMs=0;sampleRateHz=16000;segmentSeconds=600";
        Assert.Equal(RemoteIds.Sha256Hex(Expected), AudioExtractSettings.Hash());

        using var parameters = JsonDocument.Parse(AudioExtractSettings.ParamsJson());
        var root = parameters.RootElement;
        Assert.Equal(16000, root.GetProperty("sampleRateHz").GetInt32());
        Assert.Equal(1, root.GetProperty("channels").GetInt32());
        Assert.Equal("mp3", root.GetProperty("codec").GetString());
        Assert.Equal(48, root.GetProperty("bitrateKbps").GetInt32());
        Assert.Equal(600, root.GetProperty("segmentSeconds").GetInt32());
        Assert.Equal(0, root.GetProperty("overlapMs").GetInt32());
        Assert.Equal(20_971_520, root.GetProperty("maxChunkBytes").GetInt32());
        Assert.Equal(14_400, root.GetProperty("maxDurationSeconds").GetInt32());
        Assert.Equal("v1", root.GetProperty("argsVersion").GetString());

        // a chunk always fits the 24 MiB single-call cap of the transcription stage
        Assert.True(AudioExtractSettings.MaxChunkBytes < 24 * 1024 * 1024);
    }

    [Theory]
    [InlineData("live-class-recordings/2027/01/s/video.mp4", "video/mp4")]
    [InlineData("a/b.M4A", "audio/mp4")]
    [InlineData("a/b.mp3", "audio/mpeg")]
    [InlineData("a/b.wav", "audio/wav")]
    [InlineData("a/b.webm", "audio/webm")]
    [InlineData("a/b.ogg", "audio/ogg")]
    [InlineData("a/b.exe", null)]
    [InlineData("a/noextension", null)]
    public void AudioExtract_OnlyKnownRecordingTypesAreOffered(string key, string? expected)
        => Assert.Equal(expected, AudioExtractSettings.ContentTypeFor(key));

    // ── media.speaking-join ──────────────────────────────────────────────────

    private static RemoteJobRow JoinJob()
        => RemoteTestData.Row(
            kind: RemoteJobKinds.MediaSpeakingJoin,
            engine: JoinEngine,
            inputSha: SpeakingJoinSettings.InputSha256([ClipShaA, ClipShaB]),
            paramsJson: SpeakingJoinSettings.ParamsJson(600, 360),
            inputsJson: "[{\"name\":\"clip-0000\",\"sizeBytes\":10,\"sha256\":\"" + ClipShaA + "\",\"contentType\":\"audio/webm\",\"storageKey\":\"a/1\",\"recordingId\":\"r1\"},"
                + "{\"name\":\"clip-0001\",\"sizeBytes\":12,\"sha256\":\"" + ClipShaB + "\",\"contentType\":\"audio/webm\",\"storageKey\":\"a/2\",\"recordingId\":\"r2\"}]",
            settingsHash: SpeakingJoinSettings.Hash(600, 360));

    private static (string Json, List<RemoteOutputRow> Outputs) JoinResult(
        RemoteJobRow job,
        Action<Dictionary<string, object?>>? tamper = null,
        int mp3Bytes = 1_725_000)
    {
        var mp3Sha = RemoteIds.Sha256Hex("join-mp3");
        var body = new Dictionary<string, object?>
        {
            ["schema"] = "media.speaking-join.result/1",
            ["engineVersion"] = job.EngineVersion,
            ["inputSha256"] = job.InputSha256,
            ["clipCount"] = 2,
            ["durationMs"] = 287_400,
            ["truncated"] = false,
            ["mp3Bytes"] = mp3Bytes,
            ["mp3Sha256"] = mp3Sha,
        };
        tamper?.Invoke(body);
        return (JsonSerializer.Serialize(body), [new RemoteOutputRow("join.mp3", mp3Bytes, mp3Sha, $"remote-jobs/{job.Id}/1/join.mp3")]);
    }

    private static RemoteResultValidation ValidateJoin(RemoteJobRow job, string json, IReadOnlyList<RemoteOutputRow> outputs)
        => new SpeakingJoinKindHandler(NullLogger<SpeakingJoinKindHandler>.Instance).Validate(job, json, outputs, new RemoteJobsOptions());

    [Fact]
    public void SpeakingJoin_ACleanResult_IsAccepted_AndSummarisedForTheConsumer()
    {
        var job = JoinJob();
        var (json, outputs) = JoinResult(job);

        var validation = ValidateJoin(job, json, outputs);

        Assert.True(validation.IsOk, validation.Message);
        using var summary = JsonDocument.Parse(validation.SummaryJson!);
        Assert.Equal(2, summary.RootElement.GetProperty("clipCount").GetInt32());
        Assert.Equal(287_400, summary.RootElement.GetProperty("durationMs").GetInt32());
        Assert.False(summary.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal(1_725_000, summary.RootElement.GetProperty("mp3Bytes").GetInt32());
        Assert.True(RemoteIds.IsSha256Hex(summary.RootElement.GetProperty("mp3Sha256").GetString()));
    }

    [Fact]
    public void SpeakingJoin_EveryBrokenRule_IsInvalid()
    {
        var job = JoinJob();
        var cases = new Dictionary<string, Action<Dictionary<string, object?>>>
        {
            ["schema"] = body => body["schema"] = "media.speaking-join.result/9",
            ["inputSha256"] = body => body["inputSha256"] = new string('9', 64),
            ["clipCount"] = body => body["clipCount"] = 3,
            ["zeroDuration"] = body => body["durationMs"] = 0,
            ["overMaxDuration"] = body => body["durationMs"] = 360_001,
            ["emptyMp3"] = body => body["mp3Bytes"] = 0,
            ["hugeMp3"] = body => body["mp3Bytes"] = 4_194_305,
            ["mp3Hash"] = body => body["mp3Sha256"] = "short",
            ["truncatedNotABool"] = body => body["truncated"] = "no",
        };

        foreach (var (name, tamper) in cases)
        {
            var (json, outputs) = JoinResult(job, tamper);
            var validation = ValidateJoin(job, json, outputs);

            Assert.True(validation.Status == RemoteValidationStatus.Invalid, $"case '{name}' was {validation.Status}");
        }
    }

    [Fact]
    public void SpeakingJoin_AnEngineThatIsNotTheJobsEngine_IsAnEngineMismatch()
    {
        var job = JoinJob();
        var (json, outputs) = JoinResult(job, body => body["engineVersion"] = "ffmpeg:6.0/ffmpeg-pcm-join:1");

        Assert.Equal(RemoteValidationStatus.EngineMismatch, ValidateJoin(job, json, outputs).Status);
    }

    [Fact]
    public void SpeakingJoin_TheUploadMustBeExactlyOneJoinMp3_WithTheResultsSizeAndHash()
    {
        var job = JoinJob();
        var (json, outputs) = JoinResult(job);

        Assert.Equal(RemoteValidationStatus.OutputMismatch, ValidateJoin(job, json, []).Status);
        Assert.Equal(RemoteValidationStatus.OutputMismatch, ValidateJoin(job, json, [outputs[0] with { Name = "other.mp3" }]).Status);
        Assert.Equal(RemoteValidationStatus.OutputMismatch, ValidateJoin(job, json, [outputs[0] with { SizeBytes = 1 }]).Status);
        Assert.Equal(RemoteValidationStatus.OutputMismatch, ValidateJoin(job, json, [outputs[0] with { Sha256 = new string('a', 64) }]).Status);
        Assert.Equal(
            RemoteValidationStatus.OutputMismatch,
            ValidateJoin(job, json, [outputs[0], outputs[0] with { Name = "second.mp3" }]).Status);
    }

    [Fact]
    public void SpeakingJoin_TryParse_RejectsAnythingThatIsNotAWellFormedResult()
    {
        Assert.False(SpeakingJoinValidator.TryParse("42", out _, out var notObject));
        Assert.NotNull(notObject);
        Assert.False(SpeakingJoinValidator.TryParse("not json", out _, out var notJson));
        Assert.NotNull(notJson);
        Assert.False(SpeakingJoinValidator.TryParse("{\"schema\":\"media.speaking-join.result/1\"}", out _, out var missing));
        Assert.NotNull(missing);
    }

    [Fact]
    public void SpeakingJoin_TheInputFingerprintIsOrderSensitive_AndPerSession()
    {
        var forward = SpeakingJoinSettings.InputSha256([ClipShaA, ClipShaB]);
        var reversed = SpeakingJoinSettings.InputSha256([ClipShaB, ClipShaA]);

        Assert.NotEqual(forward, reversed);
        Assert.Equal(RemoteIds.Sha256Hex(ClipShaA + "," + ClipShaB), forward);

        // Two sessions holding byte-identical audio still get different jobs (no content-addressed dedup of learner audio): the
        // resource id is part of the idempotency key.
        var settings = SpeakingJoinSettings.Hash(600, 360);
        var first = RemoteJobKeys.IdempotencyKey(RemoteJobKinds.MediaSpeakingJoin, "apply", SpeakingJoinSettings.ResourceType, "session-1", forward, JoinEngine, settings);
        var second = RemoteJobKeys.IdempotencyKey(RemoteJobKinds.MediaSpeakingJoin, "apply", SpeakingJoinSettings.ResourceType, "session-2", forward, JoinEngine, settings);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void SpeakingJoin_TheFingerprintAndParamsAreTheSpecValues_AndCarryNoSessionOrLearnerData()
    {
        const string Expected = "bitrateKbps=48;engine=ffmpeg-pcm-join/1;gapMilliseconds=600;maxAudioSeconds=360;sampleRateHz=16000";
        Assert.Equal(RemoteIds.Sha256Hex(Expected), SpeakingJoinSettings.Hash(600, 360));
        Assert.NotEqual(SpeakingJoinSettings.Hash(600, 360), SpeakingJoinSettings.Hash(500, 360));

        using var parameters = JsonDocument.Parse(SpeakingJoinSettings.ParamsJson(600, 360));
        var root = parameters.RootElement;
        Assert.Equal(600, root.GetProperty("gapMilliseconds").GetInt32());
        Assert.Equal(360, root.GetProperty("maxAudioSeconds").GetInt32());
        Assert.Equal(16000, root.GetProperty("sampleRateHz").GetInt32());
        Assert.Equal(48, root.GetProperty("bitrateKbps").GetInt32());
        Assert.Equal(60, root.GetProperty("decodeTimeoutSeconds").GetInt32());
        Assert.Equal(90, root.GetProperty("encodeTimeoutSeconds").GetInt32());
        Assert.Equal("ffmpeg-pcm-join/1", root.GetProperty("engine").GetString());
        Assert.Equal(7, root.EnumerateObject().Count());
    }

    [Theory]
    [InlineData("audio/webm", "audio/webm")]
    [InlineData("audio/webm;codecs=opus", "audio/webm")]
    [InlineData(" Audio/MP4 ", "audio/mp4")]
    [InlineData("audio/x-wav", "audio/wav")]
    [InlineData("audio/mpeg", "audio/mpeg")]
    [InlineData("video/webm", "video/webm")]
    [InlineData("application/octet-stream", null)]
    [InlineData("image/png", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void SpeakingJoin_OnlyTheAudioTypesTheSpecNamesAreOffered(string? mime, string? expected)
        => Assert.Equal(expected, SpeakingJoinSettings.NormalizeContentType(mime));

    [Fact]
    public void SpeakingJoin_InputNamesAreFourDigitIndexes()
    {
        Assert.Equal("clip-0000", SpeakingJoinSettings.InputName(0));
        Assert.Equal("clip-0063", SpeakingJoinSettings.InputName(63));
        Assert.Equal(64, SpeakingJoinSettings.MaxClips);
        Assert.Equal(16L * 1024 * 1024, SpeakingJoinSettings.MaxClipBytes);
        Assert.Equal(64L * 1024 * 1024, SpeakingJoinSettings.MaxTotalBytes);
    }

    // ── registry and options ─────────────────────────────────────────────────

    [Fact]
    public void MediaKinds_AreNeverOffered_UntilAnEnginePinIsConfigured()
    {
        var unpinned = new RemoteJobsOptions();
        Assert.Null(RemoteJobKinds.EngineVersion(RemoteJobKinds.MediaAudioExtract, unpinned));
        Assert.Null(RemoteJobKinds.EngineVersion(RemoteJobKinds.MediaSpeakingJoin, unpinned));

        var pinned = new RemoteJobsOptions
        {
            Kinds = new Dictionary<string, RemoteKindOptions>(StringComparer.Ordinal)
            {
                [RemoteJobKinds.MediaAudioExtract] = new() { EngineVersion = AudioEngine },
                [RemoteJobKinds.MediaSpeakingJoin] = new() { EngineVersion = "  " },
            },
        };
        Assert.Equal(AudioEngine, RemoteJobKinds.EngineVersion(RemoteJobKinds.MediaAudioExtract, pinned));
        Assert.Null(RemoteJobKinds.EngineVersion(RemoteJobKinds.MediaSpeakingJoin, pinned)); // a blank pin is "not offered"
    }

    [Fact]
    public void MediaKinds_HaveTheRegistryLimitsOfTheSpec()
    {
        var audio = RemoteJobKinds.Find(RemoteJobKinds.MediaAudioExtract)!.Limits;
        Assert.Equal(2, audio.Weight);
        Assert.Equal(805_306_368L, audio.MaxInputBytes);
        Assert.Equal(262_144L, audio.MaxResultBytes);
        Assert.Equal(134_217_728L, audio.MaxOutputBytes);
        Assert.Equal(64, audio.MaxOutputs);

        var join = RemoteJobKinds.Find(RemoteJobKinds.MediaSpeakingJoin)!.Limits;
        Assert.Equal(1, join.Weight);
        Assert.Equal(67_108_864L, join.MaxInputBytes);
        Assert.Equal(16_384L, join.MaxResultBytes);
        Assert.Equal(4_194_304L, join.MaxOutputBytes);
        Assert.Equal(1, join.MaxOutputs);
    }

    [Fact]
    public void SpeakingJoinOutputTtl_DefaultsToADayAndIsClampedToAWeek()
    {
        Assert.Equal(24, new RemoteJobsOptions().SpeakingJoinOutputTtlHours);
        Assert.Equal(1, new RemoteJobsOptions { SpeakingJoinOutputTtlHours = 0 }.Normalized().SpeakingJoinOutputTtlHours);
        Assert.Equal(168, new RemoteJobsOptions { SpeakingJoinOutputTtlHours = 100_000 }.Normalized().SpeakingJoinOutputTtlHours);
    }

    [Fact]
    public void AudioExtractOutputTtl_DefaultsToTwoDaysAndIsClampedToTheRetentionWindow()
    {
        Assert.Equal(48, new RemoteJobsOptions().AudioExtractOutputTtlHours);
        Assert.Equal(1, new RemoteJobsOptions { AudioExtractOutputTtlHours = 0 }.Normalized().AudioExtractOutputTtlHours);
        Assert.Equal(720, new RemoteJobsOptions { AudioExtractOutputTtlHours = 100_000 }.Normalized().AudioExtractOutputTtlHours);
    }

    [Fact]
    public void LiveClassChunkRunBudget_DefaultsToAFewMinutesAndStaysWellUnderTheProcessorCeiling()
    {
        Assert.Equal(4, new RemoteJobsOptions().LiveClassChunkRunBudgetMinutes);
        Assert.Equal(1, new RemoteJobsOptions { LiveClassChunkRunBudgetMinutes = 0 }.Normalized().LiveClassChunkRunBudgetMinutes);
        Assert.Equal(15, new RemoteJobsOptions { LiveClassChunkRunBudgetMinutes = 100_000 }.Normalized().LiveClassChunkRunBudgetMinutes);
    }
}
