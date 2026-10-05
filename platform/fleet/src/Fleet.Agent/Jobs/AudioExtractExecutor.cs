using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Fleet.Agent;

/// <summary>
/// media.audio-extract v1 (protocol 6.3): one decode of the recording to raw PCM, cut into SAMPLE-EXACT fixed windows, each
/// window encoded independently to mp3 and uploaded as chunk-NNNN.mp3. ffmpeg only; no transcription on the helper.
/// </summary>
internal sealed class AudioExtractExecutor : IJobExecutor
{
    private const int Rate = FfmpegArgs.SampleRate;
    private const int BytesPerMs = FfmpegArgs.BytesPerSecond / 1000; // 32 bytes of s16le mono per millisecond

    private readonly IProcessRunner _runner;
    private readonly string _ffmpeg;
    private readonly ILogger _log;

    public AudioExtractExecutor(string engineVersion, IProcessRunner runner, string ffmpegPath, ILogger log)
    {
        EngineVersion = engineVersion;
        _runner = runner;
        _ffmpeg = ffmpegPath;
        _log = log;
    }

    public string Kind => JobKinds.MediaAudioExtract;
    public int SchemaVersion => 1;
    public string EngineVersion { get; }

    private sealed record Settings(int BitrateKbps, int SegmentSeconds, long MaxChunkBytes, int MaxDurationSeconds);

    private sealed record ChunkInfo(int Index, string Name, long StartMs, long DurationMs, long SizeBytes, string Sha256);

    public async Task<ExecutionResult> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var job = context.Job;
        if (job.Inputs.Count != 1 || job.Inputs[0].Name != "media")
        {
            throw new JobFailureException(FailCodes.InternalError, false, "unexpected input manifest");
        }

        var settings = ReadSettings(job.Params);
        context.Lease.Stage = "downloading";
        var input = job.Inputs[0];
        using var media = await context.Io.FetchInputAsync(job, context.Lease, input, Path.Combine(context.ScratchDirectory, "media.bin"), cacheable: false, ct).ConfigureAwait(false);

        context.Lease.Stage = "transcoding";
        var windowBytes = checked(settings.SegmentSeconds * FfmpegArgs.BytesPerSecond);
        var chunks = new List<ChunkInfo>();
        var outputs = new List<OutputRef>();
        long totalBytes = 0;
        var timeout = TimeSpan.FromSeconds(Math.Max(10, job.Limits.TimeoutSeconds));

        async Task ConsumeAsync(Stream pcm, CancellationToken token)
        {
            var window = new byte[windowBytes];
            while (true)
            {
                var filled = await ReadFullAsync(pcm, window, token).ConfigureAwait(false);
                filled -= filled % 2; // whole samples only
                if (filled <= 0) break;

                var startMs = totalBytes / BytesPerMs;
                totalBytes += filled;
                if (totalBytes > (long)settings.MaxDurationSeconds * FfmpegArgs.BytesPerSecond)
                {
                    throw new JobFailureException(FailCodes.DurationExceeded, false, "recording longer than maxDurationSeconds");
                }

                if (chunks.Count >= Math.Max(1, job.Limits.MaxOutputs))
                {
                    throw new JobFailureException(FailCodes.LimitsExceeded, false, "more chunks than limits.maxOutputs");
                }

                context.Lease.Stage = "encoding";
                var mp3 = new MemoryStream();
                var encoded = await _runner.RunAsync(new ProcessSpec
                {
                    FileName = _ffmpeg,
                    Arguments = FfmpegArgs.EncodePcmToMp3(settings.BitrateKbps),
                    Stdin = new MemoryStream(window, 0, filled, writable: false),
                    ConsumeStdout = (stdout, t) => stdout.CopyToAsync(mp3, t),
                    Timeout = timeout,
                }, token).ConfigureAwait(false);
                if (encoded.TimedOut) throw new JobFailureException(FailCodes.Timeout, true, "encoding a window exceeded the job timeout");
                if (encoded.ExitCode != 0 || mp3.Length == 0)
                {
                    throw new JobFailureException(FailCodes.TranscoderUnavailable, false, "ffmpeg could not encode a window");
                }

                if (mp3.Length > settings.MaxChunkBytes)
                {
                    throw new JobFailureException(FailCodes.InternalError, true, "chunk larger than maxChunkBytes");
                }

                var index = chunks.Count;
                var name = "chunk-" + index.ToString("D4", CultureInfo.InvariantCulture) + ".mp3";
                var path = Path.Combine(context.ScratchDirectory, name);
                await File.WriteAllBytesAsync(path, mp3.ToArray(), token).ConfigureAwait(false);
                context.Lease.Stage = "uploading";
                var uploaded = await context.Io.UploadOutputAsync(job, context.Lease, name, path, token).ConfigureAwait(false);
                File.Delete(path); // delete the chunk locally after a 200 (6.3 step 5)

                var endMs = totalBytes / BytesPerMs;
                chunks.Add(new ChunkInfo(index, name, startMs, endMs - startMs, uploaded.SizeBytes, uploaded.Sha256));
                outputs.Add(uploaded);
                if (filled < windowBytes) break;
            }
        }

        var decoded = await _runner.RunAsync(new ProcessSpec
        {
            FileName = _ffmpeg,
            Arguments = FfmpegArgs.DecodeFileToPcm(media.Path),
            ConsumeStdout = ConsumeAsync,
            Timeout = timeout,
        }, ct).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        if (decoded.TimedOut) return ExecutionResult.Fail(FailCodes.Timeout, true, "transcoding exceeded the job timeout");
        if (decoded.ExitCode == ProcessRunner.NotStarted) return ExecutionResult.Fail(FailCodes.TranscoderUnavailable, false, "ffmpeg is not available");
        if (decoded.ExitCode != 0)
        {
            return decoded.StderrTail.Contains("matches no streams", StringComparison.OrdinalIgnoreCase)
                ? ExecutionResult.Fail(FailCodes.NoAudioStream, false, "the recording has no audio stream")
                : ExecutionResult.Fail(FailCodes.TranscoderUnavailable, false, "ffmpeg could not decode the recording");
        }

        if (chunks.Count == 0) return ExecutionResult.Fail(FailCodes.NoAudioStream, false, "the recording has no audio stream");

        var result = Serialize(job, settings, input.Sha256, totalBytes / BytesPerMs, chunks);
        if (result.Length > job.Limits.MaxResultBytes) return ExecutionResult.Fail(FailCodes.LimitsExceeded, false, "result larger than limits.maxResultBytes");
        _log.LogInformation("audio extract finished chunks={Chunks} bytes={Bytes}", chunks.Count, outputs.Sum(o => o.SizeBytes));
        return ExecutionResult.Ok(result, outputs, media.Size, 0);
    }

    /// <summary>Validates the v1 parameter set; anything the procedure does not define is refused rather than guessed.</summary>
    private static Settings ReadSettings(System.Text.Json.JsonElement parameters)
    {
        if (ParamReader.Int(parameters, "sampleRateHz", Rate, 1, 1_000_000) != Rate
            || ParamReader.Int(parameters, "channels", 1, 1, 64) != 1
            || ParamReader.String(parameters, "codec", "mp3") != "mp3"
            || ParamReader.Int(parameters, "overlapMs", 0, 0, 60_000) != 0
            || ParamReader.String(parameters, "argsVersion", "v1") != "v1")
        {
            throw new JobFailureException(FailCodes.InternalError, false, "unsupported audio parameters");
        }

        return new Settings(
            ParamReader.Int(parameters, "bitrateKbps", 48, 8, 320),
            ParamReader.Int(parameters, "segmentSeconds", 600, 1, 3600),
            ParamReader.Int(parameters, "maxChunkBytes", 20_971_520, 1024, 67_108_864),
            ParamReader.Int(parameters, "maxDurationSeconds", 14_400, 1, 86_400));
    }

    /// <summary>Reads until the buffer is full or the stream ends; returns the number of bytes read.</summary>
    internal static async Task<int> ReadFullAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var filled = 0;
        while (filled < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(filled), ct).ConfigureAwait(false);
            if (read == 0) break;
            filled += read;
        }

        return filled;
    }

    private static byte[] Serialize(ClaimedJob job, Settings settings, string inputSha, long durationMs, List<ChunkInfo> chunks)
    {
        var buffer = new ArrayBufferWriter<byte>(2048);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "media.audio-extract.result/1");
            writer.WriteString("engineVersion", job.EngineVersion);
            writer.WriteString("inputSha256", inputSha);
            writer.WriteNumber("durationMs", durationMs);
            writer.WriteNumber("sampleRateHz", Rate);
            writer.WriteNumber("channels", 1);
            writer.WriteString("codec", "mp3");
            writer.WriteNumber("bitrateKbps", settings.BitrateKbps);
            writer.WriteNumber("segmentSeconds", settings.SegmentSeconds);
            writer.WriteNumber("chunkCount", chunks.Count);
            writer.WriteStartArray("chunks");
            foreach (var chunk in chunks)
            {
                writer.WriteStartObject();
                writer.WriteNumber("index", chunk.Index);
                writer.WriteString("name", chunk.Name);
                writer.WriteNumber("startMs", chunk.StartMs);
                writer.WriteNumber("durationMs", chunk.DurationMs);
                writer.WriteNumber("sizeBytes", chunk.SizeBytes);
                writer.WriteString("sha256", chunk.Sha256);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteNumber("totalOutputBytes", chunks.Sum(c => c.SizeBytes));
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }
}
