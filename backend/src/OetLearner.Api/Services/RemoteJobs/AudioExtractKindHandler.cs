using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.LiveClasses;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>One chunk of a parsed <c>media.audio-extract.result/1</c> (OET-RWP/1 section 6.3).</summary>
public sealed record AudioExtractChunk(int Index, string Name, int StartMs, int DurationMs, int SizeBytes, string Sha256);

/// <summary>A parsed <c>media.audio-extract.result/1</c>. Never trusted until <see cref="AudioExtractValidator"/> has verified it.</summary>
public sealed record AudioExtractResult(
    string Schema,
    string EngineVersion,
    string InputSha256,
    int DurationMs,
    int SampleRateHz,
    int Channels,
    string Codec,
    int BitrateKbps,
    int SegmentSeconds,
    int ChunkCount,
    IReadOnlyList<AudioExtractChunk> Chunks,
    int TotalOutputBytes);

/// <summary>
/// The job parameters and settings fingerprint of <c>media.audio-extract</c> v1 (OET-RWP/1 section 6.3). They are constants: a change to
/// any of them is an engine change and changes <see cref="Hash"/>, which re-keys every job and discards an in-flight result as stale.
/// </summary>
public static class AudioExtractSettings
{
    public const string ResourceType = "LiveClassRecording";

    /// <summary>The single input of the job (the stored recording).</summary>
    public const string InputName = "media";

    public const int SampleRateHz = 16_000;
    public const int Channels = 1;
    public const string Codec = "mp3";
    public const int BitrateKbps = 48;
    public const int SegmentSeconds = 600;

    /// <summary>Schema v1 requires sample-exact boundaries: no overlap.</summary>
    public const int OverlapMs = 0;

    /// <summary>One chunk must stay well under the 24 MiB single-call attachment cap of the transcription stage.</summary>
    public const int MaxChunkBytes = 20_971_520;

    /// <summary>Four hours of audio; longer recordings fail <c>duration_exceeded</c> on the helper.</summary>
    public const int MaxDurationSeconds = 14_400;

    public const string ArgsVersion = "v1";

    public static string Hash()
        => RemoteJobKeys.SettingsHash(
        [
            new KeyValuePair<string, string>("argsVersion", ArgsVersion),
            new KeyValuePair<string, string>("bitrateKbps", BitrateKbps.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("channels", Channels.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("codec", Codec),
            new KeyValuePair<string, string>("maxChunkBytes", MaxChunkBytes.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("maxDurationSeconds", MaxDurationSeconds.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("overlapMs", OverlapMs.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("sampleRateHz", SampleRateHz.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("segmentSeconds", SegmentSeconds.ToString(CultureInfo.InvariantCulture)),
        ]);

    /// <summary>The job parameters offered to the agent (no identifier, no key: <c>params</c> is sent to the node as is).</summary>
    public static string ParamsJson()
        => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["sampleRateHz"] = SampleRateHz,
            ["channels"] = Channels,
            ["codec"] = Codec,
            ["bitrateKbps"] = BitrateKbps,
            ["segmentSeconds"] = SegmentSeconds,
            ["overlapMs"] = OverlapMs,
            ["maxChunkBytes"] = MaxChunkBytes,
            ["maxDurationSeconds"] = MaxDurationSeconds,
            ["argsVersion"] = ArgsVersion,
        });

    /// <summary>The input content type for a stored recording key, or null when the extension is not one a helper is offered.</summary>
    public static string? ContentTypeFor(string storageKey)
        => Path.GetExtension(storageKey).ToLowerInvariant() switch
        {
            ".mp4" => "video/mp4",
            ".m4a" => "audio/mp4",
            ".mp3" or ".mpeg" or ".mpga" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".webm" => "audio/webm",
            ".ogg" or ".oga" => "audio/ogg",
            ".aac" => "audio/aac",
            _ => null,
        };
}

/// <summary>Strict parsing and verification of an audio-extract result. Pure: no database, no clock.</summary>
public static partial class AudioExtractValidator
{
    public const int MaxChunks = 64;

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    /// <summary>The wire name of chunk <paramref name="index"/>: <c>chunk-0000.mp3</c>.</summary>
    public static string ChunkName(int index) => "chunk-" + index.ToString("D4", CultureInfo.InvariantCulture) + ".mp3";

    public static bool TryParse(string resultJson, out AudioExtractResult? result, out string? error)
    {
        result = null;
        error = null;
        try
        {
            using var document = JsonDocument.Parse(resultJson, new JsonDocumentOptions { MaxDepth = 8 });
            var json = new StrictJson(document.RootElement);
            if (!json.IsObject) return Fail("The result is not a JSON object.", out error);

            if (!json.TryString("schema", out var schema) || schema is null) return Fail("schema is required.", out error);
            if (!json.TryString("engineVersion", out var engine) || engine is null) return Fail("engineVersion is required.", out error);
            if (!json.TryString("inputSha256", out var inputSha) || inputSha is null) return Fail("inputSha256 is required.", out error);
            if (!json.TryInt("durationMs", out var durationMs)) return Fail("durationMs is required.", out error);
            if (!json.TryInt("sampleRateHz", out var sampleRate)) return Fail("sampleRateHz is required.", out error);
            if (!json.TryInt("channels", out var channels)) return Fail("channels is required.", out error);
            if (!json.TryString("codec", out var codec) || codec is null) return Fail("codec is required.", out error);
            if (!json.TryInt("bitrateKbps", out var bitrate)) return Fail("bitrateKbps is required.", out error);
            if (!json.TryInt("segmentSeconds", out var segmentSeconds)) return Fail("segmentSeconds is required.", out error);
            if (!json.TryInt("chunkCount", out var chunkCount)) return Fail("chunkCount is required.", out error);
            if (!json.TryInt("totalOutputBytes", out var totalBytes)) return Fail("totalOutputBytes is required.", out error);
            if (!json.TryObjectArray("chunks", out var rawChunks) || rawChunks is null) return Fail("chunks is required.", out error);
            if (rawChunks.Count > MaxChunks) return Fail("chunks is too long.", out error);

            var chunks = new List<AudioExtractChunk>(rawChunks.Count);
            foreach (var raw in rawChunks)
            {
                var chunk = new StrictJson(raw);
                if (!chunk.TryInt("index", out var index)
                    || !chunk.TryString("name", out var name) || name is null
                    || !chunk.TryInt("startMs", out var startMs)
                    || !chunk.TryInt("durationMs", out var chunkDuration)
                    || !chunk.TryInt("sizeBytes", out var size)
                    || !chunk.TryString("sha256", out var sha) || sha is null)
                {
                    return Fail("chunks[] entries need index, name, startMs, durationMs, sizeBytes and sha256.", out error);
                }

                chunks.Add(new AudioExtractChunk(index, name, startMs, chunkDuration, size, sha));
            }

            result = new AudioExtractResult(
                schema, engine, inputSha, durationMs, sampleRate, channels, codec, bitrate, segmentSeconds, chunkCount, chunks, totalBytes);
            return true;
        }
        catch (JsonException)
        {
            return Fail("The result is not valid JSON.", out error);
        }

        static bool Fail(string message, out string? failure)
        {
            failure = message;
            return false;
        }
    }

    /// <summary>
    /// Verifies the result against the job and the uploaded outputs (OET-RWP/1 section 6.3): echoes, parameters, a contiguous
    /// sample-exact manifest, and one uploaded output per chunk with the same size and hash. Structural problems are
    /// <c>Invalid</c>; an output that differs from what the result claims is <c>OutputMismatch</c>.
    /// </summary>
    public static RemoteResultValidation Validate(RemoteJobRow job, AudioExtractResult r, IReadOnlyList<RemoteOutputRow> outputs)
    {
        if (!string.Equals(r.Schema, $"media.audio-extract.result/{job.SchemaVersion}", StringComparison.Ordinal))
        {
            return RemoteResultValidation.Invalid("schema differs from the job.");
        }

        if (!string.Equals(r.EngineVersion, job.EngineVersion, StringComparison.Ordinal))
        {
            return RemoteResultValidation.EngineMismatch("engineVersion differs from the job.");
        }

        if (!string.Equals(r.InputSha256, job.InputSha256, StringComparison.Ordinal))
        {
            return RemoteResultValidation.Invalid("inputSha256 differs from the job.");
        }

        var spec = RemoteJobKinds.Find(job.Kind);
        if (spec is null) return RemoteResultValidation.Invalid("The job kind is unknown.");
        var limits = RemoteJobLimits.FromJson(job.LimitsJson, spec.Limits);

        var segmentSeconds = RemoteJobParams.ReadInt(job.ParamsJson, "segmentSeconds") ?? AudioExtractSettings.SegmentSeconds;
        var maxChunkBytes = RemoteJobParams.ReadInt(job.ParamsJson, "maxChunkBytes") ?? AudioExtractSettings.MaxChunkBytes;
        var maxDurationSeconds = RemoteJobParams.ReadInt(job.ParamsJson, "maxDurationSeconds") ?? AudioExtractSettings.MaxDurationSeconds;
        if (r.SampleRateHz != (RemoteJobParams.ReadInt(job.ParamsJson, "sampleRateHz") ?? AudioExtractSettings.SampleRateHz)
            || r.Channels != (RemoteJobParams.ReadInt(job.ParamsJson, "channels") ?? AudioExtractSettings.Channels)
            || r.BitrateKbps != (RemoteJobParams.ReadInt(job.ParamsJson, "bitrateKbps") ?? AudioExtractSettings.BitrateKbps)
            || !string.Equals(r.Codec, RemoteJobParams.ReadString(job.ParamsJson, "codec") ?? AudioExtractSettings.Codec, StringComparison.Ordinal)
            || r.SegmentSeconds != segmentSeconds)
        {
            return RemoteResultValidation.Invalid("The audio parameters differ from the job.");
        }

        if (segmentSeconds <= 0) return RemoteResultValidation.Invalid("segmentSeconds is out of range.");
        var maxChunks = Math.Min(MaxChunks, limits.MaxOutputs <= 0 ? MaxChunks : limits.MaxOutputs);
        if (r.ChunkCount < 1 || r.ChunkCount > maxChunks || r.ChunkCount != r.Chunks.Count)
        {
            return RemoteResultValidation.Invalid("chunkCount does not match chunks.");
        }

        if (r.DurationMs < 1 || r.DurationMs > (long)maxDurationSeconds * 1000) return RemoteResultValidation.Invalid("durationMs is out of range.");

        var segmentMs = (long)segmentSeconds * 1000;
        long expectedStart = 0;
        long totalDuration = 0;
        long totalBytes = 0;
        for (var i = 0; i < r.Chunks.Count; i++)
        {
            var chunk = r.Chunks[i];
            if (chunk.Index != i) return RemoteResultValidation.Invalid("chunk indexes must be contiguous from 0.");
            if (!string.Equals(chunk.Name, ChunkName(i), StringComparison.Ordinal)) return RemoteResultValidation.Invalid("A chunk name is invalid.");
            if (chunk.StartMs != expectedStart) return RemoteResultValidation.Invalid("A chunk does not start where the previous one ended.");

            var last = i == r.Chunks.Count - 1;
            if (last ? chunk.DurationMs < 1 || chunk.DurationMs > segmentMs : chunk.DurationMs != segmentMs)
            {
                return RemoteResultValidation.Invalid("A chunk duration is out of range.");
            }

            if (chunk.SizeBytes < 1 || chunk.SizeBytes > maxChunkBytes) return RemoteResultValidation.Invalid("A chunk size is out of range.");
            if (!Sha256Pattern().IsMatch(chunk.Sha256)) return RemoteResultValidation.Invalid("A chunk sha256 must be 64 lowercase hex characters.");

            expectedStart += chunk.DurationMs;
            totalDuration += chunk.DurationMs;
            totalBytes += chunk.SizeBytes;
        }

        if (totalDuration != r.DurationMs) return RemoteResultValidation.Invalid("durationMs does not equal the sum of the chunk durations.");
        if (totalBytes != r.TotalOutputBytes || totalBytes > limits.MaxOutputBytes)
        {
            return RemoteResultValidation.Invalid("totalOutputBytes does not match the chunks.");
        }

        // Outputs last: the manifest is structurally sound, so a difference here is the node's upload disagreeing with its own result.
        if (outputs.Count != r.Chunks.Count) return RemoteResultValidation.OutputMismatch("The uploaded outputs are not exactly the chunks of the result.");
        foreach (var chunk in r.Chunks)
        {
            var uploaded = outputs.FirstOrDefault(output => string.Equals(output.Name, chunk.Name, StringComparison.Ordinal));
            if (uploaded is null || uploaded.SizeBytes != chunk.SizeBytes || !string.Equals(uploaded.Sha256, chunk.Sha256, StringComparison.Ordinal))
            {
                return RemoteResultValidation.OutputMismatch("An uploaded chunk differs from the result.");
            }
        }

        var summary = JsonSerializer.Serialize(new
        {
            durationMs = r.DurationMs,
            chunkCount = r.ChunkCount,
            totalOutputBytes = r.TotalOutputBytes,
        });
        return RemoteResultValidation.Ok(r, summary);
    }
}

/// <summary>
/// <c>media.audio-extract</c> v1 (OET-RWP/1 section 6.3): a helper decodes a Live Class recording once with ffmpeg and returns
/// transcription-sized mp3 chunks as per-job outputs. The applier verifies the manifest and writes it onto the recording row; the
/// transcription stage then makes one gateway call per chunk (every AI call stays on the API) and deletes the chunk audio when it is
/// done. The chunks are NOT registered as <c>MediaAsset</c>s: nothing needs them as assets, and keeping them only in the manifest means
/// they are neither listed anywhere nor outlive the transcription.
/// </summary>
public sealed class AudioExtractKindHandler(ILogger<AudioExtractKindHandler> logger) : IRemoteKindHandler
{
    public string Kind => RemoteJobKinds.MediaAudioExtract;

    public RemoteResultValidation Validate(
        RemoteJobRow job,
        string resultJson,
        IReadOnlyList<RemoteOutputRow> outputs,
        RemoteJobsOptions options)
    {
        if (!AudioExtractValidator.TryParse(resultJson, out var result, out var error))
        {
            return RemoteResultValidation.Invalid(error ?? "The result could not be parsed.");
        }

        return AudioExtractValidator.Validate(job, result!, outputs);
    }

    public async Task<RemoteApplyOutcome> ApplyAsync(RemoteApplyContext context, CancellationToken ct)
    {
        var result = (AudioExtractResult)context.Parsed;
        var job = context.Job;

        // Only the apply purpose exists for this kind: there is no shadow or canary form of an extraction.
        if (!string.Equals(job.Purpose, RemoteJobPurpose.Apply, StringComparison.Ordinal))
        {
            return RemoteApplyOutcome.NoOp("unsupported_purpose");
        }

        var db = context.Db;
        var recordingId = job.ResourceId;
        var recording = await db.LiveClassRecordings.AsNoTracking()
            .Where(r => r.Id == recordingId)
            .Select(r => new { r.S3AudioKey, r.S3VideoKey, r.TranscriptText })
            .FirstOrDefaultAsync(ct);
        if (recording is null) return RemoteApplyOutcome.Discarded("resource_gone");

        // The extraction describes ONE stored object: if the recording now points at another, the chunks are for something else.
        var currentKey = !string.IsNullOrWhiteSpace(recording.S3AudioKey) ? recording.S3AudioKey : recording.S3VideoKey;
        var input = RemoteInputOutputService.ReadManifest(job.InputsJson)
            .FirstOrDefault(entry => string.Equals(entry.Name, AudioExtractSettings.InputName, StringComparison.Ordinal));
        if (input is null || !string.Equals(input.StorageKey, currentKey, StringComparison.Ordinal))
        {
            return RemoteApplyOutcome.Discarded("stale_input");
        }

        if (!string.Equals(job.SettingsHash, AudioExtractSettings.Hash(), StringComparison.Ordinal))
        {
            return RemoteApplyOutcome.Discarded("stale_settings");
        }

        // A real transcript arrived while the helper worked (Zoom's own, an admin's): the chunks would never be used.
        if (!string.IsNullOrWhiteSpace(recording.TranscriptText)
            && !LiveClassRecordingProcessingService.IsPlaceholderTranscript(recording.TranscriptText))
        {
            return RemoteApplyOutcome.Discarded("already_transcribed");
        }

        var uploaded = context.Outputs.ToDictionary(output => output.Name, StringComparer.Ordinal);
        var manifest = new LiveClassAudioManifest
        {
            JobId = job.Id,
            Fence = context.Fence,
            InputSha256 = job.InputSha256,
            DurationMs = result.DurationMs,
            SegmentSeconds = result.SegmentSeconds,
            Chunks = result.Chunks
                .Select(chunk => new LiveClassAudioChunk
                {
                    Index = chunk.Index,
                    Name = chunk.Name,
                    StorageKey = uploaded[chunk.Name].StorageKey,
                    StartMs = chunk.StartMs,
                    DurationMs = chunk.DurationMs,
                    SizeBytes = chunk.SizeBytes,
                    Sha256 = chunk.Sha256,
                })
                .ToList(),
        };
        var manifestJson = manifest.ToJson();

        var written = await db.LiveClassRecordings
            .Where(r => r.Id == recordingId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AudioChunksJson, _ => manifestJson), ct);
        if (written == 0) return RemoteApplyOutcome.Discarded("resource_gone");

        // The chunk timestamps of the embedding stage scale by the recording length: fill it in when Zoom never reported one.
        var seconds = (int)Math.Min(int.MaxValue, (result.DurationMs + 999L) / 1000L);
        await db.LiveClassRecordings
            .Where(r => r.Id == recordingId && r.DurationSeconds == 0)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.DurationSeconds, _ => seconds), ct);

        logger.LogInformation(
            "Remote audio extraction of recording {RecordingId} applied: {ChunkCount} chunk(s), {DurationMs} ms.",
            recordingId, result.ChunkCount, result.DurationMs);
        return RemoteApplyOutcome.Applied(new { chunkCount = result.ChunkCount, durationMs = result.DurationMs, totalOutputBytes = result.TotalOutputBytes });
    }
}
