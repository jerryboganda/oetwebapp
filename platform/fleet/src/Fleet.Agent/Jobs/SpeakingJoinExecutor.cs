using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Fleet.Agent;

internal sealed record PcmJoinResult(byte[] Pcm, int DurationMs, bool Truncated);

/// <summary>
/// The only place the agent touches the API's PcmJoiner, link-compiled (never copied) once the API track has moved it into its
/// own pure file (protocol 6.4). Without that source media.speaking-join is simply not offered.
/// </summary>
internal static class PcmJoinAdapter
{
    public static bool Available =>
#if FLEET_HAS_PCM_JOINER
        true;
#else
        false;
#endif

    public static PcmJoinResult Join(IReadOnlyList<byte[]> clips, int gapMilliseconds, int maxSeconds)
    {
#if FLEET_HAS_PCM_JOINER
        var joined = OetLearner.Api.Services.Speaking.PcmJoiner.Join(clips, gapMilliseconds, maxSeconds);
        return new PcmJoinResult(joined.Pcm, joined.DurationMs, joined.Truncated);
#else
        throw new NotSupportedException("the PcmJoiner source is not linked into this build");
#endif
    }
}

/// <summary>
/// media.speaking-join v1 (protocol 6.4): candidate clips to ONE mp3, replicating FfmpegSpeakingAudioTranscoder exactly. Each
/// clip is decoded through stdin/stdout, joined by the API's own PcmJoiner, encoded once, and uploaded as join.mp3. A clip
/// file on tmpfs exists only until its decode finished.
/// </summary>
internal sealed class SpeakingJoinExecutor : IJobExecutor
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/webm", "video/webm", "audio/mp4", "audio/aac", "audio/wav", "audio/mpeg", "audio/ogg",
    };

    private readonly IProcessRunner _runner;
    private readonly string _ffmpeg;
    private readonly Func<IReadOnlyList<byte[]>, int, int, PcmJoinResult> _joiner;
    private readonly ILogger _log;

    public SpeakingJoinExecutor(string engineVersion, IProcessRunner runner, string ffmpegPath, ILogger log,
        Func<IReadOnlyList<byte[]>, int, int, PcmJoinResult>? joiner = null)
    {
        EngineVersion = engineVersion;
        _runner = runner;
        _ffmpeg = ffmpegPath;
        _log = log;
        _joiner = joiner ?? PcmJoinAdapter.Join;
    }

    public string Kind => JobKinds.MediaSpeakingJoin;
    public int SchemaVersion => 1;
    public string EngineVersion { get; }

    /// <summary>
    /// The media type without its parameters. Browser recorders label a clip "audio/webm;codecs=opus", and the allow-list names
    /// bare types: an exact match would refuse every such clip permanently (a non-retryable internal_error).
    /// </summary>
    internal static string MediaType(string contentType)
    {
        var semicolon = contentType.IndexOf(';');
        return (semicolon >= 0 ? contentType[..semicolon] : contentType).Trim();
    }

    public async Task<ExecutionResult> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var job = context.Job;
        if (job.Inputs.Count is < 1 or > 64) throw new JobFailureException(FailCodes.InternalError, false, "unexpected clip count");
        var parameters = job.Params;
        var gap = ParamReader.Int(parameters, "gapMilliseconds", 600, 0, 5000);
        var maxSeconds = ParamReader.Int(parameters, "maxAudioSeconds", 360, 1, 3600);
        var bitrate = ParamReader.Int(parameters, "bitrateKbps", 48, 8, 320);
        var decodeTimeout = TimeSpan.FromSeconds(ParamReader.Int(parameters, "decodeTimeoutSeconds", 60, 1, 600));
        var encodeTimeout = TimeSpan.FromSeconds(ParamReader.Int(parameters, "encodeTimeoutSeconds", 90, 1, 600));
        if (ParamReader.Int(parameters, "sampleRateHz", FfmpegArgs.SampleRate, 1, 1_000_000) != FfmpegArgs.SampleRate
            || ParamReader.String(parameters, "engine", "ffmpeg-pcm-join/1") != "ffmpeg-pcm-join/1")
        {
            throw new JobFailureException(FailCodes.InternalError, false, "unsupported join parameters");
        }

        long totalBytes = 0;
        foreach (var clip in job.Inputs)
        {
            totalBytes += clip.SizeBytes;
            if (clip.SizeBytes > 16L * 1024 * 1024 || (clip.ContentType is not null && !AllowedContentTypes.Contains(MediaType(clip.ContentType))))
            {
                throw new JobFailureException(FailCodes.InternalError, false, "unexpected clip");
            }
        }

        if (totalBytes > 64L * 1024 * 1024) throw new JobFailureException(FailCodes.InputTooLarge, false, "clips exceed the combined bound");

        // Per-clip decode: the file exists only until ffmpeg has consumed it.
        var decoded = new List<byte[]>(job.Inputs.Count);
        for (var i = 0; i < job.Inputs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            context.Lease.Stage = "decoding";
            var clipPath = Path.Combine(context.ScratchDirectory, "clip-" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + ".bin");
            using (var clip = await context.Io.FetchInputAsync(job, context.Lease, job.Inputs[i], clipPath, cacheable: false, ct).ConfigureAwait(false))
            {
                var pcm = new MemoryStream();
                await using (var source = new FileStream(clip.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan | FileOptions.Asynchronous))
                {
                    var run = await _runner.RunAsync(new ProcessSpec
                    {
                        FileName = _ffmpeg,
                        Arguments = FfmpegArgs.DecodeClipFromStdin(),
                        Stdin = source,
                        ConsumeStdout = (stdout, t) => stdout.CopyToAsync(pcm, t),
                        Timeout = decodeTimeout,
                    }, ct).ConfigureAwait(false);
                    if (run.TimedOut) return ExecutionResult.Fail(FailCodes.Timeout, true, "decoding exceeded its timeout");
                    if (run.ExitCode != 0) return ExecutionResult.Fail(FailCodes.TranscoderUnavailable, false, "ffmpeg could not decode a clip");
                }

                decoded.Add(pcm.ToArray());
            }

            try
            {
                File.Delete(clipPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The job directory is removed wholesale at the end.
            }
        }

        context.Lease.Stage = "joining";
        var joined = _joiner(decoded, gap, maxSeconds);
        decoded.Clear();
        if (joined.DurationMs < 1) return ExecutionResult.Fail(FailCodes.TranscoderUnavailable, false, "no audio was produced");

        context.Lease.Stage = "encoding";
        var mp3 = new MemoryStream();
        var encoded = await _runner.RunAsync(new ProcessSpec
        {
            FileName = _ffmpeg,
            Arguments = FfmpegArgs.EncodePcmToMp3(bitrate),
            Stdin = new MemoryStream(joined.Pcm, writable: false),
            ConsumeStdout = (stdout, t) => stdout.CopyToAsync(mp3, t),
            Timeout = encodeTimeout,
        }, ct).ConfigureAwait(false);
        if (encoded.TimedOut) return ExecutionResult.Fail(FailCodes.Timeout, true, "encoding exceeded its timeout");
        if (encoded.ExitCode != 0 || mp3.Length == 0) return ExecutionResult.Fail(FailCodes.TranscoderUnavailable, false, "ffmpeg produced no audio");
        if (mp3.Length > job.Limits.MaxOutputBytes) return ExecutionResult.Fail(FailCodes.LimitsExceeded, false, "mp3 larger than limits.maxOutputBytes");

        var joinPath = Path.Combine(context.ScratchDirectory, "join.mp3");
        await File.WriteAllBytesAsync(joinPath, mp3.ToArray(), ct).ConfigureAwait(false);
        context.Lease.Stage = "uploading";
        var uploaded = await context.Io.UploadOutputAsync(job, context.Lease, "join.mp3", joinPath, ct).ConfigureAwait(false);
        File.Delete(joinPath);

        var inputSha = Hashing.Sha256Hex(string.Join(",", job.Inputs.Select(c => c.Sha256)));
        var result = Serialize(job, inputSha, job.Inputs.Count, joined, uploaded);
        if (result.Length > job.Limits.MaxResultBytes) return ExecutionResult.Fail(FailCodes.LimitsExceeded, false, "result larger than limits.maxResultBytes");
        _log.LogInformation("speaking join finished clips={Clips} bytes={Bytes}", job.Inputs.Count, uploaded.SizeBytes);
        return ExecutionResult.Ok(result, [uploaded], totalBytes, 0);
    }

    private static byte[] Serialize(ClaimedJob job, string inputSha, int clipCount, PcmJoinResult joined, OutputRef mp3)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "media.speaking-join.result/1");
            writer.WriteString("engineVersion", job.EngineVersion);
            writer.WriteString("inputSha256", inputSha);
            writer.WriteNumber("clipCount", clipCount);
            writer.WriteNumber("durationMs", joined.DurationMs);
            writer.WriteBoolean("truncated", joined.Truncated);
            writer.WriteNumber("mp3Bytes", mp3.SizeBytes);
            writer.WriteString("mp3Sha256", mp3.Sha256);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }
}
